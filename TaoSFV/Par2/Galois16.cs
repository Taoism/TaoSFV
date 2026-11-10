using System;
using System.Runtime.InteropServices;

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

			Span<ushort> dst = MemoryMarshal.Cast<byte, ushort>(destination);
			ReadOnlySpan<ushort> src = MemoryMarshal.Cast<byte, ushort>(source);

			if (factor == 1)
			{
				for (int i = 0; i < dst.Length; i++)
					dst[i] ^= src[i];
				return;
			}

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
