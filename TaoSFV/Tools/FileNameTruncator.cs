using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TaoSFV.Hash;
using TaoSFV.Model.Workset;

namespace TaoSFV
{
	/// <summary>Result of FileNameTruncator.Truncate: how many files were renamed, and which weren't and why.</summary>
	public sealed class TruncateResult
	{
		public TruncateResult()
		{
			NotRenamed = new List<KeyValuePair<string, string>>();
		}

		public int Renamed { get; set; }

		/// <summary>Checksum files in the folder that listed a renamed file and were updated to the new name.</summary>
		public int ChecksumFilesUpdated { get; set; }

		/// <summary>Full path and reason for each long name that couldn't be shortened (null: the extension leaves no room).</summary>
		public List<KeyValuePair<string, string>> NotRenamed { get; private set; }
	}

	/// <summary>
	/// Truncate File Names: shortens file names longer than a limit, keeping the extension, in a folder and its
	/// subfolders (hidden folders skipped). A name that would clash with another file gets "~2", "~3"... added.
	/// </summary>
	public static class FileNameTruncator
	{
		public const int MinimumLength = 12;

		/// <param name="renaming">Called before each rename with the old and new name (may be null).</param>
		public static TruncateResult Truncate(string folder, int maxLength, Action<string, string> renaming)
		{
			if (maxLength < MinimumLength)
				throw new ArgumentOutOfRangeException("maxLength");

			TruncateResult result = new TruncateResult();
			List<string> files = new List<string>();
			Collect(folder, files);

			// old full path -> new file name, to update checksum files that list them
			Dictionary<string, string> renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (string fullPath in files)
			{
				string fileName = Path.GetFileName(fullPath);
				if (fileName.Length <= maxLength)
					continue;

				string directory = Path.GetDirectoryName(fullPath);
				string newPath = null;
				for (int attempt = 1; attempt < 1000; attempt++)
				{
					string newName = Shorten(fileName, maxLength, attempt);
					if (newName == null)
						break;

					string candidate = Path.Combine(directory, newName);
					if (!File.Exists(candidate) && !Directory.Exists(candidate))
					{
						newPath = candidate;
						break;
					}
				}

				if (newPath == null)
				{
					result.NotRenamed.Add(new KeyValuePair<string, string>(fullPath, null));
					continue;
				}

				try
				{
					if (renaming != null)
						renaming(fileName, Path.GetFileName(newPath));
					File.Move(fullPath, newPath);
					result.Renamed++;
					renamed[Path.GetFullPath(fullPath)] = Path.GetFileName(newPath);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					result.NotRenamed.Add(new KeyValuePair<string, string>(fullPath, ex.Message));
				}
			}

			if (renamed.Count != 0)
				result.ChecksumFilesUpdated = UpdateChecksumFiles(folder, renamed, result);

			return result;
		}

		/// <summary>
		/// Points entries for renamed files at their new names, in every checksum file in the folder and its subfolders
		/// (by their names after renaming). Only those lines change: comments, line endings, the encoding and
		/// '/' or '\' separators are kept. Returns how many checksum files were changed.
		/// </summary>
		private static int UpdateChecksumFiles(string folder, Dictionary<string, string> renamed, TruncateResult result)
		{
			List<string> files = new List<string>();
			Collect(folder, files);

			int updated = 0;
			foreach (string checksumFile in files)
			{
				ChecksumAlgorithm algorithm = ChecksumAlgorithm.FromExtension(checksumFile);
				if (algorithm == null)
					continue;

				try
				{
					if (UpdateChecksumFile(checksumFile, algorithm.Type, renamed))
						updated++;
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					result.NotRenamed.Add(new KeyValuePair<string, string>(checksumFile, ex.Message));
				}
			}
			return updated;
		}

		private static bool UpdateChecksumFile(string checksumFile, ChecksumType type, Dictionary<string, string> renamed)
		{
			byte[] bytes = File.ReadAllBytes(checksumFile);
			int preambleLength;
			Encoding encoding = ChecksumFileFormat.DetectEncoding(bytes, out preambleLength);
			string text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
			string directory = Path.GetDirectoryName(Path.GetFullPath(checksumFile));

			// split keeping each line's ending
			string[] lines = Regex.Split(text, @"(?<=\n)");
			bool changed = false;
			for (int i = 0; i < lines.Length; i++)
			{
				string line = ChecksumSetLoader.CleanLine(lines[i]);
				string fileName, checksum;
				if (line.Length == 0 || ChecksumFileFormat.IsComment(line, type) || !ChecksumFileFormat.TryParseLine(line, type, out fileName, out checksum))
					continue;

				string newName;
				string listed;
				try
				{
					listed = Path.GetFullPath(Path.Combine(directory, fileName));
				}
				catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
				{
					continue;
				}
				if (!renamed.TryGetValue(listed, out newName))
					continue;

				// replace just the file name part (the last segment), as written in the line
				string oldName = Path.GetFileName(fileName);
				int at = lines[i].LastIndexOf(oldName, StringComparison.OrdinalIgnoreCase);
				if (at < 0)
					continue;
				lines[i] = lines[i].Substring(0, at) + newName + lines[i].Substring(at + oldName.Length);
				changed = true;
			}

			if (!changed)
				return false;

			byte[] preamble = bytes.Take(preambleLength).ToArray();
			byte[] body = encoding.GetBytes(string.Concat(lines));
			File.WriteAllBytes(checksumFile, preamble.Concat(body).ToArray());
			return true;
		}

		/// <summary>
		/// The name cut to maxLength with its extension kept, "~attempt" added for attempts after the first;
		/// null if the extension leaves no room.
		/// </summary>
		public static string Shorten(string fileName, int maxLength, int attempt)
		{
			string ext = Path.GetExtension(fileName);
			string name = Path.GetFileNameWithoutExtension(fileName);
			string suffix = attempt <= 1 ? "" : "~" + attempt;

			int keep = Math.Min(name.Length, maxLength - ext.Length - suffix.Length);
			if (keep > 0 && keep < name.Length && char.IsHighSurrogate(name[keep - 1]))
				keep--; // don't split a character made of two UTF-16 units

			// Windows drops trailing spaces and periods, so a name can't end with them
			string shortName = keep > 0 ? name.Substring(0, keep).TrimEnd(' ', '.') : "";
			if (shortName.Length == 0)
				return null;

			return shortName + suffix + ext;
		}

		private static void Collect(string directory, List<string> result)
		{
			try
			{
				result.AddRange(Directory.GetFiles(directory));
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}

			string[] subdirectories;
			try
			{
				subdirectories = Directory.GetDirectories(directory);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
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
