using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace TaoSFV.Par2
{
	/// <summary>
	/// Finds intact slices away from their place: when bytes were inserted into or removed from a file, every slice after
	/// that point is shifted. A window one slice long slides over each damaged file with a rolling CRC32; a CRC that
	/// belongs to a missing slice is confirmed by MD5. (par2cmdline does the same.)
	/// </summary>
	internal static class Par2SliceScanner
	{
		private const int ReadSize = 4 * 1024 * 1024;
		private static readonly uint[] Table = BuildTable();

		private sealed class Wanted
		{
			public Par2FileResult File;
			public int Index;
			public int DataLength;   // < slice size for a file's last slice
		}

		public static void FindShiftedSlices(Par2Set set, List<Par2FileResult> results, CancellationToken cancel, Action<long> progress)
		{
			long sliceSize = set.SliceSize;
			if (sliceSize > int.MaxValue / 2)
				return;

			// the slices still missing, by CRC32 (full slices) and the partial last slices
			Dictionary<uint, List<Wanted>> byCrc = new Dictionary<uint, List<Wanted>>();
			List<Wanted> partial = new List<Wanted>();
			foreach (Par2FileResult result in results)
			{
				if (result.File.Slices == null || result.File.Slices.Length != result.TotalSlices ||
					(result.State != Par2FileState.Damaged && result.State != Par2FileState.Missing))
					continue;
				if (result.Locations == null)
					result.Locations = new Par2SliceLocation[result.TotalSlices];

				for (int i = 0; i < result.TotalSlices; i++)
				{
					if (result.Locations[i].Path != null)
						continue;
					int length = (int)Math.Min(sliceSize, result.File.Length - i * sliceSize);
					Wanted wanted = new Wanted { File = result, Index = i, DataLength = length };
					if (length == sliceSize)
					{
						List<Wanted> list;
						if (!byCrc.TryGetValue(result.File.Slices[i].Crc32, out list))
							byCrc[result.File.Slices[i].Crc32] = list = new List<Wanted>();
						list.Add(wanted);
					}
					else if (length > 0)
						partial.Add(wanted);
				}
			}
			if (byCrc.Count == 0 && partial.Count == 0)
				return;

			foreach (Par2FileResult damaged in results.Where(r => r.State == Par2FileState.Damaged))
			{
				string path = Path.Combine(set.Directory, damaged.File.Name);
				if (byCrc.Count != 0)
					Scan(path, sliceSize, damaged, byCrc, cancel);
				if (partial.Count != 0)
					CheckTail(path, sliceSize, partial);
			}

			foreach (Par2FileResult result in results.Where(r => r.Locations != null))
			{
				string own = Path.Combine(set.Directory, result.File.Name);
				int shifted = 0;
				for (int i = 0; i < result.Locations.Length; i++)
				{
					Par2SliceLocation location = result.Locations[i];
					if (location.Path != null && (location.Offset != i * sliceSize || !string.Equals(location.Path, own, StringComparison.OrdinalIgnoreCase)))
						shifted++;
				}
				result.ShiftedSlices = shifted;
				if (result.State == Par2FileState.Missing && result.GoodSlices == 0)
					result.Locations = null;
			}
		}

		private static void Scan(string path, long sliceSize, Par2FileResult owner, Dictionary<uint, List<Wanted>> byCrc, CancellationToken cancel)
		{
			int window = (int)sliceSize;
			long fileLength = new FileInfo(path).Length;
			if (fileLength < window)
				return;

			// CRC32 of a fixed-length window = raw CRC (no init, no final xor) ^ the CRC of that many zero bytes
			uint zeroCrc = Crc32.HashToUInt32(new byte[window]);
			uint[] outTable = BuildOutTable(window);

			byte[] buffer = new byte[window + ReadSize];
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
			{
				int filled = Fill(stream, buffer, 0);
				long bufferStart = 0;   // file position of buffer[0]
				int start = 0;          // window start within buffer
				uint raw = Crc32.HashToUInt32(buffer.AsSpan(0, window)) ^ zeroCrc;

				while (true)
				{
					if ((start & 0xFFFFF) == 0)
						cancel.ThrowIfCancellationRequested();

					long position = bufferStart + start;
					bool jumped = false;

					// skip slices of this file that are intact in place
					if (position % sliceSize == 0)
					{
						long index = position / sliceSize;
						if (index < owner.Locations.Length && owner.Locations[index].Path != null &&
							owner.Locations[index].Offset == position && owner.Locations[index].Path == path)
							jumped = true;
					}

					List<Wanted> candidates;
					if (!jumped && byCrc.TryGetValue(raw ^ zeroCrc, out candidates))
					{
						byte[] md5 = MD5.HashData(buffer.AsSpan(start, window));
						Wanted match = candidates.FirstOrDefault(c => c.File.File.Slices[c.Index].Md5.AsSpan().SequenceEqual(md5));
						if (match != null)
						{
							match.File.Locations[match.Index] = new Par2SliceLocation(path, position);
							candidates.Remove(match);
							if (candidates.Count == 0)
								byCrc.Remove(raw ^ zeroCrc);
							if (byCrc.Count == 0)
								return;
							jumped = true;
						}
					}

					if (jumped)
					{
						// next window starts right after this slice
						start += window;
						if (!Ensure(stream, buffer, ref start, ref filled, ref bufferStart, window))
							return;
						raw = Crc32.HashToUInt32(buffer.AsSpan(start, window)) ^ zeroCrc;
						continue;
					}

					// roll one byte: drop buffer[start], take buffer[start + window]
					if (start + window >= filled && !Ensure(stream, buffer, ref start, ref filled, ref bufferStart, window + 1))
						return;
					byte outgoing = buffer[start];
					byte incoming = buffer[start + window];
					raw = Table[(raw ^ incoming) & 0xFF] ^ (raw >> 8) ^ outTable[outgoing];
					start++;
				}
			}
		}

		/// <summary>Makes sure need bytes from start are in the buffer (moving the window to the front and reading more); false at the end of the file.</summary>
		private static bool Ensure(Stream stream, byte[] buffer, ref int start, ref int filled, ref long bufferStart, int need)
		{
			if (start + need <= filled)
				return true;
			int keep = Math.Max(0, filled - start);
			Buffer.BlockCopy(buffer, start, buffer, 0, keep);
			bufferStart += start;
			start = 0;
			filled = Fill(stream, buffer, keep);
			return need <= filled;
		}

		private static int Fill(Stream stream, byte[] buffer, int from)
		{
			int filled = from, n;
			while (filled < buffer.Length && (n = stream.Read(buffer, filled, buffer.Length - filled)) > 0)
				filled += n;
			return filled;
		}

		/// <summary>The last slice of a file is shorter; it can only be recognized at the end of a scanned file.</summary>
		private static void CheckTail(string path, long sliceSize, List<Wanted> partial)
		{
			long fileLength = new FileInfo(path).Length;
			foreach (Wanted wanted in partial.ToList())
			{
				if (wanted.DataLength > fileLength)
					continue;
				byte[] slice = new byte[sliceSize];
				using (FileStream stream = File.OpenRead(path))
				{
					stream.Position = fileLength - wanted.DataLength;
					stream.ReadExactly(slice, 0, wanted.DataLength);
				}
				Par2Slice expected = wanted.File.File.Slices[wanted.Index];
				if (Crc32.HashToUInt32(slice) == expected.Crc32 && MD5.HashData(slice).AsSpan().SequenceEqual(expected.Md5))
				{
					wanted.File.Locations[wanted.Index] = new Par2SliceLocation(path, fileLength - wanted.DataLength);
					partial.Remove(wanted);
				}
			}
		}

		private static uint[] BuildTable()
		{
			uint[] table = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				uint c = i;
				for (int k = 0; k < 8; k++)
					c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
				table[i] = c;
			}
			return table;
		}

		/// <summary>
		/// outTable[b] = raw CRC of byte b followed by window zero bytes: what the byte leaving the window contributes.
		/// Linear in b, so 8 CRCs (one per bit) give the whole table.
		/// </summary>
		private static uint[] BuildOutTable(int window)
		{
			byte[] data = new byte[window + 1];
			uint zeros = Crc32.HashToUInt32(data); // CRC of window + 1 zero bytes
			uint[] bits = new uint[8];
			for (int bit = 0; bit < 8; bit++)
			{
				data[0] = (byte)(1 << bit);
				bits[bit] = Crc32.HashToUInt32(data) ^ zeros;
			}

			uint[] table = new uint[256];
			for (int b = 1; b < 256; b++)
			{
				uint value = 0;
				for (int bit = 0; bit < 8; bit++)
					if ((b & (1 << bit)) != 0)
						value ^= bits[bit];
				table[b] = value;
			}
			return table;
		}
	}
}
