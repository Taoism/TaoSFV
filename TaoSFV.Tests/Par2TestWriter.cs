using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TaoSFV.Par2;

namespace TaoSFV.Tests
{
	/// <summary>
	/// Writes PAR2 files following the 2.0 specification, for tests: Main, File Description, IFSC and Creator packets,
	/// and recovery slice packets with placeholder data (enough to count them; the data isn't real parity).
	/// </summary>
	internal static class Par2TestWriter
	{
		public static void Write(string par2Path, string directory, IList<string> relativeNames, int sliceSize, int recoverySlices = 0)
		{
			List<byte[]> packets = new List<byte[]>();
			List<Tuple<byte[], byte[], byte[]>> files = new List<Tuple<byte[], byte[], byte[]>>(); // id, desc body, ifsc body

			foreach (string name in relativeNames)
			{
				byte[] data = File.ReadAllBytes(Path.Combine(directory, name));
				byte[] nameBytes = Encoding.UTF8.GetBytes(name.Replace('\\', '/'));
				byte[] hash = MD5.HashData(data);
				byte[] hash16k = MD5.HashData(data.AsSpan(0, Math.Min(data.Length, 16 * 1024)));

				byte[] idInput = new byte[16 + 8 + nameBytes.Length];
				hash16k.CopyTo(idInput, 0);
				BinaryPrimitives.WriteInt64LittleEndian(idInput.AsSpan(16), data.Length);
				nameBytes.CopyTo(idInput, 24);
				byte[] id = MD5.HashData(idInput);

				byte[] paddedName = new byte[(nameBytes.Length + 3) / 4 * 4];
				nameBytes.CopyTo(paddedName, 0);
				byte[] desc = Concat(id, hash, hash16k, Int64(data.Length), paddedName);

				int count = (data.Length + sliceSize - 1) / sliceSize;
				List<byte[]> ifsc = new List<byte[]> { id };
				for (int i = 0; i < count; i++)
				{
					byte[] slice = new byte[sliceSize];
					Array.Copy(data, (long)i * sliceSize, slice, 0, Math.Min(sliceSize, data.Length - i * sliceSize));
					byte[] crc = new byte[4];
					BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.HashToUInt32(slice));
					ifsc.Add(MD5.HashData(slice));
					ifsc.Add(crc);
				}
				files.Add(Tuple.Create(id, desc, Concat(ifsc.ToArray())));
			}

			// the Main packet lists file IDs sorted as 16-byte numbers
			List<byte[]> ids = files.Select(f => f.Item1).OrderBy(i => i, new ByteComparer()).ToList();
			byte[] mainCount = new byte[4];
			BinaryPrimitives.WriteInt32LittleEndian(mainCount, ids.Count);
			byte[] main = Concat(new[] { Int64(sliceSize), mainCount }.Concat(ids).ToArray());
			byte[] setId = MD5.HashData(main);

			packets.Add(Packet(setId, Par2Reader.MainType, main));
			foreach (var f in files)
			{
				packets.Add(Packet(setId, Par2Reader.FileDescType, f.Item2));
				packets.Add(Packet(setId, Par2Reader.IfscType, f.Item3));
			}
			packets.Add(Packet(setId, Par2Reader.CreatorType, Encoding.ASCII.GetBytes("TaoSFV tests")));

			Random random = new Random(1);
			for (int e = 0; e < recoverySlices; e++)
			{
				byte[] body = new byte[4 + sliceSize];
				BinaryPrimitives.WriteInt32LittleEndian(body, e);
				random.NextBytes(body.AsSpan(4));
				packets.Add(Packet(setId, Par2Reader.RecoverySliceType, body));
			}

			File.WriteAllBytes(par2Path, Concat(packets.ToArray()));
		}

		public static byte[] Packet(byte[] setId, byte[] type, byte[] body)
		{
			byte[] packet = new byte[64 + (body.Length + 3) / 4 * 4];
			Par2Reader.Magic.CopyTo(packet, 0);
			BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(8), packet.Length);
			setId.CopyTo(packet, 32);
			type.CopyTo(packet, 48);
			body.CopyTo(packet, 64);
			MD5.HashData(packet.AsSpan(32)).CopyTo(packet, 16);
			return packet;
		}

		private static byte[] Int64(long value)
		{
			byte[] bytes = new byte[8];
			BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
			return bytes;
		}

		private static byte[] Concat(params byte[][] parts)
		{
			return parts.SelectMany(p => p).ToArray();
		}

		private sealed class ByteComparer : IComparer<byte[]>
		{
			public int Compare(byte[] x, byte[] y)
			{
				return x.AsSpan().SequenceCompareTo(y);
			}
		}
	}
}
