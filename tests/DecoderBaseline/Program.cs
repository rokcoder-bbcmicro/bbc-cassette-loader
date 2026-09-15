using bbc_cassette_loader;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DecoderBaseline
{
	static class Program
	{
		static int failures;
		static int testsRun;
		class RecordingResult
		{
			public int Bytes;
			public readonly List<string> Headers = new List<string>();
			public int DataBlocks;
			public int Errors;
			public int RepairedBlocks;
			public int RepairedBits;
			public int RepairedHeaders;
			public int RepairedDataBlocks;
			public string ValidPayloadStreamSha256;
		}

		static int Main(string[] args)
		{
			if (args.Length == 2 && args[0] == "--probe")
				return ProbeRecording(args[1]);
			if ((args.Length == 2 || args.Length == 3) && args[0] == "--compare-crc-repair")
				return CompareCrcRepair(args[1], args.Length == 3 ? args[2] : null);
			if (args.Length == 2 && args[0] == "--compare-recovery")
				return CompareRecovery(args[1]);
			if (args.Length == 2 && args[0] == "--compare-targeted-recovery")
				return CompareTargetedRecovery(args[1]);
			if (args.Length == 2 && args[0] == "--sweep-recovery-profiles")
				return SweepRecoveryProfiles(args[1]);
			if (args.Length == 2 && args[0] == "--compare-expanded-recovery")
				return CompareExpandedRecovery(args[1]);
			if (args.Length == 2 && args[0] == "--probe-recovery")
				return ProbeRecovery(args[1]);
			if (args.Length == 2 && args[0] == "--probe-full-recovery")
				return ProbeRecovery(args[1], false);

			Run("valid block passes CRC and emits header/data", ValidBlockPasses);
			Run("CRC matches the fixed CRC-16/XMODEM reference vector", CrcMatchesReferenceVector);
			Run("corrupt header fails CRC", CorruptHeaderFails);
			Run("corrupt data fails CRC", CorruptDataFails);
			Run("empty payload consumes and validates its data CRC", EmptyPayloadValidatesDataCrc);
			Run("data block length is limited to 256 bytes", DataBlockLengthIsLimited);
			Run("cassette filename boundaries are enforced", FilenameBoundariesAreEnforced);
			Run("cassette filenames are independent of host filename rules", CassetteFilenamesAreIndependentOfHostFilenameRules);
			Run("only block flag bit 7 completes a file", FinalBlockFlagIsInterpreted);
			Run("conflicting final-block positions do not merge", ConflictingFinalBlockPositionsDoNotMerge);
			Run("maximum 16-bit final block number remains representable", MaximumFinalBlockNumberIsRepresentable);
			Run("file state retains its final-block count after persistence", FileStateRetainsFinalBlockCount);
			Run("legacy unknown block count remains unknown after recovery", LegacyUnknownBlockCountRemainsUnknown);
			Run("final block produces a complete BBC file", FinalBlockCompletesFile);
			Run("compatible blocks merge across inputs", CompatibleBlocksMergeAcrossInputs);
			Run("repeated conflicting data reuses its alternate file", RepeatedConflictReusesAlternateFile);
			Run("synthetic 1200/2400 Hz waveform decodes framed bytes", SyntheticWaveformDecodes);
			Run("phase-shifted waveform preserves recovery and runs the alternate profile", PhaseShiftedWaveformRecovers);
			Run("documented five-second carrier acquires the decoder", DocumentedCarrierDecodes);
			Run("low-level waveform decodes through a moving baseline", MovingBaselineWaveformDecodes);
			Run("synthetic decoding tolerates polarity, noise, and speed variation", SyntheticSignalVariationsDecode);
			Run("invalid start bit does not emit a stale byte", InvalidStartBitDoesNotEmitByte);
			Run("invalid stop bit does not emit a partial byte", InvalidStopBitDoesNotEmitByte);
			Run("diagnostic windows clamp safely at input boundaries", DiagnosticWindowsClampAtBoundaries);
			Run("diagnostics preserve post-error bit and byte context", DiagnosticContinuationPreservesMarkers);
			Run("diagnostic retention remains bounded on long input", DiagnosticRetentionRemainsBounded);
			Run("file decoding publishes a useful block error", FileDecodingPublishesBlockError);
			Run("synthetic WAV formats match their declared headers", SampleFormatsMatch);
			Run("stereo selection and resampling produce mono 48 kHz samples", InputNormalizationWorks);
			Run("invalid decoder sample rates are rejected", InvalidDecoderSampleRatesAreRejected);
			Run("live PCM conversion honors the recorded byte count", LivePcmConversionHonorsByteCount);
			Run("line-in WAV recording finalizes a valid PCM file", LineInRecordingFinalizesValidPcm);
			Run("line-in sessions stop, dispose, and restart", LineInSessionsStopDisposeAndRestart);
			Run("failed line-in start releases devices and permits retry", FailedLineInStartReleasesDevices);
			Run("selected line-in device reaches capture factory", SelectedLineInDeviceReachesFactory);
			Run("unique low-confidence header and data repairs pass CRC", UniqueLowConfidenceRepairsPassCrc);
			Run("handler forwards detailed CRC repair information", HandlerForwardsDetailedCrcRepairInformation);
			Run("synthetic recording decodes through the file path", CompatibleRecordingMatches);
			Run("44.1 kHz synthetic float recording decodes through conversion", FloatRecordingMatches);
			Run("concurrent file requests are decoded sequentially", ConcurrentFileRequestsAreSerialized);
			Run("active file import can be cancelled", ActiveFileImportCanBeCancelled);
			Run("new input discards an unfinished block", NewInputDiscardsUnfinishedBlock);
			Run("recovery passes preserve valid data and original diagnostics", RecoveryPreservesDataAndDiagnostics);
			Run("targeted recovery skips validated block payloads", TargetedRecoverySkipsValidatedPayloads);
			Run("recovered blocks clear earlier UI fault markers", RecoveredBlocksClearUiFaultMarkers);
			Run("recovery does not republish unchanged blocks", RecoveryDoesNotRepublishUnchangedBlocks);
			Run("recovery passes cancel and release their settings and gate", RecoveryCancellationResetsSettings);
			Run("file imports report progress for every recovery pass", FileImportsReportProgress);
			Run("raw export omits CRC bytes and writes BBC metadata", RawExportOmitsCrcAndWritesMetadata);
			Run("raw export rejects incomplete and inconsistent recovery state", RawExportRejectsInvalidState);
			Run("raw export makes host filenames safe and unique", RawExportNamesAreSafeAndUnique);
			Run("new cassette reset archives saved recovery state", NewCassetteResetArchivesState);
			Run("recovery backup restore replaces state safely", RecoveryBackupRestoreReplacesStateSafely);
			Run("closing prompts for unsaved recovery state", ClosingPromptsForUnsavedRecoveryState);
			Run("recovery state save rolls back on a failed file", RecoveryStateSaveRollsBackOnFailure);
			Run("malformed recovery state produces a warning", MalformedRecoveryStateProducesWarning);
			Run("disk image export writes DFS and ADFS layouts", DiskImageExportWritesLayouts);
			Run("block values provide consistent equality and hashes", BlockValuesProvideConsistentEquality);
			Run("main window exposes the focused recovery workflow", MainWindowExposesFocusedWorkflow);
			Run("main window exposes opt-in CRC repair", MainWindowExposesCrcRepairOption);
			Run("update checker handles release results without network access", UpdateCheckerHandlesReleaseResults);

			Console.WriteLine("{0} passed; {1} failed", testsRun - failures, failures);
			return failures == 0 ? 0 : 1;
		}

		static void UpdateCheckerHandlesReleaseResults()
		{
			HttpRequestMessage request;
			var newer = CheckUpdate(
				"{\"tag_name\":\"v1.2.3\",\"html_url\":\"https://github.com/rokcoder-bbcmicro/bbc-cassette-loader/releases/tag/v1.2.3\"}",
				HttpStatusCode.OK, new Version(1, 0, 0, 0), out request);
			Assert(newer.Status == UpdateCheckStatus.NewerAvailable && newer.LatestVersion == new Version(1, 2, 3) &&
				newer.ReleaseUrl.EndsWith("/v1.2.3", StringComparison.Ordinal), "newer GitHub release was not recognized");
			Assert(request.RequestUri.ToString() == UpdateChecker.LatestReleaseUrl && request.Headers.UserAgent.Any(),
				"update request did not use the canonical endpoint and user agent");

			var current = CheckUpdate(
				"{\"tag_name\":\"1.0.0+build.4\",\"html_url\":\"https://github.com/rokcoder-bbcmicro/bbc-cassette-loader/releases/tag/1.0.0\"}",
				HttpStatusCode.OK, new Version(1, 0, 0, 0), out request);
			Assert(current.Status == UpdateCheckStatus.UpToDate, "equal semantic release was reported as newer");

			var older = CheckUpdate(
				"{\"tag_name\":\"v0.9.9\",\"html_url\":\"https://github.com/rokcoder-bbcmicro/bbc-cassette-loader/releases/tag/v0.9.9\"}",
				HttpStatusCode.OK, new Version(1, 0, 0, 0), out request);
			Assert(older.Status == UpdateCheckStatus.UpToDate, "older published release was reported as newer");

			var prerelease = CheckUpdate(
				"{\"tag_name\":\"v2.0.0-beta.1\",\"html_url\":\"https://github.com/rokcoder-bbcmicro/bbc-cassette-loader/releases/tag/v2.0.0-beta.1\"}",
				HttpStatusCode.OK, new Version(1, 0, 0, 0), out request);
			Assert(prerelease.Status == UpdateCheckStatus.InvalidResponse, "pre-release tag was not excluded from the stable check");

			var badUrl = CheckUpdate(
				"{\"tag_name\":\"v2.0.0\",\"html_url\":\"http://example.com/release\"}",
				HttpStatusCode.OK, new Version(1, 0, 0, 0), out request);
			Assert(badUrl.Status == UpdateCheckStatus.InvalidResponse, "non-canonical release URL was accepted");

			Assert(CheckUpdate("{}", HttpStatusCode.NotFound, new Version(1, 0, 0), out request).Status == UpdateCheckStatus.NoRelease,
				"missing GitHub release was not reported");
			Assert(CheckUpdate("{}", HttpStatusCode.Forbidden, new Version(1, 0, 0), out request).Status == UpdateCheckStatus.RateLimited,
				"GitHub rate limiting was not reported");
			Assert(CheckUpdate("{}", (HttpStatusCode)429, new Version(1, 0, 0), out request).Status == UpdateCheckStatus.RateLimited,
				"HTTP 429 was not reported as rate limiting");
			Assert(CheckUpdate("not json", HttpStatusCode.OK, new Version(1, 0, 0), out request).Status == UpdateCheckStatus.InvalidResponse,
				"malformed GitHub JSON was not rejected");

			var networkFailure = new StubHttpMessageHandler { Exception = new HttpRequestException("offline") };
			using (var client = UpdateChecker.CreateClient("Test/1.0", networkFailure))
			{
				var result = UpdateChecker.CheckAsync(new Version(1, 0, 0), client, CancellationToken.None).GetAwaiter().GetResult();
				Assert(result.Status == UpdateCheckStatus.Unavailable, "network failure was not reported as unavailable");
			}

			var cancellation = new CancellationTokenSource();
			var cancelledHandler = new StubHttpMessageHandler { WaitForCancellation = true };
			using (var client = UpdateChecker.CreateClient("Test/1.0", cancelledHandler))
			{
				var check = UpdateChecker.CheckAsync(new Version(1, 0, 0), client, cancellation.Token);
				cancellation.Cancel();
				AssertThrows<OperationCanceledException>(() => check.GetAwaiter().GetResult());
			}
			cancellation.Dispose();
		}

		static UpdateCheckResult CheckUpdate(string body, HttpStatusCode status, Version currentVersion, out HttpRequestMessage request)
		{
			var handler = new StubHttpMessageHandler { Body = body, StatusCode = status };
			using (var client = UpdateChecker.CreateClient("Test/1.0", handler))
			{
				var result = UpdateChecker.CheckAsync(currentVersion, client, CancellationToken.None).GetAwaiter().GetResult();
				request = handler.LastRequest;
				return result;
			}
		}

		sealed class StubHttpMessageHandler : HttpMessageHandler
		{
			public string Body = "{}";
			public HttpStatusCode StatusCode = HttpStatusCode.OK;
			public Exception Exception;
			public bool WaitForCancellation;
			public HttpRequestMessage LastRequest;

			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				LastRequest = request;
				if (Exception != null) throw Exception;
				if (WaitForCancellation)
					await Task.Delay(Timeout.Infinite, cancellationToken);
				return new HttpResponseMessage(StatusCode)
				{
					Content = new StringContent(Body, Encoding.UTF8, "application/json")
				};
			}
		}

		static void DiskImageExportWritesLayouts()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-disk-export-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				var files = new Dictionary<string, BBCFile>
				{
					{ "ONE", FileFromBlock(MakeBlock("ONE", new byte[] { 1, 2, 3 })) },
					{ "TWO", FileFromBlock(MakeBlock("TWO", new byte[] { 4, 5 })) }
				};
				var expectedImages = new Dictionary<DiskImageFormat, int>
				{
					{ DiskImageFormat.Dfs40Ssd, 40 * 10 * 256 },
					{ DiskImageFormat.Dfs80Ssd, 80 * 10 * 256 },
					{ DiskImageFormat.Dfs40Dsd, 2 * 40 * 10 * 256 },
					{ DiskImageFormat.Dfs80Dsd, 2 * 80 * 10 * 256 },
					{ DiskImageFormat.AdfsS, 40 * 16 * 256 },
					{ DiskImageFormat.AdfsM, 80 * 16 * 256 },
					{ DiskImageFormat.AdfsL, 2 * 80 * 16 * 256 }
				};
				foreach (var expected in expectedImages)
				{
					var output = Path.Combine(directory, expected.Key + ".img");
					DiskImageExporter.Write(output, expected.Key, files);
					Assert(new FileInfo(output).Length == expected.Value, expected.Key + " image has the wrong size");
				}
				var dfs40 = File.ReadAllBytes(Path.Combine(directory, DiskImageFormat.Dfs40Ssd + ".img"));
				Assert(Encoding.ASCII.GetString(dfs40, 8, 7).TrimEnd('\0', ' ') == "TWO",
					"DFS catalogue entries are not ordered by descending start sector");
				Assert(dfs40[11] == (byte)' ' && dfs40[12] == (byte)' ' && dfs40[13] == (byte)' ' &&
					dfs40[14] == (byte)' ', "DFS short filenames are not space padded");
				Assert(dfs40[0x105] == 16, "DFS catalogue entry count is wrong");
				Assert(dfs40[0x106] == 1 && dfs40[0x107] == 0x90, "DFS sector count is wrong");
				var adfsBytes = File.ReadAllBytes(Path.Combine(directory, DiskImageFormat.AdfsS + ".img"));
				Assert(adfsBytes[0x201] == (byte)'H' && adfsBytes[0x204] == (byte)'o', "ADFS root signature is missing");
				Assert((adfsBytes[0x205] & 0x80) != 0 && (adfsBytes[0x206] & 0x80) != 0,
					"ADFS file read/write attributes are missing");
				Assert(adfsBytes[0x208] == (byte)' ' && adfsBytes[0x209] == (byte)' ' &&
					adfsBytes[0x20A] == (byte)' ' && adfsBytes[0x20B] == (byte)' ' &&
					adfsBytes[0x20C] == (byte)' ' && adfsBytes[0x20D] == (byte)' ' &&
					adfsBytes[0x20E] == (byte)' ', "ADFS short filenames are not space padded");
				Assert(adfsBytes[0x0FC] == 0x80 && adfsBytes[0x0FD] == 0x02 && adfsBytes[0x0FE] == 0,
					"ADFS sector count is wrong");
				Assert(adfsBytes[7 * 256] == 1 && adfsBytes[7 * 256 + 2] == 3, "ADFS payload was not allocated after the root directory");
				var invalidDfs = new Dictionary<string, BBCFile>
				{
					{ "TOOLONG8", FileFromBlock(MakeBlock("TOOLONG8", new byte[] { 1 })) }
				};
				var rejected = false;
				try { DiskImageExporter.Write(Path.Combine(directory, "invalid.ssd"), DiskImageFormat.Dfs40Ssd, invalidDfs); }
				catch (InvalidOperationException) { rejected = true; }
				Assert(rejected && !File.Exists(Path.Combine(directory, "invalid.ssd")),
					"incompatible DFS names must be rejected before writing");
				var selected = new Dictionary<string, string> { { "ONE", "RENAMED" }, { "TWO", "SKIPPED" } };
				var omitted = new HashSet<string> { "TWO" };
				var selectedOutput = Path.Combine(directory, "selected.ssd");
				var selectedResult = DiskImageExporter.Write(selectedOutput, DiskImageFormat.Dfs40Ssd, files, selected, omitted);
				Assert(selectedResult.files == 1 && Encoding.ASCII.GetString(File.ReadAllBytes(selectedOutput), 8, 7).TrimEnd('\0', ' ') == "RENAMED",
					"disk image export did not apply the rename and omission plan");
				Assert(DiskImageExporter.GetNameValidationError(DiskImageFormat.Dfs40Ssd, "TOOLONG8") != null &&
					DiskImageExporter.GetNameValidationError(DiskImageFormat.AdfsS, "TOOLONGNAME1") != null &&
					DiskImageExporter.GetNameValidationError(DiskImageFormat.Dfs40Ssd, "VALID") == null,
					"disk image name validation does not match the selected format");
			}
			finally
			{
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		static void NewCassetteResetArchivesState()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-reset-" + Guid.NewGuid().ToString("N"));
			BBCFileHandler handler = null;
			BBCFileHandler reader = null;
			try
			{
				handler = new BBCFileHandler(directory, false);
				handler.files.Add("ONE", FileFromBlock(MakeBlock("ONE", new byte[] { 1, 2, 3 })));
				handler.Serialise();
			Assert(!handler.HasUnsavedRecoveryState && File.Exists(Path.Combine(directory, "ONE.xml")), "saved recovery state was not created");
				handler.ResetRecoveredFiles();
			Assert(handler.files.Count == 0 && !File.Exists(Path.Combine(directory, "ONE.xml")), "reset did not clear the active recovery state");
			Assert(Directory.GetDirectories(directory, "reset-backup-*").Length == 1, "reset did not create exactly one recovery backup");
			reader = new BBCFileHandler(directory, false);
			reader.Deserialise();
			Assert(reader.files.Count == 0, "archived recovery state was reloaded as active data");
			}
			finally
			{
				if (handler != null) handler.FormClosing();
				if (reader != null) reader.FormClosing();
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		static void RecoveryBackupRestoreReplacesStateSafely()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-restore-backup-" + Guid.NewGuid().ToString("N"));
			BBCFileHandler handler = null;
			try
			{
				handler = new BBCFileHandler(directory, false);
				handler.files.Add("ONE", FileFromBlock(MakeBlock("ONE", new byte[] { 1, 2, 3 })));
				handler.Serialise();
				handler.ResetRecoveredFiles();
				var backups = handler.GetResetBackups();
				Assert(backups.Count == 1 && backups[0].FileCount == 1, "reset backup was not discoverable");
				var backupPath = backups[0].DirectoryPath;
				var originalBackup = File.ReadAllBytes(Path.Combine(backupPath, "ONE.xml"));

				handler.files.Add("TWO", FileFromBlock(MakeBlock("TWO", new byte[] { 4, 5 })));
				handler.Serialise();
				var activeBeforeSwapFailure = File.ReadAllBytes(Path.Combine(directory, "TWO.xml"));
				var moveCount = 0;
				handler.RestoreMoveFailureFactory = (source, destination) =>
					++moveCount == 2 ? new IOException("forced restore move failure") : null;
				AssertThrows<IOException>(() => handler.RestoreRecoveryBackup(backupPath));
				Assert(File.ReadAllBytes(Path.Combine(directory, "TWO.xml")).SequenceEqual(activeBeforeSwapFailure) &&
					handler.files.ContainsKey("TWO") && !handler.files.ContainsKey("ONE"),
					"failed restore did not roll back the active state");
				handler.RestoreMoveFailureFactory = null;
				handler.RestoreRecoveryBackup(backupPath);

				Assert(handler.files.Count == 1 && handler.files.ContainsKey("ONE") && !handler.files.ContainsKey("TWO"),
					"restore did not replace the active recovery session");
				Assert(File.ReadAllBytes(Path.Combine(backupPath, "ONE.xml")).SequenceEqual(originalBackup),
					"restore modified the selected backup");
				Assert(File.Exists(Path.Combine(directory, "ONE.xml")) && !File.Exists(Path.Combine(directory, "TWO.xml")),
					"restore did not replace active recovery files");
				var safetyBackups = Directory.GetDirectories(directory, "restore-backup-*");
				Assert(safetyBackups.Any(path => File.Exists(Path.Combine(path, "TWO.xml"))),
					"restore did not preserve the previous active state");

				var invalidBackup = Path.Combine(directory, "reset-backup-invalid");
				Directory.CreateDirectory(invalidBackup);
				File.WriteAllText(Path.Combine(invalidBackup, "BROKEN.xml"), "{}");
				var activeBeforeFailure = File.ReadAllBytes(Path.Combine(directory, "ONE.xml"));
				AssertThrows<InvalidDataException>(() => handler.RestoreRecoveryBackup(invalidBackup));
				Assert(File.ReadAllBytes(Path.Combine(directory, "ONE.xml")).SequenceEqual(activeBeforeFailure),
					"invalid restore changed active recovery files");
				var structuralBackup = Path.Combine(directory, "reset-backup-structural");
				Directory.CreateDirectory(structuralBackup);
				File.WriteAllText(Path.Combine(structuralBackup, "BROKEN.xml"),
					"{\"filename\":\"BROKEN\",\"totalBlocks\":0,\"totalBlocksKnown\":true,\"blocks\":null,\"currentBlock\":null}");
				AssertThrows<InvalidDataException>(() => handler.RestoreRecoveryBackup(structuralBackup));
				Assert(File.ReadAllBytes(Path.Combine(directory, "ONE.xml")).SequenceEqual(activeBeforeFailure),
					"structurally invalid restore changed active recovery files");
				var emptyBackup = Path.Combine(directory, "reset-backup-empty");
				Directory.CreateDirectory(emptyBackup);
				AssertThrows<InvalidDataException>(() => handler.RestoreRecoveryBackup(emptyBackup));
				Assert(File.ReadAllBytes(Path.Combine(directory, "ONE.xml")).SequenceEqual(activeBeforeFailure),
					"empty restore changed active recovery files");
				var nestedBackup = Path.Combine(directory, "nested", "reset-backup-nested");
				Directory.CreateDirectory(nestedBackup);
				AssertThrows<InvalidOperationException>(() => handler.RestoreRecoveryBackup(nestedBackup));
			}
			finally
			{
				if (handler != null) handler.FormClosing();
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		static void ClosingPromptsForUnsavedRecoveryState()
		{
			CloseWithChoice(DialogResult.Cancel, expectCancelled: true, expectSaved: false);
			CloseWithChoice(DialogResult.No, expectCancelled: false, expectSaved: false);
			CloseWithChoice(DialogResult.Yes, expectCancelled: false, expectSaved: true);
		}

		static void CloseWithChoice(DialogResult choice, bool expectCancelled, bool expectSaved)
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-close-prompt-" + Guid.NewGuid().ToString("N"));
			BBCFileHandler handler = null;
			var handlerClosed = false;
			try
			{
				handler = new BBCFileHandler(directory, false);
				handler.files.Add("ONE", FileFromBlock(MakeBlock("ONE", new byte[] { 1, 2, 3 })));
				handler.Serialise();
				var dirtyProperty = typeof(BBCFileHandler).GetProperty("HasUnsavedRecoveryState", BindingFlags.Instance | BindingFlags.Public);
				dirtyProperty.GetSetMethod(true).Invoke(handler, new object[] { true });

				using (var form = new MainForm())
				{
					var handlerField = typeof(MainForm).GetField("fileHandler", BindingFlags.Instance | BindingFlags.NonPublic);
					handlerField.SetValue(form, handler);
					form.CloseRecoveryPromptOverride = () => choice;
					var closing = typeof(MainForm).GetMethod("MainForm_FormClosing", BindingFlags.Instance | BindingFlags.NonPublic);
					var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
					closing.Invoke(form, new object[] { form, args });
					handlerClosed = !args.Cancel;
					Assert(args.Cancel == expectCancelled, "close choice " + choice + " produced the wrong cancellation result");
					Assert(handler.HasUnsavedRecoveryState == !expectSaved, "close choice " + choice + " produced the wrong dirty-state result");
					Assert(File.Exists(Path.Combine(directory, "ONE.xml")), "close choice " + choice + " removed the saved recovery state");
				}
			}
			finally
			{
				if (handler != null && !handlerClosed) handler.FormClosing();
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		static void RecoveryStateSaveRollsBackOnFailure()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-save-rollback-" + Guid.NewGuid().ToString("N"));
			BBCFileHandler handler = null;
			try
			{
				handler = new BBCFileHandler(directory, false);
				handler.files.Add("GOOD", FileFromBlock(MakeBlock("GOOD", new byte[] { 1 })));
				handler.Serialise();
				var original = File.ReadAllBytes(Path.Combine(directory, "GOOD.xml"));
				handler.files["GOOD"] = FileFromBlock(MakeBlock("GOOD", new byte[] { 2 }));
				handler.files.Add("BROKEN\\FILE", FileFromBlock(MakeBlock("BROKEN", new byte[] { 3 })));
				handler.Serialise();

				Assert(!Directory.Exists(Path.Combine(directory, "BROKEN")),
					"a recovery identity escaped the state directory");
				Assert(!File.ReadAllBytes(Path.Combine(directory, "GOOD.xml")).SequenceEqual(original),
					"the recovery save did not publish the updated mirror");
				var reader = new BBCFileHandler(directory, false);
				reader.Deserialise();
				Assert(reader.files.ContainsKey("BROKEN\\FILE"),
					"the journal did not preserve the original recovery identity");
				reader.FormClosing();
			}
			finally
			{
				if (handler != null) handler.FormClosing();
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		static void MalformedRecoveryStateProducesWarning()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-malformed-recovery-" + Guid.NewGuid().ToString("N"));
			BBCFileHandler handler = null;
			try
			{
				Directory.CreateDirectory(directory);
				File.WriteAllText(Path.Combine(directory, "BROKEN.xml"), "not valid json");
				handler = new BBCFileHandler(directory, false);
				var result = handler.Deserialise();
				Assert(result != null && result.Warnings.Count == 1 && handler.files.Count == 0,
					"malformed recovery state did not produce a nonfatal warning");
			}
			finally
			{
				if (handler != null) handler.FormClosing();
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		static void BlockValuesProvideConsistentEquality()
		{
			var firstHeader = HeaderFromBlock(MakeBlock("EQUAL", new byte[] { 1, 2, 3 }));
			var equalHeader = HeaderFromBlock(MakeBlock("EQUAL", new byte[] { 1, 2, 3 }));
			var differentHeader = HeaderFromBlock(MakeBlock("EQUAL", new byte[] { 1, 2, 3 }, blockNumber: 1));
			Assert(firstHeader == equalHeader && firstHeader.Equals((object)equalHeader), "equal headers disagree across equality paths");
			Assert(firstHeader.GetHashCode() == equalHeader.GetHashCode(), "equal headers have different hashes");
			Assert(firstHeader != differentHeader, "different headers compare equal");

			var firstData = new BlockData(new byte[] { 4, 5, 6 });
			var equalData = new BlockData(new byte[] { 4, 5, 6 });
			var differentData = new BlockData(new byte[] { 4, 5, 7 });
			Assert(firstData == equalData && firstData.Equals((object)equalData), "equal data blocks disagree across equality paths");
			Assert(firstData.GetHashCode() == equalData.GetHashCode(), "equal data blocks have different hashes");
			Assert(firstData != differentData, "different data blocks compare equal");
			Assert(default(BlockHeader) == default(BlockHeader) && default(BlockData) == default(BlockData),
				"default block values must compare safely");
		}

		static void MainWindowExposesFocusedWorkflow()
		{
			Exception failure = null;
			var recoveryDirectory = Path.Combine(
				Path.GetTempPath(), "bbc-ui-recovery-" + Guid.NewGuid().ToString("N"));
			var thread = new Thread(() =>
			{
				try
				{
					using (var form = new MainForm(recoveryDirectory))
					{
						Assert(form.MinimumSize.Width == 640, "The command row must remain usable at the compact window width.");
						form.ClientSize = new System.Drawing.Size(640, 480);
						form.PerformLayout();
						var import = FindControl(form, "buttonTest") as Button;
						var lineIn = FindControl(form, "buttonListen") as Button;
						var options = FindControl(form, "importOptionsButton") as Button;
						var exportButton = FindControl(form, "export") as Button;
						var cancel = FindControl(form, "buttonCancelImport") as Button;
						var list = FindControl(form, "fileListBox") as ListBox;
						var blockMap = FindControl(form, "blockMapPanel") as Panel;
						var commandBar = FindControl(form, "commandBar");
						var rootLayout = FindControl(form, "rootLayout") as TableLayoutPanel;
						var content = FindControl(form, "contentSplitContainer") as SplitContainer;
						var statusStrip = FindControl(form, "statusStrip") as StatusStrip;
						var importProgress = statusStrip == null ? null : statusStrip.Items["importProgressBar"] as ToolStripProgressBar;
						var statusSpacer = statusStrip == null ? null : statusStrip.Items["statusSpacer"] as ToolStripStatusLabel;
						var activity = statusStrip == null ? null : statusStrip.Items["activityStatusLabel"] as ToolStripStatusLabel;
						var fileMenu = form.MainMenuStrip == null ? null : form.MainMenuStrip.Items["fileToolStripMenuItem"] as ToolStripMenuItem;
						var newCassette = fileMenu == null ? null : fileMenu.DropDownItems["newToolStripMenuItem"];
						var inputMenu = form.MainMenuStrip == null ? null : form.MainMenuStrip.Items["inputMenu"] as ToolStripMenuItem;
						var exportMenu = form.MainMenuStrip == null ? null : form.MainMenuStrip.Items["exportTopMenu"] as ToolStripMenuItem;
						var helpMenu = form.MainMenuStrip == null ? null : form.MainMenuStrip.Items["helpMenu"] as ToolStripMenuItem;
						Assert(import != null && import.Text.StartsWith("Import WAV"), "Import WAV must remain a visible primary action.");
						Assert(newCassette != null && newCassette.Text == "New cassette...", "New cassette reset must be available in the File menu.");
						Assert(fileMenu != null && fileMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Restore recovery backup..."),
							"Restore recovery backup must be available in the File menu.");
						Assert(inputMenu != null && inputMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Import WAV...") &&
							inputMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Listen to line-in") &&
							inputMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Record line-in to WAV...") &&
							inputMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Audio channel") &&
							inputMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Use recovery passes") &&
							inputMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Attempt conservative CRC repair") &&
							inputMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Capture device"),
							"Input menu must expose the primary input actions and individual options.");
						Assert(inputMenu.DropDownItems.Count > 3 && inputMenu.DropDownItems[1] is ToolStripSeparator &&
							inputMenu.DropDownItems[2].Text == "Listen to line-in" &&
							inputMenu.DropDownItems[3].Text == "Record line-in to WAV...",
							"Input menu must separate WAV import from the two line-in actions.");
						Assert(lineIn.ContextMenuStrip != null && lineIn.ContextMenuStrip.Items.Count == 2 &&
							lineIn.ContextMenuStrip.Items[0].Text == "Listen to line-in" &&
							lineIn.ContextMenuStrip.Items[1].Text == "Record line-in to WAV...",
							"Line-in command button must expose Listen and Record to WAV options.");
						Assert(exportMenu != null && exportMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Recovered files and .inf...") &&
							exportMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "CSW cassette image...") &&
							exportMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "BBC Micro disk image..."),
							"Export menu must expose all supported output actions.");
						var koFiSupport = helpMenu == null ? null : helpMenu.DropDownItems.Cast<ToolStripItem>()
							.FirstOrDefault(item => item.Text == "Support RokCoder on Ko-fi...");
						Assert(helpMenu != null && helpMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Local help") &&
							helpMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Project/GitHub") &&
							koFiSupport != null &&
							Equals(koFiSupport.Tag, MainForm.KoFiSupportUrl) &&
							MainForm.KoFiSupportUrl == "https://ko-fi.com/rokcoder" &&
							helpMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "Check for updates") &&
							helpMenu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text == "About"),
							"Help menu must expose local help, project access, the exact Ko-fi URL, update checking, and About.");
						using (var about = new AboutForm())
						{
							var aboutText = GetControlText(about);
							Assert(aboutText.Contains(Application.ProductVersion) && aboutText.Contains("GPLv3-only") &&
								aboutText.Contains("Copyright © 2026 Cliff Davies"),
								"About must show the current version, copyright, and licence.");
						}
						var setImportActive = typeof(MainForm).GetMethod("SetFileImportActive", BindingFlags.Instance | BindingFlags.NonPublic);
						var importCancellationField = typeof(MainForm).GetField("fileImportCancellation", BindingFlags.Instance | BindingFlags.NonPublic);
						var importCancellation = new CancellationTokenSource();
						importCancellationField.SetValue(form, importCancellation);
						setImportActive.Invoke(form, new object[] { true });
						var fileImport = fileMenu.DropDownItems.Cast<ToolStripItem>().FirstOrDefault(item => item.Text == "Import WAV...");
						Assert(fileImport != null && !fileImport.Enabled && inputMenu.DropDownItems.OfType<ToolStripMenuItem>().All(item => !item.Enabled) &&
							!exportMenu.Enabled && import.Text == "Cancel import" && import.Enabled && cancel != null && !cancel.Visible,
							"An active import must turn the Import WAV button into the visible cancellation action.");
						var importButtonClick = typeof(MainForm).GetMethod("buttonTest_Click", BindingFlags.Instance | BindingFlags.NonPublic);
						importButtonClick.Invoke(form, new object[] { import, EventArgs.Empty });
						Assert(importCancellation.IsCancellationRequested && !import.Enabled,
							"Cancel import must request cancellation and disable itself while cleanup runs.");
						importCancellationField.SetValue(form, null);
						importCancellation.Dispose();
						setImportActive.Invoke(form, new object[] { false });
						Assert(import.Text == "Import WAV..." && import.Enabled && cancel != null && !cancel.Visible,
							"Completing import cleanup must restore Import WAV and keep the separate cancel button hidden.");
						Assert(lineIn != null && lineIn.Text == "Line-in options  \u25BE", "Line-in options must remain visible.");
						Assert(lineIn.Width >= TextRenderer.MeasureText(lineIn.Text, lineIn.Font).Width + lineIn.Padding.Left + lineIn.Padding.Right,
							"Line-in options label must remain fully visible.");
						form.Show();
						Application.DoEvents();
						lineIn.PerformClick();
						Application.DoEvents();
						Assert(lineIn.ContextMenuStrip.Visible, "Line-in options button must open its menu.");
						lineIn.ContextMenuStrip.Hide();
						var lineInMenu = lineIn.ContextMenuStrip;
						Assert(lineInMenu.Items.Count == 2 && lineInMenu.Items[0].Text == "Listen to line-in" &&
							lineInMenu.Items[1].Text == "Record line-in to WAV..." && lineInMenu.Items[0].Enabled && lineInMenu.Items[1].Enabled,
							"Line-in options must expose listening and recording while idle.");
						var setLineInActive = typeof(MainForm).GetMethod("SetLineInUiActive", BindingFlags.Instance | BindingFlags.NonPublic);
						setLineInActive.Invoke(form, new object[] { true });
						Assert(lineIn.Text == "Stop line-in" && lineIn.ContextMenuStrip == null,
							"Active line-in must expose a direct Stop line-in button.");
						lineIn.PerformClick();
						Application.DoEvents();
						Assert(!lineInMenu.Visible, "Stop line-in must stop directly instead of reopening the options menu.");
						setLineInActive.Invoke(form, new object[] { false });
						Assert(lineIn.Text == "Line-in options  \u25BE" && lineIn.ContextMenuStrip != null &&
							lineInMenu.Items[0].Text == "Listen to line-in" && lineInMenu.Items[1].Text == "Record line-in to WAV..." &&
							lineInMenu.Items[0].Enabled && lineInMenu.Items[1].Enabled,
							"Stopping line-in must restore its options dropdown.");
						form.Hide();
						Assert(options != null && options.Parent != null, "Import options must be available without occupying the command bar with individual settings.");
						Assert(exportButton != null && exportButton.Text.StartsWith("Export"), "A single visible export action must be present.");
						Assert(cancel != null && !cancel.Visible, "Cancel must be contextual when no import is active.");
						Assert(importProgress != null && !importProgress.Visible && importProgress.Minimum == 0 && importProgress.Maximum == 100,
							"The import progress indicator must be ready but hidden when no import is active.");
						Assert(statusSpacer != null && statusSpacer.Spring && activity != null && !activity.Spring &&
							statusStrip.Items.IndexOf(statusSpacer) + 1 == statusStrip.Items.IndexOf(importProgress) &&
							statusStrip.Items.IndexOf(importProgress) + 1 == statusStrip.Items.IndexOf(activity),
							"The import progress bar and its text must stay grouped at the right edge of the status bar.");
						Assert(list != null && blockMap != null && !ReferenceEquals(list.Parent, blockMap.Parent), "File selection and block detail must use separate panes.");
						Assert(commandBar != null && commandBar.Height == 44, "Actions must occupy one compact fixed-height row.");
						Assert(rootLayout != null && content != null && rootLayout.GetPositionFromControl(commandBar).Row == 0 &&
							rootLayout.GetPositionFromControl(content).Row == 1,
							"The command row must sit above the main panes instead of overlapping them.");
						Assert(content.Dock == DockStyle.Fill && content.Width == rootLayout.GetColumnWidths()[0],
							"The central panes must fill the main layout row.");
						Assert(options.Right <= options.Parent.ClientSize.Width, "Import actions must remain visible at the minimum width.");
						var compactContentWidth = content.Width;
						var compactContentHeight = content.Height;
						var compactListHeight = list.Height;
						var compactListWidth = list.Width;
						var compactBlockWidth = blockMap.Width;
						var compactBlockHeight = blockMap.Height;
						var graph = FindControl(form, "waveChart");
						var compactGraphHeight = graph.Height;
						form.ClientSize = new System.Drawing.Size(1000, 720);
						form.PerformLayout();
						Assert(content.Width > compactContentWidth && content.Height > compactContentHeight,
							"The central pane container must grow with the window.");
						Assert(list.Height > compactListHeight && list.Width > compactListWidth &&
							blockMap.Width > compactBlockWidth && blockMap.Height > compactBlockHeight && graph.Height > compactGraphHeight,
							"Filename, block, and graph panes must all grow with the window.");
						var draggedDistance = Math.Min(420, content.Width - content.Panel2MinSize - content.SplitterWidth);
						content.SplitterDistance = draggedDistance;
						form.PerformLayout();
						Assert(content.SplitterDistance == draggedDistance,
							"The vertical divider must retain the position chosen by the user.");
						var draggedRatio = (double)content.SplitterDistance / (content.ClientSize.Width - content.SplitterWidth);
						form.ClientSize = new System.Drawing.Size(1200, 800);
						form.PerformLayout();
						var resizedRatio = (double)content.SplitterDistance / (content.ClientSize.Width - content.SplitterWidth);
						Assert(Math.Abs(resizedRatio - draggedRatio) < 0.02,
							"A user-selected divider proportion must survive later window resizing.");
					}
				}
				catch (Exception error) { failure = error; }
			});
			thread.SetApartmentState(ApartmentState.STA);
			try
			{
				thread.Start();
				thread.Join();
			}
			finally
			{
				if (Directory.Exists(recoveryDirectory)) Directory.Delete(recoveryDirectory, true);
			}
			if (failure != null) throw failure;
		}

		static void MainWindowExposesCrcRepairOption()
		{
			Exception failure = null;
			var thread = new Thread(() =>
			{
				try
				{
					using (var form = new MainForm())
					{
						var field = typeof(MainForm).GetField("importOptionsMenu", BindingFlags.Instance | BindingFlags.NonPublic);
						var menu = (ContextMenuStrip)field.GetValue(form);
						var option = menu.Items["crcRepairMenuItem"] as ToolStripMenuItem;
						Assert(option != null && !option.Checked && option.Enabled,
							"CRC repair must be visible in import options and disabled by default.");
						option.PerformClick();
						Assert(option.Checked, "CRC repair option did not toggle on.");
						var headers = typeof(MainForm).GetField("crcRepairHeaders", BindingFlags.Instance | BindingFlags.NonPublic);
						var data = typeof(MainForm).GetField("crcRepairData", BindingFlags.Instance | BindingFlags.NonPublic);
						var bits = typeof(MainForm).GetField("crcRepairBits", BindingFlags.Instance | BindingFlags.NonPublic);
						headers.SetValue(form, 1);
						data.SetValue(form, 2);
						bits.SetValue(form, 5);
						var summary = (string)typeof(MainForm)
							.GetMethod("FormatRepairSummary", BindingFlags.Instance | BindingFlags.NonPublic)
							.Invoke(form, new object[] { "Import finished" });
						Assert(summary.Contains("3 CRC repairs") && summary.Contains("5 corrected bits") &&
							summary.Contains("1 header") && summary.Contains("2 data"),
							"import summary did not report repaired blocks and corrected bits separately: " + summary);
					}
				}
				catch (Exception exception) { failure = exception; }
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			thread.Join();
			if (failure != null) throw failure;
		}

		static Control FindControl(Control root, string name)
		{
			if (root.Name == name) return root;
			foreach (Control child in root.Controls)
			{
				var found = FindControl(child, name);
				if (found != null) return found;
			}
			return null;
		}

		static string GetControlText(Control root)
		{
			var text = root.Text ?? string.Empty;
			foreach (Control child in root.Controls)
				text += "\n" + GetControlText(child);
			return text;
		}

		static int ProbeRecording(string path)
		{
			var result = AnalyzeRecording(path);
			Console.WriteLine("Recording={0}", Path.GetFileName(path));
			Console.WriteLine("Bytes={0}; Headers={1}; DataBlocks={2}; Errors={3}",
				result.Bytes, result.Headers.Count, result.DataBlocks, result.Errors);
			Console.WriteLine("Files=" + string.Join(",", result.Headers.Distinct()));
			Console.WriteLine("ValidPayloadStreamSha256=" + result.ValidPayloadStreamSha256);
			return 0;
		}

		static int CompareCrcRepair(string path, string evidencePath = null)
		{
			var paths = Directory.Exists(path)
				? Directory.GetFiles(path, "*.wav", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()
				: new[] { path };
			var totalRepairs = 0;
			var totalCorrectedBits = 0;
			var totalCompleteFileGains = 0;
			var totalReadableExportGains = 0;
			var improved = 0;
			var failures = 0;
			string bestRecording = null;
			var bestReadableGain = 0;
			var evidence = new List<string> { "Recording\tValidatedBlocksBefore\tValidatedBlocksAfter\tAddedBlocks\tLostBlocks\tCompleteFilesBefore\tCompleteFilesAfter\tCompleteFileGains\tLostCompleteFiles\tReadableExportsBefore\tReadableExportsAfter\tReadableExportGains\tLostReadableExports\tAcceptedRepairs\tCorrectedBits\tHeaderRepairs\tDataRepairs\tStatus\tError" };
			foreach (var recording in paths)
			{
				var relativeRecording = EvidencePath(recording);
				try
				{
					var baseline = AnalyzeRecovery(recording, false, targetedRecovery: false, validateReadableExports: true);
					var repaired = AnalyzeRecovery(recording, false, targetedRecovery: false, enableCrcRepair: true, validateReadableExports: true);
					var addedBlocks = repaired.Blocks.Except(baseline.Blocks).Count();
					var lostBlocks = baseline.Blocks.Except(repaired.Blocks).Count();
					var addedCompleteFiles = repaired.CompleteFiles.Except(baseline.CompleteFiles).Count();
					var lostCompleteFiles = baseline.CompleteFiles.Except(repaired.CompleteFiles).Count();
					var addedReadableExports = repaired.ReadableExports.Except(baseline.ReadableExports).Count();
					var lostReadableExports = baseline.ReadableExports.Except(repaired.ReadableExports).Count();
					var status = lostBlocks == 0 && lostCompleteFiles == 0 && lostReadableExports == 0 ? "PASS" : "LOSS";
					if (addedBlocks > 0) improved++;
					if (addedReadableExports > bestReadableGain)
					{
						bestReadableGain = addedReadableExports;
						bestRecording = relativeRecording;
					}
					totalRepairs += repaired.AcceptedRepairs;
					totalCorrectedBits += repaired.CorrectedBits;
					totalCompleteFileGains += addedCompleteFiles;
					totalReadableExportGains += addedReadableExports;
					if (status != "PASS") failures++;
					evidence.Add(string.Join("\t", relativeRecording, baseline.Blocks.Count, repaired.Blocks.Count,
						addedBlocks, lostBlocks, baseline.CompleteFiles.Count, repaired.CompleteFiles.Count,
						addedCompleteFiles, lostCompleteFiles, baseline.ReadableExports.Count,
						repaired.ReadableExports.Count, addedReadableExports, lostReadableExports,
						repaired.AcceptedRepairs, repaired.CorrectedBits, repaired.RepairedHeaders,
						repaired.RepairedDataBlocks, status, "-"));
					Console.WriteLine("{0}\tBlocks={1}->{2}\tComplete={3}->{4}\tReadable={5}->{6}\tRepairs={7}\tBits={8}\tHeaders={9}\tData={10}\t{11}",
						relativeRecording, baseline.Blocks.Count, repaired.Blocks.Count,
						baseline.CompleteFiles.Count, repaired.CompleteFiles.Count,
						baseline.ReadableExports.Count, repaired.ReadableExports.Count,
						repaired.AcceptedRepairs, repaired.CorrectedBits,
						repaired.RepairedHeaders, repaired.RepairedDataBlocks, status);
				}
				catch (Exception error)
				{
					failures++;
					evidence.Add(string.Join("\t", relativeRecording, "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "FAIL", EvidenceText(error.Message)));
					Console.WriteLine("FAIL\t{0}\t{1}", relativeRecording, error);
				}
			}
			if (evidencePath == null && Directory.Exists(path))
				evidencePath = Path.Combine(path, "crc-repair-outcomes.tsv");
			if (evidencePath != null)
			{
				var evidenceDirectory = Path.GetDirectoryName(Path.GetFullPath(evidencePath));
				if (!string.IsNullOrEmpty(evidenceDirectory)) Directory.CreateDirectory(evidenceDirectory);
				File.WriteAllLines(evidencePath, evidence);
			}
			Console.WriteLine("Compared {0} recordings; improved={1}; complete-file gains={2}; readable-export gains={3}; accepted repairs={4}; corrected bits={5}; best-readable-example={6}; failures={7}",
				paths.Length, improved, totalCompleteFileGains, totalReadableExportGains, totalRepairs,
				totalCorrectedBits, bestRecording ?? "none", failures);
			return failures == 0 ? 0 : 1;
		}

		static string EvidencePath(string path)
		{
			var fullPath = Path.GetFullPath(path);
			var currentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (fullPath.StartsWith(currentDirectory, StringComparison.OrdinalIgnoreCase))
				return fullPath.Substring(currentDirectory.Length).Replace(Path.DirectorySeparatorChar, '/');
			return Path.GetFileName(fullPath);
		}

		static string EvidenceText(string text)
		{
			return (text ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
		}

		sealed class RecoveryResult
		{
			public readonly HashSet<string> Blocks = new HashSet<string>();
			public readonly HashSet<string> CompleteFiles = new HashSet<string>();
			public readonly HashSet<string> ReadableExports = new HashSet<string>();
			public int InvalidBlocks;
			public int UpdateEvents;
			public int AcceptedRepairs;
			public int CorrectedBits;
			public int RepairedHeaders;
			public int RepairedDataBlocks;
			public long RecoverySamplesProcessed;
			public long ElapsedMilliseconds;
		}

		static RecoveryResult AnalyzeRecovery(
			string path,
			bool recoveryPasses,
			bool targetedRecovery = true,
			int? profileCount = null,
			bool enableCrcRepair = false,
			bool validateReadableExports = false)
		{
			var result = new RecoveryResult();
			var handler = new BBCFileHandler(manageInputVolume: false);
			handler.EnableCrcRepair = enableCrcRepair;
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				handler.InvalidBlockReceived += (sender, value) => result.InvalidBlocks++;
				handler.UpdateFile += (sender, value) => result.UpdateEvents++;
				handler.CrcRepairAcceptedDetailed += (sender, value) =>
				{
					result.AcceptedRepairs++;
					result.CorrectedBits += value.correctedBits;
					if (value.kind == "header") result.RepairedHeaders++;
					if (value.kind == "data") result.RepairedDataBlocks++;
				};
				if (profileCount.HasValue)
					handler.StartListeningToFileWithProfilesAsync(path, profileCount.Value).GetAwaiter().GetResult();
				else
					handler.StartListeningToFileAsync(path, recoveryPasses: recoveryPasses, targetedRecovery: targetedRecovery).GetAwaiter().GetResult();
				stopwatch.Stop();
				result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
				result.RecoverySamplesProcessed = handler.RecoverySamplesProcessed;
				foreach (var file in handler.files.Values)
					foreach (var block in file.GetValidatedBlockBytes())
						result.Blocks.Add(Convert.ToBase64String(block));
				foreach (var pair in handler.files.Where(pair => pair.Value.IsComplete()))
				{
					var identity = pair.Key + ":" + pair.Value.GetBinaryString();
					result.CompleteFiles.Add(identity);
				}
				if (validateReadableExports)
					result.ReadableExports.UnionWith(ValidateReadableExports(handler));
				return result;
			}
			finally { handler.FormClosing(); }
		}

		static IEnumerable<string> ValidateReadableExports(BBCFileHandler handler)
		{
			var readable = new List<string>();
			var directory = Path.Combine(Path.GetTempPath(), "bbc-crc-repair-exports-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				var index = 0;
				foreach (var pair in handler.files.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
				{
					if (!pair.Value.IsComplete()) continue;
					var identity = pair.Key + ":" + pair.Value.GetBinaryString();
					var path = Path.Combine(directory, "file-" + index++);
					pair.Value.ExportTo(path);
					var payload = pair.Value.GetPayloadForExport();
					Assert(File.ReadAllBytes(path).SequenceEqual(payload),
						"raw export payload did not round-trip for " + pair.Key);
					var header = pair.Value.GetFirstHeaderForExport();
					var expectedInf = String.Format("$.{0,-7} {1,8:X8} {2,8:X8} {3,6:X6}",
						header.filename, header.loadAddress, header.execAddress, payload.Length);
					Assert(File.ReadAllText(path + ".inf") == expectedInf,
						"raw export metadata did not round-trip for " + pair.Key);
					readable.Add(identity);
				}
				return readable;
			}
			finally
			{
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		static int ProbeRecovery(string path, bool targetedRecovery = true)
		{
			var result = AnalyzeRecovery(path, true, targetedRecovery);
			Console.WriteLine(
				"{0}\tBlocks={1}\tComplete={2}\tUpdates={3}\tRecoverySamples={4}\tElapsedMs={5}",
				path, result.Blocks.Count, result.CompleteFiles.Count, result.UpdateEvents,
				result.RecoverySamplesProcessed, result.ElapsedMilliseconds);
			return 0;
		}

		static int SweepRecoveryProfiles(string path)
		{
			RecoveryResult previous = null;
			for (var count = 1; count <= BBCFileHandler.RecoveryProfileCount; count++)
			{
				var result = AnalyzeRecovery(path, true, profileCount: count);
				var addedBlocks = previous == null ? result.Blocks.Count : result.Blocks.Except(previous.Blocks).Count();
				var addedFiles = previous == null ? result.CompleteFiles.Count : result.CompleteFiles.Except(previous.CompleteFiles).Count();
				Console.WriteLine(
					"{0}\t{1}\tBlocks={2}\tComplete={3}\tAddedBlocks={4}\tAddedFiles={5}\tRecoverySamples={6}\tElapsedMs={7}",
					count,
					BBCFileHandler.GetRecoveryProfileName(count - 1),
					result.Blocks.Count,
					result.CompleteFiles.Count,
					addedBlocks,
					addedFiles,
					result.RecoverySamplesProcessed,
					result.ElapsedMilliseconds);
				previous = result;
			}
			return 0;
		}

		static int CompareExpandedRecovery(string path)
		{
			var paths = Directory.Exists(path)
				? Directory.GetFiles(path, "*.wav", SearchOption.AllDirectories)
				: new[] { path };
			var failed = 0;
			Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, recording =>
			{
				try
				{
					var former = AnalyzeRecovery(recording, true, profileCount: 4);
					var expanded = AnalyzeRecovery(recording, true, profileCount: BBCFileHandler.RecoveryProfileCount);
					var lostBlocks = former.Blocks.Except(expanded.Blocks).Count();
					var lostFiles = former.CompleteFiles.Except(expanded.CompleteFiles).Count();
					var passed = lostBlocks == 0 && lostFiles == 0;
					if (!passed) System.Threading.Interlocked.Increment(ref failed);
					Console.WriteLine(
						"{0}\t{1}\tBlocks={2}->{3}\tComplete={4}->{5}\tLost={6}/{7}\tRecoverySamples={8}->{9}\tElapsedMs={10}->{11}",
						passed ? "PASS" : "FAIL", recording,
						former.Blocks.Count, expanded.Blocks.Count,
						former.CompleteFiles.Count, expanded.CompleteFiles.Count,
						lostBlocks, lostFiles,
						former.RecoverySamplesProcessed, expanded.RecoverySamplesProcessed,
						former.ElapsedMilliseconds, expanded.ElapsedMilliseconds);
				}
				catch (Exception error)
				{
					System.Threading.Interlocked.Increment(ref failed);
					Console.WriteLine("FAIL\t{0}\t{1}", recording, error);
				}
			});
			Console.WriteLine("Compared {0} recordings; {1} failed", paths.Length, failed);
			return failed == 0 ? 0 : 1;
		}

		static int CompareTargetedRecovery(string path)
		{
			if (Directory.Exists(path))
			{
				var paths = Directory.GetFiles(path, "*.wav", SearchOption.AllDirectories);
				var failed = 0;
				Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, recording =>
				{
					try
					{
						if (CompareTargetedRecovery(recording) != 0)
							System.Threading.Interlocked.Increment(ref failed);
					}
					catch (Exception error)
					{
						System.Threading.Interlocked.Increment(ref failed);
						Console.WriteLine("FAIL\t{0}\t{1}", recording, error);
					}
				});
				Console.WriteLine("Compared {0} recordings; {1} failed", paths.Length, failed);
				return failed == 0 ? 0 : 1;
			}
			var full = AnalyzeRecovery(path, true, false);
			var targeted = AnalyzeRecovery(path, true, true);
			var sameBlocks = full.Blocks.SetEquals(targeted.Blocks);
			var sameFiles = full.CompleteFiles.SetEquals(targeted.CompleteFiles);
			Console.WriteLine(
				"{0}\t{1}\tBlocks={2}/{3}\tComplete={4}/{5}\tRecoverySamples={6}->{7}\tElapsedMs={8}->{9}",
				sameBlocks && sameFiles ? "PASS" : "FAIL",
				path,
				full.Blocks.Count,
				targeted.Blocks.Count,
				full.CompleteFiles.Count,
				targeted.CompleteFiles.Count,
				full.RecoverySamplesProcessed,
				targeted.RecoverySamplesProcessed,
				full.ElapsedMilliseconds,
				targeted.ElapsedMilliseconds);
			return sameBlocks && sameFiles && targeted.RecoverySamplesProcessed <= full.RecoverySamplesProcessed ? 0 : 1;
		}

		static void TargetedRecoverySkipsValidatedPayloads()
		{
			var path = Path.Combine(Path.GetTempPath(), "bbc-targeted-recovery-" + Guid.NewGuid().ToString("N") + ".wav");
			try
			{
				var good = MakeBlock("GOOD", Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
				var corrupt = MakeBlock("BAD", new byte[] { 4, 5, 6 });
				corrupt[corrupt.Length - 1] ^= 1;
				WritePcm16Wave(path, MakeWaveform(good).Concat(MakeWaveform(corrupt)));
				var full = AnalyzeRecovery(path, true, false);
				var targeted = AnalyzeRecovery(path, true, true);
				Assert(full.Blocks.SetEquals(targeted.Blocks), "targeted retries changed the validated block set");
				Assert(full.CompleteFiles.SetEquals(targeted.CompleteFiles), "targeted retries changed complete files");
				Assert(targeted.RecoverySamplesProcessed < full.RecoverySamplesProcessed,
					"targeted retries did not skip a validated payload span");
			}
			finally { File.Delete(path); }
		}

		static void RecoveredBlocksClearUiFaultMarkers()
		{
			var file = FileFromBlock(MakeBlock("FIXED", new byte[] { 1, 2, 3 }));
			var ui = new MainForm.FileUIData("FIXED");
			ui.AddInvalidBlock(new BBCFileHandler.InvalidBlockData("FIXED", 0, false, 1, "Header CRC failed"));
			ui.AddInvalidBlock(new BBCFileHandler.InvalidBlockData("FIXED", 0, true, 2, "Data CRC failed"));
			Assert(ui.HasInvalidBlock(0, false) && ui.HasInvalidBlock(0, true),
				"test UI state did not retain the reported faults");
			ui.Update(file);
			Assert(!ui.HasInvalidBlock(0, false) && !ui.HasInvalidBlock(0, true),
				"validated header/data did not clear their earlier UI fault markers");
			ui.AddInvalidBlock(new BBCFileHandler.InvalidBlockData("FIXED", 0, true, 3, "Later bad copy"));
			Assert(!ui.HasInvalidBlock(0, true), "a later failed copy re-marked already validated data");
		}

		static void RecoveryDoesNotRepublishUnchangedBlocks()
		{
			var path = Path.Combine(Path.GetTempPath(), "bbc-recovery-updates-" + Guid.NewGuid().ToString("N") + ".wav");
			var handler = new BBCFileHandler(manageInputVolume: false);
			try
			{
				WritePcm16Wave(path, MakeWaveform(MakeBlock("ONCE", new byte[] { 1, 2, 3 })));
				var updates = 0;
				handler.UpdateFile += (sender, uid) => updates++;
				handler.StartListeningToFileAsync(path).GetAwaiter().GetResult();
				Assert(updates == 2, "initial header and payload were not published exactly once");
				handler.StartListeningToFileAsync(path, recoveryPasses: true).GetAwaiter().GetResult();
				Assert(updates == 2, "recovery republished blocks already present in the model");
			}
			finally { handler.FormClosing(); File.Delete(path); }
		}

		// Compares complete header+payload bytes, not just counts or arrival hashes.
		// Directories are scanned recursively; local archive fixtures remain optional.
		static int CompareRecovery(string path)
		{
			var paths = Directory.Exists(path)
				? Directory.GetFiles(path, "*.wav", SearchOption.AllDirectories)
				: new[] { path };
			var failed = 0;
			Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, recording =>
			{
				try
				{
					var standard = AnalyzeRecovery(recording, false);
					var recovery = AnalyzeRecovery(recording, true);
					var lost = standard.Blocks.Except(recovery.Blocks).Count();
					var lostFiles = standard.CompleteFiles.Except(recovery.CompleteFiles).Count();
					var passed = lost == 0 && lostFiles == 0 && standard.InvalidBlocks == recovery.InvalidBlocks;
					if (!passed) System.Threading.Interlocked.Increment(ref failed);
					Console.WriteLine("{0}\t{1}\tBlocks={2}->{3}\tLost={4}\tComplete={5}->{6}\tLostFiles={7}\tInvalid={8}->{9}",
						passed ? "PASS" : "FAIL", recording, standard.Blocks.Count, recovery.Blocks.Count,
						lost, standard.CompleteFiles.Count, recovery.CompleteFiles.Count, lostFiles,
						standard.InvalidBlocks, recovery.InvalidBlocks);
				}
				catch (Exception error)
				{
					System.Threading.Interlocked.Increment(ref failed);
					Console.WriteLine("FAIL\t{0}\t{1}", recording, error);
				}
			});
			Console.WriteLine("Compared {0} recordings; {1} failed", paths.Length, failed);
			return failed == 0 ? 0 : 1;
		}

		static void RecoveryPreservesDataAndDiagnostics()
		{
			var path = Path.Combine(Path.GetTempPath(), "bbc-recovery-" + Guid.NewGuid().ToString("N") + ".wav");
			try
			{
				var good = MakeBlock("KEEP", new byte[] { 1, 2, 3 });
				var corrupt = MakeBlock("BAD", new byte[] { 4, 5, 6 });
				corrupt[corrupt.Length - 1] ^= 1;
				WritePcm16Wave(path, MakeWaveform(good).Concat(MakeWaveform(corrupt)));
				var standard = AnalyzeRecovery(path, false);
				var recovery = AnalyzeRecovery(path, true);
				Assert(standard.Blocks.Count == 1 && standard.CompleteFiles.Count == 1,
					"test recording did not produce its valid file");
				Assert(standard.InvalidBlocks > 0, "test recording did not produce a diagnostic");
				Assert(standard.Blocks.SetEquals(recovery.Blocks), "retries changed valid data or admitted bad CRC data");
				Assert(standard.CompleteFiles.SetEquals(recovery.CompleteFiles), "retries duplicated or changed a complete file");
				Assert(standard.InvalidBlocks == recovery.InvalidBlocks, "retries polluted original diagnostics");
			}
			finally { File.Delete(path); }
		}

		static void RecoveryCancellationResetsSettings()
		{
			var path = Path.Combine(Path.GetTempPath(), "bbc-recovery-cancel-" + Guid.NewGuid().ToString("N") + ".wav");
			var decoder = new ToneHandler(48000, false);
			try
			{
				WritePcm16Wave(path, MakeWaveform(new byte[] { 0x2A, 0x41, 0xC3 }));
				var passes = 0;
				var completions = 0;
				var lineInRejected = false;
				decoder.FileListeningComplete += (sender, args) => completions++;
				using (var cancellation = new System.Threading.CancellationTokenSource())
				{
					EventHandler cancelOnRetry = (sender, args) =>
					{
						if (++passes == 2)
						{
							try { decoder.StartListeningToLineIn(); }
							catch (InvalidOperationException) { lineInRejected = true; }
							cancellation.Cancel();
						}
					};
					decoder.InputSessionStarted += cancelOnRetry;
					try
					{
						decoder.StartListeningToFileAsync(path, cancellationToken: cancellation.Token, recoveryPasses: true)
							.GetAwaiter().GetResult();
						throw new Exception("retry ignored cancellation");
					}
					catch (OperationCanceledException) { }
					decoder.InputSessionStarted -= cancelOnRetry;
				}
				Assert(passes == 2 && completions == 0, "cancelled retries continued or reported completion");
				Assert(lineInRejected, "line-in was allowed to overlap a file import");
				Assert(!decoder.IsRecoveryPass, "cancelled retry retained its settings");
				var bytes = new List<byte>();
				decoder.ByteReceived += (sender, value) => bytes.Add(value);
				decoder.StartListeningToFileAsync(path).GetAwaiter().GetResult();
				Assert(bytes.SequenceEqual(new byte[] { 0x2A, 0x41, 0xC3 }), "ordinary import inherited retry state");
				Assert(completions == 1, "ordinary import did not complete after cancellation");
				passes = 0;
				decoder.InputSessionStarted += (sender, args) => passes++;
				decoder.StartListeningToFileAsync(path, recoveryPasses: true).GetAwaiter().GetResult();
				Assert(passes == BBCFileHandler.RecoveryProfileCount && completions == 2,
					"recovery must run every profile and report one completion");
			}
			finally { decoder.FormClosing(); File.Delete(path); }
		}

		static void FileImportsReportProgress()
		{
			var path = Path.Combine(Path.GetTempPath(), "bbc-import-progress-" + Guid.NewGuid().ToString("N") + ".wav");
			var decoder = new ToneHandler(48000, false);
			try
			{
				WritePcm16Wave(path, MakeWaveform(MakeBlock("PROG", new byte[] { 1, 2, 3 })));
				var progress = new List<ToneHandler.FileImportProgressData>();
				decoder.FileImportProgress += (sender, value) => progress.Add(value);
				decoder.StartListeningToFileWithProfilesAsync(path, 2).GetAwaiter().GetResult();

				Assert(progress.Count >= 4, "import did not report a start and finish for each pass");
				for (var pass = 1; pass <= 2; pass++)
				{
					var passProgress = progress.Where(value => value.passNumber == pass).ToList();
					Assert(passProgress.Count > 0, "import did not report pass " + pass);
					Assert(passProgress.All(value => value.passCount == 2 && value.fileName == path && value.totalSamples > 0),
						"import progress did not identify its source and total for pass " + pass);
					Assert(passProgress.First().samplesRead == 0 &&
						passProgress.Last().samplesRead == passProgress.Last().totalSamples,
						"import progress did not start and finish pass " + pass);
					Assert(passProgress.Zip(passProgress.Skip(1), (first, second) => first.samplesRead <= second.samplesRead).All(value => value),
						"import progress moved backwards within pass " + pass);
				}
				Assert(progress.Any(value => value.passNumber == 1 && value.profileName == "Standard") &&
					progress.Any(value => value.passNumber == 2 && value.profileName == "Inverted"),
					"import progress did not name the active recovery profiles");
			}
			finally { decoder.FormClosing(); File.Delete(path); }
		}

		static void RawExportOmitsCrcAndWritesMetadata()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-raw-export-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				BBCFile file = null;
				var parser = new BlockHandler();
				parser.BlockHeaderReceived += (sender, bytes) =>
				{
					var header = new BlockHeader(bytes.ToArray());
					if (file == null) file = new BBCFile(ref header);
					file.AddHeader(ref header);
				};
				parser.BlockDataReceived += (sender, bytes) =>
				{
					var data = new BlockData(bytes.ToArray());
					file.AddData(ref data);
				};
				Feed(parser, MakeBlock("RAW", new byte[] { 0x10, 0x20 }, 0, false));
				parser.ResetBlock();
				Feed(parser, MakeBlock("RAW", new byte[] { 0x30, 0x40, 0x50 }, 1, true));

				var path = Path.Combine(directory, "RAW");
				file.ExportTo(path);
				Assert(File.ReadAllBytes(path).SequenceEqual(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 }),
					"raw output contains CRC bytes or changed the payload");
				Assert(File.ReadAllText(path + ".inf") == "$.RAW     FFFF1900 FFFF1900 000005",
					"raw .inf metadata is incorrect: " + File.ReadAllText(path + ".inf"));

				var emptyPath = Path.Combine(directory, "EMPTY");
				FileFromBlock(MakeBlock("EMPTY", new byte[0])).ExportTo(emptyPath);
				Assert(new FileInfo(emptyPath).Length == 0, "empty payload export contains its CRC bytes");
				Assert(File.ReadAllText(emptyPath + ".inf") == "$.EMPTY   FFFF1900 FFFF1900 000000",
					"empty payload .inf metadata is incorrect");
			}
			finally { Directory.Delete(directory, true); }
		}

		static void RawExportRejectsInvalidState()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-raw-invalid-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				var incomplete = FileFromBlock(MakeBlock("PARTIAL", new byte[] { 1 }, 0, false));
				AssertThrows<InvalidOperationException>(() => incomplete.ExportTo(Path.Combine(directory, "partial")));
				Assert(!File.Exists(Path.Combine(directory, "partial")), "incomplete export created an output file");

				var header = HeaderFromBlock(MakeBlock("BROKEN", new byte[] { 1, 2 }));
				var inconsistent = new BBCFile(ref header);
				inconsistent.AddHeader(ref header);
				var shortData = new BlockData(new byte[] { 1, 2, 3 });
				inconsistent.AddData(ref shortData);
				var inconsistentPath = Path.Combine(directory, "inconsistent");
				AssertThrows<InvalidDataException>(() => inconsistent.ExportTo(inconsistentPath));
				Assert(!File.Exists(inconsistentPath) && !File.Exists(inconsistentPath + ".inf"),
					"invalid recovery state left partial export files");
			}
			finally { Directory.Delete(directory, true); }
		}

		static void RawExportNamesAreSafeAndUnique()
		{
			Assert(BBCFileHandler.MakeSafeExportName("A:B") == "A_3AB", "invalid filename character was not encoded");
			Assert(BBCFileHandler.MakeSafeExportName("A/B") == "A_2FB", "path separator was not encoded");
			Assert(BBCFileHandler.MakeSafeExportName("CON") == "_CON", "reserved device name was not escaped");
			Assert(BBCFileHandler.MakeSafeExportName("END.") == "END_2E", "trailing dot was not encoded");

			var directory = Path.Combine(Path.GetTempPath(), "bbc-raw-names-" + Guid.NewGuid().ToString("N"));
			var recoveryDirectory = Path.Combine(directory, "state");
			var outputDirectory = Path.Combine(directory, "output");
			var handler = new BBCFileHandler(recoveryDirectory, false);
			try
			{
				Directory.CreateDirectory(outputDirectory);
				File.WriteAllBytes(Path.Combine(outputDirectory, "A_3AB"), new byte[] { 0xFF });
				handler.files.Add("A:B", FileFromBlock(MakeBlock("A:B", new byte[] { 1 })));
				handler.files.Add("A/B", FileFromBlock(MakeBlock("A/B", new byte[] { 3 })));
				handler.files.Add("A_3AB", FileFromBlock(MakeBlock("SECOND", new byte[] { 2 })));
				handler.files.Add("PARTIAL", FileFromBlock(MakeBlock("PARTIAL", new byte[] { 3 }, 0, false)));
				Assert(handler.ExportRawFiles(outputDirectory) == 3, "batch export count is incorrect");
				Assert(File.ReadAllBytes(Path.Combine(outputDirectory, "A_3AB")).SequenceEqual(new byte[] { 0xFF }),
					"batch export overwrote an existing host file");
				Assert(File.ReadAllBytes(Path.Combine(outputDirectory, "A_3AB_2")).SequenceEqual(new byte[] { 1 }),
					"encoded host filename has the wrong payload");
				Assert(File.ReadAllText(Path.Combine(outputDirectory, "A_3AB_2.inf")).StartsWith("$.A:B "),
					"safe host-name mapping changed the original cassette metadata");
				Assert(File.ReadAllBytes(Path.Combine(outputDirectory, "A_2FB")).SequenceEqual(new byte[] { 3 }) &&
					!Directory.Exists(Path.Combine(outputDirectory, "A")),
					"cassette path separator escaped the selected output directory");
				Assert(File.ReadAllBytes(Path.Combine(outputDirectory, "A_3AB_3")).SequenceEqual(new byte[] { 2 }),
					"colliding host filename was not made unique");
				Assert(!File.Exists(Path.Combine(outputDirectory, "PARTIAL")), "batch export included an incomplete file");
			}
			finally
			{
				handler.FormClosing();
				if (Directory.Exists(directory)) Directory.Delete(directory, true);
			}
		}

		static RecordingResult AnalyzeRecording(string path, bool enableCrcRepair = false)
		{
			var decoder = new ToneHandler(48000, false);
			var parser = new BlockHandler { EnableCrcRepair = enableCrcRepair };
			var result = new RecordingResult();
			var payload = new List<byte>();
			if (enableCrcRepair)
				decoder.DecodedByteReceived += (sender, value) => { result.Bytes++; parser.AddByteWithConfidence(sender, value); };
			else
				decoder.ByteReceived += (sender, value) => { result.Bytes++; parser.AddByte(sender, value); };
			decoder.InvalidToneData += parser.ToneError;
			decoder.InvalidToneData += (sender, marker) => { result.Errors++; decoder.ResetWaitForTone(); parser.ResetBlock(); };
			parser.BlockHeaderReceived += (sender, value) => result.Headers.Add(new BlockHeader(value.ToArray()).filename);
			parser.BlockDataReceived += (sender, value) =>
			{
				result.DataBlocks++;
				payload.AddRange(value.Take(value.Count - 2));
				decoder.ResetWaitForTone();
				parser.ResetBlock();
			};
			parser.CrcRepairAcceptedDetailed += (sender, value) =>
			{
				result.RepairedBlocks++;
				result.RepairedBits += value.correctedBits;
				if (value.kind == "header") result.RepairedHeaders++;
				if (value.kind == "data") result.RepairedDataBlocks++;
			};
			parser.Error += (sender, value) => { result.Errors++; decoder.ResetWaitForTone(); parser.ResetBlock(); };

			decoder.StartListeningToFileAsync(path).GetAwaiter().GetResult();

			using (var sha256 = SHA256.Create())
				result.ValidPayloadStreamSha256 = Hex(sha256.ComputeHash(payload.ToArray())).Replace(" ", "");
			return result;
		}

		static void Run(string name, Action test)
		{
			testsRun++;
			try
			{
				test();
				Console.WriteLine("PASS: " + name);
			}
			catch (Exception exception)
			{
				failures++;
				Console.WriteLine("FAIL: {0}: {1}", name, exception.Message);
			}
		}

		static void ValidBlockPasses()
		{
			var parser = new BlockHandler();
			List<byte> receivedHeader = null;
			List<byte> receivedData = null;
			string error = null;
			parser.BlockHeaderReceived += (sender, bytes) => receivedHeader = new List<byte>(bytes);
			parser.BlockDataReceived += (sender, bytes) => receivedData = new List<byte>(bytes);
			parser.Error += (sender, message) => error = message;

			var block = MakeBlock("TEST", new byte[] { 0x12, 0x34, 0x56 });
			Feed(parser, block);

			Assert(error == null, "unexpected parser error: " + error);
			Assert(receivedHeader != null, "header was not emitted");
			Assert(receivedData != null, "data was not emitted");
			Assert(receivedData.Take(3).SequenceEqual(new byte[] { 0x12, 0x34, 0x56 }), "payload changed");
			Assert(receivedData.Count == 5, "data event should include the two CRC bytes");
		}

		static void CrcMatchesReferenceVector()
		{
			Assert(Crc(Encoding.ASCII.GetBytes("123456789")) == 0x31C3,
				"CRC-16/XMODEM reference value should be 31C3");
		}

		static void CorruptHeaderFails()
		{
			var block = MakeBlock("TEST", new byte[] { 1 });
			block[7] ^= 1;
			AssertParserError(block, "Header CRC failure");
		}

		static void CorruptDataFails()
		{
			var block = MakeBlock("TEST", new byte[] { 1, 2, 3 });
			block[block.Length - 3] ^= 1;
			AssertParserError(block, "Data CRC failure");
		}

		static void EmptyPayloadValidatesDataCrc()
		{
			var valid = MakeBlock("EMPTY", new byte[0]);
			var parser = new BlockHandler();
			List<byte> received = null;
			string error = null;
			parser.BlockDataReceived += (sender, bytes) => received = new List<byte>(bytes);
			parser.Error += (sender, message) => error = message;
			Feed(parser, valid);

			Assert(error == null, "valid empty block reported an error: " + error);
			Assert(received != null && received.SequenceEqual(new byte[] { 0, 0 }),
				"empty payload did not retain its two-byte CRC");

			valid[valid.Length - 1] ^= 1;
			AssertParserError(valid, "Data CRC failure");
		}

		static void DataBlockLengthIsLimited()
		{
			var maximum = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
			var parser = new BlockHandler();
			List<byte> received = null;
			parser.BlockDataReceived += (sender, bytes) => received = new List<byte>(bytes);
			Feed(parser, MakeBlock("MAXIMUM", maximum));
			Assert(received != null && received.Count == 258 && received.Take(256).SequenceEqual(maximum),
				"256-byte payload was not accepted intact");

			var oversized = Enumerable.Range(0, 257).Select(value => (byte)value).ToArray();
			AssertParserError(MakeBlock("TOOLONG", oversized), "Data block length exceeds 256 bytes");
		}

		static void FilenameBoundariesAreEnforced()
		{
			Assert(HeaderFromBlock(MakeBlock("A", new byte[] { 1 })).filename == "A",
				"one-character filename was not accepted");

			var parser = new BlockHandler();
			BlockHeader? received = null;
			parser.BlockHeaderReceived += (sender, bytes) => received = new BlockHeader(bytes.ToArray());
			Feed(parser, MakeBlock("1234567890", new byte[] { 1 }));
			Assert(received.HasValue && received.Value.filename == "1234567890" &&
				received.Value.loadAddress == 0xFFFF1900,
				"ten-character filename displaced or changed the header fields");

			AssertParserError(MakeBlock("", new byte[] { 1 }), "Filename must contain 1 to 10 characters");
			AssertParserError(MakeBlock("12345678901", new byte[] { 1 }), "Filename must contain 1 to 10 characters");
			AssertParserError(MakeBlock("HAS SPACE", new byte[] { 1 }), "Filename cannot contain spaces");
		}

		static void CassetteFilenamesAreIndependentOfHostFilenameRules()
		{
			var header = HeaderFromBlock(MakeBlock("A:B", new byte[] { 1 }));
			Assert(header.filename == "A:B", "a cassette filename was changed to satisfy host filename rules");
			var file = new BBCFile(ref header);
			file.AddHeader(ref header);
			var data = new BlockData(new byte[] { 1, 0, 0 });
			file.AddData(ref data);
			Assert(file.filename == "A:B" && file.IsComplete(),
				"a host-invalid cassette filename did not remain recoverable");
		}

		static void FinalBlockFlagIsInterpreted()
		{
			Assert(!FileFromBlock(MakeBlock("FLAGS", new byte[] { 1 }, blockFlag: 0x7F)).IsComplete(),
				"non-final flag bits incorrectly completed the file");
			Assert(FileFromBlock(MakeBlock("FLAGS", new byte[] { 1 }, blockFlag: 0x81)).IsComplete(),
				"bit 7 did not complete the file when another flag bit was present");
		}

		static void ConflictingFinalBlockPositionsDoNotMerge()
		{
			var file = FileFromBlock(MakeBlock("BOUNDARY", new byte[] { 1 }, 1, true));
			var earlierFinal = HeaderFromBlock(MakeBlock("BOUNDARY", new byte[] { 2 }, 0, true));
			Assert(!file.CouldContain(earlierFinal),
				"a second final marker silently shortened the expected file");

			var lastWithoutFlag = HeaderFromBlock(MakeBlock("BOUNDARY", new byte[] { 1 }, 1, false));
			Assert(!file.CouldContain(lastWithoutFlag),
				"the known last block was accepted without its final flag");
		}

		static void MaximumFinalBlockNumberIsRepresentable()
		{
			var header = HeaderFromBlock(MakeBlock("BLOCKMAX", new byte[0], ushort.MaxValue, true));
			var file = new BBCFile(ref header);
			file.AddHeader(ref header);
			Assert(file.TotalBlocksKnown && file.NumBlocks == ushort.MaxValue + 1,
				"final block 65535 overflowed or collided with the unknown-length sentinel");
			Assert(!file.IsComplete(), "file with 65,535 missing blocks was incorrectly complete");
		}

		static void FileStateRetainsFinalBlockCount()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-cassette-edge-state-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var recording = Path.Combine(directory, "empty.wav");
			BBCFileHandler writer = null;
			BBCFileHandler reader = null;
			try
			{
				WritePcm16Wave(recording, MakeWaveform(MakeBlock("EMPTY", new byte[0])));
				writer = new BBCFileHandler(directory, false);
				writer.StartListeningToFileAsync(recording).GetAwaiter().GetResult();
				Assert(writer.files["EMPTY"].IsComplete(), "empty file was not complete before persistence");
				writer.Serialise();

				reader = new BBCFileHandler(directory, false);
				reader.Deserialise();
				Assert(reader.files.ContainsKey("EMPTY") && reader.files["EMPTY"].IsComplete(),
					"known final-block count was not preserved by persistence");
			}
			finally
			{
				writer?.FormClosing();
				reader?.FormClosing();
				if (File.Exists(recording))
					File.Delete(recording);
				foreach (var stateFile in Directory.GetFiles(directory, "*.xml"))
					File.Delete(stateFile);
				if (Directory.Exists(directory))
					Directory.Delete(directory, true);
			}
		}

		static void LegacyUnknownBlockCountRemainsUnknown()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-cassette-legacy-state-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var stateFile = Path.Combine(directory, "LEGACY.xml");
			BBCFileHandler handler = null;
			try
			{
				File.WriteAllText(stateFile,
					"{\"filename\":\"LEGACY\",\"totalBlocks\":65535,\"blocks\":[],\"currentBlock\":null}");
				handler = new BBCFileHandler(directory, false);
				handler.Deserialise();
				Assert(handler.files.ContainsKey("LEGACY") && !handler.files["LEGACY"].TotalBlocksKnown,
					"legacy unknown-length sentinel was interpreted as 65,535 known blocks");
			}
			finally
			{
				handler?.FormClosing();
				if (File.Exists(stateFile))
					File.Delete(stateFile);
				if (Directory.Exists(directory))
					Directory.Delete(directory);
			}
		}

		static void FinalBlockCompletesFile()
		{
			var parser = new BlockHandler();
			BBCFile file = null;
			parser.BlockHeaderReceived += (sender, bytes) =>
			{
				var header = new BlockHeader(bytes.ToArray());
				file = new BBCFile(ref header);
				file.AddHeader(ref header);
			};
			parser.BlockDataReceived += (sender, bytes) =>
			{
				var data = new BlockData(bytes.ToArray());
				file.AddData(ref data);
			};

			Feed(parser, MakeBlock("ONE", new byte[] { 0xA5 }));
			Assert(file != null && file.IsComplete(), "single final block should complete the file");
			Assert(!file.HasHeader(-1) && !file.HasData(-1), "negative block indexes should be absent");
			var extraData = new BlockData(new byte[] { 0x01 });
			Assert(!file.CouldContain(extraData, 1), "known single-block file accepted out-of-range data");
		}

		static void CompatibleBlocksMergeAcrossInputs()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-cassette-merge-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var firstPath = Path.Combine(directory, "first.wav");
			var secondPath = Path.Combine(directory, "second.wav");
			BBCFileHandler handler = null;
			try
			{
				WritePcm16Wave(firstPath, MakeWaveform(MakeBlock("MERGE", new byte[] { 0x10 }, 0, false)));
				WritePcm16Wave(secondPath, MakeWaveform(MakeBlock("MERGE", new byte[] { 0x20 }, 1, true)));

				handler = new BBCFileHandler(directory, false);
				handler.StartListeningToFileAsync(firstPath).GetAwaiter().GetResult();
				Assert(handler.files.Count == 1 && !handler.files["MERGE"].IsComplete(),
					"first non-final block did not create one incomplete file");
				handler.StartListeningToFileAsync(secondPath).GetAwaiter().GetResult();

				Assert(handler.files.Count == 1, "compatible block created a duplicate file identity");
				Assert(handler.files["MERGE"].IsComplete(), "compatible blocks did not merge into a complete file");
			}
			finally
			{
				handler?.FormClosing();
				DeleteFilesAndDirectory(directory, firstPath, secondPath);
			}
		}

		static void RepeatedConflictReusesAlternateFile()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-cassette-conflict-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var firstPath = Path.Combine(directory, "version-a.wav");
			var secondPath = Path.Combine(directory, "version-b.wav");
			BBCFileHandler handler = null;
			try
			{
				WritePcm16Wave(firstPath, MakeWaveform(MakeBlock("SAME", new byte[] { 0x11, 0x22 })));
				WritePcm16Wave(secondPath, MakeWaveform(MakeBlock("SAME", new byte[] { 0x33, 0x44 })));

				handler = new BBCFileHandler(directory, false);
				handler.StartListeningToFileAsync(firstPath).GetAwaiter().GetResult();
				handler.StartListeningToFileAsync(secondPath).GetAwaiter().GetResult();
				Assert(handler.files.Count == 2 && handler.files.ContainsKey("SAME2"),
					"conflicting payload did not create one alternate file");
				var alternate = handler.files["SAME2"];

				handler.StartListeningToFileAsync(secondPath).GetAwaiter().GetResult();
				Assert(handler.files.Count == 2, "repeated conflicting payload created another duplicate identity");
				Assert(ReferenceEquals(handler.files["SAME2"], alternate),
					"repeated payload did not reuse its compatible alternate file");
				Assert(handler.files["SAME"].IsComplete() && handler.files["SAME2"].IsComplete(),
					"separate file versions were not retained as complete files");
			}
			finally
			{
				handler?.FormClosing();
				DeleteFilesAndDirectory(directory, firstPath, secondPath);
			}
		}

		static void SyntheticWaveformDecodes()
		{
			var decoder = new ToneHandler(48000, false);
			var received = new List<byte>();
			decoder.ByteReceived += (sender, value) => received.Add(value);
			var expected = new byte[] { 0x2A, 0x41, 0xC3 };
			decoder.HandleData(MakeWaveform(expected));
			Assert(received.SequenceEqual(expected),
				"expected " + Hex(expected) + ", received " + Hex(received.Take(8)));
		}

		static void PhaseShiftedWaveformRecovers()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-phase-shift-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, "phase.wav");
			BBCFileHandler standard = null;
			BBCFileHandler recovery = null;
			var profiles = new List<string>();
			try
			{
				var block = MakeBlock("PHASE", new byte[] { 1, 2, 3 });
				WritePcm16Wave(path, MakeContinuousPhaseWaveform(block, Math.PI / 2));
				standard = new BBCFileHandler(manageInputVolume: false);
				standard.StartListeningToFileAsync(path).GetAwaiter().GetResult();
				recovery = new BBCFileHandler(manageInputVolume: false);
				recovery.FileImportProgress += (sender, value) => profiles.Add(value.profileName);
				recovery.StartListeningToFileAsync(path, recoveryPasses: true).GetAwaiter().GetResult();
				Assert(standard.files.ContainsKey("PHASE") && standard.files["PHASE"].IsComplete(),
					"the phase-shift fixture was not preserved by the standard alignment");
				Assert(recovery.files.ContainsKey("PHASE") && recovery.files["PHASE"].IsComplete(),
					"the alternate phase profile did not recover the shifted fixture");
				Assert(profiles.Contains("PhaseShift"), "recovery passes did not execute the phase-shift profile");
			}
			finally
			{
				standard?.FormClosing();
				recovery?.FormClosing();
				DeleteFilesAndDirectory(directory, path);
			}
		}

		static void DocumentedCarrierDecodes()
		{
			var expected = new byte[] { 0x2A, 0x41 };
			Assert(DecodeSynthetic(MakeWaveform(expected, 12000)).SequenceEqual(expected),
				"documented five-second 2400 Hz carrier did not acquire the decoder");
		}

		static void MovingBaselineWaveformDecodes()
		{
			var decoder = new ToneHandler(48000, false);
			var received = new List<byte>();
			decoder.ByteReceived += (sender, value) => received.Add(value);
			var expected = new byte[] { 0x2A, 0x41, 0xC3 };
			var samples = MakeWaveform(expected);
			for (var index = 0; index < samples.Length; index++)
				samples[index] = samples[index] * 0.08f +
					(float)(0.4 * Math.Sin(2 * Math.PI * 50 * index / 48000));

			const int chunkSize = 1379;
			for (var start = 0; start < samples.Length; start += chunkSize)
			{
				var count = Math.Min(chunkSize, samples.Length - start);
				var chunk = new float[count];
				Array.Copy(samples, start, chunk, 0, count);
				decoder.HandleData(chunk);
			}
			Assert(received.SequenceEqual(expected),
				"moving baseline changed decoded bytes: " + Hex(received.Take(8)));
		}

		static void SyntheticSignalVariationsDecode()
		{
			var expected = new byte[] { 0x2A, 0x41, 0xC3 };
			var original = MakeWaveform(expected);
			var random = new Random(13013);
			var cases = new Dictionary<string, float[]>
			{
				["inverted"] = original.Select(sample => -sample).ToArray(),
				["quiet and noisy"] = original.Select(sample => sample * 0.08f +
					(float)((random.NextDouble() * 2 - 1) * 0.02)).ToArray(),
				["15% slow"] = TimeScale(original, 0.85),
				["15% fast"] = TimeScale(original, 1.15),
				["20% slow"] = TimeScale(original, 0.80),
				["20% fast"] = TimeScale(original, 1.20)
			};
			var failures = new List<string>();
			foreach (var item in cases)
			{
				var received = DecodeSynthetic(item.Value);
				if (!received.SequenceEqual(expected))
					failures.Add(item.Key + "=" + Hex(received.Take(8)));
			}
			Assert(failures.Count == 0, "variation failures: " + string.Join("; ", failures));
		}

		static List<byte> DecodeSynthetic(float[] samples)
		{
			var decoder = new ToneHandler(48000, false);
			var received = new List<byte>();
			decoder.ByteReceived += (sender, value) => received.Add(value);
			decoder.HandleData(samples);
			return received;
		}

		static float[] TimeScale(float[] source, double speed)
		{
			var scaledLength = (int)Math.Round(source.Length / speed);
			var result = new float[scaledLength + 300];
			for (var index = 0; index < scaledLength; index++)
			{
				var sourcePosition = index * speed;
				var before = Math.Min((int)sourcePosition, source.Length - 1);
				var after = Math.Min(before + 1, source.Length - 1);
				var fraction = sourcePosition - before;
				result[index] = (float)(source[before] * (1 - fraction) + source[after] * fraction);
			}
			return result;
		}

		static void InvalidStartBitDoesNotEmitByte()
		{
			var decoder = new ToneHandler(48000, false);
			var received = new List<byte>();
			var errors = new List<string>();
			decoder.ByteReceived += (sender, value) => received.Add(value);
			decoder.InvalidToneData += (sender, value) =>
			{
				errors.Add(value.markerDescription);
				decoder.ResetWaitForTone();
			};

			var beforeRecovery = new List<int>();
			AddFrame(beforeRecovery, 0x5A);
			beforeRecovery.Add(1);
			var afterRecovery = new List<int>();
			AddFrame(afterRecovery, 0xC3);
			decoder.HandleData(MakeRecoveryWaveform(beforeRecovery, afterRecovery));

			Assert(received.SequenceEqual(new byte[] { 0x5A, 0xC3 }),
				"invalid start emitted or displaced a byte: " + Hex(received));
			Assert(errors.Count == 1 && errors[0] == "Start bit not zero",
				"invalid start did not report exactly one framing error");
		}

		static void InvalidStopBitDoesNotEmitByte()
		{
			var decoder = new ToneHandler(48000, false);
			var received = new List<byte>();
			var errors = new List<string>();
			decoder.ByteReceived += (sender, value) => received.Add(value);
			decoder.InvalidToneData += (sender, value) =>
			{
				errors.Add(value.markerDescription);
				decoder.ResetWaitForTone();
			};

			var beforeRecovery = new List<int>();
			AddFrame(beforeRecovery, 0x5A);
			AddFrame(beforeRecovery, 0xA5, 0);
			var afterRecovery = new List<int>();
			AddFrame(afterRecovery, 0xC3);
			decoder.HandleData(MakeRecoveryWaveform(beforeRecovery, afterRecovery));

			Assert(received.SequenceEqual(new byte[] { 0x5A, 0xC3 }),
				"invalid stop emitted or displaced a byte: " + Hex(received));
			Assert(errors.Count == 1 && errors[0] == "End bit not one",
				"invalid stop did not report exactly one framing error");

			var decoderWithoutErrorHandler = new ToneHandler(48000, false);
			var receivedWithoutErrorHandler = new List<byte>();
			decoderWithoutErrorHandler.ByteReceived += (sender, value) => receivedWithoutErrorHandler.Add(value);
			var continuousBits = new List<int>();
			AddFrame(continuousBits, 0x5A);
			AddFrame(continuousBits, 0xA5, 0);
			AddFrame(continuousBits, 0xC3);
			decoderWithoutErrorHandler.HandleData(MakeBitWaveform(continuousBits));
			Assert(receivedWithoutErrorHandler.SequenceEqual(new byte[] { 0x5A, 0xC3 }),
				"invalid stop was not reset without an error subscriber: " + Hex(receivedWithoutErrorHandler));
		}

		static void DiagnosticWindowsClampAtBoundaries()
		{
			var decoder = new ToneHandler(48000, false);
			var published = 0;
			decoder.DiagnosticAvailable += (sender, value) => published++;
			decoder.HandleData(new float[100]);
			decoder.RecordDiagnosticError(new ToneHandler.MarkerData(0, "input start"));
			decoder.RecordDiagnosticError(new ToneHandler.MarkerData(decoder.RetainedSampleCount - 1, "input end"));
			decoder.FinalizeDiagnostics();

			Assert(decoder.DiagnosticCount == 2 && published == 2,
				"boundary diagnostics were not retained and published");
			var start = decoder.GetErrorData(0);
			var end = decoder.GetErrorData(1);
			Assert(start.data.Count > 0 && start.errorData.markerPosition == 0,
				"start diagnostic was not clamped to the available samples");
			Assert(end.data.Count > 0 && end.errorData.markerPosition == end.data.Count - 1,
				"end diagnostic was not clamped to the available samples");
		}

		static void DiagnosticContinuationPreservesMarkers()
		{
			var decoder = new ToneHandler(48000, false);
			var received = new List<byte>();
			ToneHandler.ErrorDataForGraph? diagnostic = null;
			decoder.ByteReceived += (sender, value) => received.Add(value);
			decoder.InvalidToneData += (sender, marker) =>
			{
				decoder.RecordDiagnosticError(marker);
				decoder.ResetWaitForTone();
			};
			decoder.DiagnosticAvailable += (sender, value) => diagnostic = value;

			var beforeError = new List<int>();
			AddFrame(beforeError, 0x5A);
			AddFrame(beforeError, 0xA5, 0);
			var afterError = new List<int>();
			AddFrame(afterError, 0xC3);
			AddFrame(afterError, 0x3C);
			var samples = new List<float>();
			AddCarrier(samples);
			foreach (var bit in beforeError) AddBit(samples, bit);
			foreach (var bit in afterError) AddBit(samples, bit, 0.05);
			for (var cycle = 0; cycle < 10; cycle++) AddCycles(samples, 2400, 1, 0.05);
			decoder.HandleData(samples.ToArray());
			decoder.FinalizeDiagnostics();

			Assert(received.SequenceEqual(new byte[] { 0x5A }),
				"diagnostic continuation changed functional byte recovery: " + Hex(received));
			Assert(diagnostic.HasValue && diagnostic.Value.bitBoundaryMarker.Any(marker =>
				marker.markerPosition < diagnostic.Value.errorData.markerPosition &&
				marker.markerDescription == "bit 0 = 0"),
				"confirmed pre-error bit label omitted its index or value");
			Assert(diagnostic.HasValue &&
				diagnostic.Value.bitBoundaryMarker.Any(marker =>
					marker.markerPosition > diagnostic.Value.errorData.markerPosition &&
					(marker.markerDescription == "0" || marker.markerDescription == "1")),
				"diagnostic continuation did not add post-error bit boundaries");
			Assert(diagnostic.Value.byteBoundaryMarker.Any(marker =>
				marker.markerPosition > diagnostic.Value.errorData.markerPosition &&
				marker.markerDescription == "diagnostic byte C3"),
				"diagnostic continuation did not recover the following framed byte: " +
				string.Join(", ", diagnostic.Value.byteBoundaryMarker.Select(marker => marker.markerDescription)) +
				" | " + string.Join(", ", diagnostic.Value.bitBoundaryMarker
					.Where(marker => marker.markerPosition > diagnostic.Value.errorData.markerPosition)
					.Select(marker => marker.markerDescription)));
			var projectedByte = diagnostic.Value.byteBoundaryMarker.First(marker =>
				marker.markerDescription == "diagnostic byte C3");
			Assert(diagnostic.Value.bitBoundaryMarker.Any(marker =>
				marker.markerPosition == projectedByte.markerPosition &&
				marker.markerDescription == "0"),
				"diagnostic frame start did not retain its projected bit marker");

			var startDecoder = new ToneHandler(48000, false);
			var startReceived = new List<byte>();
			ToneHandler.ErrorDataForGraph? startDiagnostic = null;
			startDecoder.ByteReceived += (sender, value) => startReceived.Add(value);
			startDecoder.InvalidToneData += (sender, marker) =>
			{
				startDecoder.RecordDiagnosticError(marker);
				startDecoder.ResetWaitForTone();
			};
			startDecoder.DiagnosticAvailable += (sender, value) => startDiagnostic = value;
			var startSamples = new List<float>();
			AddCarrier(startSamples);
			var validBeforeStartError = new List<int>();
			AddFrame(validBeforeStartError, 0x5A);
			foreach (var bit in validBeforeStartError) AddBit(startSamples, bit);
			AddBit(startSamples, 1);
			foreach (var bit in afterError) AddBit(startSamples, bit);
			for (var cycle = 0; cycle < 10; cycle++) AddCycles(startSamples, 2400, 1);
			startDecoder.HandleData(startSamples.ToArray());
			startDecoder.FinalizeDiagnostics();
			Assert(startReceived.SequenceEqual(new byte[] { 0x5A }) && startDiagnostic.HasValue &&
				startDiagnostic.Value.byteBoundaryMarker.Any(marker =>
					marker.markerPosition > startDiagnostic.Value.errorData.markerPosition &&
					marker.markerDescription == "diagnostic byte C3"),
				"diagnostic continuation did not recover immediately after an invalid start bit");

			var silenceDecoder = new ToneHandler(48000, false);
			ToneHandler.ErrorDataForGraph? silenceDiagnostic = null;
			silenceDecoder.InvalidToneData += (sender, marker) =>
			{
				silenceDecoder.RecordDiagnosticError(marker);
				silenceDecoder.ResetWaitForTone();
			};
			silenceDecoder.DiagnosticAvailable += (sender, value) => silenceDiagnostic = value;
			var silenceSamples = new List<float>();
			AddCarrier(silenceSamples);
			foreach (var bit in beforeError) AddBit(silenceSamples, bit);
			silenceSamples.AddRange(new float[2000]);
			silenceDecoder.HandleData(silenceSamples.ToArray());
			silenceDecoder.FinalizeDiagnostics();
			Assert(silenceDiagnostic.HasValue && !silenceDiagnostic.Value.byteBoundaryMarker.Any(marker =>
				marker.markerPosition > silenceDiagnostic.Value.errorData.markerPosition &&
				marker.markerDescription.StartsWith("diagnostic byte ")),
				"diagnostic continuation projected a framed byte into post-error silence");
		}

		static void DiagnosticRetentionRemainsBounded()
		{
			var decoder = new ToneHandler(48000, false);
			for (var chunk = 0; chunk < 100; chunk++)
				decoder.HandleData(new float[16384]);
			Assert(decoder.RetainedSampleCount < 5000,
				"long input retained " + decoder.RetainedSampleCount + " conditioned samples");

			for (var error = 0; error < ToneHandler.MaximumRetainedDiagnostics + 5; error++)
			{
				decoder.RecordDiagnosticError(new ToneHandler.MarkerData(
					decoder.RetainedSampleCount - 1,
					"error " + error));
				decoder.FinalizeDiagnostics();
			}
			Assert(decoder.DiagnosticCount == ToneHandler.MaximumRetainedDiagnostics,
				"diagnostic snapshot count was not capped");
			Assert(decoder.GetErrorData(0).errorData.markerDescription == "error 5",
				"diagnostic cap did not retain the most recent snapshots");
		}

		static void FileDecodingPublishesBlockError()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-cassette-diagnostic-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var headerPath = Path.Combine(directory, "corrupt-header.wav");
			var dataPath = Path.Combine(directory, "corrupt-data.wav");
			BBCFileHandler handler = null;
			var handlerClosed = false;
			try
			{
				var headerBlock = MakeBlock("BROKEN", new byte[] { 1, 2, 3 });
				headerBlock[8] ^= 1;
				var dataBlock = MakeBlock("BROKEN", new byte[] { 1, 2, 3 });
				dataBlock[dataBlock.Length - 3] ^= 1;
				WritePcm16Wave(headerPath, MakeWaveform(headerBlock));
				WritePcm16Wave(dataPath, MakeWaveform(dataBlock));
				ToneHandler.ErrorDataForGraph? diagnostic = null;
				var invalidBlocks = new List<BBCFileHandler.InvalidBlockData>();
				handler = new BBCFileHandler(directory, false);
				handler.NewData += (sender, value) => diagnostic = value;
				handler.InvalidBlockReceived += (sender, value) => invalidBlocks.Add(value);
				handler.StartListeningToFileAsync(headerPath).GetAwaiter().GetResult();
				ToneHandler.ErrorDataForGraph selected;
				Assert(invalidBlocks.Count == 1 &&
					handler.TryGetDiagnostic(invalidBlocks[0].diagnosticId, out selected) &&
					selected.errorData.markerDescription == "Header CRC failure" &&
					selected.bitBoundaryMarker.Any(marker =>
						marker.markerPosition > selected.errorData.markerPosition &&
						(marker.markerDescription == "0" || marker.markerDescription == "1")),
					"header error could not retrieve its diagnostic by ID");
				handler.StartListeningToFileAsync(dataPath).GetAwaiter().GetResult();

				Assert(diagnostic.HasValue, "block error was not published after file decoding");
				Assert(diagnostic.Value.errorData.markerDescription == "Data CRC failure" &&
					diagnostic.Value.data.Count > 0,
					"published diagnostic did not contain the expected error context");
				Assert(invalidBlocks.Count == 2 &&
					invalidBlocks[0].fileUID == "BROKEN" && invalidBlocks[0].blockNum == 0 && !invalidBlocks[0].isData &&
					invalidBlocks[1].fileUID == "BROKEN" && invalidBlocks[1].blockNum == 0 && invalidBlocks[1].isData,
					"header and data errors were not associated with the correct block regions");
				Assert(handler.TryGetDiagnostic(invalidBlocks[0].diagnosticId, out selected) &&
					selected.errorData.markerDescription == "Header CRC failure" &&
					selected.bitBoundaryMarker.Any(marker =>
						marker.markerPosition > selected.errorData.markerPosition &&
						(marker.markerDescription == "0" || marker.markerDescription == "1")),
					"new input lost the earlier header diagnostic");
				Assert(handler.TryGetDiagnostic(invalidBlocks[1].diagnosticId, out selected) &&
					selected.errorData.markerDescription == "Data CRC failure",
					"current input did not retain its data diagnostic");

				handler.StartListeningToFileAsync(dataPath).GetAwaiter().GetResult();
				Assert(invalidBlocks.Count == 3 &&
					invalidBlocks.Select(value => value.diagnosticId).Distinct().Count() == 3,
					"repeated block failures did not retain distinct diagnostic identities");
				foreach (var invalidBlock in invalidBlocks)
					Assert(handler.TryGetDiagnostic(invalidBlock.diagnosticId, out selected) &&
						selected.data.Count > 0,
						"a displayed invalid block lost its diagnostic waveform");
				Assert(handler.StoredDiagnosticCount == 3 && Directory.Exists(handler.DiagnosticStoreDirectory),
					"completed diagnostics were not retained in the temporary store");

				var diagnosticStoreDirectory = handler.DiagnosticStoreDirectory;
				handler.FormClosing();
				handlerClosed = true;
				Assert(handler.StoredDiagnosticCount == 0 && !Directory.Exists(diagnosticStoreDirectory),
					"closing the decoder did not remove its temporary diagnostic store");
			}
			finally
			{
				if (!handlerClosed) handler?.FormClosing();
				DeleteFilesAndDirectory(directory, headerPath, dataPath);
			}
		}

		static void SampleFormatsMatch()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-format-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var floatPath = Path.Combine(directory, "float-44100.wav");
			var secondFloatPath = Path.Combine(directory, "float-44100-second.wav");
			var pcmPath = Path.Combine(directory, "pcm-48000-8bit.wav");
			try
			{
				WriteFloatWave(floatPath, new float[] { -1, -0.5f, 0, 0.5f, 1 }, 44100);
				WriteFloatWave(secondFloatPath, new float[] { 1, 0.5f, 0, -0.5f, -1 }, 44100);
				WritePcm8Wave(pcmPath, new float[] { -1, -0.5f, 0, 0.5f, 1 }, 48000);
				AssertWave(floatPath, 3, 1, 44100, 32);
				AssertWave(secondFloatPath, 3, 1, 44100, 32);
				AssertWave(pcmPath, 1, 1, 48000, 8);
			}
			finally
			{
				DeleteFilesAndDirectory(directory, floatPath, secondFloatPath, pcmPath);
			}
		}

		static void InputNormalizationWorks()
		{
			var stereoSamples = new float[] { 1, -1, 0.5f, 0.25f };
			AssertNormalizedSamples(stereoSamples, AudioChannelMode.Left, new float[] { 1, 0.5f });
			AssertNormalizedSamples(stereoSamples, AudioChannelMode.Right, new float[] { -1, 0.25f });
			AssertNormalizedSamples(stereoSamples, AudioChannelMode.Mix, new float[] { 0, 0.375f });

			var source = new ArraySampleProvider(new float[441], 44100, 1);
			var resampled = ToneHandler.NormalizeInput(source, AudioChannelMode.Mix);
			Assert(resampled.WaveFormat.SampleRate == 48000, "input was not resampled to 48 kHz");
			Assert(resampled.WaveFormat.Channels == 1, "resampled input was not mono");
			Assert(resampled.Read(new float[600], 0, 600) > 0, "resampler returned no samples");
		}

		static void InvalidDecoderSampleRatesAreRejected()
		{
			AssertThrows<ArgumentOutOfRangeException>(() => new ToneHandler(0, false));
			AssertThrows<ArgumentOutOfRangeException>(() => new ToneHandler(9596, false));
			AssertThrows<ArgumentOutOfRangeException>(() => ToneHandler.NormalizeInput(
				new ArraySampleProvider(new float[4], 48000, 1), AudioChannelMode.Mix, 9596));
		}

		static void LivePcmConversionHonorsByteCount()
		{
			var buffer = new byte[] { 0x00, 0x80, 0xFF, 0x7F, 0x34, 0x12, 0xAA, 0xBB };
			var samples = ToneHandler.ConvertPcm16(buffer, 6);

			Assert(samples.Length == 3, "conversion included unused buffer capacity");
			Assert(samples[0] == -1, "minimum PCM sample was converted incorrectly");
			Assert(Math.Abs(samples[1] - 32767f / 32768f) < 0.000001f,
				"maximum PCM sample was converted incorrectly");
			Assert(Math.Abs(samples[2] - 0x1234 / 32768f) < 0.000001f,
				"valid trailing PCM sample was converted incorrectly");
		}

		static void LineInRecordingFinalizesValidPcm()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-line-in-recording-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, "capture.wav");
			var spontaneousPath = Path.Combine(directory, "spontaneous.wav");
			var capture = new FakeLineInCapture(48000);
			LineInRecordingResult recordingResult = null;
			var decoder = new ToneHandler(
				48000,
				false,
				rate => capture,
				input => new FakeLineInPlayback());
			decoder.LineInRecordingStopped += (sender, result) => recordingResult = result;

			try
			{
				decoder.StartListeningToLineIn(path);
				var pcm = new byte[] { 0, 0, 0x34, 0x12, 0, 0 };
				capture.RaiseData(pcm, 4);
				decoder.StopListening();

				Assert(recordingResult != null, "line-in recording did not report completion");
				Assert(recordingResult.Error == null, "valid line-in recording reported an error");
				Assert(recordingResult.BytesWritten == 4, "line-in recording did not preserve the valid byte count");
				Assert(File.Exists(path), "line-in recording did not create the requested WAV");
				Assert(!Directory.GetFiles(directory, "*.recording-*.tmp").Any(), "temporary line-in recording was left behind");
				AssertWave(path, 1, 1, 48000, 16);
				using (var reader = new WaveFileReader(path))
				{
					var data = new byte[4];
					Assert(reader.Read(data, 0, data.Length) == data.Length, "recorded WAV data length was not readable");
					Assert(data.SequenceEqual(pcm.Take(4)), "recorded WAV data was changed");
				}

				recordingResult = null;
				decoder.StartListeningToLineIn(spontaneousPath);
				capture.RaiseData(pcm, pcm.Length);
				capture.RaiseRecordingStopped();
				Assert(!decoder.IsListeningToLineIn, "spontaneous capture stop left line-in active");
				Assert(recordingResult != null && recordingResult.Error == null, "spontaneous capture stop did not finalize WAV recording");
				Assert(File.Exists(spontaneousPath), "spontaneous capture stop did not preserve the WAV");
				AssertWave(spontaneousPath, 1, 1, 48000, 16);
			}
			finally
			{
				decoder.FormClosing();
				DeleteFilesAndDirectory(directory, path, spontaneousPath);
			}
		}

		static void LineInSessionsStopDisposeAndRestart()
		{
			var captures = new List<FakeLineInCapture>();
			var playbacks = new List<FakeLineInPlayback>();
			var stoppedEvents = 0;
			var decoder = new ToneHandler(
				48000,
				false,
				rate =>
				{
					var capture = new FakeLineInCapture(rate);
					captures.Add(capture);
					return capture;
				},
				input =>
				{
					var playback = new FakeLineInPlayback();
					playbacks.Add(playback);
					return playback;
				});
			decoder.LineInStopped += (sender, args) => stoppedEvents++;

			decoder.StartListeningToLineIn();
			Assert(decoder.IsListeningToLineIn, "line-in state did not report the active session");
			Assert(captures[0].StartCount == 1 && playbacks[0].PlayCount == 1,
				"first line-in session did not start both devices");
			Assert(captures[0].DataHandlerCount == 1 && captures[0].StoppedHandlerCount == 1,
				"first line-in session did not attach handlers");
			var staleDataHandler = captures[0].DataHandler;
			var staleStoppedHandler = captures[0].StoppedHandler;

			decoder.StartListeningToLineIn();
			Assert(captures[0].StopCount == 1 && captures[0].DisposeCount == 1,
				"restarting did not stop and dispose the old capture");
			Assert(playbacks[0].StopCount == 1 && playbacks[0].DisposeCount == 1,
				"restarting did not stop and dispose the old playback");
			Assert(captures[0].DataHandlerCount == 0 && captures[0].StoppedHandlerCount == 0,
				"restarting left old capture handlers attached");
			Assert(captures[1].StartCount == 1 && playbacks[1].PlayCount == 1,
				"replacement line-in session did not start");
			staleDataHandler(captures[0], null);
			staleStoppedHandler(captures[0], new StoppedEventArgs());
			Assert(captures[1].DisposeCount == 0 && playbacks[1].DisposeCount == 0,
				"stale callbacks disposed the replacement line-in session");

			captures[1].RaiseRecordingStopped();
			Assert(!decoder.IsListeningToLineIn, "line-in state remained active after spontaneous stop");
			Assert(captures[1].DisposeCount == 1 && playbacks[1].DisposeCount == 1,
				"spontaneous capture stop did not dispose the session");
			Assert(captures[1].DataHandlerCount == 0 && captures[1].StoppedHandlerCount == 0,
				"spontaneous capture stop left handlers attached");

			decoder.StartListeningToLineIn();
			decoder.StopListening();
			Assert(!decoder.IsListeningToLineIn, "line-in state remained active after explicit stop");
			decoder.StopListening();
			Assert(captures[2].StopCount == 1 && captures[2].DisposeCount == 1,
				"explicit stop was not idempotent for capture");
			Assert(playbacks[2].StopCount == 1 && playbacks[2].DisposeCount == 1,
				"explicit stop was not idempotent for playback");
			Assert(stoppedEvents == 3, "line-in stop notifications did not cover restart, spontaneous, and explicit stops");
			decoder.FormClosing();
		}

		static void FailedLineInStartReleasesDevices()
		{
			var captures = new List<FakeLineInCapture>();
			var playbacks = new List<FakeLineInPlayback>();
			var failPlayback = true;
			var decoder = new ToneHandler(
				48000,
				false,
				rate =>
				{
					var capture = new FakeLineInCapture(rate);
					captures.Add(capture);
					return capture;
				},
				input =>
				{
					var playback = new FakeLineInPlayback { ThrowOnPlay = failPlayback };
					failPlayback = false;
					playbacks.Add(playback);
					return playback;
				});

			var failed = false;
			try
			{
				decoder.StartListeningToLineIn();
			}
			catch (InvalidOperationException exception)
			{
				failed = exception.Message == "Playback start failed.";
			}

			Assert(failed, "line-in setup failure was not preserved");
			Assert(captures[0].StopCount == 1 && captures[0].DisposeCount == 1,
				"failed start did not stop and dispose capture");
			Assert(playbacks[0].StopCount == 1 && playbacks[0].DisposeCount == 1,
				"failed start did not stop and dispose playback");
			Assert(captures[0].DataHandlerCount == 0 && captures[0].StoppedHandlerCount == 0,
				"failed start left handlers attached");

			decoder.StartListeningToLineIn();
			Assert(captures[1].StartCount == 1 && playbacks[1].PlayCount == 1,
				"decoder could not retry after a failed line-in start");
			decoder.StopListening();

			var subscriptionCapture = new FakeLineInCapture(48000) { ThrowOnStoppedHandlerAdd = true };
			var subscriptionPlayback = new FakeLineInPlayback();
			var subscriptionDecoder = new ToneHandler(
				48000,
				false,
				rate => subscriptionCapture,
				input => subscriptionPlayback);
			failed = false;
			try
			{
				subscriptionDecoder.StartListeningToLineIn();
			}
			catch (InvalidOperationException exception)
			{
				failed = exception.Message == "Stopped-handler subscription failed.";
			}
			Assert(failed, "event-subscription failure was not preserved");
			Assert(subscriptionCapture.DisposeCount == 1 && subscriptionPlayback.DisposeCount == 1,
				"event-subscription failure leaked line-in devices");
			Assert(subscriptionCapture.DataHandlerCount == 0 && subscriptionCapture.StoppedHandlerCount == 0,
				"event-subscription failure left a handler attached");

			var recordingDirectory = Path.Combine(Path.GetTempPath(), "bbc-line-in-start-failure-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(recordingDirectory);
			var recordingPath = Path.Combine(recordingDirectory, "failed.wav");
			var recordingCapture = new FakeLineInCapture(48000);
			var recordingDecoder = new ToneHandler(
				48000,
				false,
				rate => recordingCapture,
				input => new FakeLineInPlayback { ThrowOnPlay = true });
			try
			{
				failed = false;
				try { recordingDecoder.StartListeningToLineIn(recordingPath); }
				catch (InvalidOperationException exception) { failed = exception.Message == "Playback start failed."; }
				Assert(failed, "recording startup failure was not preserved");
				Assert(!File.Exists(recordingPath) && !Directory.GetFiles(recordingDirectory, "*.recording-*.tmp").Any(),
					"recording startup failure left an output or temporary WAV");
			}
			finally
			{
				recordingDecoder.FormClosing();
				DeleteFilesAndDirectory(recordingDirectory, recordingPath);
			}
		}

		static void SelectedLineInDeviceReachesFactory()
		{
			var requestedDevice = -1;
			var capture = new FakeLineInCapture(48000);
			var decoder = new ToneHandler(
				48000,
				false,
				(rate, device) =>
				{
					requestedDevice = device;
					return capture;
				},
				input => new FakeLineInPlayback());

			decoder.SetCaptureDeviceNumber(3);
			decoder.StartListeningToLineIn();
			Assert(requestedDevice == 3, "selected capture device was not passed to the factory");
			decoder.StopListening();
			decoder.SetCaptureDeviceNumber(5);
			decoder.StartListeningToLineIn();
			Assert(requestedDevice == 5, "updated capture device was not used by the next session");
			decoder.StopListening();
			decoder.FormClosing();
		}

		static void UniqueLowConfidenceRepairsPassCrc()
		{
			var source = MakeBlock("REPAIR", new byte[] { 1, 2, 3, 4 });
			var repairedHeaders = 0;
			var repairedData = 0;
			var repairedBits = 0;
			var detailedKinds = new List<string>();
			var parser = new BlockHandler();
			parser.EnableCrcRepair = true;
			parser.BlockHeaderReceived += (sender, value) => repairedHeaders++;
			parser.BlockDataReceived += (sender, value) => repairedData++;
			parser.CrcRepairAcceptedDetailed += (sender, value) =>
			{
				repairedBits += value.correctedBits;
				detailedKinds.Add(value.kind);
			};

			var corruptedHeader = (byte[])source.Clone();
			corruptedHeader[8] ^= 0x04;
			FeedWithConfidence(parser, corruptedHeader, 8);
			Assert(repairedHeaders == 1 && repairedData == 1,
				"a unique low-confidence header bit was not repaired");

			parser.ResetBlock();
			var corruptedData = (byte[])source.Clone();
			var dataIndex = 1 + "REPAIR".Length + 20 + 2;
			corruptedData[dataIndex] ^= 0x0C;
			FeedWithConfidence(parser, corruptedData, dataIndex, dataIndex);
			Assert(repairedHeaders == 2 && repairedData == 2,
				"a unique low-confidence data bit was not repaired");
			Assert(repairedBits == 3 && detailedKinds.SequenceEqual(new[] { "header", "data" }),
				"detailed CRC repair information did not report corrected bits and kinds");
		}

		static void HandlerForwardsDetailedCrcRepairInformation()
		{
			var handler = new BBCFileHandler(manageInputVolume: false);
			try
			{
				var field = typeof(BBCFileHandler).GetField("blockHandler", BindingFlags.Instance | BindingFlags.NonPublic);
				var parser = (BlockHandler)field.GetValue(handler);
				var repairCount = 0;
				var parserRepairCount = 0;
				var correctedBits = 0;
				string kind = null;
				handler.CrcRepairAcceptedDetailed += (sender, value) =>
				{
					repairCount++;
					correctedBits += value.correctedBits;
					kind = value.kind;
				};
				parser.CrcRepairAcceptedDetailed += (sender, value) => parserRepairCount++;
				handler.EnableCrcRepair = true;
				parser.EnableCrcRepair = true;
				var corrupted = (byte[])MakeBlock("REPAIR", new byte[] { 1, 2, 3 }).Clone();
				corrupted[8] ^= 0x04;
				FeedWithConfidence(parser, corrupted, 8);
				Assert(parserRepairCount == 1 && repairCount == 1 && correctedBits == 1 && kind == "header",
					"BBCFileHandler did not forward detailed CRC repair information: parser=" + parserRepairCount + " handler=" + repairCount);
			}
			finally { handler.FormClosing(); }
		}

		static void FeedWithConfidence(BlockHandler parser, byte[] block, params int[] lowConfidenceIndexes)
		{
			for (var index = 0; index < block.Length; index++)
			{
				var confidence = new float[] { 1, 1, 1, 1, 1, 1, 1, 1 };
				if (lowConfidenceIndexes.Contains(index))
				{
					confidence[2] = 0;
					confidence[3] = 0;
				}
				parser.AddByteWithConfidence(null, new ToneHandler.DecodedByteData(block[index], confidence));
			}
		}

		static void AssertNormalizedSamples(float[] sourceSamples, AudioChannelMode mode, float[] expected)
		{
			var normalized = ToneHandler.NormalizeInput(new ArraySampleProvider(sourceSamples, 48000, 2), mode);
			var actual = new float[expected.Length];
			var count = normalized.Read(actual, 0, actual.Length);
			Assert(count == expected.Length, mode + " returned the wrong sample count");
			for (var index = 0; index < expected.Length; index++)
				Assert(Math.Abs(actual[index] - expected[index]) < 0.000001f, mode + " channel conversion changed sample " + index);
		}

		static void CompatibleRecordingMatches()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-compatible-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, "synthetic.wav");
			try
			{
				var payload = new byte[] { 0x01, 0x23, 0x45, 0x67 };
				WritePcm16Wave(path, MakeWaveform(MakeBlock("SYNTH", payload)));
				var result = AnalyzeRecording(path);
				Assert(result.Bytes == 32, "decoded byte count changed: " + result.Bytes);
				Assert(result.Headers.SequenceEqual(new[] { "SYNTH" }), "decoded filenames changed");
				Assert(result.DataBlocks == 1, "data-block count changed: " + result.DataBlocks);
				Assert(result.Errors == 0, "synthetic recording reported errors: " + result.Errors);
				Assert(result.ValidPayloadStreamSha256 == "E314EC0E5963F2F9F74FF4E884CF1F09ABAF4FDE9024F4CD4A47807F8DA9F096",
					"valid payload stream SHA-256 changed: " + result.ValidPayloadStreamSha256);
			}
			finally
			{
				DeleteFilesAndDirectory(directory, path);
			}
		}

		static void FloatRecordingMatches()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-float-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, "synthetic-float.wav");
			try
			{
				var payload = new byte[] { 0x10, 0x20, 0x30 };
				var waveform = MakeWaveform(MakeBlock("FLOAT", payload));
				WriteFloatWave(path, Resample(waveform, 48000, 44100), 44100);
				var result = AnalyzeRecording(path);
				Assert(result.Bytes == 31, "decoded byte count changed: " + result.Bytes);
				Assert(result.Headers.SequenceEqual(new[] { "FLOAT" }), "decoded filenames changed");
				Assert(result.DataBlocks == 1, "data-block count changed: " + result.DataBlocks);
				Assert(result.Errors == 0, "synthetic float recording reported errors: " + result.Errors);
			}
			finally
			{
				DeleteFilesAndDirectory(directory, path);
			}
		}

		static void ConcurrentFileRequestsAreSerialized()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-concurrent-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, "synthetic.wav");
			WritePcm16Wave(path, MakeWaveform(MakeBlock("SERIAL", new byte[] { 1, 2, 3 })));
			var decoder = new ToneHandler(48000, false);
			var parser = new BlockHandler();
			var bytes = 0;
			var headers = 0;
			var dataBlocks = 0;
			var errors = 0;
			var completions = 0;

			decoder.ByteReceived += (sender, value) => { bytes++; parser.AddByte(sender, value); };
			decoder.InvalidToneData += parser.ToneError;
			decoder.InvalidToneData += (sender, marker) => { errors++; decoder.ResetWaitForTone(); parser.ResetBlock(); };
			decoder.FileListeningComplete += (sender, args) => completions++;
			parser.BlockHeaderReceived += (sender, value) => headers++;
			parser.BlockDataReceived += (sender, value) =>
			{
				dataBlocks++;
				decoder.ResetWaitForTone();
				parser.ResetBlock();
			};
			parser.Error += (sender, value) => { errors++; decoder.ResetWaitForTone(); parser.ResetBlock(); };

			try
			{
				var expected = AnalyzeRecording(path);
				var first = decoder.StartListeningToFileAsync(path);
				var second = decoder.StartListeningToFileAsync(path);
				Task.WhenAll(first, second).GetAwaiter().GetResult();

				Assert(bytes == expected.Bytes * 2, "concurrent requests changed the combined byte count: " + bytes);
				Assert(headers == expected.Headers.Count * 2, "concurrent requests changed the combined header count: " + headers);
				Assert(dataBlocks == expected.DataBlocks * 2, "concurrent requests changed the combined data-block count: " + dataBlocks);
				Assert(errors == expected.Errors * 2, "concurrent requests changed the combined error count: " + errors);
				Assert(completions == 2, "each serialized import should complete exactly once");
			}
			finally
			{
				decoder.FormClosing();
				DeleteFilesAndDirectory(directory, path);
			}
		}

		static void ActiveFileImportCanBeCancelled()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-cancel-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, "long-synthetic.wav");
			var restartPath = Path.Combine(directory, "restart.wav");
			var blocks = Enumerable.Range(0, 20)
				.SelectMany(index => MakeBlock("CANCEL", new byte[128], (ushort)index, index == 19))
				.ToArray();
			WritePcm16Wave(path, MakeWaveform(blocks, 30000));
			WritePcm16Wave(restartPath, MakeWaveform(MakeBlock("RESTART", new byte[] { 1 })));
			var decoder = new ToneHandler(48000, false);
			var completed = false;
			decoder.FileListeningComplete += (sender, args) => completed = true;

			try
			{
				var import = decoder.StartListeningToFileAsync(path);
				decoder.StopListening();
				var cancelled = false;
				try { import.GetAwaiter().GetResult(); }
				catch (OperationCanceledException) { cancelled = true; }

				Assert(cancelled, "active import did not report cancellation");
				Assert(!completed, "cancelled import reported successful completion");

				decoder.StartListeningToFileAsync(restartPath).GetAwaiter().GetResult();
				Assert(completed, "decoder could not start another import after cancellation");
			}
			finally
			{
				decoder.FormClosing();
				DeleteFilesAndDirectory(directory, path, restartPath);
			}
		}

		static void NewInputDiscardsUnfinishedBlock()
		{
			var directory = Path.Combine(Path.GetTempPath(), "bbc-cassette-reset-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			var retainedPath = Path.Combine(directory, "retained.wav");
			var partialPath = Path.Combine(directory, "partial.wav");
			var validPath = Path.Combine(directory, "valid.wav");

			BBCFileHandler handler = null;
			try
			{
				WritePcm16Wave(retainedPath, MakeWaveform(MakeBlock("KEEP", new byte[] { 0xA5 })));
				WritePcm16Wave(partialPath, MakeWaveform(new byte[] { 0x2A, (byte)'B', (byte)'A', (byte)'D' }));
				WritePcm16Wave(validPath, MakeWaveform(MakeBlock("GOOD", new byte[] { 0x12, 0x34 })));

				handler = new BBCFileHandler(directory, false);
				handler.StartListeningToFileAsync(retainedPath).GetAwaiter().GetResult();
				Assert(handler.files.ContainsKey("KEEP") && handler.files["KEEP"].IsComplete(),
					"initial recovered file was not complete");
				handler.StartListeningToFileAsync(partialPath).GetAwaiter().GetResult();
				Assert(handler.files.Count == 1, "partial input changed recovered files");
				handler.StartListeningToFileAsync(validPath).GetAwaiter().GetResult();

				Assert(handler.files.ContainsKey("KEEP") && handler.files["KEEP"].IsComplete(),
					"session reset discarded an earlier recovered file");
				Assert(handler.files.ContainsKey("GOOD"), "next input did not start at sync");
				Assert(handler.files["GOOD"].IsComplete(), "valid file was not recovered after the reset");
			}
			finally
			{
				handler?.FormClosing();
				if (File.Exists(retainedPath))
					File.Delete(retainedPath);
				if (File.Exists(partialPath))
					File.Delete(partialPath);
				if (File.Exists(validPath))
					File.Delete(validPath);
				if (Directory.Exists(directory))
					Directory.Delete(directory);
			}
		}

		static byte[] MakeBlock(
			string filename,
			byte[] payload,
			ushort blockNumber = 0,
			bool finalBlock = true,
			byte? blockFlag = null)
		{
			var header = new List<byte>(Encoding.ASCII.GetBytes(filename));
			header.Add(0);
			AddLittleEndian(header, 0xFFFF1900, 4);
			AddLittleEndian(header, 0xFFFF1900, 4);
			AddLittleEndian(header, blockNumber, 2);
			AddLittleEndian(header, (uint)payload.Length, 2);
			header.Add(blockFlag ?? (finalBlock ? (byte)0x80 : (byte)0));
			AddLittleEndian(header, 0, 4);

			var bytes = new List<byte> { 0x2A };
			bytes.AddRange(WithCrc(header));
			bytes.AddRange(WithCrc(payload));
			return bytes.ToArray();
		}

		static BBCFile FileFromBlock(byte[] block)
		{
			BBCFile file = null;
			var parser = new BlockHandler();
			parser.BlockHeaderReceived += (sender, bytes) =>
			{
				var header = new BlockHeader(bytes.ToArray());
				file = new BBCFile(ref header);
				file.AddHeader(ref header);
			};
			parser.BlockDataReceived += (sender, bytes) =>
			{
				var data = new BlockData(bytes.ToArray());
				file.AddData(ref data);
			};
			Feed(parser, block);
			Assert(file != null, "block did not produce a BBC file");
			return file;
		}

		static BlockHeader HeaderFromBlock(byte[] block)
		{
			BlockHeader? header = null;
			var parser = new BlockHandler();
			parser.BlockHeaderReceived += (sender, bytes) => header = new BlockHeader(bytes.ToArray());
			Feed(parser, block);
			Assert(header.HasValue, "block did not produce a header");
			return header.Value;
		}

		static IEnumerable<byte> WithCrc(IEnumerable<byte> input)
		{
			var bytes = input.ToList();
			var crc = Crc(bytes);
			bytes.Add((byte)(crc >> 8));
			bytes.Add((byte)crc);
			return bytes;
		}

		static ushort Crc(IEnumerable<byte> input)
		{
			uint crc = 0;
			foreach (var value in input)
			{
				crc ^= (uint)value << 8;
				for (var bit = 0; bit < 8; bit++)
					crc = (crc << 1 ^ ((crc & 0x8000) != 0 ? 0x1021u : 0)) & 0xFFFF;
			}
			return (ushort)crc;
		}

		static void AddLittleEndian(List<byte> target, uint value, int count)
		{
			for (var index = 0; index < count; index++)
				target.Add((byte)(value >> (index * 8)));
		}

		static void Feed(BlockHandler parser, IEnumerable<byte> bytes)
		{
			foreach (var value in bytes)
				parser.AddByte(null, value);
		}

		static void AssertParserError(IEnumerable<byte> block, string expected)
		{
			var parser = new BlockHandler();
			string actual = null;
			parser.Error += (sender, message) => actual = message;
			Feed(parser, block);
			Assert(actual == expected, "expected '" + expected + "', received '" + actual + "'");
		}

		static float[] MakeWaveform(IEnumerable<byte> bytes, int carrierCycles = 1500)
		{
			var bits = new List<int>();
			foreach (var value in bytes)
				AddFrame(bits, value);
			return MakeBitWaveform(bits, carrierCycles);
		}

		static float[] MakeContinuousPhaseWaveform(IEnumerable<byte> bytes, double phase)
		{
			var bits = new List<int>();
			foreach (var value in bytes) AddFrame(bits, value);
			var samples = new List<float>();
			var angle = phase;
			const int sampleRate = 48000;
			const int samplesPerBit = 40;
			for (var index = 0; index < 1500 * 20; index++)
			{
				var frequency = 2400;
				samples.Add((float)(100.0 / 128.0 * Math.Sin(angle)));
				angle += 2 * Math.PI * frequency / sampleRate;
			}
			foreach (var bit in bits)
			{
				var frequency = bit == 0 ? 1200 : 2400;
				for (var index = 0; index < samplesPerBit; index++)
				{
					samples.Add((float)(100.0 / 128.0 * Math.Sin(angle)));
					angle += 2 * Math.PI * frequency / sampleRate;
				}
			}
			for (var index = 0; index < 400; index++)
			{
				samples.Add((float)(100.0 / 128.0 * Math.Sin(angle)));
				angle += 2 * Math.PI * 2400 / sampleRate;
			}
			return samples.ToArray();
		}

		static void AddFrame(List<int> bits, byte value, int stopBit = 1)
		{
			bits.Add(0);
			for (var bit = 0; bit < 8; bit++)
				bits.Add((value >> bit) & 1);
			bits.Add(stopBit);
		}

		static float[] MakeBitWaveform(IEnumerable<int> bits, int carrierCycles = 1500)
		{
			var samples = new List<float>();
			AddCarrier(samples, carrierCycles);
			foreach (var bit in bits)
				AddBit(samples, bit);
			// HandleData intentionally retains scanDistance * 40 samples as look-ahead.
			for (var cycle = 0; cycle < 10; cycle++)
				AddCycles(samples, 2400, 1);
			return samples.ToArray();
		}

		static float[] MakeRecoveryWaveform(IEnumerable<int> beforeRecovery, IEnumerable<int> afterRecovery)
		{
			var samples = new List<float>();
			AddCarrier(samples);
			foreach (var bit in beforeRecovery)
				AddBit(samples, bit);
			AddCarrier(samples);
			foreach (var bit in afterRecovery)
				AddBit(samples, bit);
			for (var cycle = 0; cycle < 10; cycle++)
				AddCycles(samples, 2400, 1);
			return samples.ToArray();
		}

		static void AddCarrier(List<float> samples, int cycles = 1500)
		{
			for (var cycle = 0; cycle < cycles; cycle++)
				AddCycles(samples, 2400, 1);
		}

		static void AddBit(List<float> samples, int bit, double amplitudeScale = 1)
		{
			AddCycles(samples, bit == 0 ? 1200 : 2400, bit == 0 ? 1 : 2, amplitudeScale);
		}

		static void AddCycles(List<float> samples, int frequency, int cycles, double amplitudeScale = 1)
		{
			var samplesPerCycle = 48000 / frequency;
			for (var cycle = 0; cycle < cycles; cycle++)
				for (var index = 0; index < samplesPerCycle; index++)
					samples.Add((float)(amplitudeScale * 100.0 / 128.0 * Math.Sin(2 * Math.PI * index / samplesPerCycle)));
		}

		static void WritePcm16Wave(string path, IEnumerable<float> samples)
		{
			var values = samples.ToArray();
			const int sampleRate = 48000;
			const short channels = 1;
			const short bitsPerSample = 16;
			var dataLength = values.Length * sizeof(short);

			using (var writer = new BinaryWriter(File.Create(path)))
			{
				writer.Write(Encoding.ASCII.GetBytes("RIFF"));
				writer.Write(36 + dataLength);
				writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
				writer.Write(16);
				writer.Write((short)1);
				writer.Write(channels);
				writer.Write(sampleRate);
				writer.Write(sampleRate * channels * bitsPerSample / 8);
				writer.Write((short)(channels * bitsPerSample / 8));
				writer.Write(bitsPerSample);
				writer.Write(Encoding.ASCII.GetBytes("data"));
				writer.Write(dataLength);
				foreach (var value in values)
					writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, value)) * short.MaxValue));
			}
		}

		static void WritePcm8Wave(string path, IEnumerable<float> samples, int sampleRate)
		{
			var values = samples.ToArray();
			using (var writer = OpenWave(path, 1, 1, sampleRate, 8, values.Length))
				foreach (var value in values)
					writer.Write((byte)Math.Round((Math.Max(-1, Math.Min(1, value)) + 1) * 127.5));
		}

		static void WriteFloatWave(string path, IEnumerable<float> samples, int sampleRate)
		{
			var values = samples.ToArray();
			using (var writer = OpenWave(path, 3, 1, sampleRate, 32, values.Length))
				foreach (var value in values)
					writer.Write(value);
		}

		static BinaryWriter OpenWave(string path, ushort format, ushort channels, int sampleRate, ushort bitsPerSample, int sampleCount)
		{
			var bytesPerSample = bitsPerSample / 8;
			var dataLength = sampleCount * channels * bytesPerSample;
			var writer = new BinaryWriter(File.Create(path));
			writer.Write(Encoding.ASCII.GetBytes("RIFF"));
			writer.Write(36 + dataLength);
			writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
			writer.Write(16);
			writer.Write(format);
			writer.Write(channels);
			writer.Write(sampleRate);
			writer.Write(sampleRate * channels * bytesPerSample);
			writer.Write((short)(channels * bytesPerSample));
			writer.Write(bitsPerSample);
			writer.Write(Encoding.ASCII.GetBytes("data"));
			writer.Write(dataLength);
			return writer;
		}

		static float[] Resample(float[] samples, int sourceRate, int destinationRate)
		{
			var outputLength = (int)Math.Round(samples.Length * (double)destinationRate / sourceRate);
			var output = new float[outputLength];
			for (var index = 0; index < output.Length; index++)
			{
				var sourcePosition = index * (double)sourceRate / destinationRate;
				var lower = Math.Min((int)sourcePosition, samples.Length - 1);
				var upper = Math.Min(lower + 1, samples.Length - 1);
				var fraction = sourcePosition - lower;
				output[index] = (float)(samples[lower] + (samples[upper] - samples[lower]) * fraction);
			}
			return output;
		}

		static void DeleteFilesAndDirectory(string directory, params string[] files)
		{
			foreach (var file in files)
				if (File.Exists(file))
					File.Delete(file);
			if (Directory.Exists(directory))
				Directory.Delete(directory);
		}

		static void AssertWave(string path, ushort format, ushort channels, uint rate, ushort bits)
		{
			using (var reader = new BinaryReader(File.OpenRead(path)))
			{
				reader.BaseStream.Position = 12;
				while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
				{
					var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
					var size = reader.ReadUInt32();
					if (id == "fmt ")
					{
						Assert(reader.ReadUInt16() == format, Path.GetFileName(path) + " format changed");
						Assert(reader.ReadUInt16() == channels, Path.GetFileName(path) + " channels changed");
						Assert(reader.ReadUInt32() == rate, Path.GetFileName(path) + " sample rate changed");
						reader.ReadUInt32();
						reader.ReadUInt16();
						Assert(reader.ReadUInt16() == bits, Path.GetFileName(path) + " bit depth changed");
						return;
					}
					reader.BaseStream.Position += size + size % 2;
				}
			}
			throw new InvalidDataException("No fmt chunk in " + path);
		}

		sealed class ArraySampleProvider : ISampleProvider
		{
			readonly float[] samples;
			int position;

			public ArraySampleProvider(float[] samples, int sampleRate, int channels)
			{
				this.samples = samples;
				WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
			}

			public WaveFormat WaveFormat { get; private set; }

			public int Read(float[] buffer, int offset, int count)
			{
				var available = Math.Min(count, samples.Length - position);
				Array.Copy(samples, position, buffer, offset, available);
				position += available;
				return available;
			}
		}

		sealed class FakeLineInCapture : ILineInCapture
		{
			EventHandler<WaveInEventArgs> dataAvailable;
			EventHandler<StoppedEventArgs> recordingStopped;

			public FakeLineInCapture(int sampleRate)
			{
				WaveFormat = new WaveFormat(sampleRate, 16, 1);
			}

			public event EventHandler<WaveInEventArgs> DataAvailable
			{
				add { dataAvailable += value; }
				remove { dataAvailable -= value; }
			}

			public event EventHandler<StoppedEventArgs> RecordingStopped
			{
				add
				{
					if (ThrowOnStoppedHandlerAdd)
						throw new InvalidOperationException("Stopped-handler subscription failed.");
					recordingStopped += value;
				}
				remove { recordingStopped -= value; }
			}

			public WaveFormat WaveFormat { get; private set; }
			public bool ThrowOnStoppedHandlerAdd { get; set; }
			public int StartCount { get; private set; }
			public int StopCount { get; private set; }
			public int DisposeCount { get; private set; }
			public int DataHandlerCount => dataAvailable?.GetInvocationList().Length ?? 0;
			public int StoppedHandlerCount => recordingStopped?.GetInvocationList().Length ?? 0;
			public EventHandler<WaveInEventArgs> DataHandler => dataAvailable;
			public EventHandler<StoppedEventArgs> StoppedHandler => recordingStopped;

			public void StartRecording()
			{
				StartCount++;
			}

			public void StopRecording()
			{
				StopCount++;
				RaiseRecordingStopped();
			}

			public void RaiseRecordingStopped()
			{
				recordingStopped?.Invoke(this, new StoppedEventArgs());
			}

			public void RaiseData(byte[] buffer, int bytesRecorded)
			{
				dataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytesRecorded));
			}

			public void Dispose()
			{
				DisposeCount++;
			}
		}

		sealed class FakeLineInPlayback : ILineInPlayback
		{
			public bool ThrowOnPlay { get; set; }
			public int PlayCount { get; private set; }
			public int StopCount { get; private set; }
			public int DisposeCount { get; private set; }

			public void Play()
			{
				PlayCount++;
				if (ThrowOnPlay)
					throw new InvalidOperationException("Playback start failed.");
			}

			public void Stop()
			{
				StopCount++;
			}

			public void Dispose()
			{
				DisposeCount++;
			}
		}

		static string Hex(IEnumerable<byte> bytes)
		{
			return string.Join(" ", bytes.Select(value => value.ToString("X2")));
		}

		static void AssertThrows<TException>(Action action) where TException : Exception
		{
			try
			{
				action();
			}
			catch (TException)
			{
				return;
			}
			throw new InvalidOperationException("Expected " + typeof(TException).Name + " was not thrown");
		}

		static void Assert(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}
	}
}
