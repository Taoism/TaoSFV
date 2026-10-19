using System;
using System.Collections.Generic;
using System.Linq;
using TaoSFV.Model.Workset;

namespace TaoSFV
{
	/// <summary>
	/// Finds files in a set with the same checksum, for Find Duplicate Files. Files marked IsDeleted are ignored.
	/// </summary>
	public static class DuplicateFinder
	{
		/// <summary>Groups of two or more files with the same checksum (ignoring case), in the set's order.</summary>
		public static List<List<ChecksumFile>> GetDuplicates(ChecksumSet set)
		{
			return set.Files
				.Where(p => !p.IsDeleted)
				.GroupBy(p => p.OriginalChecksum, StringComparer.OrdinalIgnoreCase)
				.Where(p => p.Count() > 1)
				.Select(p => p.ToList())
				.ToList();
		}

		/// <summary>Files of a group not yet deleted.</summary>
		public static List<ChecksumFile> Remaining(IEnumerable<ChecksumFile> group)
		{
			return group.Where(p => !p.IsDeleted).ToList();
		}

		/// <summary>
		/// Duplicates under a folder of the set (relative, ending in a backslash) that can be deleted: for each group,
		/// its files in the folder, unless that's all of them, so at least one copy is always kept.
		/// </summary>
		public static List<ChecksumFile> GetDeletableIn(IEnumerable<List<ChecksumFile>> groups, string folder)
		{
			List<ChecksumFile> result = new List<ChecksumFile>();
			foreach (List<ChecksumFile> group in groups)
			{
				List<ChecksumFile> remaining = Remaining(group);
				if (remaining.Count < 2)
					continue;

				List<ChecksumFile> inFolder = remaining.Where(p => p.FileName.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList();
				if (inFolder.Count != remaining.Count)
					result.AddRange(inFolder);
			}
			return result;
		}
	}
}
