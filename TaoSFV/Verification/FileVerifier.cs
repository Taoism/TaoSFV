using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TaoSFV.Hash;
using TaoSFV.Model.Settings;
using TaoSFV.Model.Workset;

namespace TaoSFV.Verification
{
	/// <summary>What checking a file may do besides hashing it.</summary>
	public sealed class VerifyOptions
	{
		public bool UseCache { get; set; }
		public bool CreateMissingMarkers { get; set; }
		public bool CleanupBadMissing { get; set; }
		public CheckRenaming Renaming { get; set; }
		public bool RenameBadFiles { get; set; }
		public bool DeleteFailedFiles { get; set; }
		public bool UseRecycleBin { get; set; }

		/// <summary>Only reads files: no markers, renames or deletes.</summary>
		public static VerifyOptions ReadOnly(bool useCache)
		{
			return new VerifyOptions { UseCache = useCache, Renaming = CheckRenaming.None };
		}

		public static VerifyOptions FromSettings(ProgramSettings settings)
		{
			return new VerifyOptions
			{
				UseCache = settings.General.UseCachedResults,
				CreateMissingMarkers = settings.Check.CreateMissingMarkers,
				CleanupBadMissing = settings.Check.CleanupBadMissing,
				Renaming = settings.Check.Renaming,
				RenameBadFiles = settings.Check.RenameBadFiles,
				DeleteFailedFiles = settings.Check.DeleteFailedFiles,
				UseRecycleBin = settings.Check.UseRecyleBin
			};
		}
	}

	/// <summary>
	/// Shared state for one checking run: cancellation, pause and byte counts across worker threads.
	/// </summary>
	public sealed class VerifyContext
	{
		private readonly ConcurrentDictionary<ChecksumFile, long[]> _inFlight = new ConcurrentDictionary<ChecksumFile, long[]>();
		private long _completedBytes;
		private long _skippedBytes;

		internal VerifyContext(CancellationToken cancel, PauseGate pause)
		{
			Cancel = cancel;
			Pause = pause;
		}

		public VerifyContext(CancellationToken cancel)
			: this(cancel, new PauseGate())
		{
		}

		public CancellationToken Cancel { get; private set; }
		internal PauseGate Pause { get; private set; }

		/// <summary>Bytes of finished files (their full length, cached or not) plus bytes read so far of files in progress.</summary>
		public long BytesProcessed
		{
			get
			{
				long total = Interlocked.Read(ref _completedBytes);
				foreach (long[] counter in _inFlight.Values)
					total += Interlocked.Read(ref counter[0]);
				return total;
			}
		}

		/// <summary>
		/// BytesProcessed without the unread part of files that failed (unreadable or cancelled): the bytes actually
		/// checked, for speed and statistics. BytesProcessed still counts them so progress reaches 100%.
		/// </summary>
		public long BytesChecked
		{
			get { return BytesProcessed - Interlocked.Read(ref _skippedBytes); }
		}

		private readonly ConcurrentQueue<string> _notRecycled = new ConcurrentQueue<string>();

		/// <summary>Failed files that were to be deleted but the Recycle Bin wouldn't take (left in place).</summary>
		public IReadOnlyCollection<string> NotRecycled
		{
			get { return _notRecycled; }
		}

		internal void AddNotRecycled(string fileName)
		{
			_notRecycled.Enqueue(fileName);
		}

		/// <summary>Fraction (0-1) of a file in progress, or null if it isn't being read right now.</summary>
		public double? GetFileProgress(ChecksumFile file)
		{
			long[] counter;
			if (file.FileInfo == null || file.FileInfo.Length == 0 || !_inFlight.TryGetValue(file, out counter))
				return null;
			return Math.Min(1.0, (double)Interlocked.Read(ref counter[0]) / file.FileInfo.Length);
		}

		internal long[] Begin(ChecksumFile file)
		{
			long[] counter = new long[1];
			_inFlight[file] = counter;
			return counter;
		}

		internal void End(ChecksumFile file, long length, bool finished)
		{
			long[] removed;
			_inFlight.TryRemove(file, out removed);
			Interlocked.Add(ref _completedBytes, length);
			if (!finished && removed != null)
				Interlocked.Add(ref _skippedBytes, Math.Max(0, length - Interlocked.Read(ref removed[0])));
		}
	}

	/// <summary>
	/// Checks one file of a set: handles .bad/.missing files, optional renaming, the result cache,
	/// hashing and what to do with a failed file. Safe to call from several threads for different files.
	/// </summary>
	public static class FileVerifier
	{
		/// <summary>Builds a case-insensitive lookup of cached checksums for a set's directory.</summary>
		public static IReadOnlyDictionary<string, ChecksumFile> LoadCache(ChecksumSet set, VerifyOptions options)
		{
			Dictionary<string, ChecksumFile> lookup = new Dictionary<string, ChecksumFile>(StringComparer.OrdinalIgnoreCase);
			if (!options.UseCache)
				return lookup;

			foreach (ChecksumFile cached in Cache.GetCache(set.Type, set.Directory))
				lookup[cached.FileName] = cached; // later rows are newer
			return lookup;
		}

