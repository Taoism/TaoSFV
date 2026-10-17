using System;
using System.IO;
using System.Text;
using TaoSFV.Hash;
using Xunit;

namespace TaoSFV.Tests
{
	public class HashTests
	{
		// The byte-at-a-time table implementation that ilSFV shipped, kept as a reference.
		private static uint ReferenceCrc32(byte[] data)
		{
			uint[] table = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				uint c = i;
				for (int j = 0; j < 8; j++)
					c = (c & 1) == 1 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
				table[i] = c;
			}

			uint crc = 0xFFFFFFFF;
			foreach (byte b in data)
				crc = (crc >> 8) ^ table[b ^ (crc & 0xFF)];
			return ~crc;
		}

		[Theory]
		[InlineData("", "00000000")]
		[InlineData("123456789", "CBF43926")] // CRC-32/ISO-HDLC check value
		[InlineData("The quick brown fox jumps over the lazy dog", "414FA339")]
		public void Crc32_KnownVectors(string input, string expected)
		{
			Assert.Equal(expected, CRC32.Calculate(Encoding.ASCII.GetBytes(input)));
		}

		[Theory]
		[InlineData(1)]
		[InlineData(4095)]
		[InlineData(1024 * 1024)]         // exactly one buffer
		[InlineData(3 * 1024 * 1024 + 7)] // several buffers plus a partial one
		public void Crc32_MatchesReferenceImplementation(int length)
		{
			byte[] data = new byte[length];
			new Random(length).NextBytes(data);

			Assert.Equal(ReferenceCrc32(data), CRC32.CalculateInt32(data));
		}

		[Fact]
		public void Crc32_StartsFromBeginningOfStream()
		{
			byte[] data = Encoding.ASCII.GetBytes("123456789");
			using (MemoryStream stream = new MemoryStream(data))
			{
				stream.Position = 5;
				Assert.Equal("CBF43926", CRC32.Calculate(stream));
			}
		}

		[Fact]
		public void Crc32_File()
		{
			string path = Path.GetTempFileName();
			try
			{
				File.WriteAllText(path, "123456789");
				Assert.Equal("CBF43926", CRC32.Calculate(new FileInfo(path)));
			}
			finally
			{
				File.Delete(path);
			}
		}

		[Theory]
		[InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
		[InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
		public void Md5_KnownVectors(string input, string expected)
		{
			Assert.Equal(expected, MD5.Calculate(Encoding.ASCII.GetBytes(input)));
		}

		[Theory]
		[InlineData("", "da39a3ee5e6b4b0d3255bfef95601890afd80709")]
		[InlineData("abc", "a9993e364706816aba3e25717850c26c9cd0d89d")]
		public void Sha1_KnownVectors(string input, string expected)
		{
			Assert.Equal(expected, SHA1.Calculate(Encoding.ASCII.GetBytes(input)));
		}

		[Fact]
		public void Md5AndSha1_MatchFrameworkAcrossBufferBoundaries()
		{
			byte[] data = new byte[2 * 1024 * 1024 + 13];
			new Random(42).NextBytes(data);

			Assert.Equal(Convert.ToHexString(System.Security.Cryptography.MD5.HashData(data)).ToLowerInvariant(), MD5.Calculate(data));
			Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(data)).ToLowerInvariant(), SHA1.Calculate(data));
		}
	}
}
