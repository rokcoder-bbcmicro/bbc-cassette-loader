using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace bbc_cassette_loader
{
	[Serializable]
	public struct BlockHeader : ISerializable, IEquatable<BlockHeader>
	{
		public readonly ImmutableArray<byte> data;
		public readonly string filename;
		public readonly uint loadAddress;
		public readonly uint execAddress;
		public readonly ushort blockNum;
		public readonly ushort dataBlockLen;
		public readonly byte blockFlag;
		public readonly uint nextAddress;

		public BlockHeader(byte[] d)
		{
			data = ImmutableArray.Create<byte>(d);

			var filenameLength = Array.IndexOf(d, (byte)0);
			if (filenameLength < 1 || filenameLength > 10)
				throw new ArgumentException("Cassette headers require a 1 to 10 byte filename followed by zero.", "d");
			if (Array.IndexOf(d, (byte)' ', 0, filenameLength) >= 0)
				throw new ArgumentException("Cassette filenames cannot contain spaces.", "d");

			if (d.Length < filenameLength + 20)
				throw new ArgumentException("Cassette header is incomplete.", "d");

			filename = new string(d.Take(filenameLength).Select(value => (char)value).ToArray());
			loadAddress = BitConverter.ToUInt32(d, filenameLength + 1);
			execAddress = BitConverter.ToUInt32(d, filenameLength + 5);
			blockNum = BitConverter.ToUInt16(d, filenameLength + 9);
			dataBlockLen = BitConverter.ToUInt16(d, filenameLength + 11);
			if (dataBlockLen > 256)
				throw new ArgumentException("Cassette data blocks cannot exceed 256 bytes.", "d");
			blockFlag = d[filenameLength + 13];
			nextAddress = BitConverter.ToUInt32(d, filenameLength + 14);
		}

		BlockHeader(SerializationInfo info, StreamingContext context)
		{
			data = (ImmutableArray<byte>)info.GetValue("data", typeof(ImmutableArray<byte>));
			filename = (string)info.GetValue("filename", typeof(string));
			loadAddress = (uint)info.GetValue("loadAddress", typeof(uint));
			execAddress = (uint)info.GetValue("execAddress", typeof(uint));
			blockNum = (ushort)info.GetValue("blockNum", typeof(ushort));
			dataBlockLen = (ushort)info.GetValue("dataBlockLen", typeof(ushort));
			blockFlag = (byte)info.GetValue("blockFlag", typeof(byte));
			nextAddress = (uint)info.GetValue("nextAddress", typeof(uint));
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("data", data, typeof(ImmutableArray<byte>));
			info.AddValue("filename", filename, typeof(string));
			info.AddValue("loadAddress", loadAddress, typeof(uint));
			info.AddValue("execAddress", execAddress, typeof(uint));
			info.AddValue("blockNum", blockNum, typeof(ushort));
			info.AddValue("dataBlockLen", dataBlockLen, typeof(ushort));
			info.AddValue("blockFlag", blockFlag, typeof(byte));
			info.AddValue("nextAddress", nextAddress, typeof(uint));
		}

		public bool Equals(BlockHeader other)
		{
			return filename == other.filename &&
				loadAddress == other.loadAddress &&
				execAddress == other.execAddress &&
				blockNum == other.blockNum &&
				dataBlockLen == other.dataBlockLen &&
				blockFlag == other.blockFlag &&
				nextAddress == other.nextAddress &&
				ByteArraysEqual(data, other.data);
		}

		public override bool Equals(object obj)
		{
			return obj is BlockHeader && Equals((BlockHeader)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				var hash = 17;
				hash = hash * 31 + (filename == null ? 0 : StringComparer.Ordinal.GetHashCode(filename));
				hash = hash * 31 + loadAddress.GetHashCode();
				hash = hash * 31 + execAddress.GetHashCode();
				hash = hash * 31 + blockNum.GetHashCode();
				hash = hash * 31 + dataBlockLen.GetHashCode();
				hash = hash * 31 + blockFlag.GetHashCode();
				hash = hash * 31 + nextAddress.GetHashCode();
				if (!data.IsDefault)
					foreach (var value in data) hash = hash * 31 + value;
				return hash;
			}
		}

		static bool ByteArraysEqual(ImmutableArray<byte> first, ImmutableArray<byte> second)
		{
			if (first.IsDefault || second.IsDefault) return first.IsDefault == second.IsDefault;
			return first.SequenceEqual(second);
		}

		public static bool operator ==(BlockHeader c1, BlockHeader c2)
		{
			return c1.Equals(c2);
		}

		public static bool operator !=(BlockHeader c1, BlockHeader c2)
		{
			return !(c1 == c2);
		}
	}

	[Serializable]
	public struct BlockData : ISerializable, IEquatable<BlockData>
	{
		public readonly ImmutableArray<byte> data;

		public BlockData(byte[] d)
		{
			data = ImmutableArray.Create<byte>(d);
		}

		BlockData(SerializationInfo info, StreamingContext context)
		{
			data = (ImmutableArray<byte>)info.GetValue("data", typeof(ImmutableArray<byte>));
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("data", data, typeof(ImmutableArray<byte>));
		}

		public bool Equals(BlockData other)
		{
			if (data.IsDefault || other.data.IsDefault) return data.IsDefault == other.data.IsDefault;
			return data.SequenceEqual(other.data);
		}

		public override bool Equals(object obj)
		{
			return obj is BlockData && Equals((BlockData)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				var hash = 17;
				if (!data.IsDefault)
					foreach (var value in data) hash = hash * 31 + value;
				return hash;
			}
		}

		public static bool operator ==(BlockData c1, BlockData c2)
		{
			return c1.Equals(c2);
		}

		public static bool operator !=(BlockData c1, BlockData c2)
		{
			return !(c1 == c2);
		}
	}

	[Serializable]
	public class Block : ISerializable
	{
		public BlockHeader? Header;
		public BlockData? Data;

		public Block()
		{
			Header = null;
			Data = null;
		}

		protected Block(SerializationInfo info, StreamingContext context)
		{
			Header = (BlockHeader?)info.GetValue("Header", typeof(BlockHeader?));
			Data = (BlockData?)info.GetValue("Data", typeof(BlockData?));
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("Header", Header, typeof(BlockHeader?));
			info.AddValue("Data", Data, typeof(BlockData?));
		}
	}

	[Serializable]
	public class BBCFile : ISerializable
	{
		public readonly string filename;
		int totalBlocks;
		List<Block> blocks;
		Block currentBlock;

		public Block CurrentBlock { get { return currentBlock; } }

		protected BBCFile(SerializationInfo info, StreamingContext context)
		{
			filename = (string)info.GetValue("filename", typeof(string));
			var storedTotalBlocks = info.GetInt32("totalBlocks");
			try
			{
				totalBlocks = info.GetBoolean("totalBlocksKnown") ? storedTotalBlocks : -1;
			}
			catch (SerializationException)
			{
				// Earlier recovery files used UInt16.MaxValue as the unknown-length sentinel.
				totalBlocks = storedTotalBlocks == ushort.MaxValue ? -1 : storedTotalBlocks;
			}
			blocks = (List<Block>)info.GetValue("blocks", typeof(List<Block>));
			currentBlock = (Block)info.GetValue("currentBlock", typeof(Block));
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("filename", filename, typeof(string));
			info.AddValue("totalBlocks", totalBlocks, typeof(int));
			info.AddValue("totalBlocksKnown", TotalBlocksKnown, typeof(bool));
			info.AddValue("blocks", blocks, typeof(List<Block>));
			info.AddValue("currentBlock", currentBlock, typeof(Block));
		}

		public BBCFile() { } // For serialisation

		public BBCFile(ref BlockHeader header)
		{
			blocks = new List<Block>();
			filename = "";
			totalBlocks = -1;
			filename = header.filename;
			currentBlock = null;
		}

		public bool TotalBlocksKnown { get { return totalBlocks >= 0; } }
		public bool HasHeader(int i) { return i >= 0 && blocks.Count > i && blocks[i].Header != null; }
		public bool HasData(int i) { return i >= 0 && blocks.Count > i && blocks[i].Data != null; }
		public int NumBlocks { get { return blocks.Count; } }

		internal IEnumerable<byte[]> GetValidatedBlockBytes()
		{
			return blocks
				.Where(block => block.Header.HasValue && block.Data.HasValue)
				.Select(block => block.Header.Value.data
					.Concat(block.Data.Value.data)
					.ToArray());
		}

		public bool CouldContain(BlockHeader h)
		{
			if (h.filename != filename)
				return false;
			if (!HasConsistentFinalBlock(h))
				return false;
			if (HasHeader(h.blockNum) && blocks[h.blockNum].Header != h)
				return false;
			return true;
		}

		private bool HasConsistentFinalBlock(BlockHeader header)
		{
			var isFinal = (header.blockFlag & 0x80) != 0;
			var declaredBlockCount = header.blockNum + 1;
			if (TotalBlocksKnown)
				return header.blockNum < totalBlocks &&
					(isFinal ? declaredBlockCount == totalBlocks : declaredBlockCount != totalBlocks);
			return !isFinal || blocks.Count <= declaredBlockCount;
		}

		public bool CouldContain(BlockData data, ushort blockNum)
		{
			if (TotalBlocksKnown && blockNum >= totalBlocks)
				return false;
			if (HasData(blockNum) && blocks[blockNum].Data != data)
				return false;
			return true;
		}

		public void AddHeader(ref BlockHeader header)
		{
			currentBlock = null;

			if (header.filename != filename)
				throw new DifferentFilenameException();

			if (!HasConsistentFinalBlock(header))
				throw new UnexpectedBlockNumException();

			while (header.blockNum >= blocks.Count)
				blocks.Add(new Block());

			if (blocks[header.blockNum].Header != null && header != blocks[header.blockNum].Header)
				throw new DifferentHeaderException();

			currentBlock = blocks[header.blockNum];
			blocks[header.blockNum].Header = header;

			if ((header.blockFlag & 0x80) == 0x80)
				totalBlocks = header.blockNum + 1;
		}

		public void AddData(ref BlockData data)
		{
			if (currentBlock.Data != null && data != currentBlock.Data)
				throw new DifferentHeaderException();

			currentBlock.Data = data;
			currentBlock = null;
		}

		public bool IsComplete()
		{
			// Check we know the file length
			if (!TotalBlocksKnown || blocks == null || blocks.Count < totalBlocks)
				return false;

			// Check we have verified data for each block
			for (var i = 0; i < totalBlocks; i++)
				if (blocks[i].Header == null || blocks[i].Data == null)
					return false;

			return true;
		}

		internal BlockHeader GetFirstHeaderForExport()
		{
			if (!IsComplete()) throw new InvalidOperationException("Only complete recovered files can be exported.");
			return blocks[0].Header.Value;
		}

		internal byte[] GetPayloadForExport()
		{
			if (!IsComplete()) throw new InvalidOperationException("Only complete recovered files can be exported.");
			var payload = new List<byte>();
			for (var i = 0; i < totalBlocks; i++)
			{
				var header = blocks[i].Header.Value;
				var data = blocks[i].Data.Value.data;
				if (data.Length != header.dataBlockLen + 2)
					throw new InvalidDataException("Recovered block data does not match its declared payload length.");
				payload.AddRange(data.Take(header.dataBlockLen));
			}
			return payload.ToArray();
		}

		public void ExportTo(string file)
		{
			if (file == null)
				throw new ArgumentNullException(nameof(file));
			if (!IsComplete())
				throw new InvalidOperationException("Only complete recovered files can be exported.");

			long payloadLength = 0;
			var payloads = new List<byte[]>(totalBlocks);
			for (var i = 0; i < totalBlocks; i++)
			{
				var header = blocks[i].Header.Value;
				var data = blocks[i].Data.Value.data;
				if (data.Length != header.dataBlockLen + 2)
					throw new InvalidDataException("Recovered block data does not match its declared payload length.");
				payloads.Add(data.Take(header.dataBlockLen).ToArray());
				payloadLength += header.dataBlockLen;
			}

			AtomicFile.Write(file, stream =>
			{
				foreach (var payload in payloads)
					stream.Write(payload, 0, payload.Length);
			});

			var firstHeader = blocks[0].Header.Value;
			AtomicFile.Write(file + ".inf", stream =>
			{
				using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
					writer.Write(String.Format(
						"$.{0,-7} {1,8:X8} {2,8:X8} {3,6:X6}",
						filename,
						firstHeader.loadAddress,
						firstHeader.execAddress,
						payloadLength));
			});
		}

		public StringBuilder GetBinaryString()
		{
			var binaryString = new StringBuilder();

			if (IsComplete())
			{
				// 5.1 seconds of "1" lead-in
				binaryString.Append(new string('1', (int)(1200 * 5.1)));

				foreach (var block in blocks)
				{
					// Header
					var headerData = block.Header.Value.data;
					binaryString.Append(getBinaryOfByte(0x2A)); // Sync byte
					foreach (var b in headerData)
					{
						binaryString.Append(getBinaryOfByte(b));
					}

					// Data
					var data = block.Data.Value.data;
					foreach (var b in data)
					{
						binaryString.Append(getBinaryOfByte(b));
					}

					// 1.3 seconds of "1" after each block
					binaryString.Append(new string('1', (int)(1200 * 1.3)));
				}

				// 4 seconds of "1" lead-out
				binaryString.Append(new string('1', (int)(1200 * 4)));
			}
			return binaryString;
		}

		private StringBuilder getBinaryOfByte(byte b)
		{
			var s = new StringBuilder("0");
			var d = 1;
			while (d < 256)
			{
				if ((b & d) > 0)
				{
					s.Append("1");
				}
				else
				{
					s.Append("0");
				}
				d <<= 1;
			}
			s.Append("1");
			return s;
		}
	}

	[Serializable]
	class DifferentFilenameException : Exception
	{
		public DifferentFilenameException() : base("Filename mismatch") { }
	}

	[Serializable]
	class DifferentHeaderException : Exception
	{
		public DifferentHeaderException() : base("Header collision") { }
	}

	[Serializable]
	class UnexpectedBlockNumException : Exception
	{
		public UnexpectedBlockNumException() : base("Unexpected block number") { }
	}
}
