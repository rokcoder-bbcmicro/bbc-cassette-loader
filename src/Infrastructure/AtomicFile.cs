using System;
using System.IO;

namespace bbc_cassette_loader
{
	internal static class AtomicFile
	{
		public static void Write(string path, Action<Stream> write)
		{
			if (path == null) throw new ArgumentNullException(nameof(path));
			if (write == null) throw new ArgumentNullException(nameof(write));

			var fullPath = Path.GetFullPath(path);
			var directory = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
			var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
			try
			{
				using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
				{
					write(stream);
					stream.Flush(true);
				}

				if (File.Exists(fullPath))
					File.Replace(temporaryPath, fullPath, null, true);
				else
					File.Move(temporaryPath, fullPath);
			}
			finally
			{
				try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}
		}
	}
}
