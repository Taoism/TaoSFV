using System;
using System.Collections.Generic;
using System.IO;

namespace TaoSFV.Par2
{
	/// <summary>
	/// The input slices of a set in PAR2 order (files as the Main packet lists them, slices in file order), and where to
	/// read each one. Reads are zero-padded past a file's end, as PAR2 pads the last slice.
	/// </summary>
	internal sealed class Par2Slices : IDisposable
	{
		public sealed class Source
		{
			public Par2File File;
			public int SliceIndex;     // within the file
			public string Path;        // where the bytes are read from (null: not available)
		}

		private readonly Dictionary<string, FileStream> _streams = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

		public Par2Slices(Par2Set set)
		{
			SliceSize = set.SliceSize;
			Inputs = new List<Source>();
			foreach (Par2File file in set.RecoveryFiles)
			{
				int count = file.SliceCount(set.SliceSize);
				for (int i = 0; i < count; i++)
					Inputs.Add(new Source { File = file, SliceIndex = i });
			}
			Constants = Galois16.InputConstants(Inputs.Count);
		}

		public long SliceSize { get; private set; }
		public List<Source> Inputs { get; private set; }
		public ushort[] Constants { get; private set; }

		/// <summary>Reads part of an input slice (count bytes from offset within the slice), zero-padded past the file's end.</summary>
		public void ReadInput(Source input, long offset, Span<byte> buffer)
		{
			long position = input.SliceIndex * SliceSize + offset;
			ReadFile(input.Path, position, Math.Max(0, Math.Min(buffer.Length, input.File.Length - position)), buffer);
		}

		/// <summary>Reads count bytes at position into buffer (the rest of buffer zeroed).</summary>
		public void ReadFile(string path, long position, long count, Span<byte> buffer)
		{
			buffer.Clear();
			if (count <= 0)
				return;

			FileStream stream;
			if (!_streams.TryGetValue(path, out stream))
			{
				stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
				_streams[path] = stream;
			}
			stream.Position = position;
			int done = 0;
			while (done < count)
			{
				int n = stream.Read(buffer.Slice(done, (int)count - done));
				if (n <= 0)
					break; // shorter than expected: the rest stays zero
				done += n;
			}
		}

		public void CloseFiles()
		{
			foreach (FileStream stream in _streams.Values)
				stream.Dispose();
			_streams.Clear();
		}

		public void Dispose()
		{
			CloseFiles();
		}
	}

	/// <summary>Computes PAR2 recovery slices: for exponent e, the sum over all input slices of constant^e * slice.</summary>
	internal static class Par2Encoder
	{
		public static byte[][] ComputeRecovery(Par2Set set, IList<int> exponents)
		{
			using (Par2Slices slices = new Par2Slices(set))
			{
				foreach (Par2Slices.Source input in slices.Inputs)
					input.Path = Path.Combine(set.Directory, input.File.Name);

				byte[][] recovery = new byte[exponents.Count][];
				for (int r = 0; r < recovery.Length; r++)
					recovery[r] = new byte[set.SliceSize];

				byte[] buffer = new byte[set.SliceSize];
				for (int i = 0; i < slices.Inputs.Count; i++)
				{
					slices.ReadInput(slices.Inputs[i], 0, buffer);
					for (int r = 0; r < recovery.Length; r++)
						Galois16.MultiplyAdd(recovery[r], buffer, Galois16.Power(slices.Constants[i], exponents[r]));
				}
				return recovery;
			}
		}
	}
}
