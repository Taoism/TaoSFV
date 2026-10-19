using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TaoSFV.Hash;

namespace TaoSFV.Model.Workset
{
	/// <summary>
	/// Loads checksum files into sets and finds checksum files in folders, without any UI.
	/// </summary>
	public static class ChecksumSetLoader
	{
		// besides whitespace: NUL padding (left by some download/FTP tools) and the DOS Ctrl+Z end-of-file marker
		private static readonly char[] JunkChars = { '\0', '\x1a' };

		/// <summary>Trims whitespace, NUL and Ctrl+Z from both ends of a line.</summary>
		public static string CleanLine(string line)
		{
			return line.Trim().Trim(JunkChars).Trim();
		}

		/// <summary>
		/// Reads a checksum file. Returns null for an unsupported extension; throws
		/// InvalidChecksumFileException for a line in no recognized format.
		/// </summary>
		public static ChecksumSet Parse(string fileName)
		{
			ChecksumAlgorithm algorithm = ChecksumAlgorithm.FromExtension(Path.GetExtension(fileName));
			if (algorithm == null)
				return null;

			ChecksumSet set = new ChecksumSet(fileName, Path.GetDirectoryName(fileName), algorithm.Type);
			string[] lines = ChecksumFileFormat.ReadAllLines(fileName);

			StringBuilder comment = new StringBuilder();
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (line == null)
					continue;

				line = CleanLine(line);
				if (string.IsNullOrEmpty(line))
					continue;

				if (ChecksumFileFormat.IsComment(line, set.Type))
				{
					comment.AppendLine(line.Substring(1, line.Length - 1));
					continue;
				}

				string chkFileName;
				string chkChecksum;
				if (!ChecksumFileFormat.TryParseLine(line, set.Type, out chkFileName, out chkChecksum))
					throw new InvalidChecksumFileException(fileName, set.Type, lines, i);

				ChecksumFile file = new ChecksumFile(set);
				file.FileName = chkFileName;
				file.OriginalChecksum = chkChecksum;
				set.Files.Add(file);
			}

			set.Comments = comment.ToString();
			return set;
		}

		/// <summary>Sets FileInfo for listed files that exist and totals their size.</summary>
		public static void ResolveFiles(ChecksumSet set, Action<int> percentDone)
		{
			set.TotalSize = 0;
			for (int i = 0; i < set.Files.Count; i++)
			{
				if (percentDone != null)
					percentDone(i * 100 / set.Files.Count);

				ChecksumFile file = set.Files[i];
				FileInfo fileInfo = new FileInfo(Path.Combine(set.Directory, file.FileName));
				if (fileInfo.Exists)
				{
					file.FileInfo = fileInfo;
					set.TotalSize += fileInfo.Length;
				}
			}
		}

		/// <summary>
		/// Loads checksum files, and every checksum file in folders and their subfolders, resolving the files they
		/// list. Meant for a background thread: `loading` (may be null) is called with each folder scanned and
		/// each checksum file read. A checksum file that's missing, unreadable or not in a recognized format is
		/// skipped and reported; other files are ignored. Throws OperationCanceledException when cancelled.
		/// </summary>
		public static LoadResult LoadAll(IEnumerable<string> paths, Action<string> loading, CancellationToken cancel)
		{
			LoadResult result = new LoadResult();
			foreach (string path in paths)
			{
				cancel.ThrowIfCancellationRequested();

				if (Directory.Exists(path))
				{
					List<string> files = new List<string>();
					Collect(path, files, loading, cancel);
					files.Sort(StringComparer.OrdinalIgnoreCase);
					foreach (string file in files)
						Load(file, true, result, loading, cancel);
				}
				else if (!File.Exists(path))
				{
					result.Skipped.Add(new SkippedChecksumFile(path, false, new FileNotFoundException(null, path)));
				}
				else if (ChecksumAlgorithm.IsChecksumFile(path))
				{
					Load(path, false, result, loading, cancel);
				}
			}
			return result;
		}

		private static void Load(string fileName, bool inFolder, LoadResult result, Action<string> loading, CancellationToken cancel)
		{
			cancel.ThrowIfCancellationRequested();
			if (loading != null)
				loading(fileName);

			try
			{
				ChecksumSet set = Parse(fileName);
				ResolveFiles(set, null);
				result.Sets.Add(set);
			}
			catch (Exception ex) when (ex is InvalidChecksumFileException || ex is IOException || ex is UnauthorizedAccessException)
			{
				result.Skipped.Add(new SkippedChecksumFile(fileName, inFolder, ex));
			}
		}

		/// <summary>
		/// Checksum files in a folder and all subfolders, skipping hidden folders and unreadable ones, in path order.
		/// </summary>
		public static List<string> FindChecksumFiles(string directory)
		{
			List<string> result = new List<string>();
			Collect(directory, result, null, CancellationToken.None);
			result.Sort(StringComparer.OrdinalIgnoreCase);
			return result;
		}

		private static void Collect(string directory, List<string> result, Action<string> scanning, CancellationToken cancel)
		{
			cancel.ThrowIfCancellationRequested();
			if (scanning != null)
				scanning(directory);

			try
			{
				result.AddRange(Directory.GetFiles(directory).Where(ChecksumAlgorithm.IsChecksumFile));
			}
			catch (UnauthorizedAccessException)
			{
			}
			catch (IOException)
			{
			}

			string[] subdirectories;
			try
			{
				subdirectories = Directory.GetDirectories(directory);
			}
			catch (UnauthorizedAccessException)
			{
				return;
			}
			catch (IOException)
			{
				return;
			}

			foreach (string subdirectory in subdirectories)
			{
				if ((new DirectoryInfo(subdirectory).Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
					continue;
				Collect(subdirectory, result, scanning, cancel);
			}
		}
	}

	/// <summary>What ChecksumSetLoader.LoadAll read: the sets in order, and the checksum files it skipped.</summary>
	public sealed class LoadResult
	{
		public LoadResult()
		{
			Sets = new List<ChecksumSet>();
			Skipped = new List<SkippedChecksumFile>();
		}

		public List<ChecksumSet> Sets { get; private set; }
		public List<SkippedChecksumFile> Skipped { get; private set; }
	}

	public sealed class SkippedChecksumFile
	{
		public SkippedChecksumFile(string path, bool inFolder, Exception error)
		{
			Path = path;
			InFolder = inFolder;
			Error = error;
		}

		public string Path { get; private set; }

		/// <summary>Found by scanning a folder, rather than opened directly.</summary>
		public bool InFolder { get; private set; }

		/// <summary>InvalidChecksumFileException, FileNotFoundException for a path that doesn't exist, or another IOException / UnauthorizedAccessException.</summary>
		public Exception Error { get; private set; }
	}
}
