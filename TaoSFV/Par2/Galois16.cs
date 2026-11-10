using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace TaoSFV.Par2
{
	/// <summary>
	/// GF(2^16) arithmetic as PAR2 uses it: generator polynomial x^16 + x^12 + x^3 + x + 1 (0x1100B), data as
	/// little-endian 16-bit words.
	/// </summary>
	internal static class Galois16
	{
		public const int Order = 65535; // multiplicative group size
		private const int Generator = 0x1100B;

		private static readonly ushort[] Exp = new ushort[Order * 2];
		private static readonly int[] Log = new int[65536];

		static Galois16()
		{
			int x = 1;
			for (int i = 0; i < Order; i++)
			{
				Exp[i] = (ushort)x;
				Exp[i + Order] = (ushort)x;
				Log[x] = i;
				x <<= 1;
				if ((x & 0x10000) != 0)
					x ^= Generator;
			}
			Log[0] = -1;
		}

		public static ushort Multiply(ushort a, ushort b)
		{
			if (a == 0 || b == 0)
				return 0;
			return Exp[Log[a] + Log[b]];
		}

		public static ushort Divide(ushort a, ushort b)
		{
			if (b == 0)
				throw new DivideByZeroException();
			if (a == 0)
				return 0;
			return Exp[Log[a] - Log[b] + Order];
		}

		public static ushort Power(ushort a, int exponent)
		{
			if (exponent == 0)
				return 1;
			if (a == 0)
				return 0;
			return Exp[(int)((long)Log[a] * exponent % Order)];
		}

		/// <summary>2^n: PAR2's constant for an input slice is 2 to the power of the n-th logarithm coprime to 65535.</summary>
		public static ushort Antilog(int n)
		{
			return Exp[n % Order];
		}

		/// <summary>
		/// The constants of the first <paramref name="count"/> input slices: 2^n for n = 1, 2, 4, 7, 8, ... (n coprime
		/// to 65535 = 3 * 5 * 17 * 257), as the spec and par2cmdline define them.
		/// </summary>
		public static ushort[] InputConstants(int count)
		{
			if (count > 32768)
				throw new NotSupportedException("PAR2 allows at most 32768 input slices");
			ushort[] constants = new ushort[count];
			int n = 0;
			for (int i = 0; i < count; i++)
			{
				do
				{
					n++;
				} while (n % 3 == 0 || n % 5 == 0 || n % 17 == 0 || n % 257 == 0);
				constants[i] = Antilog(n);
			}
			return constants;
		}

		/// <summary>destination ^= factor * source, word by word (both little-endian 16-bit words, same even length).</summary>
		public static void MultiplyAdd(Span<byte> destination, ReadOnlySpan<byte> source, ushort factor)
		{
			if (factor == 0)
				return;
			if (source.Length != destination.Length || (source.Length & 1) != 0)
				throw new ArgumentException("source and destination must be the same even length");

			if (factor == 1)
			{
				Xor(destination, source);
				return;
			}

			int done = 0;
			if (Avx2.IsSupported && source.Length >= 64)
				done = MultiplyAddAvx2(destination, source, factor);
			else if (Ssse3.IsSupported && source.Length >= 32)
				done = MultiplyAddSsse3(destination, source, factor);

			if (done < source.Length)
				MultiplyAddScalar(destination.Slice(done), source.Slice(done), factor);
		}

		private static void Xor(Span<byte> destination, ReadOnlySpan<byte> source)
		{
			int i = 0;
			if (Vector.IsHardwareAccelerated)
			{
				Span<Vector<byte>> dv = MemoryMarshal.Cast<byte, Vector<byte>>(destination);
				ReadOnlySpan<Vector<byte>> sv = MemoryMarshal.Cast<byte, Vector<byte>>(source);
				for (int v = 0; v < dv.Length; v++)
					dv[v] ^= sv[v];
				i = dv.Length * Vector<byte>.Count;
			}
			for (; i < destination.Length; i++)
				destination[i] ^= source[i];
		}

		/// <summary>
		/// The products of factor with each nibble position: tables[2k] / tables[2k+1] are the low / high bytes of
		/// factor * (v &lt;&lt; 4k) for v = 0..15, the 16-entry lookups PSHUFB does.
		/// </summary>
		private static void NibbleTables(ushort factor, Span<byte> tables)
		{
			for (int k = 0; k < 4; k++)
			{
				for (int v = 0; v < 16; v++)
				{
					ushort product = Multiply(factor, (ushort)(v << (4 * k)));
					tables[(2 * k) * 16 + v] = (byte)product;
					tables[(2 * k + 1) * 16 + v] = (byte)(product >> 8);
				}
			}
		}

		// gathers the even (low) bytes of 8 words into the first half and the odd (high) bytes into the second
		private static readonly Vector128<byte> Deinterleave = Vector128.Create((byte)0, 2, 4, 6, 8, 10, 12, 14, 1, 3, 5, 7, 9, 11, 13, 15);

		/// <summary>16 words (32 bytes) per step with SSSE3; returns the bytes done.</summary>
		internal static int MultiplyAddSsse3(Span<byte> destination, ReadOnlySpan<byte> source, ushort factor)
		{
			Span<byte> t = stackalloc byte[128];
			NibbleTables(factor, t);
			ref byte tr = ref MemoryMarshal.GetReference(t);
			Vector128<byte> l0 = Vector128.LoadUnsafe(ref tr, 0), h0 = Vector128.LoadUnsafe(ref tr, 16);
			Vector128<byte> l1 = Vector128.LoadUnsafe(ref tr, 32), h1 = Vector128.LoadUnsafe(ref tr, 48);
			Vector128<byte> l2 = Vector128.LoadUnsafe(ref tr, 64), h2 = Vector128.LoadUnsafe(ref tr, 80);
			Vector128<byte> l3 = Vector128.LoadUnsafe(ref tr, 96), h3 = Vector128.LoadUnsafe(ref tr, 112);
			Vector128<byte> mask = Vector128.Create((byte)0x0F);

			ref byte src = ref MemoryMarshal.GetReference(source);
			ref byte dst = ref MemoryMarshal.GetReference(destination);
			int end = source.Length & ~31;
			for (int i = 0; i < end; i += 32)
			{
				Vector128<byte> a = Ssse3.Shuffle(Vector128.LoadUnsafe(ref src, (nuint)i), Deinterleave);
				Vector128<byte> b = Ssse3.Shuffle(Vector128.LoadUnsafe(ref src, (nuint)i + 16), Deinterleave);
				Vector128<byte> lo = Sse2.UnpackLow(a.AsUInt64(), b.AsUInt64()).AsByte();
				Vector128<byte> hi = Sse2.UnpackHigh(a.AsUInt64(), b.AsUInt64()).AsByte();

				Vector128<byte> n0 = lo & mask, n1 = Sse2.ShiftRightLogical(lo.AsUInt16(), 4).AsByte() & mask;
				Vector128<byte> n2 = hi & mask, n3 = Sse2.ShiftRightLogical(hi.AsUInt16(), 4).AsByte() & mask;

				Vector128<byte> rl = Ssse3.Shuffle(l0, n0) ^ Ssse3.Shuffle(l1, n1) ^ Ssse3.Shuffle(l2, n2) ^ Ssse3.Shuffle(l3, n3);
				Vector128<byte> rh = Ssse3.Shuffle(h0, n0) ^ Ssse3.Shuffle(h1, n1) ^ Ssse3.Shuffle(h2, n2) ^ Ssse3.Shuffle(h3, n3);

				(Vector128.LoadUnsafe(ref dst, (nuint)i) ^ Sse2.UnpackLow(rl, rh)).StoreUnsafe(ref dst, (nuint)i);
				(Vector128.LoadUnsafe(ref dst, (nuint)i + 16) ^ Sse2.UnpackHigh(rl, rh)).StoreUnsafe(ref dst, (nuint)i + 16);
			}
			return end;
		}

		/// <summary>32 words (64 bytes) per step with AVX2 (the shuffles work per 128-bit lane, which this layout keeps).</summary>
		internal static int MultiplyAddAvx2(Span<byte> destination, ReadOnlySpan<byte> source, ushort factor)
		{
			Span<byte> t = stackalloc byte[128];
			NibbleTables(factor, t);
			ref byte tr = ref MemoryMarshal.GetReference(t);
			Vector256<byte> l0 = Broadcast(ref tr, 0), h0 = Broadcast(ref tr, 16);
			Vector256<byte> l1 = Broadcast(ref tr, 32), h1 = Broadcast(ref tr, 48);
			Vector256<byte> l2 = Broadcast(ref tr, 64), h2 = Broadcast(ref tr, 80);
			Vector256<byte> l3 = Broadcast(ref tr, 96), h3 = Broadcast(ref tr, 112);
			Vector256<byte> mask = Vector256.Create((byte)0x0F);
			Vector256<byte> deinterleave = Vector256.Create(Deinterleave, Deinterleave);

			ref byte src = ref MemoryMarshal.GetReference(source);
			ref byte dst = ref MemoryMarshal.GetReference(destination);
			int end = source.Length & ~63;
			for (int i = 0; i < end; i += 64)
			{
				Vector256<byte> a = Avx2.Shuffle(Vector256.LoadUnsafe(ref src, (nuint)i), deinterleave);
				Vector256<byte> b = Avx2.Shuffle(Vector256.LoadUnsafe(ref src, (nuint)i + 32), deinterleave);
				Vector256<byte> lo = Avx2.UnpackLow(a.AsUInt64(), b.AsUInt64()).AsByte();
				Vector256<byte> hi = Avx2.UnpackHigh(a.AsUInt64(), b.AsUInt64()).AsByte();

				Vector256<byte> n0 = lo & mask, n1 = Avx2.ShiftRightLogical(lo.AsUInt16(), 4).AsByte() & mask;
				Vector256<byte> n2 = hi & mask, n3 = Avx2.ShiftRightLogical(hi.AsUInt16(), 4).AsByte() & mask;

				Vector256<byte> rl = Avx2.Shuffle(l0, n0) ^ Avx2.Shuffle(l1, n1) ^ Avx2.Shuffle(l2, n2) ^ Avx2.Shuffle(l3, n3);
				Vector256<byte> rh = Avx2.Shuffle(h0, n0) ^ Avx2.Shuffle(h1, n1) ^ Avx2.Shuffle(h2, n2) ^ Avx2.Shuffle(h3, n3);

				(Vector256.LoadUnsafe(ref dst, (nuint)i) ^ Avx2.UnpackLow(rl, rh)).StoreUnsafe(ref dst, (nuint)i);
				(Vector256.LoadUnsafe(ref dst, (nuint)i + 32) ^ Avx2.UnpackHigh(rl, rh)).StoreUnsafe(ref dst, (nuint)i + 32);
			}
			return end;
		}

		private static Vector256<byte> Broadcast(ref byte table, nuint offset)
		{
			Vector128<byte> v = Vector128.LoadUnsafe(ref table, offset);
			return Vector256.Create(v, v);
		}

		/// <summary>The portable version, also the reference the vector versions are tested against.</summary>
		internal static void MultiplyAddScalar(Span<byte> destination, ReadOnlySpan<byte> source, ushort factor)
		{
			if (factor == 0)
				return;

			Span<ushort> dst = MemoryMarshal.Cast<byte, ushort>(destination);
			ReadOnlySpan<ushort> src = MemoryMarshal.Cast<byte, ushort>(source);

			// a product splits into the products of the low and high bytes: two 256-entry tables per factor
			Span<ushort> low = stackalloc ushort[256];
			Span<ushort> high = stackalloc ushort[256];
			int logFactor = Log[factor];
			low[0] = 0;
			high[0] = 0;
			for (int b = 1; b < 256; b++)
			{
				low[b] = Exp[Log[b] + logFactor];
				high[b] = Exp[Log[b << 8] + logFactor];
			}

			for (int i = 0; i < dst.Length; i++)
			{
				ushort w = src[i];
				dst[i] ^= (ushort)(low[w & 0xFF] ^ high[w >> 8]);
			}
		}

		/// <summary>Inverts a square matrix in place (Gauss-Jordan); false if it's singular.</summary>
		public static bool Invert(ushort[,] matrix)
		{
			int n = matrix.GetLength(0);
			ushort[,] inverse = new ushort[n, n];
			for (int i = 0; i < n; i++)
				inverse[i, i] = 1;

			for (int column = 0; column < n; column++)
			{
				int pivot = column;
				while (pivot < n && matrix[pivot, column] == 0)
					pivot++;
				if (pivot == n)
					return false;
				if (pivot != column)
				{
					SwapRows(matrix, pivot, column);
					SwapRows(inverse, pivot, column);
				}

				ushort scale = matrix[column, column];
				for (int j = 0; j < n; j++)
				{
					matrix[column, j] = Divide(matrix[column, j], scale);
					inverse[column, j] = Divide(inverse[column, j], scale);
				}

				for (int row = 0; row < n; row++)
				{
					ushort factor = matrix[row, column];
					if (row == column || factor == 0)
						continue;
					for (int j = 0; j < n; j++)
					{
						matrix[row, j] ^= Multiply(factor, matrix[column, j]);
						inverse[row, j] ^= Multiply(factor, inverse[column, j]);
					}
				}
			}

			Array.Copy(inverse, matrix, inverse.Length);
			return true;
		}

		private static void SwapRows(ushort[,] m, int a, int b)
		{
			for (int j = 0; j < m.GetLength(1); j++)
			{
				ushort t = m[a, j];
				m[a, j] = m[b, j];
				m[b, j] = t;
			}
		}
	}
}
