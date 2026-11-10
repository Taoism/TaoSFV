using System;
using System.IO;

namespace TaoSFV.Model.Workset
{
	/// <summary>
	/// Keeps file names read from checksum and PAR2 files inside the folder they belong to. Those files often come
	/// with downloads, so a name such as "..\..\Startup\x.cmd" or "C:\Windows\x" must never lead TaoSFV to create,
	/// rename, delete or repair a file outside the set's folder.
	/// </summary>
	public static class SafePath
	{
		/// <summary>
		/// True when `name` is a relative name (subfolders allowed) that stays inside `directory`: not rooted, no
		/// drive or UNC prefix, no ':' (drive letters, alternate data streams) and no ".." part.
		/// </summary>
		public static bool IsInside(string directory, string name)
		{
			return Resolve(directory, name) != null;
		}

		/// <summary>The full path of `name` in `directory`, or null when the name would point outside it.</summary>
		public static string Resolve(string directory, string name)
		{
			if (string.IsNullOrEmpty(directory) || string.IsNullOrWhiteSpace(name))
				return null;
			if (name.IndexOf(':') >= 0 || name.IndexOf('\0') >= 0 || Path.IsPathRooted(name))
				return null;
			if (name.StartsWith("\\", StringComparison.Ordinal) || name.StartsWith("/", StringComparison.Ordinal))
				return null;

			foreach (string part in name.Split('\\', '/'))
			{
				if (part == "..")
					return null;
			}

			try
			{
				string root = Path.GetFullPath(directory);
				if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
					root += Path.DirectorySeparatorChar;
				string full = Path.GetFullPath(Path.Combine(root, name));
				return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length ? full : null;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
			{
				return null;
			}
		}
	}
}
