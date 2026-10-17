using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
		/// Checksum files in a folder and all subfolders, skipping hidden folders and unreadable ones, in path order.
		/// </summary>
		public static List<string> FindChecksumFiles(string directory)
		{
			List<string> result = new List<string>();
			Collect(directory, result);
			result.Sort(StringComparer.OrdinalIgnoreCase);
			return result;
		}

		private static void Collect(string directory, List<string> result)
		{
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
				Collect(subdirectory, result);
			}
		}
	}
}
