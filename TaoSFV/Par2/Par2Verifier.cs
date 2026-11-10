using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace TaoSFV.Par2
{
	public enum Par2FileState
	{
		OK,
		/// <summary>Not under its name, but an identical file was found under another name in the folder.</summary>
		Renamed,
		Damaged,
		Missing
	}

	public sealed class Par2FileResult
	{
		public Par2File File { get; internal set; }
		public Par2FileState State { get; internal set; }

		/// <summary>True for files the recovery data covers; damage to the others can only be reported.</summary>
		public bool Protected { get; internal set; }

		/// <summary>For Renamed: the full path of the identical file.</summary>
		public string FoundAs { get; internal set; }

		public int TotalSlices { get; internal set; }

		/// <summary>Slices that are intact in place (all of them for OK and Renamed).</summary>
		public int GoodSlices { get; internal set; }

		/// <summary>Per slice: intact in place. Null when the file is missing.</summary>
		public bool[] SliceOk { get; internal set; }

		public int MissingSlices
		{
			get { return TotalSlices - GoodSlices; }
		}
	}

	public sealed class Par2Result
	{
		public Par2Set Set { get; internal set; }
		public List<Par2FileResult> Files { get; internal set; }

		/// <summary>Slices of protected files that are damaged or missing.</summary>
		public int SlicesNeeded
		{
			get { return Files.Where(f => f.Protected).Sum(f => f.MissingSlices); }
		}

		public int RecoverySlicesAvailable
		{
			get { return Set.RecoverySlices.Count; }
		}

		public bool AllOk
		{
			get { return Files.All(f => f.State == Par2FileState.OK); }
		}

		/// <summary>Protected files can be repaired: enough recovery slices, and no damage outside the protected files.</summary>
		public bool Repairable
		{
			get { return SlicesNeeded <= RecoverySlicesAvailable && Files.Where(f => !f.Protected).All(f => f.State == Par2FileState.OK); }
		}
	}

	/// <summary>Checks the files of a PAR2 set: whole-file MD5, then slice by slice (CRC32, confirmed by MD5) for damaged files.</summary>
	public static class Par2Verifier
	{
		private const int Hash16kLength = 16 * 1024;

		public static Par2Result Verify(Par2Set set, CancellationToken cancel = default(CancellationToken), Action<long> progress = null)
		{
			List<Par2FileResult> results = new List<Par2FileResult>();
			HashSet<string> claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			IEnumerable<Tuple<Guid, bool>> ids = set.RecoveryFileIds.Select(id => Tuple.Create(id, true))
				.Concat(set.NonRecoveryFileIds.Select(id => Tuple.Create(id, false)));

			foreach (Tuple<Guid, bool> entry in ids)
			{
				cancel.ThrowIfCancellationRequested();
				Par2File file;
				if (!set.Files.TryGetValue(entry.Item1, out file) || !file.HasDescription)
					continue; // its File Description packet was lost; nothing to check against

				Par2FileResult result = VerifyFile(set, file, cancel, progress);
				result.Protected = entry.Item2;
				string path = Path.Combine(set.Directory, file.Name);
				claimed.Add(path);
				results.Add(result);
			}

			// a missing file may be there under another name (same size and hashes)
			foreach (Par2FileResult result in results.Where(r => r.State == Par2FileState.Missing))
			{
				string found = FindRenamed(set, result.File, claimed, cancel);
				if (found != null)
				{
					claimed.Add(found);
					result.State = Par2FileState.Renamed;
					result.FoundAs = found;
					result.GoodSlices = result.TotalSlices;
					result.SliceOk = Enumerable.Repeat(true, result.TotalSlices).ToArray();
				}
			}

			return new Par2Result { Set = set, Files = results };
		}

		private static Par2FileResult VerifyFile(Par2Set set, Par2File file, CancellationToken cancel, Action<long> progress)
		{
			int total = file.SliceCount(set.SliceSize);
			Par2FileResult result = new Par2FileResult { File = file, TotalSlices = total };
			string path = Path.Combine(set.Directory, file.Name);

			if (!File.Exists(path))
			{
				result.State = Par2FileState.Missing;
				return result;
			}

			// one pass: the whole-file MD5, and each slice's CRC32 (cheap) in case the file turns out damaged
			long length = new FileInfo(path).Length;
			uint[] crcs = new uint[total];
			byte[] fileHash = ReadSlices(path, set.SliceSize, total, cancel, progress, (index, slice) => crcs[index] = Crc32.HashToUInt32(slice));

			if (length == file.Length && fileHash.AsSpan().SequenceEqual(file.Hash))
			{
				result.State = Par2FileState.OK;
				result.GoodSlices = total;
				result.SliceOk = Enumerable.Repeat(true, total).ToArray();
				return result;
			}

			// damaged: a slice is good when its CRC32 and MD5 both match (MD5 only for the CRC matches, in a second pass)
			bool[] ok = new bool[total];
			if (file.Slices != null && file.Slices.Length == total)
			{
				bool[] candidates = new bool[total];
				for (int i = 0; i < total; i++)
					candidates[i] = crcs[i] == file.Slices[i].Crc32 && (long)i * set.SliceSize < length;

				if (candidates.Any(c => c))
				{
					ReadSlices(path, set.SliceSize, total, cancel, null, (index, slice) =>
					{
						if (candidates[index])
							ok[index] = MD5.HashData(slice).AsSpan().SequenceEqual(file.Slices[index].Md5);
					});
				}
			}

			result.State = Par2FileState.Damaged;
			result.SliceOk = ok;
			result.GoodSlices = ok.Count(b => b);
			return result;
		}

		/// <summary>
		/// Reads a file slice by slice (the last one, and any slices past the end, zero-padded to the slice size, as PAR2
		/// hashes them) and returns the MD5 of the file's actual bytes.
		/// </summary>
		internal static byte[] ReadSlices(string path, long sliceSize, int sliceCount, CancellationToken cancel, Action<long> progress,
			Action<int, ReadOnlySpan<byte>> onSlice)
		{
			byte[] buffer = ArrayPool<byte>.Shared.Rent((int)sliceSize);
			try
			{
				using (IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
				using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
				{
					int size = (int)sliceSize;
					for (int index = 0; ; index++)
					{
						cancel.ThrowIfCancellationRequested();
						int read = 0, n;
						while (read < size && (n = stream.Read(buffer, read, size - read)) > 0)
							read += n;

						if (read > 0)
						{
							md5.AppendData(buffer, 0, read);
							if (progress != null)
								progress(read);
						}

						// bytes beyond the expected slices (a file that grew) only count toward the MD5
						if (index < sliceCount)
						{
							Array.Clear(buffer, read, size - read);
							onSlice(index, new ReadOnlySpan<byte>(buffer, 0, size));
						}

						if (read < size)
						{
							// end of file: slices the file is too short for are all zeros
							Array.Clear(buffer, 0, size);
							for (int rest = index + 1; rest < sliceCount; rest++)
								onSlice(rest, new ReadOnlySpan<byte>(buffer, 0, size));
							break;
						}
					}
					return md5.GetHashAndReset();
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}

		private static string FindRenamed(Par2Set set, Par2File file, HashSet<string> claimed, CancellationToken cancel)
		{
			string folder = Path.GetDirectoryName(Path.Combine(set.Directory, file.Name));
			if (!Directory.Exists(folder))
				return null;

			foreach (string candidate in Directory.GetFiles(folder))
			{
				cancel.ThrowIfCancellationRequested();
				if (claimed.Contains(candidate) || Par2Reader.IsPar2File(candidate))
					continue;
				FileInfo info = new FileInfo(candidate);
				if (info.Length != file.Length)
					continue;
				if (!Hash16k(candidate).AsSpan().SequenceEqual(file.Hash16k))
					continue;
				using (FileStream stream = File.OpenRead(candidate))
				{
					if (MD5.HashData(stream).AsSpan().SequenceEqual(file.Hash))
						return candidate;
				}
			}
			return null;
		}

		internal static byte[] Hash16k(string path)
		{
			using (FileStream stream = File.OpenRead(path))
			{
				byte[] buffer = new byte[Hash16kLength];
				int read = 0, n;
				while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0)
					read += n;
				return MD5.HashData(buffer.AsSpan(0, read));
			}
		}
	}
}
