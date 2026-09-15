using System;
using System.Collections.Generic;
using System.Linq;

namespace bbc_cassette_loader
{
	public class BlockHandler
	{
		public sealed class CrcRepairInfo
		{
			public readonly string kind;
			public readonly int correctedBits;

			internal CrcRepairInfo(string kind, int correctedBits)
			{
				this.kind = kind;
				this.correctedBits = correctedBits;
			}
		}

		public event EventHandler<List<byte>> BlockHeaderReceived;
		public event EventHandler<List<byte>> BlockDataReceived;
		public event EventHandler<string> Error;
		public event EventHandler<string> LogDetail;
		public event EventHandler<string> CrcRepairAccepted;
		public event EventHandler<CrcRepairInfo> CrcRepairAcceptedDetailed;
		public bool EnableCrcRepair { get; set; }
		const int MaximumRepairBits = 8;

		private enum DataStage
		{
			sync,
			filename,
			loadAddress,
			execAddress,
			blockNum,
			dataBlockLen,
			blockFlag,
			redundantFourBytes,
			headerCRC,
			data,
			dataCRC,
			blockFinished
		}

		private DataStage stage;

		private enum ExpectedData
		{
			text,
			bytes
		}

		private ExpectedData dataType;
		private int incomingDataCount;
		private int incomingDataIndex;
		private uint incomingData;
		private string incomingString;
		private List<byte> headerBlock; // Used for CRC
		private List<float[]> headerConfidence;

		public string filename; // Maximum of ten characters
		private uint loadAddress;
		private uint execAddress;
		public ushort blockNum;
		private ushort dataBlockLen;
		private byte blockFlag;
		private uint nextAddress;
		private ushort headerCRC;
		private List<byte> dataBlock;
		private List<float[]> dataConfidence;
		private ushort dataCRC;
		private bool filenameKnown;
		private bool blockNumKnown;
		private bool headerAccepted;

		public BlockHandler()
		{
			headerBlock = new List<byte>();
			headerConfidence = new List<float[]>();
			dataBlock = new List<byte>();
			dataConfidence = new List<float[]>();

			ResetBlock();
		}

		public void LoadBlock()
		{
		}

		public void ResetBlock()
		{
			filename = "";
			loadAddress = 0;
			execAddress = 0;
			blockNum = 0;
			dataBlockLen = 0;
			blockFlag = 0;
			nextAddress = 0;
			headerCRC = 0;
			dataCRC = 0;
			filenameKnown = false;
			blockNumKnown = false;
			headerAccepted = false;

			headerBlock.Clear();
			headerConfidence.Clear();
			dataBlock.Clear();
			dataConfidence.Clear();

			SetNextStageBytes(DataStage.sync, 1);
		}

		private void SetNextStageBytes(DataStage s, int numBytes)
		{
			stage = s;
			dataType = ExpectedData.bytes;
			incomingDataCount = numBytes;
			incomingDataIndex = 0;
			incomingData = 0;
		}

		private void SetNextStageString(DataStage s, int maxBytes)
		{
			stage = s;
			dataType = ExpectedData.text;
			incomingDataCount = maxBytes;
			incomingDataIndex = 0;
			incomingString = "";
		}

		public void ToneError(object sender, ToneHandler.MarkerData data)
		{
			// I'm only analysing errors in header and data blocks - not when looking to sync at the beginning of a header
			// This could lead to the missing of entire sections of the sync byte is corrupted - might be something to contemplate later!
			if (stage != DataStage.sync)
			{
				((ToneHandler)sender).RecordDiagnosticError(data);
				LogDetail?.Invoke(this, string.Format("File: {0} | Block: {1} | ByteIndex: {4} | State: {3} | Error: {2}", filename, blockNum, data.markerDescription, stage.ToString(), dataBlock.Count));
			}
		}

		// Technically AddByte is doing way more than necessary but that's fine
		// - At this point we just need the data and CRC check for the header and data blocks
		// - And it's only the fact that the filename is variable length that means we have to parse anything at all

		public void AddByte(object sender, byte b)
		{
			AddByteWithConfidence(sender, new ToneHandler.DecodedByteData(b, null));
		}

