using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TaoSFV.Par2
{
	public sealed class Par2RepairResult
	{
		/// <summary>Files rebuilt from recovery data (a damaged original is kept as name.bad).</summary>
		public List<string> Repaired { get; } = new List<string>();

		/// <summary>Files moved back to their name from the name they were found under.</summary>
		public List<string> Renamed { get; } = new List<string>();

		/// <summary>Damaged originals that were kept, renamed to .bad.</summary>
		public List<string> KeptDamaged { get; } = new List<string>();
	}

	/// <summary>
	/// Rebuilds damaged and missing files of a PAR2 set from its recovery slices (Reed-Solomon over GF(2^16)).
	/// New files are written next to the originals and checked against their MD5 before anything is replaced.
	/// </summary>
	public static class Par2Repairer
	{
		private const string TempSuffix = ".taosfv-repair";

		/// <param name="verified">A fresh result of Par2Verifier.Verify for the set.</param>
		/// <param name="memoryLimit">Bytes for the rebuilt slices held at once; bigger sets are worked through in parts.</param>
		public static Par2RepairResult Repair(Par2Result verified, CancellationToken cancel = default(CancellationToken),
			Action<long> progress = null, long memoryLimit = 256L * 1024 * 1024)
		{
			Par2Set set = verified.Set;
			Par2RepairResult result = new Par2RepairResult();
			if (verified.AllOk)
				return result;
			if (!verified.Repairable)
				throw new InvalidOperationException(string.Format("Not enough recovery data: {0} blocks needed, {1} available.",
					verified.SlicesNeeded, verified.RecoverySlicesAvailable));

			Dictionary<Guid, Par2FileResult> byId = verified.Files.ToDictionary(f => f.File.Id);
			List<Par2FileResult> toRebuild = verified.Files.Where(f => f.Protected && (f.State == Par2FileState.Damaged || f.State == Par2FileState.Missing)).ToList();
			Dictionary<Par2File, string> temps = new Dictionary<Par2File, string>();

			try
			{
				using (Par2Slices slices = new Par2Slices(set))
				{
					// where each input slice can be read; null = to be rebuilt
					foreach (Par2Slices.Source input in slices.Inputs)
					{
						Par2FileResult file = byId[input.File.Id];
						if (file.Locations != null && file.Locations[input.SliceIndex].Path != null)
						{
							input.Path = file.Locations[input.SliceIndex].Path;
							input.Offset = file.Locations[input.SliceIndex].Offset;
						}
					}

					List<int> missing = Enumerable.Range(0, slices.Inputs.Count).Where(i => slices.Inputs[i].Path == null).ToList();
					if (missing.Count != 0)
						Rebuild(set, slices, missing, toRebuild, temps, cancel, progress, memoryLimit);
				}

				// every rebuilt file must match its MD5 before anything is replaced
				foreach (KeyValuePair<Par2File, string> temp in temps)
				{
					byte[] hash;
					using (FileStream stream = File.OpenRead(temp.Value))
						hash = MD5.HashData(stream);
					if (!hash.AsSpan().SequenceEqual(temp.Key.Hash))
						throw new InvalidDataException("The rebuilt " + temp.Key.Name + " doesn't match its MD5; nothing was changed.");
				}
			}
			catch
			{
				foreach (string temp in temps.Values)
					TryDelete(temp);
				throw;
			}

			foreach (KeyValuePair<Par2File, string> temp in temps)
			{
				string path = Path.Combine(set.Directory, temp.Key.Name);
				if (File.Exists(path))
				{
					string bad = UniqueName(path + ".bad");
					File.Move(path, bad);
					result.KeptDamaged.Add(bad);
				}
				File.Move(temp.Value, path);
				result.Repaired.Add(path);
			}

			foreach (Par2FileResult file in verified.Files.Where(f => f.State == Par2FileState.Renamed))
			{
				string path = Path.Combine(set.Directory, file.File.Name);
				if (!File.Exists(path))
				{
					File.Move(file.FoundAs, path);
					result.Renamed.Add(path);
				}
			}
			return result;
		}

		/// <summary>Stops before writing when the rebuilt files can't fit (a damaged or crafted set can claim any size).</summary>
		private static void CheckFreeSpace(string directory, long needed)
		{
			long free;
			try
			{
				free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))).AvailableFreeSpace;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
			{
				return; // e.g. a network share DriveInfo can't read: let the write itself fail
			}
			if (needed > free)
				throw new IOException(string.Format("Repair needs {0:#,0} MB free in {1}; only {2:#,0} MB is free. Nothing was changed.",
					needed >> 20, directory, free >> 20));
		}

		private static void Rebuild(Par2Set set, Par2Slices slices, List<int> missing, List<Par2FileResult> toRebuild,
			Dictionary<Par2File, string> temps, CancellationToken cancel, Action<long> progress, long memoryLimit)
		{
			int m = missing.Count;
			List<int> exponents;
			ushort[,] inverse = SolveFor(set, slices, missing, out exponents);
			List<Par2RecoverySlice> recovery = exponents.Select(e => set.RecoverySlices[e]).ToList();

			// each rebuilt slice is a combination of every available input slice and the chosen recovery slices:
			// slice_k = sum_r inv[k,r] * (recovery_r + sum_i c_i^e_r * input_i)
			List<int> present = Enumerable.Range(0, slices.Inputs.Count).Where(i => slices.Inputs[i].Path != null).ToList();
			ushort[][] presentFactors = new ushort[present.Count][];
			Parallel.For(0, present.Count, p =>
			{
				ushort[] powers = new ushort[m];
				for (int r = 0; r < m; r++)
					powers[r] = Galois16.Power(slices.Constants[present[p]], exponents[r]);
				ushort[] factors = new ushort[m];
				for (int k = 0; k < m; k++)
				{
					ushort sum = 0;
					for (int r = 0; r < m; r++)
						sum ^= Galois16.Multiply(inverse[k, r], powers[r]);
					factors[k] = sum;
				}
				presentFactors[p] = factors;
			});

			CheckFreeSpace(set.Directory, toRebuild.Sum(f => f.File.Length));

			// rebuilt files start as copies of their intact slices, wherever those were found
			foreach (Par2FileResult file in toRebuild)
			{
				string temp = Path.Combine(set.Directory, file.File.Name) + TempSuffix;
				temps[file.File] = temp;
				using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write))
				{
					output.SetLength(file.File.Length);
					if (file.Locations != null)
					{
						byte[] buffer = new byte[set.SliceSize];
						for (int s = 0; s < file.TotalSlices; s++)
						{
							Par2SliceLocation location = file.Locations[s];
							if (location.Path == null)
								continue;
							long position = s * set.SliceSize;
							int count = (int)Math.Min(set.SliceSize, file.File.Length - position);
							slices.ReadFile(location.Path, location.Offset, count, buffer);
							output.Position = position;
							output.Write(buffer, 0, count);
						}
					}
				}
			}

			Dictionary<Par2File, FileStream> outputs = temps.ToDictionary(t => t.Key, t => new FileStream(t.Value, FileMode.Open, FileAccess.Write));
			try
			{
				// the slice is worked through in parts small enough for all rebuilt slices to fit in memory_limit
				long chunk = Math.Max(4, Math.Min(set.SliceSize, memoryLimit / m) / 4 * 4);
				byte[][] rebuilt = new byte[m][];
				for (int k = 0; k < m; k++)
					rebuilt[k] = new byte[chunk];
				byte[] input = new byte[chunk];

				for (long offset = 0; offset < set.SliceSize; offset += chunk)
				{
					int length = (int)Math.Min(chunk, set.SliceSize - offset);
					for (int k = 0; k < m; k++)
						Array.Clear(rebuilt[k], 0, length);

					for (int p = 0; p < present.Count; p++)
					{
						cancel.ThrowIfCancellationRequested();
						slices.ReadInput(slices.Inputs[present[p]], offset, input.AsSpan(0, length));
						Accumulate(rebuilt, input, length, presentFactors[p]);
						if (progress != null)
							progress(length);
					}

					for (int r = 0; r < m; r++)
					{
						cancel.ThrowIfCancellationRequested();
						slices.ReadFile(recovery[r].FileName, recovery[r].Offset + offset, length, input.AsSpan(0, length));
						ushort[] factors = new ushort[m];
						for (int k = 0; k < m; k++)
							factors[k] = inverse[k, r];
						Accumulate(rebuilt, input, length, factors);
						if (progress != null)
							progress(length);
					}

					for (int k = 0; k < m; k++)
					{
						Par2Slices.Source target = slices.Inputs[missing[k]];
						long position = target.SliceIndex * set.SliceSize + offset;
						long count = Math.Min(length, target.File.Length - position);
						if (count <= 0)
							continue; // padding past the end of the file
						FileStream output = outputs[target.File];
						output.Position = position;
						output.Write(rebuilt[k], 0, (int)count);
					}
				}
			}
			finally
			{
				foreach (FileStream output in outputs.Values)
					output.Dispose();
				slices.CloseFiles();
			}
		}

		private static void Accumulate(byte[][] rebuilt, byte[] input, int length, ushort[] factors)
		{
			Parallel.For(0, rebuilt.Length, k => Galois16.MultiplyAdd(rebuilt[k].AsSpan(0, length), input.AsSpan(0, length), factors[k]));
		}

		/// <summary>
		/// Picks recovery slices whose matrix (c_missing^e) can be inverted, and returns the inverse. PAR2's matrix can
		/// occasionally be singular; other recovery slices are tried then.
		/// </summary>
		private static ushort[,] SolveFor(Par2Set set, Par2Slices slices, List<int> missing, out List<int> exponents)
		{
			int m = missing.Count;
			List<int> available = set.RecoverySlices.Keys.OrderBy(e => e).ToList();
			for (int start = 0; start + m <= available.Count; start++)
			{
				exponents = available.GetRange(start, m);
				ushort[,] matrix = new ushort[m, m];
				for (int r = 0; r < m; r++)
					for (int k = 0; k < m; k++)
						matrix[r, k] = Galois16.Power(slices.Constants[missing[k]], exponents[r]);
				if (Galois16.Invert(matrix))
					return matrix;
			}
			throw new InvalidOperationException("The recovery data can't be combined to rebuild these blocks.");
		}

		private static string UniqueName(string path)
		{
			string candidate = path;
			for (int i = 1; File.Exists(candidate); i++)
				candidate = path + "." + i;
			return candidate;
		}

		private static void TryDelete(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}
}
