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

			foreach (ChecksumFile file in set.Files)
			{
				if (file.State != ChecksumFileState.Missing)
					continue;

				string fullFileName = Path.Combine(set.Directory, file.FileName);
				string directory = Path.GetDirectoryName(fullFileName);
				string lookForExt = Path.GetExtension(fullFileName);

				if (!Directory.Exists(directory))
					continue;

				foreach (string candidate in Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly))
				{
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
