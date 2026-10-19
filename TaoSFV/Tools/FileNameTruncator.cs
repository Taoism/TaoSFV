using System;
using System.Collections.Generic;
using System.IO;

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
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					result.NotRenamed.Add(new KeyValuePair<string, string>(fullPath, ex.Message));
				}
			}

			return result;
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
