using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;

namespace TaoSFV.Hash
{
	/// <summary>
	/// Hashes a stream with large reads; HashAlgorithm.HashData(Stream) reads in small chunks.
	/// </summary>
	internal static class StreamHasher
	{
		private const int BUFFER_SIZE = 1024 * 1024;

		public static byte[] Hash(HashAlgorithmName algorithm, Stream stream)
		{
			using (IncrementalHash hash = IncrementalHash.CreateHash(algorithm))
			{
				byte[] buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
				try
				{
					int count;
					while ((count = stream.Read(buffer, 0, BUFFER_SIZE)) > 0)
						hash.AppendData(buffer, 0, count);
				}
				finally
				{
					ArrayPool<byte>.Shared.Return(buffer);
				}

				return hash.GetHashAndReset();
			}
		}
	}
}
