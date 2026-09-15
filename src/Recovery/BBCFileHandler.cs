using Ionic.Zlib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace bbc_cassette_loader
{
	internal sealed class RecoveryBackupInfo
	{
		public readonly string DirectoryPath;
		public readonly DateTime LastWriteTime;
		public readonly int FileCount;

		public RecoveryBackupInfo(string directoryPath, DateTime lastWriteTime, int fileCount)
		{
			DirectoryPath = directoryPath;
			LastWriteTime = lastWriteTime;
			FileCount = fileCount;
		}

		public override string ToString()
		{
			return LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "  (" + FileCount +
				(FileCount == 1 ? " file" : " files") + ")";
		}
	}

	sealed class RecoveryManifest
	{
		public int version = 1;
		public string generation;
		public List<RecoveryManifestEntry> files = new List<RecoveryManifestEntry>();
	}

	sealed class RecoveryManifestEntry
	{
		public string key;
		public string file;
	}

	public sealed class RecoveryLoadResult
	{
		public readonly IReadOnlyList<string> Warnings;

		internal RecoveryLoadResult(IReadOnlyList<string> warnings)
		{
			Warnings = warnings;
		}
	}

	class BBCFileHandler
	{
		const int DiagnosticFileMagic = 0x42424344;
		const int DiagnosticFileVersion = 1;
		const int MaximumDiagnosticSamples = 1000000;
		const int MaximumDiagnosticMarkers = 100000;

		public struct InvalidBlockData
		{
			public readonly string fileUID;
			public readonly ushort blockNum;
			public readonly bool isData;
			public readonly long diagnosticId;
			public readonly string description;

			public InvalidBlockData(
				string fileUID,
				ushort blockNum,
				bool isData,
				long diagnosticId,
				string description)
			{
				this.fileUID = fileUID;
				this.blockNum = blockNum;
				this.isData = isData;
				this.diagnosticId = diagnosticId;
				this.description = description;
			}
		}

		public event EventHandler<string> UpdateFile;
		public event EventHandler<ToneHandler.ErrorDataForGraph> NewData;
		public event EventHandler<InvalidBlockData> InvalidBlockReceived;
		public event EventHandler<ToneHandler.FileImportProgressData> FileImportProgress;
		public event EventHandler<string> CrcRepairAccepted;
		public event EventHandler<BlockHandler.CrcRepairInfo> CrcRepairAcceptedDetailed;
		public event EventHandler ListeningCompleteEvent;
		public event EventHandler LineInStoppedEvent;
		public event EventHandler<LineInRecordingResult> LineInRecordingStoppedEvent;

		public readonly Dictionary<string, BBCFile> files;

		public bool IsListeningToLineIn => toneHandler.IsListeningToLineIn;
		public bool IsRecordingToFile => toneHandler.IsRecordingToFile;
		// Regression tests can force a forward restore move to fail; rollback moves
		// always use the real filesystem operation.
		internal Func<string, string, Exception> RestoreMoveFailureFactory { get; set; }

		readonly BlockHandler blockHandler;
		readonly ToneHandler toneHandler;
		readonly string recoveryStateDirectory;
		readonly object diagnosticStoreLock;
		readonly Dictionary<long, string> diagnosticFiles;
		readonly HashSet<long> selectableDiagnosticIds;
		readonly string diagnosticStoreDirectory;
		readonly object fileUpdateLock = new object();
		readonly HashSet<string> pendingFileUpdates = new HashSet<string>();
		public bool HasUnsavedRecoveryState { get; private set; }
		public bool HasRecoveredFiles { get { return files.Count > 0; } }

		string currentFileUID;
		BlockHeader? recoveryHeader;
		long currentBlockDataStart;
		bool diagnosticStoreDisposed;

		public BBCFileHandler(
			string recoveryStateDirectory = null,
			bool manageInputVolume = false,
			int captureDeviceNumber = 0,
			bool enableCrcRepair = false)
		{
			files = new Dictionary<string, BBCFile>();
			diagnosticStoreLock = new object();
			diagnosticFiles = new Dictionary<long, string>();
			selectableDiagnosticIds = new HashSet<long>();
			diagnosticStoreDirectory = Path.Combine(
				Path.GetTempPath(),
				"bbc-cassette-loader-diagnostics-" + Guid.NewGuid().ToString("N"));
			this.recoveryStateDirectory = recoveryStateDirectory == null
				? ResolveDefaultRecoveryStateDirectory()
				: Path.GetFullPath(recoveryStateDirectory);

			EnableCrcRepair = enableCrcRepair;
			blockHandler = new BlockHandler();
			toneHandler = new ToneHandler(
				manageInputVolume: manageInputVolume,
				captureDeviceNumber: captureDeviceNumber);

			blockHandler.BlockHeaderReceived += BlockHeaderReceived;
			blockHandler.BlockDataReceived += BlockDataReceived;
			blockHandler.LogDetail += Log;
			blockHandler.Error += BlockError;
			blockHandler.CrcRepairAccepted += CrcRepairAcceptedReceived;
			blockHandler.CrcRepairAcceptedDetailed += CrcRepairAcceptedDetailedReceived;

			toneHandler.DecodedByteReceived += blockHandler.AddByteWithConfidence;
			toneHandler.InvalidToneData += blockHandler.ToneError;
			toneHandler.InvalidToneData += ToneError;
			toneHandler.DiagnosticAvailable += DiagnosticAvailable;
			toneHandler.FileImportProgress += FileImportProgressReceived;
			toneHandler.InputSessionStarted += InputSessionStarted;
			toneHandler.FileListeningComplete += ListeningComplete;
			toneHandler.LineInStopped += LineInStopped;
			toneHandler.LineInRecordingStopped += LineInRecordingStopped;

			currentFileUID = null;
		}

		static string ResolveDefaultRecoveryStateDirectory()
		{
			var besideExecutable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export");
			try
			{
				Directory.CreateDirectory(besideExecutable);
				var probe = Path.Combine(besideExecutable, ".write-test-" + Guid.NewGuid().ToString("N"));
				using (File.Create(probe)) { }
				File.Delete(probe);
				return Path.GetFullPath(besideExecutable);
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }

			var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (string.IsNullOrEmpty(localApplicationData))
				throw new InvalidOperationException("No writable recovery-state directory is available.");
			return Path.Combine(localApplicationData, "bbc-cassette-loader", "export");
		}

		public void StopListening()
		{
			toneHandler.StopListening();
			FlushFileUpdates();
		}

		public void FormClosing()
		{
			try
			{
				StopListening();

				blockHandler.BlockHeaderReceived -= BlockHeaderReceived;
				blockHandler.BlockDataReceived -= BlockDataReceived;
				blockHandler.LogDetail -= Log;
				blockHandler.Error -= BlockError;
				blockHandler.CrcRepairAccepted -= CrcRepairAcceptedReceived;
				blockHandler.CrcRepairAcceptedDetailed -= CrcRepairAcceptedDetailedReceived;

				toneHandler.DecodedByteReceived -= blockHandler.AddByteWithConfidence;
				toneHandler.InvalidToneData -= blockHandler.ToneError;
				toneHandler.InvalidToneData -= ToneError;
				toneHandler.DiagnosticAvailable -= DiagnosticAvailable;
				toneHandler.FileImportProgress -= FileImportProgressReceived;
				toneHandler.InputSessionStarted -= InputSessionStarted;
				toneHandler.FileListeningComplete -= ListeningComplete;
				toneHandler.LineInStopped -= LineInStopped;
				toneHandler.LineInRecordingStopped -= LineInRecordingStopped;

				toneHandler.FormClosing();
			}
			finally
			{
				DisposeDiagnosticStore();
			}
		}

		public void StartListeningToLineIn()
		{
			blockHandler.EnableCrcRepair = false;
			toneHandler.StartListeningToLineIn();
		}

		public void StartListeningToLineIn(string recordingFile)
		{
			blockHandler.EnableCrcRepair = false;
			toneHandler.StartListeningToLineIn(recordingFile);
		}

		public bool EnableCrcRepair { get; set; }

		public void SetCaptureDeviceNumber(int deviceNumber)
		{
			toneHandler.SetCaptureDeviceNumber(deviceNumber);
		}

		public async Task StartListeningToFileAsync(
			string filename,
			AudioChannelMode channelMode = AudioChannelMode.Mix,
			CancellationToken cancellationToken = default(CancellationToken),
			bool recoveryPasses = false,
			bool targetedRecovery = true)
		{
			try
			{
				blockHandler.EnableCrcRepair = EnableCrcRepair && !recoveryPasses;
				await toneHandler.StartListeningToFileAsync(filename, channelMode, cancellationToken, recoveryPasses, targetedRecovery);
			}
			finally { FlushFileUpdates(); }
		}

		internal long RecoverySamplesProcessed => toneHandler.RecoverySamplesProcessed;
		internal static int RecoveryProfileCount => ToneHandler.RecoveryProfileCount;
		internal static string GetRecoveryProfileName(int index) => ToneHandler.GetRecoveryProfileName(index);

		internal async Task StartListeningToFileWithProfilesAsync(string filename, int profileCount)
		{
			try { await toneHandler.StartListeningToFileWithProfilesAsync(filename, profileCount); }
			finally { FlushFileUpdates(); }
		}

		public bool TryGetDiagnostic(long diagnosticId, out ToneHandler.ErrorDataForGraph diagnostic)
		{
			diagnostic = default(ToneHandler.ErrorDataForGraph);
			lock (diagnosticStoreLock)
			{
				string path;
				if (!diagnosticFiles.TryGetValue(diagnosticId, out path))
					return false;
				try
				{
					diagnostic = ReadDiagnostic(path);
					return true;
				}
				catch (Exception ex) when (
					ex is IOException || ex is UnauthorizedAccessException ||
					ex is InvalidDataException || ex is EndOfStreamException)
				{
					Log("Unable to read diagnostic snapshot: " + ex.Message);
					try { File.Delete(path); } catch (IOException) { }
					catch (UnauthorizedAccessException) { }
					diagnosticFiles.Remove(diagnosticId);
					return false;
				}
			}
		}

		public bool IsDiagnosticPending(long diagnosticId)
		{
			lock (diagnosticStoreLock)
				return selectableDiagnosticIds.Contains(diagnosticId);
		}

		internal string DiagnosticStoreDirectory => diagnosticStoreDirectory;
		internal int StoredDiagnosticCount
		{
			get
			{
				lock (diagnosticStoreLock) return diagnosticFiles.Count;
			}
		}

		void InputSessionStarted(object sender, EventArgs e)
		{
			blockHandler.ResetBlock();
			currentFileUID = null;
			recoveryHeader = null;
			currentBlockDataStart = -1;
		}

		void ListeningComplete(object s, EventArgs e)
		{
			FlushFileUpdates();
			ListeningCompleteEvent?.Invoke(s, e);
		}

		void LineInStopped(object s, EventArgs e)
		{
			LineInStoppedEvent?.Invoke(this, e);
		}

		void LineInRecordingStopped(object s, LineInRecordingResult result)
		{
			LineInRecordingStoppedEvent?.Invoke(this, result);
		}

		void FileImportProgressReceived(object sender, ToneHandler.FileImportProgressData progress)
		{
			blockHandler.EnableCrcRepair = EnableCrcRepair && progress.passNumber == progress.passCount;
			FileImportProgress?.Invoke(this, progress);
		}

		void CrcRepairAcceptedReceived(object sender, string kind)
		{
			CrcRepairAccepted?.Invoke(this, kind);
		}

		void CrcRepairAcceptedDetailedReceived(object sender, BlockHandler.CrcRepairInfo repair)
		{
			CrcRepairAcceptedDetailed?.Invoke(this, repair);
		}

		void NotifyFileChanged(string fileUID)
		{
			HasUnsavedRecoveryState = true;
			var flush = false;
			lock (fileUpdateLock)
			{
				pendingFileUpdates.Add(fileUID);
				flush = Stopwatch.GetTimestamp() - lastFileUpdateTimestamp >= Stopwatch.Frequency / 20;
			}
			if (flush) FlushFileUpdates();
		}

		long lastFileUpdateTimestamp;

		void FlushFileUpdates()
		{
			string[] fileUIDs;
			lock (fileUpdateLock)
			{
				fileUIDs = pendingFileUpdates.ToArray();
				pendingFileUpdates.Clear();
				lastFileUpdateTimestamp = Stopwatch.GetTimestamp();
			}
			foreach (var fileUID in fileUIDs)
				UpdateFile?.Invoke(this, fileUID);
		}

		void UpdateUID(ref string uid, BlockHeader header, BlockData? data)
		{
			if (uid == null || !CouldContain(files[uid], header, data))
			{
				foreach (var f in files)
				{
					if (CouldContain(f.Value, header, data))
					{
						uid = f.Key;
						return;
					}
				}

				uid = header.filename;
				var uidId = 2;
				while (true)
				{
					if (!files.ContainsKey(uid))
					{
						files.Add(uid, new BBCFile(ref header));
						return;
					}

					uid = header.filename + uidId;
					uidId++;
				}
			}
		}

		static bool CouldContain(BBCFile file, BlockHeader header, BlockData? data)
		{
			return file.CouldContain(header) &&
				(data == null || file.CouldContain(data.Value, header.blockNum));
		}

		void BlockHeaderReceived(object sender, List<byte> data)
		{
			var blockHeader = new BlockHeader(data.ToArray());
			currentBlockDataStart = toneHandler.CurrentSamplePosition;
			if (toneHandler.IsRecoveryPass)
			{
				// A retry contributes only a complete CRC-checked header/data pair.
				// Failed attempts must not add file identities or error markers.
				recoveryHeader = blockHeader;
				return;
			}
			UpdateUID(ref currentFileUID, blockHeader, null);
			var changed = !files[currentFileUID].HasHeader(blockHeader.blockNum);
			files[currentFileUID].AddHeader(ref blockHeader);
			if (changed) NotifyFileChanged(currentFileUID);
		}

		void BlockDataReceived(object sender, List<byte> data)
		{
			var blockData = new BlockData(data.ToArray());
			var changed = false;
			if (toneHandler.IsRecoveryPass)
			{
				if (!recoveryHeader.HasValue)
					throw new InvalidOperationException("Recovery data has no validated header.");
				var recoveredHeader = recoveryHeader.Value;
				UpdateUID(ref currentFileUID, recoveredHeader, blockData);
				changed = !files[currentFileUID].HasHeader(recoveredHeader.blockNum);
				files[currentFileUID].AddHeader(ref recoveredHeader);
				recoveryHeader = null;
			}
			var header = files[currentFileUID].CurrentBlock.Header.Value;

			if (!files[currentFileUID].CouldContain(blockData, header.blockNum))
			{
				// This block had a matching header but the existing block has data which doesn't match this - we'll try our best to find a suitable file
				UpdateUID(ref currentFileUID, header, blockData);
				changed = changed || !files[currentFileUID].HasHeader(header.blockNum);
				files[currentFileUID].AddHeader(ref header);
			}

			changed = changed || !files[currentFileUID].HasData(header.blockNum);
			files[currentFileUID].AddData(ref blockData);
			if (currentBlockDataStart >= 0)
				toneHandler.RecordValidBlockRange(currentBlockDataStart, toneHandler.CurrentSamplePosition);
			currentBlockDataStart = -1;

			if (changed) NotifyFileChanged(currentFileUID);

			toneHandler.ResetWaitForTone();
			blockHandler.ResetBlock();
		}

		void DiagnosticAvailable(object sender, ToneHandler.ErrorDataForGraph diagnostic)
		{
			if (toneHandler.IsRecoveryPass) return;
			StoreDiagnostic(diagnostic);
			NewData?.Invoke(this, diagnostic);
		}

		void StoreDiagnostic(ToneHandler.ErrorDataForGraph diagnostic)
		{
			var diagnosticId = diagnostic.errorData.diagnosticId;
			if (diagnosticId == 0 || diagnostic.data == null) return;

			lock (diagnosticStoreLock)
			{
				if (diagnosticStoreDisposed || diagnosticFiles.ContainsKey(diagnosticId) ||
					!selectableDiagnosticIds.Contains(diagnosticId)) return;
				var path = Path.Combine(diagnosticStoreDirectory, diagnosticId + ".bin");
				var temporaryPath = path + ".tmp";
				try
				{
					Directory.CreateDirectory(diagnosticStoreDirectory);
					using (var writer = new BinaryWriter(File.Open(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)))
					{
						writer.Write(DiagnosticFileMagic);
						writer.Write(DiagnosticFileVersion);
						WriteMarker(writer, diagnostic.errorData);
						WriteMarkers(writer, diagnostic.byteBoundaryMarker);
						WriteMarkers(writer, diagnostic.bitBoundaryMarker);
						writer.Write(diagnostic.data.Count);
						foreach (var sample in diagnostic.data) writer.Write(sample);
					}
					File.Move(temporaryPath, path);
					diagnosticFiles.Add(diagnosticId, path);
					selectableDiagnosticIds.Remove(diagnosticId);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					Log("Unable to retain diagnostic snapshot: " + ex.Message);
					selectableDiagnosticIds.Remove(diagnosticId);
					try { File.Delete(temporaryPath); } catch (IOException) { }
					catch (UnauthorizedAccessException) { }
				}
			}
		}

		static void WriteMarkers(BinaryWriter writer, List<ToneHandler.MarkerData> markers)
		{
			writer.Write(markers.Count);
			foreach (var marker in markers) WriteMarker(writer, marker);
		}

		static void WriteMarker(BinaryWriter writer, ToneHandler.MarkerData marker)
		{
			writer.Write(marker.markerPosition);
			writer.Write(marker.markerDescription ?? string.Empty);
			writer.Write(marker.diagnosticId);
		}

		static ToneHandler.ErrorDataForGraph ReadDiagnostic(string path)
		{
			using (var reader = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
			{
				if (reader.ReadInt32() != DiagnosticFileMagic || reader.ReadInt32() != DiagnosticFileVersion)
					throw new InvalidDataException("Unsupported diagnostic snapshot format.");
				var diagnostic = new ToneHandler.ErrorDataForGraph(ReadMarker(reader));
				diagnostic.byteBoundaryMarker = ReadMarkers(reader);
				diagnostic.bitBoundaryMarker = ReadMarkers(reader);
				var sampleCount = ReadBoundedCount(reader, MaximumDiagnosticSamples, "sample");
				diagnostic.data = new List<float>(sampleCount);
				for (var i = 0; i < sampleCount; i++) diagnostic.data.Add(reader.ReadSingle());
				return diagnostic;
			}
		}

		static List<ToneHandler.MarkerData> ReadMarkers(BinaryReader reader)
		{
			var count = ReadBoundedCount(reader, MaximumDiagnosticMarkers, "marker");
			var markers = new List<ToneHandler.MarkerData>(count);
			for (var i = 0; i < count; i++) markers.Add(ReadMarker(reader));
			return markers;
		}

		static int ReadBoundedCount(BinaryReader reader, int maximum, string description)
		{
			var count = reader.ReadInt32();
			if (count < 0 || count > maximum)
				throw new InvalidDataException("Invalid diagnostic " + description + " count.");
			return count;
		}

		static ToneHandler.MarkerData ReadMarker(BinaryReader reader)
		{
			return new ToneHandler.MarkerData(reader.ReadInt32(), reader.ReadString(), reader.ReadInt64());
		}

		void DisposeDiagnosticStore()
		{
			lock (diagnosticStoreLock)
			{
				if (diagnosticStoreDisposed) return;
				diagnosticStoreDisposed = true;
				foreach (var path in diagnosticFiles.Values)
				{
					try { File.Delete(path); } catch (IOException) { }
					catch (UnauthorizedAccessException) { }
				}
				diagnosticFiles.Clear();
				selectableDiagnosticIds.Clear();
				try
				{
					if (Directory.Exists(diagnosticStoreDirectory)) Directory.Delete(diagnosticStoreDirectory);
				}
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}
		}

		void Error(string error)
		{
			recoveryHeader = null;
			if (error != null) Log(error);
			if (error != null && !toneHandler.IsRecoveryPass) toneHandler.BlockError(error);
			toneHandler.ResetWaitForTone();
			blockHandler.ResetBlock();
		}

		void ToneError(object sender, ToneHandler.MarkerData data)
		{
			if (toneHandler.IsRecoveryPass)
			{
				Error(null);
				return;
			}
			string filename;
			ushort blockNum;
			bool isData;
			if (blockHandler.TryGetInvalidBlockContext(out filename, out blockNum, out isData))
			{
				var fileUID = isData && currentFileUID != null ? currentFileUID : filename;
				lock (diagnosticStoreLock)
					if (!diagnosticStoreDisposed) selectableDiagnosticIds.Add(data.diagnosticId);
				InvalidBlockReceived?.Invoke(this, new InvalidBlockData(
					fileUID,
					blockNum,
					isData,
					data.diagnosticId,
					data.markerDescription));
			}
			Error(null);
		}

		void BlockError(object sender, string message)
		{
			Error(message);
		}

		void Log(object sender, string message)
		{
			Log(message);
		}

		void Log(string message)
		{
			Debug.WriteLine(message);
		}

		public void Serialise()
		{
			CommitRecoverySnapshot(files);
			HasUnsavedRecoveryState = false;
		}

		void CommitRecoverySnapshot(IEnumerable<KeyValuePair<string, BBCFile>> snapshot)
		{
			Directory.CreateDirectory(recoveryStateDirectory);
			var generation = ".recovery-generation-" + Guid.NewGuid().ToString("N");
			var generationDirectory = Path.Combine(recoveryStateDirectory, generation);
			var manifest = new RecoveryManifest { generation = generation };
			var serializer = new JsonSerializer();
			Directory.CreateDirectory(generationDirectory);
			try
			{
				foreach (var pair in snapshot)
				{
					var storedName = "file-" + Guid.NewGuid().ToString("N") + ".xml";
					var storedPath = Path.Combine(generationDirectory, storedName);
					using (var stream = new FileStream(storedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
					using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
					using (var json = new JsonTextWriter(writer))
					{
						serializer.Serialize(json, pair.Value);
						stream.Flush(true);
					}
					manifest.files.Add(new RecoveryManifestEntry { key = pair.Key, file = storedName });
					Log(JsonConvert.SerializeObject(pair.Value));
				}

				AtomicFile.Write(Path.Combine(generationDirectory, ".complete"), stream => { });
				AtomicFile.Write(Path.Combine(recoveryStateDirectory, "recovery-manifest.json"), stream =>
				{
					using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
					using (var json = new JsonTextWriter(writer))
						serializer.Serialize(json, manifest);
				});
				SyncLegacyRecoveryMirror(manifest, generationDirectory);
			}
			catch
			{
				TryDeleteDirectory(generationDirectory);
				throw;
			}
		}

		void SyncLegacyRecoveryMirror(RecoveryManifest manifest, string generationDirectory)
		{
			try
			{
				foreach (var existing in Directory.GetFiles(recoveryStateDirectory, "*.xml", SearchOption.TopDirectoryOnly))
					File.Delete(existing);
				foreach (var entry in manifest.files)
				{
					var mirrorName = MakeSafeRecoveryFileName(entry.key) + ".xml";
					File.Copy(Path.Combine(generationDirectory, entry.file), Path.Combine(recoveryStateDirectory, mirrorName));
				}
			}
			catch (IOException exception) { Log("Recovery mirror update failed: " + exception.Message); }
			catch (UnauthorizedAccessException exception) { Log("Recovery mirror update failed: " + exception.Message); }
		}

		static string MakeSafeRecoveryFileName(string key)
		{
			var safe = MakeSafeExportName(key);
			return string.IsNullOrEmpty(safe) ? "file" : safe;
		}

		static void TryDeleteDirectory(string directory)
		{
			try
			{
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}

		public RecoveryLoadResult Deserialise()
		{
			Directory.CreateDirectory(recoveryStateDirectory);
			var warnings = new List<string>();
			Dictionary<string, BBCFile> recovered;
			try
			{
				recovered = LoadActiveRecoveryFiles(warnings);
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				recovered = new Dictionary<string, BBCFile>(StringComparer.OrdinalIgnoreCase);
				warnings.Add("Recovery state could not be read: " + exception.Message);
			}
			files.Clear();
			foreach (var pair in recovered)
			{
				files.Add(pair.Key, pair.Value);
				UpdateFile?.Invoke(this, pair.Key);
			}
			HasUnsavedRecoveryState = false;
			return new RecoveryLoadResult(warnings);
		}

		Dictionary<string, BBCFile> LoadActiveRecoveryFiles(List<string> warnings)
		{
			var manifestPath = Path.Combine(recoveryStateDirectory, "recovery-manifest.json");
			if (File.Exists(manifestPath))
			{
				try
				{
					return LoadManifestFiles(recoveryStateDirectory, manifestPath, warnings);
				}
				catch (Exception exception) when (exception is InvalidDataException || exception is JsonException || exception is SerializationException || exception is ArgumentException)
				{
					warnings.Add("The journaled recovery state was incomplete or malformed; legacy recovery files were checked instead.");
				}
			}
			return LoadRecoveryFiles(recoveryStateDirectory, warnings);
		}

		Dictionary<string, BBCFile> LoadManifestFiles(string root, string manifestPath, List<string> warnings)
		{
			var manifest = ReadRecoveryManifest(manifestPath);
			if (manifest == null || manifest.version != 1 || string.IsNullOrWhiteSpace(manifest.generation) ||
				manifest.generation != Path.GetFileName(manifest.generation) || manifest.files == null)
				throw new InvalidDataException("The recovery manifest is invalid.");
			var generation = Path.GetFullPath(Path.Combine(root, manifest.generation));
			var expectedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!generation.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(generation, ".complete")))
				throw new InvalidDataException("The recovery generation is incomplete.");

			var recovered = new Dictionary<string, BBCFile>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in manifest.files)
			{
				if (entry == null || string.IsNullOrWhiteSpace(entry.key) || string.IsNullOrWhiteSpace(entry.file) ||
					entry.file != Path.GetFileName(entry.file))
					throw new InvalidDataException("The recovery manifest contains an invalid file entry.");
				var file = LoadRecoveryFile(Path.Combine(generation, entry.file), entry.key);
				recovered.Add(entry.key, file);
			}
			return recovered;
		}

		static RecoveryManifest ReadRecoveryManifest(string manifestPath)
		{
			var serializer = new JsonSerializer();
			using (var sr = new StreamReader(manifestPath))
			using (var reader = new JsonTextReader(sr))
				return serializer.Deserialize<RecoveryManifest>(reader);
		}

		internal List<RecoveryBackupInfo> GetResetBackups()
		{
			var backups = new List<RecoveryBackupInfo>();
			if (!Directory.Exists(recoveryStateDirectory)) return backups;
			try
			{
				foreach (var directory in Directory.GetDirectories(recoveryStateDirectory, "reset-backup-*", SearchOption.TopDirectoryOnly))
				{
					var fileCount = Directory.GetFiles(directory, "*.xml", SearchOption.TopDirectoryOnly).Length;
					backups.Add(new RecoveryBackupInfo(directory, Directory.GetLastWriteTime(directory), fileCount));
				}
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
			backups.Sort((first, second) => second.LastWriteTime.CompareTo(first.LastWriteTime));
			return backups;
		}

		internal void RestoreRecoveryBackup(string backupDirectory)
		{
			var backupPath = ValidateBackupPath(backupDirectory);
			var restoredFiles = LoadRecoveryFiles(backupPath);
			if (restoredFiles.Count == 0)
				throw new InvalidDataException("The selected recovery backup contains no recovery files.");

			StopListening();
			Directory.CreateDirectory(recoveryStateDirectory);
			var stagingDirectory = Path.Combine(recoveryStateDirectory, ".restore-" + Guid.NewGuid().ToString("N"));
			var safetyDirectory = Path.Combine(recoveryStateDirectory,
				"restore-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
			var movedCurrentFiles = new List<string>();
			var committedFiles = new List<string>();
			var currentFiles = Directory.GetFiles(recoveryStateDirectory, "*.xml", SearchOption.TopDirectoryOnly);
			try
			{
				Directory.CreateDirectory(stagingDirectory);
				foreach (var filename in Directory.GetFiles(backupPath, "*.xml", SearchOption.TopDirectoryOnly))
					File.Copy(filename, Path.Combine(stagingDirectory, Path.GetFileName(filename)));

				if (currentFiles.Length > 0)
				{
					Directory.CreateDirectory(safetyDirectory);
					foreach (var filename in currentFiles)
					{
						var destination = Path.Combine(safetyDirectory, Path.GetFileName(filename));
						MoveRestoreFile(filename, destination);
						movedCurrentFiles.Add(destination);
					}
				}

				foreach (var filename in Directory.GetFiles(stagingDirectory, "*.xml", SearchOption.TopDirectoryOnly))
				{
					var destination = Path.Combine(recoveryStateDirectory, Path.GetFileName(filename));
					MoveRestoreFile(filename, destination);
					committedFiles.Add(destination);
				}

				// The visible XML mirror is retained for compatibility, but the journal
				// becomes authoritative only after the whole replacement is present.
				CommitRecoverySnapshot(restoredFiles);
			}
			catch
			{
				foreach (var destination in committedFiles)
				{
					try { File.Delete(destination); }
					catch (IOException) { }
					catch (UnauthorizedAccessException) { }
				}
				foreach (var destination in movedCurrentFiles)
				{
					var original = Path.Combine(recoveryStateDirectory, Path.GetFileName(destination));
					if (!File.Exists(destination)) continue;
					try { File.Move(destination, original); }
					catch (IOException) { }
					catch (UnauthorizedAccessException) { }
				}
				throw;
			}
			finally
			{
				TryDeleteDirectory(stagingDirectory);
			}

			files.Clear();
			foreach (var pair in restoredFiles)
			{
				files.Add(pair.Key, pair.Value);
				UpdateFile?.Invoke(this, pair.Key);
			}
			lock (fileUpdateLock) pendingFileUpdates.Clear();
			currentFileUID = null;
			recoveryHeader = null;
			currentBlockDataStart = -1;
			blockHandler.ResetBlock();
			toneHandler.ResetWaitForTone();
			ClearDiagnosticSnapshots();
			HasUnsavedRecoveryState = false;
		}

		Dictionary<string, BBCFile> LoadRecoveryFiles(string directory, List<string> warnings = null)
		{
			var manifestPath = Path.Combine(directory, "recovery-manifest.json");
			if (File.Exists(manifestPath))
				return LoadManifestFiles(directory, manifestPath, warnings);

			var recovered = new Dictionary<string, BBCFile>(StringComparer.OrdinalIgnoreCase);
			foreach (var filename in Directory.GetFiles(directory, "*.xml", SearchOption.TopDirectoryOnly))
			{
				try
				{
					var key = Path.GetFileNameWithoutExtension(filename);
					recovered.Add(key, LoadRecoveryFile(filename, key));
				}
				catch (Exception exception) when (exception is InvalidDataException || exception is JsonException ||
					exception is SerializationException || exception is ArgumentException || exception is InvalidCastException)
				{
					if (warnings == null) throw new InvalidDataException("Recovery file is malformed: " + Path.GetFileName(filename), exception);
					warnings.Add("Recovery file skipped: " + Path.GetFileName(filename));
				}
			}
			return recovered;
		}

		BBCFile LoadRecoveryFile(string filename, string key)
		{
			var serializer = new JsonSerializer();
			using (var sr = new StreamReader(filename))
			using (var reader = new JsonTextReader(sr))
			{
				var file = serializer.Deserialize<BBCFile>(reader);
				if (file == null || string.IsNullOrWhiteSpace(file.filename))
					throw new InvalidDataException("Recovery file is empty or invalid: " + key);
				ValidateRecoveryFileStructure(file, filename);
				return file;
			}
		}

		static void ValidateRecoveryFileStructure(BBCFile file, string filename)
		{
			try
			{
				var blockCount = file.NumBlocks;
				for (var index = 0; index < blockCount; index++)
				{
					file.HasHeader(index);
					file.HasData(index);
				}
				file.IsComplete();
			}
			catch (Exception exception) when (exception is NullReferenceException ||
				exception is ArgumentOutOfRangeException || exception is InvalidOperationException)
			{
				throw new InvalidDataException("Recovery file has invalid internal block state: " + Path.GetFileName(filename), exception);
			}
		}

		void MoveRestoreFile(string source, string destination)
		{
			var failure = RestoreMoveFailureFactory == null ? null : RestoreMoveFailureFactory(source, destination);
			if (failure != null) throw failure;
			File.Move(source, destination);
		}

		string ValidateBackupPath(string backupDirectory)
		{
			if (string.IsNullOrWhiteSpace(backupDirectory))
				throw new ArgumentException("A recovery backup must be selected.", nameof(backupDirectory));
			var recoveryRoot = Path.GetFullPath(recoveryStateDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var backupPath = Path.GetFullPath(backupDirectory).TrimEnd(Path.DirectorySeparatorChar);
			var parent = Directory.GetParent(backupPath);
			if (!backupPath.StartsWith(recoveryRoot, StringComparison.OrdinalIgnoreCase) ||
				parent == null || !string.Equals(
					parent.FullName.TrimEnd(Path.DirectorySeparatorChar),
					recoveryRoot.TrimEnd(Path.DirectorySeparatorChar),
					StringComparison.OrdinalIgnoreCase) ||
				!Path.GetFileName(backupPath).StartsWith("reset-backup-", StringComparison.OrdinalIgnoreCase) ||
				!Directory.Exists(backupPath))
				throw new InvalidOperationException("The selected recovery backup is not valid.");
			return backupPath;
		}

		public void ResetRecoveredFiles()
		{
			StopListening();
			ArchiveSavedRecoveryState();
			files.Clear();
			lock (fileUpdateLock) pendingFileUpdates.Clear();
			currentFileUID = null;
			recoveryHeader = null;
			currentBlockDataStart = -1;
			blockHandler.ResetBlock();
			toneHandler.ResetWaitForTone();
			ClearDiagnosticSnapshots();
			HasUnsavedRecoveryState = false;
		}

		void ArchiveSavedRecoveryState()
		{
			var savedFiles = Directory.Exists(recoveryStateDirectory)
				? Directory.GetFiles(recoveryStateDirectory, "*.xml")
				: new string[0];
			var manifestPath = Path.Combine(recoveryStateDirectory, "recovery-manifest.json");
			if (savedFiles.Length == 0 && !File.Exists(manifestPath)) return;
			var backup = Path.Combine(recoveryStateDirectory, "reset-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
			var staging = Path.Combine(recoveryStateDirectory, ".reset-staging-" + Guid.NewGuid().ToString("N"));
			string generationPath = null;
			try
			{
				Directory.CreateDirectory(staging);
				if (File.Exists(manifestPath))
				{
					var manifest = ReadRecoveryManifest(manifestPath);
					if (manifest == null || string.IsNullOrWhiteSpace(manifest.generation) ||
						manifest.generation != Path.GetFileName(manifest.generation))
						throw new InvalidDataException("The active recovery manifest is invalid.");
					generationPath = Path.GetFullPath(Path.Combine(recoveryStateDirectory, manifest.generation));
					var recoveryRoot = Path.GetFullPath(recoveryStateDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
					if (!generationPath.StartsWith(recoveryRoot, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException("The active recovery manifest points outside the recovery directory.");
					if (Directory.Exists(generationPath))
						CopyDirectory(generationPath, Path.Combine(staging, manifest.generation));
					File.Copy(manifestPath, Path.Combine(staging, "recovery-manifest.json"));
				}
				foreach (var filename in savedFiles)
				{
					File.Copy(filename, Path.Combine(staging, Path.GetFileName(filename)));
				}
				AtomicFile.Write(Path.Combine(staging, ".complete"), stream => { });
				Directory.Move(staging, backup);

				if (File.Exists(manifestPath)) File.Delete(manifestPath);
				foreach (var filename in savedFiles)
					if (File.Exists(filename)) File.Delete(filename);
				if (generationPath != null) TryDeleteDirectory(generationPath);
			}
			catch
			{
				TryDeleteDirectory(staging);
				throw;
			}
		}

		static void CopyDirectory(string source, string destination)
		{
			Directory.CreateDirectory(destination);
			foreach (var filename in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
				File.Copy(filename, Path.Combine(destination, Path.GetFileName(filename)));
			foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
			{
				CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
			}
		}

		void ClearDiagnosticSnapshots()
		{
			lock (diagnosticStoreLock)
			{
				foreach (var path in diagnosticFiles.Values)
				{
					try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
				}
				diagnosticFiles.Clear();
				selectableDiagnosticIds.Clear();
			}
		}

		public int ExportRawFiles(string directory)
		{
			if (directory == null)
				throw new ArgumentNullException(nameof(directory));
			Directory.CreateDirectory(directory);
			var usedPaths = new HashSet<string>(
				Directory.EnumerateFiles(directory).Select(Path.GetFileName),
				StringComparer.OrdinalIgnoreCase);
			var exported = 0;
			foreach (var pair in files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
			{
				if (!pair.Value.IsComplete()) continue;
				var fileName = MakeSafeExportName(pair.Key);
				var uniqueName = fileName;
				var suffix = 2;
				while (usedPaths.Contains(uniqueName) || usedPaths.Contains(uniqueName + ".inf"))
					uniqueName = fileName + "_" + suffix++;
				usedPaths.Add(uniqueName);
				usedPaths.Add(uniqueName + ".inf");
				pair.Value.ExportTo(Path.Combine(directory, uniqueName));
				exported++;
			}
			return exported;
		}

		public DiskImageExportResult ExportDiskImage(string path, DiskImageFormat format)
		{
			return DiskImageExporter.Write(path, format, files);
		}

		public DiskImageExportResult ExportDiskImage(string path, DiskImageFormat format, IReadOnlyDictionary<string, string> names, ISet<string> excluded)
		{
			return DiskImageExporter.Write(path, format, files, names, excluded);
		}

		internal static string MakeSafeExportName(string name)
		{
			if (string.IsNullOrEmpty(name)) return "file";
			var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
			var result = new StringBuilder();
			foreach (var character in name)
			{
				if (invalid.Contains(character) || char.IsControl(character))
					result.Append("_" + ((int)character).ToString("X2"));
				else
					result.Append(character);
			}
			while (result.Length > 0 && (result[result.Length - 1] == '.' || result[result.Length - 1] == ' '))
			{
				var trailing = result[result.Length - 1];
				result.Length--;
				result.Append("_" + ((int)trailing).ToString("X2"));
			}
			var safeName = result.Length == 0 ? "file" : result.ToString();
			var stem = safeName.Split('.')[0];
			var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
			if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) safeName = "_" + safeName;
			return safeName;
		}

		public void Save()
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog();
			saveFileDialog.InitialDirectory = @"C:\";
			saveFileDialog.Title = "Save CSW File";
			saveFileDialog.CheckFileExists = false;
			saveFileDialog.CheckPathExists = true;
			saveFileDialog.DefaultExt = "csw";
			saveFileDialog.Filter = "CSW files (*.csw)|*.csw|All files (*.*)|*.*";
			saveFileDialog.FilterIndex = 0;
			saveFileDialog.RestoreDirectory = false;
			if (saveFileDialog.ShowDialog() == DialogResult.OK)
			{
				var binaryString = new StringBuilder();
				foreach (var f in files)
				{
					binaryString.Append(f.Value.GetBinaryString());
				}
				var countOnes = binaryString.ToString().Count(f => f == '1');
				var halfPulses = countOnes * 4 + (binaryString.Length - countOnes) * 2;
				var bytes = new List<byte>(halfPulses);

				writeRLEBinaryData(binaryString, bytes);
				AtomicFile.Write(saveFileDialog.FileName, stream =>
				{
					using (var bw = new BinaryWriter(stream, Encoding.UTF8, true))
					{
						bw.Write("Compressed Square Wave".ToCharArray());
						bw.Write((byte)0x1A);
						bw.Write((byte)0x02);
						bw.Write((byte)0x00);
						bw.Write(44100);
						bw.Write(halfPulses);
						bw.Write((byte)0x01);
						bw.Write((byte)0x00);
						bw.Write((byte)0x00);
						bw.Write("RokCoderConverts".ToCharArray());
					}
					using (var zip = new ZlibStream(stream, CompressionMode.Compress, CompressionLevel.Level9, true))
						zip.Write(bytes.ToArray(), 0, bytes.Count);
				});
			}
		}

		private void writeRLEBinaryData(StringBuilder binaryString, List<byte> bytes)
		{
			var s = binaryString.ToString();
			foreach (var b in s)
			{
				if (b == '1')
				{
					bytes.Add((byte)9);
					bytes.Add((byte)9);
					bytes.Add((byte)9);
					bytes.Add((byte)9);
				}
				else
				{
					bytes.Add((byte)18);
					bytes.Add((byte)18);
				}
			}
		}
	}
}
