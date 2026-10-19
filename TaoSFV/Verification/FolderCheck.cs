using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaoSFV.Model.Workset;

namespace TaoSFV.Verification
{
	/// <summary>
	/// Checks checksum files and folders without any UI, reading every file (no cache, nothing renamed or deleted):
	/// what a scheduled check runs.
	/// </summary>
	public static class FolderCheck
	{
		/// <param name="parallelReadsSetting">Files read at once; 0 picks by drive.</param>
		public static LoadResult Run(IEnumerable<string> paths, int parallelReadsSetting, CancellationToken cancel)
		{
			LoadResult loaded = ChecksumSetLoader.LoadAll(paths, null, cancel);
			VerifyOptions options = VerifyOptions.ReadOnly(false);
			VerifyContext context = new VerifyContext(cancel);
			IReadOnlyDictionary<string, ChecksumFile> noCache = new Dictionary<string, ChecksumFile>();

			foreach (ChecksumSet set in loaded.Sets)
			{
				ParallelOptions parallel = new ParallelOptions
				{
					MaxDegreeOfParallelism = DiskInfo.GetParallelism(set.Directory, parallelReadsSetting),
					CancellationToken = cancel
				};
				Parallel.ForEach(set.Files, parallel, file =>
				{
					try
					{
						FileVerifier.Verify(set, file, options, noCache, context);
					}
					catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
					{
						file.State = ChecksumFileState.Unreadable;
						file.ErrorMessage = ex.Message;
					}
				});
			}
			return loaded;
		}

		/// <summary>True when every file was found and matched, and no checksum file was skipped.</summary>
		public static bool AllOK(LoadResult result)
		{
			return result.Skipped.Count == 0 && result.Sets.All(s => s.Files.All(f => f.State == ChecksumFileState.OK));
		}
	}
}
