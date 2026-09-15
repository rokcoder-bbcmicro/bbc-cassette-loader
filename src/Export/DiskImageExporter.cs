using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace bbc_cassette_loader
{
	public enum DiskImageFormat
	{
		Dfs40Ssd,
		Dfs80Ssd,
		Dfs40Dsd,
		Dfs80Dsd,
		AdfsS,
		AdfsM,
		AdfsL
	}

	public sealed class DiskImageExportResult
	{
		public readonly int files;
		public readonly int sectorsUsed;
		public readonly int sectorsAvailable;

		internal DiskImageExportResult(int files, int sectorsUsed, int sectorsAvailable)
		{
			this.files = files;
			this.sectorsUsed = sectorsUsed;
			this.sectorsAvailable = sectorsAvailable;
		}
	}

	sealed class DiskImageFile
	{
		public readonly string name;
		public readonly BlockHeader header;
		public readonly byte[] payload;

		public DiskImageFile(string name, BBCFile file)
		{
			this.name = name;
			header = file.GetFirstHeaderForExport();
			payload = file.GetPayloadForExport();
		}

		public int Sectors { get { return Math.Max(1, (payload.Length + 255) / 256); } }
	}

	public static class DiskImageExporter
	{
		const int SectorSize = 256;
		const string DiskTitle = "BBC CASSETTE";

		public static DiskImageExportResult Write(
			string path,
			DiskImageFormat format,
			IEnumerable<KeyValuePair<string, BBCFile>> files)
		{
			return Write(path, format, files, null, null);
		}

		public static DiskImageExportResult Write(
			string path,
			DiskImageFormat format,
			IEnumerable<KeyValuePair<string, BBCFile>> files,
			IReadOnlyDictionary<string, string> names,
			ISet<string> excluded)
		{
			if (path == null) throw new ArgumentNullException(nameof(path));
			if (files == null) throw new ArgumentNullException(nameof(files));
			if (!Enum.IsDefined(typeof(DiskImageFormat), format))
				throw new ArgumentOutOfRangeException(nameof(format));
			var source = files.Where(pair => pair.Value.IsComplete() && (excluded == null || !excluded.Contains(pair.Key)))
				.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
				.Select(pair => new DiskImageFile(names != null && names.ContainsKey(pair.Key) ? names[pair.Key] : pair.Value.filename, pair.Value)).ToList();
			if (source.GroupBy(file => file.name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
				throw new InvalidOperationException("The disk image cannot contain duplicate recovered filenames.");
			var image = IsAdfs(format)
				? BuildAdfs(format, source)
				: BuildDfs(format, source);
			var directory = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
			AtomicFile.Write(path, stream => stream.Write(image.bytes, 0, image.bytes.Length));
			return new DiskImageExportResult(source.Count, image.used, image.capacity);
		}

		public static DiskImageExportResult Preview(
			DiskImageFormat format,
			IEnumerable<KeyValuePair<string, BBCFile>> files,
			IReadOnlyDictionary<string, string> names,
			ISet<string> excluded)
		{
			if (files == null) throw new ArgumentNullException(nameof(files));
			if (!Enum.IsDefined(typeof(DiskImageFormat), format))
				throw new ArgumentOutOfRangeException(nameof(format));
			var source = files.Where(pair => pair.Value.IsComplete() && (excluded == null || !excluded.Contains(pair.Key)))
				.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
				.Select(pair => new DiskImageFile(names != null && names.ContainsKey(pair.Key) ? names[pair.Key] : pair.Value.filename, pair.Value)).ToList();
			if (source.GroupBy(file => file.name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
				throw new InvalidOperationException("The disk image cannot contain duplicate recovered filenames.");
			var image = IsAdfs(format) ? BuildAdfs(format, source) : BuildDfs(format, source);
			return new DiskImageExportResult(source.Count, image.used, image.capacity);
		}

		public static string GetDefaultExtension(DiskImageFormat format)
		{
			if (!Enum.IsDefined(typeof(DiskImageFormat), format))
				throw new ArgumentOutOfRangeException(nameof(format));
			switch (format)
			{
				case DiskImageFormat.Dfs40Ssd:
				case DiskImageFormat.Dfs80Ssd: return "ssd";
				case DiskImageFormat.Dfs40Dsd:
				case DiskImageFormat.Dfs80Dsd: return "dsd";
				case DiskImageFormat.AdfsL: return "adl";
				default: return "adf";
			}
		}

		public static string GetDescription(DiskImageFormat format)
		{
			switch (format)
			{
				case DiskImageFormat.Dfs40Ssd: return "DFS 40T SSD";
				case DiskImageFormat.Dfs80Ssd: return "DFS 80T SSD";
				case DiskImageFormat.Dfs40Dsd: return "DFS 40T DSD";
				case DiskImageFormat.Dfs80Dsd: return "DFS 80T DSD";
				case DiskImageFormat.AdfsS: return "ADFS-S (40T, single-sided)";
				case DiskImageFormat.AdfsM: return "ADFS-M (80T, single-sided)";
				case DiskImageFormat.AdfsL: return "ADFS-L (80T, double-sided)";
				default: throw new ArgumentOutOfRangeException(nameof(format));
			}
		}

		public static string GetNameValidationError(DiskImageFormat format, string name)
		{
			if (!Enum.IsDefined(typeof(DiskImageFormat), format)) return "Unknown disk image format.";
			try
			{
				if (IsAdfs(format)) ValidateAdfsName(name);
				else ValidateDfsName(name);
				return null;
			}
			catch (InvalidOperationException exception) { return exception.Message; }
		}

		public static int GetDataSectorCapacity(DiskImageFormat format)
		{
			if (!Enum.IsDefined(typeof(DiskImageFormat), format)) throw new ArgumentOutOfRangeException(nameof(format));
			if (IsAdfs(format))
			{
				var tracks = format == DiskImageFormat.AdfsS ? 40 : 80;
				var sides = format == DiskImageFormat.AdfsL ? 2 : 1;
				return tracks * sides * 16 - 7;
			}
			var dfsTracks = format == DiskImageFormat.Dfs40Ssd || format == DiskImageFormat.Dfs40Dsd ? 40 : 80;
			var dfsSides = format == DiskImageFormat.Dfs40Dsd || format == DiskImageFormat.Dfs80Dsd ? 2 : 1;
			return dfsTracks * dfsSides * 10 - 2 * dfsSides;
		}

		public static int GetFileSectorCount(BBCFile file)
		{
			if (file == null) throw new ArgumentNullException(nameof(file));
			return Math.Max(1, (file.GetPayloadForExport().Length + 255) / 256);
		}

		sealed class ImageData
		{
			public readonly byte[] bytes;
			public readonly int used;
			public readonly int capacity;

			public ImageData(byte[] bytes, int used, int capacity)
			{
				this.bytes = bytes;
				this.used = used;
				this.capacity = capacity;
			}
		}

		sealed class DfsEntry
		{
			public readonly DiskImageFile file;
			public readonly int start;

			public DfsEntry(DiskImageFile file, int start) { this.file = file; this.start = start; }
		}

		static ImageData BuildDfs(DiskImageFormat format, List<DiskImageFile> files)
		{
			var doubleSided = format == DiskImageFormat.Dfs40Dsd || format == DiskImageFormat.Dfs80Dsd;
			var tracks = format == DiskImageFormat.Dfs40Ssd || format == DiskImageFormat.Dfs40Dsd ? 40 : 80;
			var sides = doubleSided ? 2 : 1;
			var sectorsPerSide = tracks * 10;
			var entries = new List<DfsEntry>[sides];
			for (var side = 0; side < sides; side++) entries[side] = new List<DfsEntry>();

			foreach (var file in files)
			{
				ValidateDfsName(file.name);
				var side = entries[0].Count == 31 || UsedSectors(entries[0]) + file.Sectors > sectorsPerSide
					? 1 : 0;
				if (side >= sides) throw new InvalidOperationException("DFS image cannot contain all complete recovered files.");
				if (entries[side].Count >= 31 || UsedSectors(entries[side]) + file.Sectors > sectorsPerSide)
					throw new InvalidOperationException("DFS image cannot contain all complete recovered files.");
				entries[side].Add(new DfsEntry(file, UsedSectors(entries[side])));
			}

			var image = new byte[sectorsPerSide * sides * SectorSize];
			for (var side = 0; side < sides; side++)
			{
				WriteDfsCatalogue(image, tracks, side, entries[side], sectorsPerSide);
				foreach (var entry in entries[side])
					WritePayload(image, DfsOffset(tracks, side, entry.start, doubleSided), entry.file.payload);
			}
			return new ImageData(image, entries.Sum(side => side.Sum(entry => entry.file.Sectors)), sectorsPerSide * sides - 2 * sides);
		}

		static int UsedSectors(IEnumerable<DfsEntry> entries)
		{
			return 2 + entries.Sum(entry => entry.file.Sectors);
		}

		static void WriteDfsCatalogue(byte[] image, int tracks, int side, List<DfsEntry> entries, int sectorsPerSide)
		{
			var catalog = DfsOffset(tracks, side, 0, side != 0 || image.Length > sectorsPerSide * SectorSize);
			var title = Encoding.ASCII.GetBytes(DiskTitle);
			Array.Copy(title, 0, image, catalog, Math.Min(8, title.Length));
			Array.Copy(title, 8, image, catalog + SectorSize, Math.Min(4, title.Length - 8));
			image[catalog + SectorSize + 4] = 0;
			image[catalog + SectorSize + 5] = (byte)(entries.Count * 8);
			var sectorCount = tracks * 10;
			image[catalog + SectorSize + 6] = (byte)((sectorCount >> 8) & 3);
			image[catalog + SectorSize + 7] = (byte)sectorCount;

			var catalogIndex = 0;
			foreach (var entry in entries.OrderByDescending(item => item.start))
			{
				var index = catalogIndex++;
				var nameOffset = catalog + 8 + index * 8;
				var name = Encoding.ASCII.GetBytes(entry.file.name);
				for (var nameIndex = 0; nameIndex < 7; nameIndex++) image[nameOffset + nameIndex] = (byte)' ';
				Array.Copy(name, 0, image, nameOffset, Math.Min(7, name.Length));
				image[nameOffset + 7] = (byte)'$';
				var detailOffset = catalog + SectorSize + 8 + index * 8;
				Put16(image, detailOffset, entry.file.header.loadAddress);
				Put16(image, detailOffset + 2, entry.file.header.execAddress);
				Put16(image, detailOffset + 4, (uint)entry.file.payload.Length);
				image[detailOffset + 6] = (byte)((uint)((entry.start >> 8) & 3) |
					(uint)((entry.file.header.loadAddress >> 16) & 3) << 2 |
					(uint)((entry.file.payload.Length >> 16) & 3) << 4 |
					(uint)((entry.file.header.execAddress >> 16) & 3) << 6);
				image[detailOffset + 7] = (byte)entry.start;
			}
		}

		static int DfsOffset(int tracks, int side, int sector, bool interleaved)
		{
			if (!interleaved) return sector * SectorSize;
			var track = sector / 10;
			var sectorInTrack = sector % 10;
			return ((track * 2 + side) * 10 + sectorInTrack) * SectorSize;
		}

		static ImageData BuildAdfs(DiskImageFormat format, List<DiskImageFile> files)
		{
			var tracks = format == DiskImageFormat.AdfsS ? 40 : 80;
			var sides = format == DiskImageFormat.AdfsL ? 2 : 1;
			var totalSectors = tracks * sides * 16;
			if (files.Count > 47) throw new InvalidOperationException("ADFS root directory cannot contain more than 47 files.");
			foreach (var file in files) ValidateAdfsName(file.name);
			var ordered = files.OrderBy(file => file.name, StringComparer.OrdinalIgnoreCase).ToList();
			var nextSector = 7;
			var starts = new Dictionary<DiskImageFile, int>();
			foreach (var file in ordered)
			{
				if (nextSector + file.Sectors > totalSectors)
					throw new InvalidOperationException("ADFS image cannot contain all complete recovered files.");
				starts.Add(file, nextSector);
				nextSector += file.Sectors;
			}

			var image = new byte[totalSectors * SectorSize];
			WriteAdfsMap(image, totalSectors, nextSector);
			WriteAdfsDirectory(image, ordered, starts);
			foreach (var file in ordered)
				WritePayload(image, AdfsOffset(tracks, sides, starts[file]), file.payload);
			return new ImageData(image, nextSector - 7, totalSectors - 7);
		}

		static int AdfsOffset(int tracks, int sides, int sector)
		{
			if (sides == 1) return sector * SectorSize;
			var sectorsPerSide = tracks * 16;
			var side = sector / sectorsPerSide;
			var track = (sector % sectorsPerSide) / 16;
			var sectorInTrack = sector % 16;
			return ((track * 2 + side) * 16 + sectorInTrack) * SectorSize;
		}

		static void WriteAdfsMap(byte[] image, int totalSectors, int nextSector)
		{
			if (nextSector < totalSectors) Put24(image, 0, (uint)nextSector);
			if (nextSector < totalSectors) Put24(image, 0x100, (uint)(totalSectors - nextSector));
			Put24(image, 0xFC, (uint)totalSectors);
			Put16(image, 0x1FB, 0);
			image[0x1FD] = 0;
			image[0x1FE] = (byte)(nextSector < totalSectors ? 3 : 0);
			image[0xFF] = AdfsMapChecksum(image, 0, 0xFE);
			image[0x1FF] = AdfsMapChecksum(image, 0x100, 0x1FE);
		}

		static byte AdfsMapChecksum(byte[] image, int start, int end)
		{
			var sum = 0;
			for (var i = start; i <= end; i++)
			{
				var value = image[i] + sum;
				sum = value & 0xFF;
				if (value > 0xFF) sum++;
			}
			return (byte)sum;
		}

		static void WriteAdfsDirectory(byte[] image, List<DiskImageFile> files, Dictionary<DiskImageFile, int> starts)
		{
			const int root = 0x200;
			image[root] = 0;
			WriteAscii(image, root + 1, "Hugo", 4);
			for (var i = 0; i < files.Count; i++)
			{
				var entry = root + 5 + i * 26;
				WriteAdfsObjectName(image, entry, files[i].name);
				Put32(image, entry + 10, files[i].header.loadAddress);
				Put32(image, entry + 14, files[i].header.execAddress);
				Put32(image, entry + 18, (uint)files[i].payload.Length);
				Put24(image, entry + 22, (uint)starts[files[i]]);
				image[entry + 25] = 0;
			}
			var tail = root + 5 + 47 * 26;
			image[tail] = 0;
			WriteAscii(image, tail + 1, "$", 10);
			Put24(image, tail + 0x0B, 2u);
			WriteAscii(image, tail + 0x0E, DiskTitle, 19);
			image[tail + 0x2F] = 0;
			WriteAscii(image, tail + 0x30, "Hugo", 4);
			image[tail + 0x34] = 0;
		}

		static void WritePayload(byte[] image, int offset, byte[] payload)
		{
			Array.Copy(payload, 0, image, offset, payload.Length);
		}

		static void ValidateDfsName(string name)
		{
			if (name == null || name.Length < 1 || name.Length > 7)
				throw new InvalidOperationException("DFS export requires filenames of 1 to 7 safe characters: " + name);
			for (var i = 0; i < name.Length; i++)
				if (name[i] < 32 || name[i] > 126 || "#*: .".Contains(name[i]) || (name[i] == '!' && i != 0))
					throw new InvalidOperationException("DFS export requires filenames of 1 to 7 safe characters: " + name);
		}

		static void ValidateAdfsName(string name)
		{
			if (name == null || name.Length < 1 || name.Length > 10 || name.Any(c => c < 32 || c > 126 || c == '$' || c == '.'))
				throw new InvalidOperationException("ADFS export requires filenames of 1 to 10 safe characters: " + name);
		}

		static bool IsAdfs(DiskImageFormat format) { return format >= DiskImageFormat.AdfsS; }

		static void WriteAscii(byte[] target, int offset, string value, int length)
		{
			var bytes = Encoding.ASCII.GetBytes(value ?? "");
			for (var i = 0; i < length; i++) target[offset + i] = i < bytes.Length ? bytes[i] : (byte)' ';
		}

		static void WriteAdfsObjectName(byte[] target, int offset, string value)
		{
			WriteAscii(target, offset, value, 10);
			// Old ADFS stores R and W attributes in the top bits of name bytes 0 and 1.
			target[offset] |= 0x80;
			target[offset + 1] |= 0x80;
		}

		static void Put16(byte[] target, int offset, uint value)
		{
			target[offset] = (byte)value;
			target[offset + 1] = (byte)(value >> 8);
		}

		static void Put24(byte[] target, int offset, uint value)
		{
			target[offset] = (byte)value;
			target[offset + 1] = (byte)(value >> 8);
			target[offset + 2] = (byte)(value >> 16);
		}

		static void Put32(byte[] target, int offset, uint value)
		{
			Put16(target, offset, value);
			Put16(target, offset + 2, value >> 16);
		}
	}
}
