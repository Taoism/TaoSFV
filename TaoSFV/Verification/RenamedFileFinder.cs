using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TaoSFV.Hash;
using TaoSFV.Model.Workset;

namespace TaoSFV.Verification
{
	/// <summary>
	/// Finds missing files of a set under other names: hashes files in the same folder with the same extension
	/// (or none) that the set doesn't list, and renames a match back to the listed name.
	/// </summary>
	public static class RenamedFileFinder
	{
		/// <summary>Returns the files that were found and renamed; their State is set to OK.</summary>
		public static List<ChecksumFile> Find(ChecksumSet set, bool lowercaseNames, VerifyContext context)
		{
			ChecksumAlgorithm algorithm = ChecksumAlgorithm.Get(set.Type);
			List<ChecksumFile> found = new List<ChecksumFile>();

			HashSet<string> inSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ChecksumFile file in set.Files)
				inSet.Add(Path.Combine(set.Directory, file.FileName));

			// a candidate file is hashed at most once, however many missing files it's compared against
			Dictionary<string, string> candidateChecksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			// and each folder is listed once (null: doesn't exist), not once per missing file
			Dictionary<string, string[]> folderFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
			HashSet<string> moved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (ChecksumFile file in set.Files)
			{
				if (file.State != ChecksumFileState.Missing)
					continue;

				string fullFileName = Path.Combine(set.Directory, file.FileName);
				string directory = Path.GetDirectoryName(fullFileName);
				string lookForExt = Path.GetExtension(fullFileName);

				string[] candidates;
				if (!folderFiles.TryGetValue(directory, out candidates))
				{
					candidates = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly) : null;
					folderFiles[directory] = candidates;
				}
				if (candidates == null)
					continue;

				foreach (string candidate in candidates)
				{
					if (moved.Contains(candidate))
						continue; // already renamed back to another missing file's name

					context.Cancel.ThrowIfCancellationRequested();

					string candidateExt = Path.GetExtension(candidate);
					if (string.Compare(lookForExt, candidateExt, true) != 0 && !string.IsNullOrEmpty(candidateExt) && candidateExt != ".")
						continue; // different extension, and the candidate has one

					if (inSet.Contains(candidate))
						continue; // the set's own files were already checked

					string checksum;
					if (!candidateChecksums.TryGetValue(candidate, out checksum))
					{
						try
						{
							checksum = FileVerifier.HashFile(algorithm, new FileInfo(candidate), context);
						}
						catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
						{
							Trace.WriteLine(ex);
							checksum = null;
						}
						candidateChecksums[candidate] = checksum;
					}

					if (checksum == null || string.Compare(checksum, file.OriginalChecksum, true) != 0)
						continue;

					string target = lowercaseNames ? fullFileName.ToLower() : fullFileName;
					try
					{
						File.Move(candidate, target);
					}
					catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
					{
						Trace.WriteLine(ex);
						continue;
					}

					candidateChecksums.Remove(candidate);
					moved.Add(candidate);
					inSet.Add(target);
					file.CurrentChecksum = checksum;
					file.FileInfo = new FileInfo(target);
					file.State = ChecksumFileState.OK;
					found.Add(file);
					break;
				}
			}

			return found;
		}
	}
}