		public void AddByteWithConfidence(object sender, ToneHandler.DecodedByteData decoded)
		{
			var confidence = decoded.bitConfidence;
			if (stage == DataStage.blockFinished)
				return;

			if (stage > DataStage.sync && stage <= DataStage.headerCRC)
			{
				headerBlock.Add(decoded.value);
				headerConfidence.Add(confidence == null ? Ones() : (float[])confidence.Clone());
			}

			if (stage == DataStage.dataCRC || stage == DataStage.data)
			{
				dataBlock.Add(decoded.value);
				dataConfidence.Add(confidence == null ? Ones() : (float[])confidence.Clone());
			}

			switch (dataType)
			{
				case ExpectedData.text:
					if (decoded.value == 0)
					{
						if (incomingDataIndex == 0)
						{
							HandleError("Filename must contain 1 to 10 characters");
							return;
						}
						break;
					}
					if (incomingDataIndex >= incomingDataCount)
					{
						HandleError("Filename must contain 1 to 10 characters");
						return;
					}
					if (decoded.value == (byte)' ')
					{
						HandleError("Filename cannot contain spaces");
						return;
					}
					incomingString += (char)decoded.value;
					incomingDataIndex++;
					return;

				case ExpectedData.bytes:
					incomingData |= (uint)decoded.value << ((incomingDataIndex++) * 8);
					if (incomingDataIndex < incomingDataCount)
						return;
					break;
			}

			switch (stage)
			{
				case DataStage.sync:
					SetNextStageString(DataStage.filename, 10);
					if (incomingData != 0x2A)
						HandleError(null);  // ("Sync byte not found");
					break;

				case DataStage.filename:
					filename = incomingString;
					filenameKnown = true;
					SetNextStageBytes(DataStage.loadAddress, 4);
					break;

				case DataStage.loadAddress:
					loadAddress = incomingData;
					SetNextStageBytes(DataStage.execAddress, 4);
					break;

				case DataStage.execAddress:
					execAddress = incomingData;
					SetNextStageBytes(DataStage.blockNum, 2);
					break;

				case DataStage.blockNum:
					blockNum = (ushort)incomingData;
					blockNumKnown = true;
					SetNextStageBytes(DataStage.dataBlockLen, 2);
					break;

				case DataStage.dataBlockLen:
					dataBlockLen = (ushort)incomingData;
					if (dataBlockLen > 256)
						HandleError("Data block length exceeds 256 bytes");
					else
						SetNextStageBytes(DataStage.blockFlag, 1);
					break;

				case DataStage.blockFlag:
					blockFlag = (byte)incomingData;
					SetNextStageBytes(DataStage.redundantFourBytes, 4);
					break;

				case DataStage.redundantFourBytes:
					nextAddress = incomingData;
					SetNextStageBytes(DataStage.headerCRC, 2);
					break;

				case DataStage.headerCRC:
					headerCRC = (ushort)incomingData;
					if (headerCRC != CRC(ref headerBlock))
					{
						List<byte> repairedHeader;
						if (!EnableCrcRepair || !TryRepairBlock(headerBlock, headerConfidence, true, headerCRC, out repairedHeader))
						{
							HandleError("Header CRC failure");
							break;
						}
						var correctedBits = CountChangedBits(headerBlock, repairedHeader);
						headerBlock = repairedHeader;
						var validatedHeader = new BlockHeader(headerBlock.ToArray());
						filename = validatedHeader.filename;
						blockNum = validatedHeader.blockNum;
						dataBlockLen = validatedHeader.dataBlockLen;
						CrcRepairAccepted?.Invoke(this, "header");
						CrcRepairAcceptedDetailed?.Invoke(this, new CrcRepairInfo("header", correctedBits));
					}
					headerAccepted = true;
					BlockHeaderReceived?.Invoke(this, headerBlock);
					if (dataBlockLen == 0)
						SetNextStageBytes(DataStage.dataCRC, 2);
					else
						SetNextStageBytes(DataStage.data, 1);
					break;

				case DataStage.data:
					if (dataBlock.Count < dataBlockLen)
						SetNextStageBytes(DataStage.data, 1);
					else
						SetNextStageBytes(DataStage.dataCRC, 2);
					break;

				case DataStage.dataCRC:
					dataCRC = (ushort)incomingData;
					if (dataCRC != CRC(ref dataBlock))
					{
						List<byte> repairedData;
						if (!EnableCrcRepair || !TryRepairBlock(dataBlock, dataConfidence, false, dataCRC, out repairedData))
						{
							HandleError("Data CRC failure");
							break;
						}
						var correctedBits = CountChangedBits(dataBlock, repairedData);
						dataBlock = repairedData;
						CrcRepairAccepted?.Invoke(this, "data");
						CrcRepairAcceptedDetailed?.Invoke(this, new CrcRepairInfo("data", correctedBits));
						EndOfBlock();
					}
					else
						EndOfBlock();
					break;
			}
		}