		/// <summary>
		/// Sets file.State and file.CurrentChecksum. Throws OperationCanceledException when the run is cancelled.
		/// </summary>
		public static ChecksumFileState Verify(ChecksumSet set, ChecksumFile file, VerifyOptions options,
			IReadOnlyDictionary<string, ChecksumFile> cache, VerifyContext context)
		{
			bool checkCacheThisFile = options.UseCache;

			// a name outside the set's folder (absolute or with ".."): check it, but never create, rename or delete anything
			if (!file.IsInsideSet)
				options = VerifyOptions.ReadOnly(options.UseCache);

			// check for .bad if missing
			if (file.FileInfo == null)
			{
				string fullFileName = Path.Combine(set.Directory, file.FileName);
				string badFileName = fullFileName + ".bad";
				if (File.Exists(badFileName))
				{
					File.Move(badFileName, fullFileName);
					file.FileInfo = TryGetFileInfo(fullFileName);
					checkCacheThisFile = false;
				}
			}

			if (file.FileInfo == null)
			{
				file.State = ChecksumFileState.Missing;

				if (options.CreateMissingMarkers)
				{
					string missingFileName = Path.Combine(set.Directory, file.FileName) + ".missing";
					if (!File.Exists(missingFileName))
						File.WriteAllBytes(missingFileName, new byte[0]);
				}

				return file.State;
			}

			long length = file.FileInfo.Length;
			long[] counter = context.Begin(file);
			bool finished = false;
			try
			{
				if (options.CleanupBadMissing)
				{
					DeleteIfEmpty(file.FileInfo.FullName + ".missing");
					DeleteIfEmpty(file.FileInfo.FullName + ".bad");
				}

				if (options.Renaming != CheckRenaming.None)
					Rename(set, file, options.Renaming);

				file.CurrentChecksum = null;

				bool fromCache = false;
				ChecksumFile cached;
				if (checkCacheThisFile && cache.TryGetValue(file.FileInfo.FullName, out cached) && cached.CacheLength == length)
				{
					TimeSpan delta = cached.CacheLastWriteUtc - file.FileInfo.LastWriteTimeUtc;
					// a cached checksum that doesn't match is re-checked from the file, in case the cache is stale
					if (delta < TimeSpan.FromSeconds(0.01) && delta > TimeSpan.FromSeconds(-0.01) &&
						string.Compare(cached.OriginalChecksum, file.OriginalChecksum, true) == 0)
					{
						file.CurrentChecksum = cached.OriginalChecksum;
						fromCache = true;
					}
				}

				if (!fromCache)
				{
					file.CurrentChecksum = ComputeChecksum(ChecksumAlgorithm.Get(set.Type), file.FileInfo, context, counter);
					if (options.UseCache)
						Cache.Update(set.Type, file.FileInfo, file.CurrentChecksum);
				}

				finished = true;

				if (string.Compare(file.CurrentChecksum, file.OriginalChecksum, true) == 0)
				{
					file.State = ChecksumFileState.OK;
				}
				else
				{
					file.State = ChecksumFileState.Bad;

					string changedFileName = file.FileInfo.FullName;
					if (options.RenameBadFiles)
					{
						string badFileName = changedFileName + ".bad";
						if (!File.Exists(badFileName))
						{
							File.Move(changedFileName, badFileName);
							changedFileName = badFileName;
						}
					}

					if (options.DeleteFailedFiles && !Program.SafeDelete(changedFileName, options.UseRecycleBin))
						context.AddNotRecycled(changedFileName);
				}

				return file.State;
			}
			finally
			{
				context.End(file, length, finished);
			}
		}

		/// <summary>
		/// Hashes a file, counting its bytes in the context's progress (tracked as `file`) and honoring pause and cancellation.
		/// </summary>
		public static string HashFile(ChecksumAlgorithm algorithm, ChecksumFile file, VerifyContext context)
		{
			long[] counter = context.Begin(file);
			bool finished = false;
			try
			{
				string checksum = ComputeChecksum(algorithm, file.FileInfo, context, counter);
				finished = true;
				return checksum;
			}
			finally
			{
				context.End(file, file.FileInfo.Length, finished);
			}
		}

		/// <summary>Hashes a file that isn't part of a set (counted in the context's progress).</summary>
		public static string HashFile(ChecksumAlgorithm algorithm, FileInfo fileInfo, VerifyContext context)
		{
			return HashFile(algorithm, new ChecksumFile(null) { FileInfo = fileInfo }, context);
		}

		private static string ComputeChecksum(ChecksumAlgorithm algorithm, FileInfo fileInfo, VerifyContext context, long[] counter)
		{
			Action<int> onRead = null;
			if (counter != null)
				onRead = n => Interlocked.Add(ref counter[0], n);

			FileStream file = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
			using (ProgressStream stream = new ProgressStream(file, onRead, context.Cancel, context.Pause))
				return algorithm.Compute(stream);
		}

		private static void Rename(ChecksumSet set, ChecksumFile file, CheckRenaming renaming)
		{
			string realFileName = Path.GetFileName(file.FileInfo.FullName);
			string newFileName = null;

			if (renaming == CheckRenaming.Lowercase)
				newFileName = realFileName.ToLower();
			else if (renaming == CheckRenaming.MatchSet)
				newFileName = Path.GetFileName(Path.Combine(set.Directory, file.FileName));

			if (string.IsNullOrEmpty(newFileName))
				return;

			string fullNewFileName = Path.Combine(Path.GetDirectoryName(file.FileInfo.FullName), newFileName);

			Random r = new Random();
			string tempFileName;
			do
			{
				tempFileName = string.Format("{0}.{1}", fullNewFileName, r.Next(0, 9999));
			} while (File.Exists(tempFileName));

			File.Move(file.FileInfo.FullName, tempFileName);
			File.Move(tempFileName, fullNewFileName);
		}

		private static void DeleteIfEmpty(string fileName)
		{
			FileInfo info = TryGetFileInfo(fileName);
			if (info != null && info.Length == 0)
				File.Delete(fileName);
		}

		private static FileInfo TryGetFileInfo(string fileName)
		{
			FileInfo info = new FileInfo(fileName);
			return info.Exists ? info : null;
		}
	}
}
