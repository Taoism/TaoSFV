using System;
using System.Buffers;
using System.IO;
using System.IO.Hashing;

namespace TaoSFV.Hash
{
	/// <summary>
	/// Non-cryptographic-speed hashes for large libraries: XXH128 (xxHash's XXH3, 128-bit, as xxhsum -H2 writes it)
	/// and BLAKE3 (as b3sum writes it). Lowercase hex, like md5sum.
	/// </summary>
	internal static class FastHashes
	{
		private const int BUFFER_SIZE = 1024 * 1024;

		public static string Xxh128(Stream stream)
		{
			XxHash128 hash = new XxHash128();
			Read(stream, (buffer, count) => hash.Append(new ReadOnlySpan<byte>(buffer, 0, count)));
			// GetCurrentHash writes the canonical (big-endian) form, which is what xxhsum prints
			return Convert.ToHexStringLower(hash.GetCurrentHash());
		}

		public static string Blake3(Stream stream)
		{
			using (global::Blake3.Hasher hasher = global::Blake3.Hasher.New())
			{
				// UpdateWithJoin spreads large blocks over several cores (BLAKE3's tree hashing)
				Read(stream, (buffer, count) => hasher.UpdateWithJoin(new ReadOnlySpan<byte>(buffer, 0, count)));
				Span<byte> digest = stackalloc byte[32];
				hasher.Finalize(digest);
				return Convert.ToHexStringLower(digest);
			}
		}

		private static void Read(Stream stream, Action<byte[], int> append)
		{
			stream.Position = 0;
			byte[] buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
			try
			{
				int count;
				while ((count = stream.Read(buffer, 0, BUFFER_SIZE)) > 0)
					append(buffer, count);
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}
}
