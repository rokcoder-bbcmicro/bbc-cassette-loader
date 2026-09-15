using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/* What we're analysing
 * ====================
 * We're interested in two distinct tones - 
 *      1200 Hz - One sinusoidal wave represents a ZERO
 *      2400 Hz - Two sinusoidal waves represent a ONE
 *      
 * I'm recording at f = 48000 Hz (unsigned 8-bit) so one sinusoidal wave at 1200 Hz or two sinusoidal waves at 2400 Hz take approximately 40 sample values
 * 
 * What problems exhibit on cassette rcordings
 * ===========================================
 * Looking through the recording on lots of cassettes I have found the following issues -
 * - Slight innaccuracies, etc. as would be expected
 * - Large discrepencies between the amplitude of 1200Hz and 2400Hz sinusoidal waves
 * - The waves not being centred at an amplitude of zero - in fact the centre point seeming to move on a slight sinusoidal wave itself in some cases!
 * 
 * Solutions proposed/used
 * =======================
 * As we can't rely on constant amplitudes or even a steady baseline, each wave is anaylsed based on maximum and minimum points
 * Each sinusoidal wave is a full one. This means that consecutive waves are joined on the upswing (at the end of one wave and the beginning of the next)
 * The simplest method is to interpolate between maximum and minimum points to assess where the base/zero line is crossed and to use that to calculate the frequency
 * The main problem with this method is that waves of different frequencies are consecutively joined so performing that calculation on an upwardly moving sample set gives incorrect values if a 1200Hz and 2400Hz wave connect
 * So I'm calculating on the downswing only
 * I'm not actually verifying that consecutive waves are connected but that should be caught automatically anyway
 * 
 * BBC Micro cassette data format
 * ==============================
 * A leader tone is 2400Hz for approximately 5 seconds (there's also a dummy byte of 0xAA at the beginning of a leader tone which I'm ignoring at the moment)
 * Between data blocks we have 2400Hz for approximately 1 second
 * A trailer tone is 2400Hz for approximately 5 seconds
 * 
 * Byte data is stored as one start bit (0), 8 data bits and one stop bit (1)
 * 
 * A data block consists of -
 * 
 * One synchronisation byte (&2A)
 * The block header:
 * - File name (one to ten characters).
 * - One end of file name marker byte (&00).
 * - Load address of file, four bytes, low byte first.
 * - Execution address of file, four bytes, low byte first.
 * - Block number, two bytes, low byte first.
 * - Data block length, two bytes, low byte first.
 * - Block flag, one byte.
 * - Address of next file, four bytes. See Data layer above.
 * CRC on header, two bytes.
 * Data, number of bytes as stated in the data block length field.
 * CRC on data, two bytes. Omitted if data block length = 0.
 */

// This class should collect data between lead-ins and nothing else
// That data should then be passed to FileHandler for CRC checks, verification, storage, etc

namespace bbc_cassette_loader
{
	internal interface ILineInCapture : IDisposable
	{
		event EventHandler<WaveInEventArgs> DataAvailable;
		event EventHandler<StoppedEventArgs> RecordingStopped;
		WaveFormat WaveFormat { get; }
		void StartRecording();
		void StopRecording();
	}

	internal interface ILineInPlayback : IDisposable
	{
		void Play();
		void Stop();
	}

	sealed class WaveInCapture : ILineInCapture
	{
		readonly WaveIn capture;

		public WaveInCapture(int sampleRate, int deviceNumber)
		{
			capture = new WaveIn
			{
				DeviceNumber = deviceNumber,
				WaveFormat = new WaveFormat(sampleRate, 16, 1)
			};
		}

		public event EventHandler<WaveInEventArgs> DataAvailable
		{
			add { capture.DataAvailable += value; }
			remove { capture.DataAvailable -= value; }
		}

		public event EventHandler<StoppedEventArgs> RecordingStopped
		{
			add { capture.RecordingStopped += value; }
			remove { capture.RecordingStopped -= value; }
		}

		public WaveFormat WaveFormat => capture.WaveFormat;
		public void StartRecording() => capture.StartRecording();
		public void StopRecording() => capture.StopRecording();
		public void Dispose() => capture.Dispose();
	}

	public sealed class LineInRecordingResult : EventArgs
	{
		public readonly string FilePath;
		public readonly Exception Error;
		public readonly long BytesWritten;
		public readonly bool Discarded;

		internal LineInRecordingResult(string filePath, Exception error, long bytesWritten, bool discarded)
		{
			FilePath = filePath;
			Error = error;
			BytesWritten = bytesWritten;
			Discarded = discarded;
		}
	}

	sealed class LineInRecorder
	{
		const int MaximumPendingBuffers = 64;
		readonly string outputPath;
		readonly string temporaryPath;
		readonly BlockingCollection<byte[]> pendingBuffers =
			new BlockingCollection<byte[]>(MaximumPendingBuffers);
		readonly WaveFileWriter writer;
		readonly Task writerTask;
		readonly object stopLock = new object();
		Exception error;
		LineInRecordingResult result;
		int stopping;
		long bytesWritten;

		public LineInRecorder(string path, WaveFormat format)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A WAV output path is required.", nameof(path));
			if (format == null)
				throw new ArgumentNullException(nameof(format));

			outputPath = Path.GetFullPath(path);
			var directory = Path.GetDirectoryName(outputPath);
			if (string.IsNullOrEmpty(directory))
				throw new InvalidOperationException("The WAV output path has no directory.");
			Directory.CreateDirectory(directory);
			temporaryPath = outputPath + ".recording-" + Guid.NewGuid().ToString("N") + ".tmp";
			writer = new WaveFileWriter(temporaryPath, format);
			writerTask = Task.Factory.StartNew(
				WritePendingBuffers,
				CancellationToken.None,
				TaskCreationOptions.LongRunning,
				TaskScheduler.Default);
		}

		public bool TryWrite(byte[] buffer, int bytesRecorded)
		{
			if (buffer == null || bytesRecorded < 0 || bytesRecorded > buffer.Length)
				return false;
			if (Thread.VolatileRead(ref stopping) != 0 || error != null)
				return false;

			var copy = new byte[bytesRecorded];
			Buffer.BlockCopy(buffer, 0, copy, 0, bytesRecorded);
			try
			{
				if (pendingBuffers.TryAdd(copy))
					return true;
			}
			catch (InvalidOperationException)
			{
				// Stop raced the capture callback. The session owns finalization.
				return false;
			}

			SetError(new IOException("The WAV recording queue is full; live decoding continued without recording the dropped audio."));
			return false;
		}

		void WritePendingBuffers()
		{
			try
			{
				foreach (var buffer in pendingBuffers.GetConsumingEnumerable())
				{
					writer.Write(buffer, 0, buffer.Length);
					Interlocked.Add(ref bytesWritten, buffer.Length);
				}
			}
			catch (Exception exception)
			{
				SetError(exception);
			}
		}

		void SetError(Exception exception)
		{
			Interlocked.CompareExchange(ref error, exception, null);
			try { pendingBuffers.CompleteAdding(); }
			catch (InvalidOperationException) { }
		}