		internal bool TryGetInvalidBlockContext(out string blockFilename, out ushort invalidBlockNum, out bool isData)
		{
			blockFilename = filename;
			invalidBlockNum = blockNum;
			isData = headerAccepted;
			return filenameKnown && blockNumKnown;
		}

		void HandleError(string error = null)
		{
			stage = DataStage.blockFinished;
			Error?.Invoke(this, error);
		}

		void EndOfBlock()
		{
			stage = DataStage.blockFinished;
			BlockDataReceived?.Invoke(this, dataBlock);
		}

		private ushort CRC(ref List<byte> data)
		{
			const uint poly = 0x1021;
			uint crc = 0;
			for (var num = 0; num < data.Count - 2; num++) // -2 because the actual CRC shouldn't be involved
			{
				crc ^= (uint)data[num] << 8;
				for (var i = 0; i < 8; i++)
				{
					crc <<= 1;
					if ((crc & 0x10000) == 0x10000)
						crc = (crc ^ poly) & 0xFFFF;
				}
			}
			return (ushort)(crc >> 8 | crc << 8);
		}

		static float[] Ones()
		{
			return new float[] { 1, 1, 1, 1, 1, 1, 1, 1 };
		}

		static int CountChangedBits(List<byte> source, List<byte> repaired)
		{
			var count = 0;
			for (var index = 0; index < source.Count - 2; index++)
				count += CountBits((byte)(source[index] ^ repaired[index]));
			return count;
		}

		static int CountBits(byte value)
		{
			var count = 0;
			while (value != 0)
			{
				count += value & 1;
				value >>= 1;
			}
			return count;
		}

		bool TryRepairBlock(
			List<byte> source,
			List<float[]> confidence,
			bool header,
			ushort expectedCrc,
			out List<byte> repaired)
		{
			repaired = null;
			var candidates = new List<Tuple<float, int, int>>();
			var bodyLength = source.Count - 2;
			for (var byteIndex = 0; byteIndex < bodyLength; byteIndex++)
			{
				var bits = byteIndex < confidence.Count ? confidence[byteIndex] : null;
				if (bits == null || bits.Length < 8) continue;
				for (var bitIndex = 0; bitIndex < 8; bitIndex++)
					if (bits[bitIndex] < 0.999f)
						candidates.Add(Tuple.Create(bits[bitIndex], byteIndex, bitIndex));
			}
			candidates = candidates.OrderBy(candidate => candidate.Item1)
				.Take(MaximumRepairBits)
				.ToList();
			if (candidates.Count == 0) return false;

			var matches = new List<List<byte>>();
			for (var first = 0; first < candidates.Count; first++)
			{
				TryRepairCandidate(source, candidates[first], header, expectedCrc, matches);
				for (var second = first + 1; second < candidates.Count; second++)
					TryRepairCandidate(source, candidates[first], candidates[second], header, expectedCrc, matches);
				if (matches.Count > 1) return false;
			}
			if (matches.Count != 1) return false;
			repaired = matches[0];
			return true;
		}

		void TryRepairCandidate(
			List<byte> source,
			Tuple<float, int, int> first,
			bool header,
			ushort expectedCrc,
			List<List<byte>> matches)
		{
			TryRepairCandidate(source, first, null, header, expectedCrc, matches);
		}

		void TryRepairCandidate(
			List<byte> source,
			Tuple<float, int, int> first,
			Tuple<float, int, int> second,
			bool header,
			ushort expectedCrc,
			List<List<byte>> matches)
		{
			var candidate = new List<byte>(source);
			candidate[first.Item2] ^= (byte)(1 << first.Item3);
			if (second != null)
				candidate[second.Item2] ^= (byte)(1 << second.Item3);
			if (expectedCrc != CRC(ref candidate)) return;
			if (header)
			{
				try { new BlockHeader(candidate.ToArray()); }
				catch (ArgumentException) { return; }
			}
			if (!matches.Any(match => match.SequenceEqual(candidate)))
				matches.Add(candidate);
		}
	}
}