		public LineInRecordingResult Stop(bool discard)
		{
			lock (stopLock)
			{
				if (result != null)
					return result;
				Interlocked.Exchange(ref stopping, 1);
				try { pendingBuffers.CompleteAdding(); }
				catch (InvalidOperationException) { }
				try { writerTask.Wait(); }
				catch (AggregateException aggregate)
				{
					SetError(aggregate.GetBaseException());
				}
				try { writer.Dispose(); }
				catch (Exception exception) { SetError(exception); }

				if (discard || error != null)
				{
					try
					{
						if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
					}
					catch (Exception exception) { SetError(exception); }
				}
				else
				{
					try
					{
						if (File.Exists(outputPath))
							File.Replace(temporaryPath, outputPath, null);
						else
							File.Move(temporaryPath, outputPath);
					}
					catch (Exception exception) { SetError(exception); }
				}
				result = new LineInRecordingResult(outputPath, error, Interlocked.Read(ref bytesWritten), discard || error != null);
				return result;
			}
		}
	}

	sealed class WaveOutPlayback : ILineInPlayback
	{
		readonly WaveOut playback;

		public WaveOutPlayback(ISampleProvider input)
		{
			playback = new WaveOut();
			playback.Init(input);
		}

		public void Play() => playback.Play();
		public void Stop() => playback.Stop();
		public void Dispose() => playback.Dispose();
	}

	public enum AudioChannelMode
	{
		Mix,
		Left,
		Right
	}

	public class ToneHandler
	{
		public struct DecodedByteData
		{
			public readonly byte value;
			public readonly float[] bitConfidence;

			public DecodedByteData(byte value, float[] bitConfidence)
			{
				this.value = value;
				this.bitConfidence = bitConfidence;
			}
		}

		public event EventHandler<DecodedByteData> DecodedByteReceived;
		public static IReadOnlyList<string> GetCaptureDeviceNames()
		{
			var names = new List<string>();
			for (var index = 0; index < WaveIn.DeviceCount; index++)
				names.Add(WaveIn.GetCapabilities(index).ProductName);
			return names;
		}

		public struct FileImportProgressData
		{
			public readonly string fileName;
			public readonly int passNumber;
			public readonly int passCount;
			public readonly string profileName;
			public readonly long samplesRead;
			public readonly long totalSamples;

			public FileImportProgressData(
				string fileName,
				int passNumber,
				int passCount,
				string profileName,
				long samplesRead,
				long totalSamples)
			{
				this.fileName = fileName;
				this.passNumber = passNumber;
				this.passCount = passCount;
				this.profileName = profileName;
				this.samplesRead = samplesRead;
				this.totalSamples = totalSamples;
			}
		}

		sealed class LineInSession
		{
			public readonly ILineInCapture Capture;
			public readonly ILineInPlayback Playback;
			public readonly BufferedWaveProvider Buffer;
			public readonly LineInRecorder Recorder;
			public EventHandler<WaveInEventArgs> DataAvailableHandler;
			public EventHandler<StoppedEventArgs> RecordingStoppedHandler;

			public LineInSession(
				ILineInCapture capture,
				ILineInPlayback playback,
				BufferedWaveProvider buffer,
				LineInRecorder recorder)
			{
				Capture = capture;
				Playback = playback;
				Buffer = buffer;
				Recorder = recorder;
			}
		}

		enum DecodeProfile
		{
			// Order is cumulative: established profiles stay first so later targeted
			// profiles can skip every payload that an earlier profile validated.
			Standard,
			Inverted,
			GentleHighPass,
			GentleHighPassInverted,
			NarrowLowPassInverted,
			WideLowPass,
			StrongHighPass,
			PhaseShift
		}

		public event EventHandler<byte> ByteReceived;
		public event EventHandler<MarkerData> InvalidToneData;
		public event EventHandler<ErrorDataForGraph> DiagnosticAvailable;
		public event EventHandler<FileImportProgressData> FileImportProgress;
		internal event EventHandler InputSessionStarted;
		public event EventHandler FileListeningComplete;
		public event EventHandler LineInStopped;
		public event EventHandler<LineInRecordingResult> LineInRecordingStopped;

		public bool IsListeningToLineIn
		{
			get
			{
				lock (lineInLock)
					return lineInSession != null;
			}
		}

		public bool IsRecordingToFile
		{
			get
			{
				lock (lineInLock)
					return lineInSession != null && lineInSession.Recorder != null;
			}
		}

		readonly int sampleFrequency;
		readonly int scanDistance;
		readonly List<float> sampleData;
		readonly int oneBitWavesInHalfSecond;
		readonly int shortestOneHalfCycle;
		readonly int longestOneHalfCycle;
		readonly int longestZeroHalfCycle;
		readonly int diagnosticRange;
		readonly int retainedProcessingSamples;
		readonly SemaphoreSlim fileImportGate = new SemaphoreSlim(1, 1);
		readonly object fileCancellationLock = new object();
		readonly object lineInLock = new object();
		readonly object lineInProcessingLock = new object();
		readonly object lineInLifecycleLock = new object();
		readonly Func<int, int, ILineInCapture> selectedLineInCaptureFactory;
		readonly Func<ISampleProvider, ILineInPlayback> lineInPlaybackFactory;
		readonly List<SampleRange> validBlockRanges = new List<SampleRange>();

		const int SampleRate = 48000;
		const int DecodingHighPassCutoffHz = 300;
		const int DecodingLowPassCutoffHz = 6000;
		const float DecodingHighPassQ = 1f;
		const float DecodingLowPassQ = 0.7071f;
		const int RecoveryHighPassCutoffHz = 50;
		const float RecoveryHighPassQ = 0.707f;
		const int StrongHighPassCutoffHz = 800;
		const int NarrowLowPassCutoffHz = 4000;
		const int WideLowPassCutoffHz = 8000;
		const int MinimumZeroToneHz = 800;
		const int ToneBoundaryHz = 1600;
		const int MaximumOneToneHz = 3000;
		const int DiagnosticContextBits = 40;
		const int SeekPreRollSamples = SampleRate / 10;
		internal const int MaximumRetainedDiagnostics = 20;

		DecodeProfile decodeProfile;
		internal bool IsRecoveryPass => decodeProfile != DecodeProfile.Standard;
		internal static int RecoveryProfileCount => Enum.GetValues(typeof(DecodeProfile)).Length;
		internal static string GetRecoveryProfileName(int index) => ((DecodeProfile)index).ToString();
		// T028: the alternate alignment is a final complementary recovery profile.
		bool phaseShift => decodeProfile == DecodeProfile.PhaseShift;
		bool InvertSamples => decodeProfile == DecodeProfile.Inverted ||
			decodeProfile == DecodeProfile.GentleHighPassInverted ||
			decodeProfile == DecodeProfile.NarrowLowPassInverted;

		readonly int initialInputVolume;
		readonly bool manageInputVolume;
		int captureDeviceNumber;
		int inputVolume;

		LineInSession lineInSession;
		CancellationTokenSource activeFileCancellation;

		int streamIndex;
		int peakIndex;
		long sampleDataOffset;
		bool readingData;
		int toneReps;
		int bitCount;
		byte byteValue;
		float bitConfidence;
		readonly float[] byteBitConfidence = new float[8];

		int incomingCounter;
		Queue<int> incomingTracker;
		long nextDiagnosticId;

		enum ExtremeType
		{
			peak,
			trough,
			unknown
		}

		ExtremeType lastExtreme;
		int lastToneMarker;
		int samplesPerBit;
		BiQuadFilter decodingHighPassFilter;
		BiQuadFilter decodingLowPassFilter;

		public struct MarkerData
		{
			public int markerPosition;
			public string markerDescription;
			public long diagnosticId;

			public MarkerData(int m, string t, long id = 0)
			{
				markerPosition = m;
				markerDescription = t;
				diagnosticId = id;
			}
		}
		readonly List<MarkerData> pendingErrorMarkers;
		readonly List<MarkerData> byteMarkers;
		readonly List<MarkerData> bitMarkers;
		readonly List<ErrorDataForGraph> errorDiagnostics;
		internal long CurrentSamplePosition => sampleDataOffset + streamIndex;
		internal long RecoverySamplesProcessed { get; private set; }

		struct SampleRange
		{
			public long Start;
			public long End;

			public SampleRange(long start, long end)
			{
				Start = start;
				End = end;
			}
		}

		public struct ErrorDataForGraph
		{
			public MarkerData errorData;
			public List<MarkerData> byteBoundaryMarker;
			public List<MarkerData> bitBoundaryMarker;
			public List<float> data;

			public ErrorDataForGraph(MarkerData e)
			{
				errorData = e;
				byteBoundaryMarker = new List<MarkerData>();
				bitBoundaryMarker = new List<MarkerData>();
				data = null;
			}
		}

		struct DiagnosticBit
		{
			public int position;
			public byte value;

			public DiagnosticBit(int position, byte value)
			{
				this.position = position;
				this.value = value;
			}
		}

		public ToneHandler(int sampleRate = SampleRate, bool manageInputVolume = false, int captureDeviceNumber = 0)
			: this(
				sampleRate,
				manageInputVolume,
				(rate, device) => new WaveInCapture(rate, device),
				input => new WaveOutPlayback(input))
		{
			SetCaptureDeviceNumber(captureDeviceNumber);
		}

		internal ToneHandler(
			int sampleRate,
			bool manageInputVolume,
			Func<int, ILineInCapture> lineInCaptureFactory,
			Func<ISampleProvider, ILineInPlayback> lineInPlaybackFactory)
			: this(
				sampleRate,
				manageInputVolume,
				(rate, device) => lineInCaptureFactory(rate),
				lineInPlaybackFactory)
		{
			if (lineInCaptureFactory == null)
				throw new ArgumentNullException(nameof(lineInCaptureFactory));
		}

		internal ToneHandler(
			int sampleRate,
			bool manageInputVolume,
			Func<int, int, ILineInCapture> lineInCaptureFactory,
			Func<ISampleProvider, ILineInPlayback> lineInPlaybackFactory)
		{
			if (sampleRate < 9600 || sampleRate % 4 != 0)
				throw new ArgumentOutOfRangeException(nameof(sampleRate), "The decoder sample rate must be at least 9600 Hz and divisible by four.");
			if (lineInCaptureFactory == null)
				throw new ArgumentNullException(nameof(lineInCaptureFactory));
			if (lineInPlaybackFactory == null)
				throw new ArgumentNullException(nameof(lineInPlaybackFactory));

			incomingTracker = new Queue<int>();
			this.manageInputVolume = manageInputVolume;
			this.selectedLineInCaptureFactory = lineInCaptureFactory;
			this.lineInPlaybackFactory = lineInPlaybackFactory;
			initialInputVolume = manageInputVolume ? GetCurrentInputVolume() : 0;
			inputVolume = 50;
			if (manageInputVolume)
				SetCurrentInputVolume(inputVolume);

			sampleFrequency = sampleRate;
			sampleData = new List<float>(sampleFrequency);
			oneBitWavesInHalfSecond = 2400 / 2;
			shortestOneHalfCycle = (int)Math.Ceiling(sampleFrequency / (2.0 * MaximumOneToneHz));
			longestOneHalfCycle = sampleFrequency / (2 * ToneBoundaryHz);
			longestZeroHalfCycle = sampleFrequency / (2 * MinimumZeroToneHz);
			scanDistance = sampleFrequency / 2400 / 4;
			diagnosticRange = sampleFrequency / 1200 * DiagnosticContextBits;
			retainedProcessingSamples = diagnosticRange * 2 + scanDistance * 40;
			byteMarkers = new List<MarkerData>();
			bitMarkers = new List<MarkerData>();
			pendingErrorMarkers = new List<MarkerData>();
			errorDiagnostics = new List<ErrorDataForGraph>();

			ResetForNewInput();
		}

		public void SetCaptureDeviceNumber(int deviceNumber)
		{
			if (deviceNumber < 0)
				throw new ArgumentOutOfRangeException(nameof(deviceNumber));
			lock (lineInLifecycleLock)
			{
				captureDeviceNumber = deviceNumber;
			}
		}

		void ResetForNewInput(bool resetFilters = true)
		{
			if (resetFilters) ResetDecodingFilters();
			sampleData.Clear();
			streamIndex = scanDistance;
			sampleDataOffset = 0;
			// streamIndex = (int)((1356.969) * 48000); //TODO This is specific to where I'm seeing an error in a certain file
			peakIndex = 0;
			byteMarkers.Clear();
			bitMarkers.Clear();
			pendingErrorMarkers.Clear();
			errorDiagnostics.Clear();
			incomingCounter = 0;
		}

		void ResetDecodingFilters()
		{
			var gentleHighPass = decodeProfile == DecodeProfile.GentleHighPass ||
				decodeProfile == DecodeProfile.GentleHighPassInverted;
			var strongHighPass = decodeProfile == DecodeProfile.StrongHighPass;
			var highPassCutoff = gentleHighPass ? RecoveryHighPassCutoffHz :
				strongHighPass ? StrongHighPassCutoffHz : DecodingHighPassCutoffHz;
			var highPassQ = gentleHighPass || strongHighPass ? RecoveryHighPassQ : DecodingHighPassQ;
			var lowPassCutoff = decodeProfile == DecodeProfile.NarrowLowPassInverted ? NarrowLowPassCutoffHz :
				decodeProfile == DecodeProfile.WideLowPass ?
				WideLowPassCutoffHz : DecodingLowPassCutoffHz;
			decodingHighPassFilter = BiQuadFilter.HighPassFilter(
				sampleFrequency,
				highPassCutoff,
				highPassQ);
			decodingLowPassFilter = BiQuadFilter.LowPassFilter(
				sampleFrequency,
				lowPassCutoff,
				DecodingLowPassQ);
		}

		public void StartListeningToLineIn(string recordingFile = null)
		{
			lock (fileCancellationLock)
			{
				if (activeFileCancellation != null)
					throw new InvalidOperationException("Line-in cannot start while a file import is active.");
				lock (lineInLifecycleLock)
				{
					StopListeningToLineInCore();
					decodeProfile = DecodeProfile.Standard;
					BeginInputSession();

					ILineInCapture capture = null;
					ILineInPlayback playback = null;
					LineInRecorder recorder = null;
					LineInSession session = null;
					try
					{
						capture = selectedLineInCaptureFactory(sampleFrequency, captureDeviceNumber)
							?? throw new InvalidOperationException("The line-in capture factory returned no device.");
						var buffer = new BufferedWaveProvider(capture.WaveFormat)
						{
							DiscardOnBufferOverflow = true
						};
						var filteredInput = new HighPassFilter(buffer.ToSampleProvider(), 800);
						playback = lineInPlaybackFactory(filteredInput)
							?? throw new InvalidOperationException("The line-in playback factory returned no device.");
						if (recordingFile != null)
						{
							if (capture.WaveFormat.Encoding != WaveFormatEncoding.Pcm ||
								capture.WaveFormat.BitsPerSample != 16 ||
								capture.WaveFormat.Channels != 1 ||
								capture.WaveFormat.SampleRate != sampleFrequency)
								throw new InvalidOperationException("Line-in recording requires 48 kHz mono 16-bit PCM capture.");
							recorder = new LineInRecorder(recordingFile, capture.WaveFormat);
						}
						session = new LineInSession(capture, playback, buffer, recorder);
						session.DataAvailableHandler = (sender, args) => StreamInData(session, args);
						session.RecordingStoppedHandler = (sender, args) => LineInCaptureStopped(session);
						lock (lineInLock)
							lineInSession = session;
						capture.DataAvailable += session.DataAvailableHandler;
						capture.RecordingStopped += session.RecordingStoppedHandler;

						playback.Play();
						capture.StartRecording();
					}
					catch
					{
						try
						{
							if (session != null)
							{
								if (DetachLineInSession(session))
									DisposeLineInSession(session, true, true);
							}
							else
							{
								playback?.Dispose();
								capture?.Dispose();
							}
						}
						catch
						{
							// Preserve the setup failure after making a best effort to release devices.
						}
						throw;
					}
				}
			}
		}

		void StreamInData(LineInSession session, WaveInEventArgs waveInEventArgs)
		{
			lock (lineInProcessingLock)
			{
				lock (lineInLock)
				{
					if (!ReferenceEquals(lineInSession, session))
						return;

					session.Recorder?.TryWrite(waveInEventArgs.Buffer, waveInEventArgs.BytesRecorded);
					session.Buffer.AddSamples(waveInEventArgs.Buffer, 0, waveInEventArgs.BytesRecorded);
				}
				HandleData(ConvertPcm16(waveInEventArgs.Buffer, waveInEventArgs.BytesRecorded));
			}
		}

		void StopListeningToLineIn()
		{
			lock (lineInLifecycleLock)
				StopListeningToLineInCore();
		}

		void StopListeningToLineInCore()
		{
			LineInSession session;
			lock (lineInLock)
			{
				session = lineInSession;
				lineInSession = null;
			}
			if (session == null)
				return;

			DetachLineInHandlers(session);
			try
			{
				lock (lineInProcessingLock) { }
				FinalizeDiagnostics();
			}
			finally
			{
				try { DisposeLineInSession(session, true, false); }
				finally { LineInStopped?.Invoke(this, EventArgs.Empty); }
			}
		}

		void LineInCaptureStopped(LineInSession session)
		{
			if (!DetachLineInSession(session))
				return;

			try
			{
				lock (lineInProcessingLock) { }
				FinalizeDiagnostics();
			}
			finally
			{
				try { DisposeLineInSession(session, false, false); }
				finally { LineInStopped?.Invoke(this, EventArgs.Empty); }
			}
		}

		bool DetachLineInSession(LineInSession session)
		{
			lock (lineInLock)
			{
				if (!ReferenceEquals(lineInSession, session))
					return false;
				lineInSession = null;
			}
			DetachLineInHandlers(session);
			return true;
		}

		static void DetachLineInHandlers(LineInSession session)
		{
			try
			{
				session.Capture.DataAvailable -= session.DataAvailableHandler;
			}
			catch
			{
				// Device cleanup must continue even if an event accessor fails.
			}
			try
			{
				session.Capture.RecordingStopped -= session.RecordingStoppedHandler;
			}
			catch
			{
				// Disposing the capture below prevents further callbacks.
			}
		}

		void DisposeLineInSession(LineInSession session, bool stopCapture, bool discardRecording)
		{
			LineInRecordingResult recordingResult = null;
			Exception cleanupError = null;
			lock (lineInProcessingLock) { }
			try
			{
				if (stopCapture) TryCleanup(() => session.Capture.StopRecording(), ref cleanupError);
				TryCleanup(() => session.Playback.Stop(), ref cleanupError);
				TryCleanup(() => session.Playback.Dispose(), ref cleanupError);
				TryCleanup(() => session.Capture.Dispose(), ref cleanupError);
			}
			finally
			{
				try { recordingResult = session.Recorder?.Stop(discardRecording); }
				catch (Exception exception) { cleanupError = cleanupError ?? exception; }
			}
			if (recordingResult != null && !discardRecording)
				LineInRecordingStopped?.Invoke(this, recordingResult);
			if (cleanupError != null)
				System.Diagnostics.Debug.WriteLine("Line-in cleanup failed: " + cleanupError);
		}

		static void TryCleanup(Action action, ref Exception firstError)
		{
			try { action(); }
			catch (Exception exception) { firstError = firstError ?? exception; }
		}

		internal static float[] ConvertPcm16(byte[] buffer, int bytesRecorded)
		{
			if (buffer == null)
				throw new ArgumentNullException(nameof(buffer));
			if (bytesRecorded < 0 || bytesRecorded > buffer.Length)
				throw new ArgumentOutOfRangeException(nameof(bytesRecorded));
			if (bytesRecorded % 2 != 0)
				throw new ArgumentException("16-bit PCM input must contain complete samples.", nameof(bytesRecorded));

			var incoming = new float[bytesRecorded / 2];
			for (var index = 0; index < incoming.Length; index++)
				incoming[index] = BitConverter.ToInt16(buffer, index * 2) / 32768f;
			return incoming;
		}

		public Task StartListeningToFileAsync(
			string file,
			AudioChannelMode channelMode = AudioChannelMode.Mix,
			CancellationToken cancellationToken = default(CancellationToken),
			bool recoveryPasses = false,
			bool targetedRecovery = true)
		{
			return StartListeningToFileCoreAsync(
				file, channelMode, cancellationToken, recoveryPasses ? RecoveryProfileCount : 1, targetedRecovery);
		}

		internal Task StartListeningToFileWithProfilesAsync(
			string file,
			int profileCount,
			AudioChannelMode channelMode = AudioChannelMode.Mix,
			CancellationToken cancellationToken = default(CancellationToken),
			bool targetedRecovery = true)
		{
			if (profileCount < 1 || profileCount > RecoveryProfileCount)
				throw new ArgumentOutOfRangeException(nameof(profileCount));
			return StartListeningToFileCoreAsync(file, channelMode, cancellationToken, profileCount, targetedRecovery);
		}

		async Task StartListeningToFileCoreAsync(
			string file,
			AudioChannelMode channelMode,
			CancellationToken cancellationToken,
			int profileCount,
			bool targetedRecovery)
		{
			await fileImportGate.WaitAsync(cancellationToken);
			CancellationTokenSource sessionCancellation = null;
			try
			{
				sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				lock (fileCancellationLock)
				{
					activeFileCancellation = sessionCancellation;
					// A file and live capture share decoder state and cannot overlap.
					StopListeningToLineIn();
				}

				await Task.Run(
					() =>
					{
						validBlockRanges.Clear();
						RecoverySamplesProcessed = 0;
						// Keep the original pass first. Extra passes complement it; callers
						// must merge CRC-valid blocks rather than select a winning pass.
						for (var profile = 0; profile < profileCount; profile++)
						{
							decodeProfile = (DecodeProfile)profile;
							ProcessFile(
								file,
								channelMode,
								sessionCancellation.Token,
								targetedRecovery && profile > 0,
								profile + 1,
								profileCount);
						}
					},
					sessionCancellation.Token);

				decodeProfile = DecodeProfile.Standard;
				FileListeningComplete?.Invoke(this, EventArgs.Empty);
			}
			finally
			{
				decodeProfile = DecodeProfile.Standard;
				lock (fileCancellationLock)
				{
					if (ReferenceEquals(activeFileCancellation, sessionCancellation))
						activeFileCancellation = null;
					sessionCancellation?.Dispose();
				}
				fileImportGate.Release();
			}
		}

		void ProcessFile(
			string file,
			AudioChannelMode channelMode,
			CancellationToken cancellationToken,
			bool skipValidBlocks,
			int passNumber,
			int passCount)
		{
			cancellationToken.ThrowIfCancellationRequested();

			using (var reader = new AudioFileReader(file))
			{
				var totalSamples = Math.Max(1L, (long)Math.Ceiling(reader.TotalTime.TotalSeconds * sampleFrequency));
				var lastReportedPercent = -1;
				ReportFileImportProgress(file, passNumber, passCount, 0, totalSamples, ref lastReportedPercent);
				var skipRanges = skipValidBlocks ? GetMergedValidBlockRanges() : new List<SampleRange>();
				// Seeking after resampling can shift the resampler's phase. Restrict the
				// direct path to WAVs already at the decoder rate, where sample positions
				// map exactly; other supported inputs retain the streamed safe path.
				if (skipRanges.Count > 0 && reader.CanSeek && reader.WaveFormat.SampleRate == sampleFrequency &&
					string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase))
				{
					ProcessSeekableRecoveryFile(
						reader, channelMode, cancellationToken, skipRanges, totalSamples,
						file, passNumber, passCount, ref lastReportedPercent);
					return;
				}

				ProcessStreamedFile(
					NormalizeInput(reader, channelMode, sampleFrequency), cancellationToken, skipRanges,
					totalSamples, file, passNumber, passCount, ref lastReportedPercent);
			}
		}

		void ProcessStreamedFile(
			ISampleProvider input,
			CancellationToken cancellationToken,
			List<SampleRange> skipRanges,
			long totalSamples,
			string file,
			int passNumber,
			int passCount,
			ref int lastReportedPercent)
		{
			var data = new float[16384];
				var skipIndex = 0;
				long samplePosition = 0;
				var segmentActive = false;
				int samplesRead;
				while ((samplesRead = input.Read(data, 0, data.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var chunkEnd = samplePosition + samplesRead;
					var cursor = samplePosition;
					while (cursor < chunkEnd)
					{
						while (skipIndex < skipRanges.Count && skipRanges[skipIndex].End <= cursor) skipIndex++;
						if (skipIndex < skipRanges.Count && skipRanges[skipIndex].Start <= cursor)
						{
							if (segmentActive)
							{
								FinalizeDiagnostics();
								segmentActive = false;
							}
							cursor = Math.Min(chunkEnd, skipRanges[skipIndex].End);
							continue;
						}

						var end = skipIndex < skipRanges.Count ? Math.Min(chunkEnd, skipRanges[skipIndex].Start) : chunkEnd;
						if (!segmentActive)
						{
							BeginInputSession(cursor);
							segmentActive = true;
						}
						var count = (int)(end - cursor);
						if (count > 0)
						{
							var segment = new float[count];
							Array.Copy(data, (int)(cursor - samplePosition), segment, 0, count);
							HandleData(segment);
						}
						cursor = end;
					}
					samplePosition = chunkEnd;
					ReportFileImportProgress(file, passNumber, passCount, samplePosition, totalSamples, ref lastReportedPercent);
				}
			if (segmentActive) FinalizeDiagnostics();
			ReportFileImportProgress(file, passNumber, passCount, totalSamples, totalSamples, ref lastReportedPercent);
		}

		void ProcessSeekableRecoveryFile(
			AudioFileReader reader,
			AudioChannelMode channelMode,
			CancellationToken cancellationToken,
			List<SampleRange> skipRanges,
			long totalSamples,
			string file,
			int passNumber,
			int passCount,
			ref int lastReportedPercent)
		{
			long cursor = 0;
			foreach (var range in skipRanges)
			{
				var skipStart = Math.Max(cursor, Math.Min(range.Start, totalSamples));
				if (cursor < skipStart)
					ProcessSeekableSegment(reader, channelMode, cancellationToken, cursor, skipStart,
						totalSamples, file, passNumber, passCount, ref lastReportedPercent);
				cursor = Math.Max(cursor, Math.Min(range.End, totalSamples));
				ReportFileImportProgress(file, passNumber, passCount, cursor, totalSamples, ref lastReportedPercent);
			}
			if (cursor < totalSamples)
				ProcessSeekableSegment(reader, channelMode, cancellationToken, cursor, totalSamples,
					totalSamples, file, passNumber, passCount, ref lastReportedPercent);
		}

		void ProcessSeekableSegment(
			AudioFileReader reader,
			AudioChannelMode channelMode,
			CancellationToken cancellationToken,
			long start,
			long end,
			long totalSamples,
			string file,
			int passNumber,
			int passCount,
			ref int lastReportedPercent)
		{
			var preRollStart = start == 0 ? 0 : Math.Max(0, start - SeekPreRollSamples);
			reader.Position = GetSourcePosition(reader, preRollStart);
			var input = NormalizeInput(reader, channelMode, sampleFrequency);
			if (preRollStart < start)
			{
				ResetDecodingFilters();
				PrimeDecodingFilters(input, start - preRollStart, cancellationToken);
				BeginInputSession(start, false);
			}
			else
				BeginInputSession(start);

			var data = new float[16384];
			var remaining = end - start;
			long processed = 0;
			while (remaining > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var requested = (int)Math.Min(data.Length, remaining);
				var samplesRead = input.Read(data, 0, requested);
				if (samplesRead == 0) break;
				var segment = new float[samplesRead];
				Array.Copy(data, segment, samplesRead);
				HandleData(segment);
				processed += samplesRead;
				remaining -= samplesRead;
				ReportFileImportProgress(file, passNumber, passCount, start + processed, totalSamples, ref lastReportedPercent);
			}
			FinalizeDiagnostics();
		}

		long GetSourcePosition(AudioFileReader reader, long normalizedSamplePosition)
		{
			var sourceFrames = (long)Math.Floor(normalizedSamplePosition *
				(double)reader.WaveFormat.SampleRate / sampleFrequency);
			var position = sourceFrames * reader.WaveFormat.BlockAlign;
			return Math.Max(0, Math.Min(reader.Length, position - position % reader.WaveFormat.BlockAlign));
		}

		void PrimeDecodingFilters(ISampleProvider input, long sampleCount, CancellationToken cancellationToken)
		{
			var data = new float[16384];
			while (sampleCount > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var samplesRead = input.Read(data, 0, (int)Math.Min(data.Length, sampleCount));
				if (samplesRead == 0) return;
				for (var index = 0; index < samplesRead; index++)
					decodingLowPassFilter.Transform(decodingHighPassFilter.Transform(InvertSamples ? -data[index] : data[index]));
				sampleCount -= samplesRead;
			}
		}

		void ReportFileImportProgress(
			string file,
			int passNumber,
			int passCount,
			long samplesRead,
			long totalSamples,
			ref int lastReportedPercent)
		{
			var percent = (int)Math.Min(100, samplesRead * 100 / totalSamples);
			if (percent == lastReportedPercent) return;
			lastReportedPercent = percent;
			FileImportProgress?.Invoke(this, new FileImportProgressData(
				file,
				passNumber,
				passCount,
				decodeProfile.ToString(),
				Math.Min(samplesRead, totalSamples),
				totalSamples));
		}

		internal void RecordValidBlockRange(long start, long end)
		{
			if (end > start)
				validBlockRanges.Add(new SampleRange(start, end));
		}

		List<SampleRange> GetMergedValidBlockRanges()
		{
			var result = new List<SampleRange>();
			foreach (var range in validBlockRanges.OrderBy(value => value.Start))
			{
				if (result.Count == 0 || range.Start > result[result.Count - 1].End)
					result.Add(range);
				else if (range.End > result[result.Count - 1].End)
				{
					var merged = result[result.Count - 1];
					merged.End = range.End;
					result[result.Count - 1] = merged;
				}
			}
			return result;
		}

		void BeginInputSession(long sampleOffset = 0, bool resetFilters = true)
		{
			ResetForNewInput(resetFilters);
			sampleDataOffset = sampleOffset;
			ResetWaitForTone();
			InputSessionStarted?.Invoke(this, EventArgs.Empty);
		}

		public static ISampleProvider NormalizeInput(ISampleProvider input, AudioChannelMode channelMode, int targetSampleRate = SampleRate)
		{
			if (input == null)
				throw new ArgumentNullException(nameof(input));
			if (targetSampleRate < 9600 || targetSampleRate % 4 != 0)
				throw new ArgumentOutOfRangeException(nameof(targetSampleRate), "The decoder sample rate must be at least 9600 Hz and divisible by four.");

			ISampleProvider monoInput;
			if (input.WaveFormat.Channels == 1)
			{
				monoInput = input;
			}
			else if (input.WaveFormat.Channels == 2)
			{
				var stereoToMono = new StereoToMonoSampleProvider(input);
				switch (channelMode)
				{
					case AudioChannelMode.Left:
						stereoToMono.LeftVolume = 1;
						stereoToMono.RightVolume = 0;
						break;
					case AudioChannelMode.Right:
						stereoToMono.LeftVolume = 0;
						stereoToMono.RightVolume = 1;
						break;
					default:
						stereoToMono.LeftVolume = 0.5f;
						stereoToMono.RightVolume = 0.5f;
						break;
				}
				monoInput = stereoToMono;
			}
			else
			{
				throw new NotSupportedException("Only mono and stereo audio inputs are supported.");
			}

			return monoInput.WaveFormat.SampleRate == targetSampleRate
				? monoInput
				: new WdlResamplingSampleProvider(monoInput, targetSampleRate);
		}

		public void StopListening()
		{
			lock (fileCancellationLock)
				activeFileCancellation?.Cancel();
			StopListeningToLineIn();
		}

		public void FormClosing()
		{
			StopListening();
			if (manageInputVolume)
				SetCurrentInputVolume(initialInputVolume);
		}

		public void ResetWaitForTone()
		{
			ResetToneReps();
			bitCount = 0;
			readingData = false;
			lastExtreme = ExtremeType.unknown;
		}


		void AutoAdjustLineInVolume(in byte[] incoming)
		{
			var a = false;
			foreach (var b in incoming)
			{
				if (b > 250 || b < 5)
				{
					incomingTracker.Enqueue(incomingCounter);
					a = true;
				}
				incomingCounter++;
			}

			while (incomingTracker.Count > 0 && incomingTracker.Peek() < incomingCounter - sampleFrequency * 10)
				incomingTracker.Dequeue();

			// At this point the queue contains the number of times the amplitude of a wave exceeded 250 in the last second
			var i = inputVolume;
			if (a)
				inputVolume--;
			if (incomingTracker.Count == 0 && inputVolume < 100)
				inputVolume++;

			if (i != inputVolume)
				SetCurrentInputVolume(inputVolume);
		}

		public void HandleData(float[] incoming)
		{
			if (IsRecoveryPass) RecoverySamplesProcessed += incoming.Length;
			for (var index = 0; index < incoming.Length; index++)
				sampleData.Add(decodingLowPassFilter.Transform(
					decodingHighPassFilter.Transform(InvertSamples ? -incoming[index] : incoming[index])));

			// I would discard data after processing but am keeping it for wave display of errors
			while (streamIndex < sampleData.Count - scanDistance * 40) //TODO Remove the *40
			{
				if (readingData == false)
				{
					if ((!phaseShift && IsPeak()) || (phaseShift && IsTrough()))
					{
						peakIndex = streamIndex;
						streamIndex += scanDistance;
					}
					else if ((!phaseShift && IsTrough()) || (phaseShift && IsPeak()))
					{
						var halfCycleSamples = streamIndex - peakIndex;
						var frequency = ClassifyToneHalfCycle(halfCycleSamples);
						readingData = CheckForTone(frequency);
						if (readingData == false)
							streamIndex += scanDistance;
						else
						{
							if (!phaseShift)
								streamIndex -= samplesPerBit * 3 / 4;
							else
								streamIndex -= samplesPerBit * 1 / 4;
						}
					}
					else
						streamIndex++;
				}

				if (readingData == true)
				{
					var bit = ReadNextBit();
					var currentBitIndex = bitCount - 1;
					CheckForVariance(bit); // Check we're at a trough and adjust accordingly
					var byteComplete = ParseDataBit(bit);
					if (currentBitIndex >= 0 && currentBitIndex < byteBitConfidence.Length)
						byteBitConfidence[currentBitIndex] = bitConfidence;
					streamIndex += samplesPerBit;
					if (byteComplete)
					{
						ByteReceived?.Invoke(this, byteValue);
						DecodedByteReceived?.Invoke(this, new DecodedByteData(
							byteValue,
							(float[])byteBitConfidence.Clone()));
					}
				}
			}

			CaptureReadyDiagnostics(false);
			TrimProcessedDiagnostics();
		}

		int ClassifyToneHalfCycle(int halfCycleSamples)
		{
			if (halfCycleSamples >= shortestOneHalfCycle && halfCycleSamples <= longestOneHalfCycle)
				return 2400;
			if (halfCycleSamples > longestOneHalfCycle && halfCycleSamples <= longestZeroHalfCycle)
				return 1200;
			return 0;
		}

		void CheckForVariance(byte bit)
		{
			if (bit == 0)
			{
				// Check if one 1200Hz wave needs adjusting

				if (!phaseShift)
					streamIndex += samplesPerBit * 3 / 4;
				else
					streamIndex += samplesPerBit * 1 / 4;

				int troughI = streamIndex - 3;
				float troughVal = sampleData[troughI];
				for (var i = troughI + 1; i < troughI + 7; i++)
				{
					if (sampleData[i] <= troughVal)
					{
						troughI = i;
						troughVal = sampleData[i];
					}
				}
				streamIndex = troughI;

				if (!phaseShift)
					streamIndex -= samplesPerBit * 3 / 4;
				else
					streamIndex -= samplesPerBit * 1 / 4;
			}
			else
			{
				// Check if two 2400Hz waves need adjusting

				if (!phaseShift)
					streamIndex += samplesPerBit * 3 / 8;
				else
					streamIndex += samplesPerBit * 1 / 8;

				int troughI = streamIndex - 3;
				var troughI2 = troughI;
				float troughVal = sampleData[troughI];
				for (var i = troughI + 1; i < troughI + 7; i++)
				{
					var s = sampleData[i];
					if (s < troughVal)
					{
						troughI = i;
						troughI2 = i;
						troughVal = sampleData[i];
					}
					else if (s == troughVal)
					{
						troughI2 = i;
					}
				}
				streamIndex = (troughI + troughI2) / 2;

				if (!phaseShift)
					streamIndex -= samplesPerBit * 3 / 8;
				else
					streamIndex -= samplesPerBit * 1 / 8;
			}
		}

		byte ReadNextBit()
		{
			var halfBitSamples = samplesPerBit / 2;
			float firstMean = 0;
			float secondMean = 0;
			for (var offset = 0; offset < halfBitSamples; offset++)
			{
				firstMean += sampleData[streamIndex + offset];
				secondMean += sampleData[streamIndex + halfBitSamples + offset];
			}
			firstMean /= halfBitSamples;
			secondMean /= halfBitSamples;

			float correlation = 0;
			float firstEnergy = 0;
			float secondEnergy = 0;
			for (var offset = 0; offset < halfBitSamples; offset++)
			{
				var first = sampleData[streamIndex + offset] - firstMean;
				var second = sampleData[streamIndex + halfBitSamples + offset] - secondMean;
				correlation += first * second;
				firstEnergy += first * first;
				secondEnergy += second * second;
			}
			var energyProduct = firstEnergy * secondEnergy;
			bitConfidence = energyProduct <= 0
				? 0
				: (float)(Math.Abs(correlation) / Math.Sqrt(energyProduct));

			// A 1200 Hz cycle changes sign between half-bits; two 2400 Hz cycles repeat.
			return (byte)(correlation < 0 ? 0 : 1);
		}

		bool CheckForTone(int frequency)
		{
			if (frequency == 2400)
			{
				toneReps++;
				// We can calculate the number of samples between trough and peak of a 2400hz wave from the lead in tone data
				samplesPerBit = (streamIndex - lastToneMarker) * 2 / toneReps;
				return false;
			}

			if (frequency == 1200)
				if (toneReps > oneBitWavesInHalfSecond)
				{
					ResetToneReps();
					return true;
				}

			ResetToneReps();
			return false;
		}

		void ResetToneReps()
		{
			toneReps = 0;
			lastToneMarker = streamIndex;
		}

		public void BlockError(string name)
		{
			InvalidToneData?.Invoke(this, CreateErrorMarker(name));
		}

		MarkerData CreateErrorMarker(string description)
		{
			return new MarkerData(streamIndex, description, ++nextDiagnosticId);
		}

		bool ParseDataBit(byte bit)
		{
			if (bitCount == 0)
			{
				if (bit == 1)
				{
					InvalidToneData?.Invoke(this, CreateErrorMarker("Start bit not zero"));
					return false;
				}
				byteValue = 0;
				byteMarkers.Add(new MarkerData(streamIndex, ""));
				bitCount = 1;
				return false;
			}

			if (bitCount == 9)
			{
				if (bit == 0)
				{
					bitCount = 0;
					InvalidToneData?.Invoke(this, CreateErrorMarker("End bit not one"));
					return false;
				}
				bitMarkers.Add(new MarkerData(streamIndex, ""));
				bitCount = 0;
				return true;
			}

			byteValue = (byte)((byteValue / 2) + (bit * 128));
			bitMarkers.Add(new MarkerData(
				streamIndex,
				string.Format("bit {0} = {1}", bitCount - 1, bit)));
			bitCount++;
			return false;
		}

		bool IsPeak()
		{
			if (lastExtreme == ExtremeType.peak)
				return false;

			var d = sampleData[streamIndex];
			for (var i = streamIndex - scanDistance; i < streamIndex + scanDistance; i++)
				if (d < sampleData[i])
					return false;

			var min = streamIndex - scanDistance;
			while (sampleData[min] != d)
				min++;
			var max = streamIndex + scanDistance;
			while (sampleData[max] != d)
				max--;

			if (streamIndex >= (max + min) / 2)
			{
				lastExtreme = ExtremeType.peak;
				streamIndex = (max + min) / 2;
				return true;
			}

			return false;
		}

		bool IsTrough()
		{
			if (lastExtreme == ExtremeType.trough)
				return false;

			var d = sampleData[streamIndex];
			for (var i = streamIndex - scanDistance; i < streamIndex + scanDistance; i++)
				if (d > sampleData[i])
					return false;

			var min = streamIndex - scanDistance;
			while (sampleData[min] != d)
				min++;
			var max = streamIndex + scanDistance;
			while (sampleData[max] != d)
				max--;

			if (streamIndex >= (max + min) / 2)
			{
				lastExtreme = ExtremeType.trough;
				streamIndex = (max + min) / 2;
				return true;
			}

			return false;
		}

		internal int RetainedSampleCount { get { return sampleData.Count; } }
		public int DiagnosticCount { get { return errorDiagnostics.Count; } }

		internal long RecordDiagnosticError(MarkerData marker)
		{
			if (marker.diagnosticId == 0)
				marker.diagnosticId = ++nextDiagnosticId;
			pendingErrorMarkers.Add(marker);
			return marker.diagnosticId;
		}

		internal void FinalizeDiagnostics()
		{
			CaptureReadyDiagnostics(true);
			TrimProcessedDiagnostics();
		}

		void CaptureReadyDiagnostics(bool includePartialWindow)
		{
			while (pendingErrorMarkers.Count > 0)
			{
				var marker = pendingErrorMarkers[0];
				if (!includePartialWindow && marker.markerPosition + diagnosticRange > sampleData.Count)
					break;

				var min = Math.Max(0, marker.markerPosition - diagnosticRange);
				var max = Math.Min(sampleData.Count, marker.markerPosition + diagnosticRange);
				if (max > min)
				{
					var diagnostic = new ErrorDataForGraph(
						new MarkerData(
							marker.markerPosition - min,
							marker.markerDescription,
							marker.diagnosticId));
					CopyMarkersInRange(byteMarkers, diagnostic.byteBoundaryMarker, min, max);
					CopyMarkersInRange(bitMarkers, diagnostic.bitBoundaryMarker, min, max);
					diagnostic.data = sampleData.GetRange(min, max - min);
					AddPostErrorDiagnosticMarkers(ref diagnostic);
					if (errorDiagnostics.Count == MaximumRetainedDiagnostics)
						errorDiagnostics.RemoveAt(0);
					errorDiagnostics.Add(diagnostic);
					DiagnosticAvailable?.Invoke(this, diagnostic);
				}
				pendingErrorMarkers.RemoveAt(0);
			}
		}

		void AddPostErrorDiagnosticMarkers(ref ErrorDataForGraph diagnostic)
		{
			if (samplesPerBit < 2 || diagnostic.data == null) return;

			var errorPosition = diagnostic.errorData.markerPosition;
			if (diagnostic.bitBoundaryMarker.Any(marker => marker.markerPosition > errorPosition)) return;
			var diagnosticBitSamples = GetDiagnosticBitSamples(diagnostic.bitBoundaryMarker, errorPosition);
			var referenceStart = Math.Max(0, errorPosition - diagnosticBitSamples * 10);
			var referenceLevel = CalculateAcRms(
				diagnostic.data,
				referenceStart,
				errorPosition - referenceStart);
			if (referenceLevel <= 0) return;

			var projectedBits = new List<DiagnosticBit>();
			var position = errorPosition + diagnosticBitSamples;
			while (position >= 0 && position + diagnosticBitSamples <= diagnostic.data.Count)
			{
				var signalLevel = CalculateAcRms(diagnostic.data, position, diagnosticBitSamples);
				if (signalLevel < referenceLevel * 0.02) break;

				byte value;
				if (!TryClassifyDiagnosticBit(diagnostic.data, position, diagnosticBitSamples, out value)) break;
				position = AdjustDiagnosticBitBoundary(diagnostic.data, position, value, diagnosticBitSamples);
				if (position < 0 || position + diagnosticBitSamples > diagnostic.data.Count) break;

				projectedBits.Add(new DiagnosticBit(position, value));
				diagnostic.bitBoundaryMarker.Add(new MarkerData(
					position,
					value.ToString()));
				position += diagnosticBitSamples;
			}

			for (var index = 0; index + 9 < projectedBits.Count; index++)
			{
				if (projectedBits[index].value != 0 || projectedBits[index + 9].value != 1)
					continue;

				byte value = 0;
				for (var bit = 0; bit < 8; bit++)
					value |= (byte)(projectedBits[index + bit + 1].value << bit);
				diagnostic.byteBoundaryMarker.Add(new MarkerData(
					projectedBits[index].position,
					"diagnostic byte " + value.ToString("X2")));
				index += 9;
			}
		}

		int GetDiagnosticBitSamples(List<MarkerData> markers, int errorPosition)
		{
			var intervals = new List<int>();
			var previous = -1;
			foreach (var marker in markers)
			{
				if (marker.markerPosition >= errorPosition) break;
				if (previous >= 0)
				{
					var interval = marker.markerPosition - previous;
					if (interval >= samplesPerBit * 3 / 4 && interval <= samplesPerBit * 5 / 4)
						intervals.Add(interval);
				}
				previous = marker.markerPosition;
			}
			if (intervals.Count == 0) return samplesPerBit;
			intervals.Sort();
			return intervals[intervals.Count / 2];
		}

		static double CalculateAcRms(List<float> data, int start, int count)
		{
			if (count <= 0) return 0;
			double mean = 0;
			for (var index = start; index < start + count; index++) mean += data[index];
			mean /= count;

			double sumSquares = 0;
			for (var index = start; index < start + count; index++)
			{
				var centered = data[index] - mean;
				sumSquares += centered * centered;
			}
			return Math.Sqrt(sumSquares / count);
		}

		static bool TryClassifyDiagnosticBit(
			List<float> data,
			int position,
			int bitSamples,
			out byte value)
		{
			var halfBitSamples = bitSamples / 2;
			double firstMean = 0;
			double secondMean = 0;
			for (var offset = 0; offset < halfBitSamples; offset++)
			{
				firstMean += data[position + offset];
				secondMean += data[position + halfBitSamples + offset];
			}
			firstMean /= halfBitSamples;
			secondMean /= halfBitSamples;

			double correlation = 0;
			double firstEnergy = 0;
			double secondEnergy = 0;
			for (var offset = 0; offset < halfBitSamples; offset++)
			{
				var first = data[position + offset] - firstMean;
				var second = data[position + halfBitSamples + offset] - secondMean;
				correlation += first * second;
				firstEnergy += first * first;
				secondEnergy += second * second;
			}

			var energyProduct = firstEnergy * secondEnergy;
			if (energyProduct <= 0 || Math.Abs(correlation) / Math.Sqrt(energyProduct) < 0.35)
			{
				value = 0;
				return false;
			}
			value = (byte)(correlation < 0 ? 0 : 1);
			return true;
		}

		int AdjustDiagnosticBitBoundary(List<float> data, int position, byte bit, int bitSamples)
		{
			var phaseOffset = bit == 0
				? bitSamples * (phaseShift ? 1 : 3) / 4
				: bitSamples * (phaseShift ? 1 : 3) / 8;
			var searchStart = Math.Max(0, position + phaseOffset - 3);
			var searchEnd = Math.Min(data.Count - 1, position + phaseOffset + 3);
			var troughStart = searchStart;
			var troughEnd = searchStart;
			var troughValue = data[searchStart];
			for (var index = searchStart + 1; index <= searchEnd; index++)
			{
				if (data[index] < troughValue)
				{
					troughStart = index;
					troughEnd = index;
					troughValue = data[index];
				}
				else if (bit == 1 && data[index] == troughValue)
				{
					troughEnd = index;
				}
			}
			return (troughStart + troughEnd) / 2 - phaseOffset;
		}

		static void CopyMarkersInRange(
			List<MarkerData> source,
			List<MarkerData> destination,
			int min,
			int max)
		{
			foreach (var marker in source)
				if (marker.markerPosition >= min && marker.markerPosition < max)
					destination.Add(new MarkerData(
						marker.markerPosition - min,
						marker.markerDescription,
						marker.diagnosticId));
		}

		void TrimProcessedDiagnostics()
		{
			var removeCount = Math.Max(0, streamIndex - retainedProcessingSamples);
			if (pendingErrorMarkers.Count > 0)
				removeCount = Math.Min(
					removeCount,
					Math.Max(0, pendingErrorMarkers[0].markerPosition - diagnosticRange));
			if (removeCount == 0)
				return;

			sampleData.RemoveRange(0, removeCount);
			sampleDataOffset += removeCount;
			streamIndex -= removeCount;
			peakIndex -= removeCount;
			lastToneMarker -= removeCount;
			ShiftAndDiscardMarkers(byteMarkers, removeCount);
			ShiftAndDiscardMarkers(bitMarkers, removeCount);
			ShiftAndDiscardMarkers(pendingErrorMarkers, removeCount);
		}

		static void ShiftAndDiscardMarkers(List<MarkerData> markers, int removedSamples)
		{
			var writeIndex = 0;
			for (var readIndex = 0; readIndex < markers.Count; readIndex++)
			{
				var marker = markers[readIndex];
				if (marker.markerPosition < removedSamples)
					continue;
				markers[writeIndex++] = new MarkerData(
					marker.markerPosition - removedSamples,
					marker.markerDescription,
					marker.diagnosticId);
			}
			if (writeIndex < markers.Count)
				markers.RemoveRange(writeIndex, markers.Count - writeIndex);
		}

		public ErrorDataForGraph GetErrorData(int errorNumber)
		{
			if (errorNumber < 0 || errorNumber >= errorDiagnostics.Count)
				throw new ArgumentOutOfRangeException(nameof(errorNumber));
			return errorDiagnostics[errorNumber];
		}

		#region ----- MonitorInputVolume --------------------------------------------------------------------------------------------------------------------------------------------------------------

		private int GetCurrentInputVolume()
		{
			int volume = 0;
			using (var enumerator = new MMDeviceEnumerator())
			{
				var inputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
				var mMDevice = inputDevices.FirstOrDefault();
				if (mMDevice != null)
				{
					using (mMDevice)
						volume = Convert.ToInt16(mMDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
				}
			}
			return volume;
		}

		private void SetCurrentInputVolume(int volume)
		{
			using (var enumerator = new MMDeviceEnumerator())
			{
				var inputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
				var mMDevice = inputDevices.FirstOrDefault();
				if (mMDevice != null)
				{
					using (mMDevice)
						mMDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100.0f;
				}
			}
		}

		#endregion //MonitorInputVolume
	}
}
