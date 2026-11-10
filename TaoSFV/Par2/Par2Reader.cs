using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TaoSFV.Par2
{
	/// <summary>
	/// Reads PAR2 recovery sets (the Parity Volume Set Specification 2.0). Every .par2 file with the same base name
	/// ("set.par2", "set.vol00+01.par2", ...) is scanned for packets; a packet whose MD5 doesn't match is skipped,
	/// and damaged stretches are passed over by searching for the next packet header.
	/// </summary>
	public static class Par2Reader
	{
		internal const int HeaderLength = 64;
		internal static readonly byte[] Magic = Encoding.ASCII.GetBytes("PAR2\0PKT");

		internal static readonly byte[] MainType = Type("PAR 2.0\0Main\0\0\0\0");
		internal static readonly byte[] FileDescType = Type("PAR 2.0\0FileDesc");
		internal static readonly byte[] IfscType = Type("PAR 2.0\0IFSC\0\0\0\0");
		internal static readonly byte[] RecoverySliceType = Type("PAR 2.0\0RecvSlic");
		internal static readonly byte[] CreatorType = Type("PAR 2.0\0Creator\0");

		// packets other than recovery slices are small; anything bigger is damage
		private const long MaxSmallPacket = 64L * 1024 * 1024;

		private static byte[] Type(string text)
		{
			byte[] bytes = Encoding.ASCII.GetBytes(text);
			if (bytes.Length != 16)
				throw new InvalidOperationException(text);
			return bytes;
		}

		public static bool IsPar2File(string path)
		{
			return string.Equals(Path.GetExtension(path), ".par2", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>"set" for "set.par2", "set.vol03+04.par2" and "set.PAR2".</summary>
		public static string GetBaseName(string path)
		{
			string name = Path.GetFileNameWithoutExtension(path);
			int vol = name.LastIndexOf(".vol", StringComparison.OrdinalIgnoreCase);
			if (vol > 0 && IsVolumeSuffix(name.Substring(vol + 4)))
				name = name.Substring(0, vol);
			return name;
		}

		// "00+01", "3+4", also "00-01" from some tools
		private static bool IsVolumeSuffix(string suffix)
		{
			int sep = suffix.IndexOfAny(new[] { '+', '-' });
			return sep > 0 && sep < suffix.Length - 1 && suffix.Remove(sep, 1).All(char.IsDigit);
		}

		/// <summary>The .par2 files that belong to the same set as the given one (including it).</summary>
		public static List<string> FindSetFiles(string par2Path)
		{
			string directory = Path.GetDirectoryName(Path.GetFullPath(par2Path));
			string baseName = GetBaseName(par2Path);
			List<string> files = Directory.GetFiles(directory, "*.par2")
				.Where(f => string.Equals(GetBaseName(f), baseName, StringComparison.OrdinalIgnoreCase))
				.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (!files.Contains(Path.GetFullPath(par2Path), StringComparer.OrdinalIgnoreCase))
				files.Insert(0, Path.GetFullPath(par2Path));
			return files;
		}

		/// <summary>Loads the set the given .par2 file belongs to. Throws InvalidDataException if no Main packet is found.</summary>
		public static Par2Set Load(string par2Path)
		{
			return Load(FindSetFiles(par2Path), Path.GetDirectoryName(Path.GetFullPath(par2Path)));
		}

		public static Par2Set Load(IEnumerable<string> par2Files, string directory)
		{
			Collector collector = new Collector();
			List<string> read = new List<string>();
			foreach (string file in par2Files)
			{
				using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
					ReadPackets(stream, file, collector);
				read.Add(file);
			}

			Par2Set set = collector.Build();
			if (set == null)
				throw new InvalidDataException("No PAR2 main packet found in " + string.Join(", ", read.Select(Path.GetFileName)));
			set.Directory = directory;
			set.Par2Files.AddRange(read);
			return set;
		}

		// ------------------------------------------------------------------ packet scanning

		private sealed class Packet
		{
			public Guid SetId;
			public byte[] Type;
			public byte[] Body;          // null for recovery slices (not kept in memory)
			public string FileName;
			public long BodyOffset;
			public long BodyLength;
		}

		private static void ReadPackets(Stream stream, string fileName, Collector collector)
		{
			long length = stream.Length;
			long position = 0;
			byte[] header = new byte[HeaderLength];

			while (position + HeaderLength <= length)
			{
				stream.Position = position;
				ReadExactly(stream, header, HeaderLength);

				long packetLength;
				if (header.AsSpan(0, 8).SequenceEqual(Magic) &&
					(packetLength = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8))) >= HeaderLength &&
					packetLength % 4 == 0 && position + packetLength <= length)
				{
					Packet packet;
					if (TryReadPacket(stream, header, position, packetLength, fileName, out packet))
					{
						collector.Add(packet);
						position += packetLength;
						continue;
					}
					collector.Damaged++;
				}

				long next = FindMagic(stream, position + 1, length);
				if (next < 0)
					break;
				position = next;
			}
		}

		private static bool TryReadPacket(Stream stream, byte[] header, long position, long packetLength, string fileName, out Packet packet)
		{
			packet = null;
			byte[] type = header.AsSpan(48, 16).ToArray();
			bool recovery = type.AsSpan().SequenceEqual(RecoverySliceType);
			if (!recovery && packetLength > MaxSmallPacket)
				return false;

			long bodyLength = packetLength - HeaderLength;
			byte[] body = null;

			// the packet hash covers the set ID, type and body (everything after the hash itself)
			using (IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
			{
				md5.AppendData(header, 32, 32);
				stream.Position = position + HeaderLength;
				if (recovery)
				{
					byte[] buffer = new byte[1 << 20];
					long left = bodyLength;
					while (left > 0)
					{
						int n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
						if (n <= 0)
							return false;
						md5.AppendData(buffer, 0, n);
						left -= n;
					}
				}
				else
				{
					body = new byte[bodyLength];
					ReadExactly(stream, body, body.Length);
					md5.AppendData(body);
				}

				if (!md5.GetHashAndReset().AsSpan().SequenceEqual(header.AsSpan(16, 16)))
					return false;
			}

			packet = new Packet
			{
				SetId = new Guid(header.AsSpan(32, 16)),
				Type = type,
				Body = body,
				FileName = fileName,
				BodyOffset = position + HeaderLength,
				BodyLength = bodyLength
			};

			// a recovery slice body starts with its exponent
			if (recovery)
			{
				if (bodyLength < 4)
					return false;
				byte[] exponent = new byte[4];
				stream.Position = packet.BodyOffset;
				ReadExactly(stream, exponent, 4);
				packet.Body = exponent;
			}
			return true;
		}

		private static long FindMagic(Stream stream, long from, long length)
		{
			byte[] buffer = new byte[1 << 20];
			long position = from;
			while (position + Magic.Length <= length)
			{
				stream.Position = position;
				int n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - position));
				if (n < Magic.Length)
					return -1;
				int index = buffer.AsSpan(0, n).IndexOf(Magic);
				if (index >= 0)
					return position + index;
				position += n - (Magic.Length - 1); // a header may straddle two reads
			}
			return -1;
		}

		private static void ReadExactly(Stream stream, byte[] buffer, int count)
		{
			int done = 0;
			while (done < count)
			{
				int n = stream.Read(buffer, done, count - done);
				if (n <= 0)
					throw new EndOfStreamException();
				done += n;
			}
		}

		// ------------------------------------------------------------------ building the set

		private sealed class Collector
		{
			private readonly List<Packet> _packets = new List<Packet>();
			public int Damaged;

			public void Add(Packet packet)
			{
				_packets.Add(packet);
			}

			public Par2Set Build()
			{
				// the set is the one with a Main packet (a .par2 file could hold packets of other sets too)
				Packet main = _packets.FirstOrDefault(p => p.Type.AsSpan().SequenceEqual(MainType));
				if (main == null || main.Body.Length < 12)
					return null;

				Par2Set set = new Par2Set();
				set.SetId = main.SetId;
				set.DamagedPackets = Damaged;
				set.SliceSize = BinaryPrimitives.ReadInt64LittleEndian(main.Body);
				if (set.SliceSize <= 0 || set.SliceSize % 4 != 0)
					throw new InvalidDataException("Invalid PAR2 slice size " + set.SliceSize);

				int recoveryCount = BinaryPrimitives.ReadInt32LittleEndian(main.Body.AsSpan(8));
				int idCount = (main.Body.Length - 12) / 16;
				for (int i = 0; i < idCount; i++)
				{
					Guid id = new Guid(main.Body.AsSpan(12 + i * 16, 16));
					(i < recoveryCount ? set.RecoveryFileIds : set.NonRecoveryFileIds).Add(id);
				}

				foreach (Packet packet in _packets.Where(p => p.SetId == set.SetId))
				{
					ReadOnlySpan<byte> type = packet.Type;
					if (type.SequenceEqual(FileDescType))
						ReadFileDescription(set, packet.Body);
					else if (type.SequenceEqual(IfscType))
						ReadIfsc(set, packet.Body);
					else if (type.SequenceEqual(RecoverySliceType))
					{
						int exponent = BinaryPrimitives.ReadInt32LittleEndian(packet.Body);
						// the slice data must be exactly one slice long
						if (packet.BodyLength - 4 == set.SliceSize && exponent >= 0 && exponent < 65535 && !set.RecoverySlices.ContainsKey(exponent))
							set.RecoverySlices[exponent] = new Par2RecoverySlice { Exponent = exponent, FileName = packet.FileName, Offset = packet.BodyOffset + 4 };
					}
					else if (type.SequenceEqual(CreatorType))
						set.Creator = Encoding.ASCII.GetString(packet.Body).TrimEnd('\0');
				}
				return set;
			}

			private static Par2File GetFile(Par2Set set, Guid id)
			{
				Par2File file;
				if (!set.Files.TryGetValue(id, out file))
				{
					file = new Par2File(id);
					set.Files[id] = file;
				}
				return file;
			}

			private static void ReadFileDescription(Par2Set set, byte[] body)
			{
				if (body.Length < 56)
					return;
				Par2File file = GetFile(set, new Guid(body.AsSpan(0, 16)));
				if (file.HasDescription)
					return;
				file.Hash = body.AsSpan(16, 16).ToArray();
				file.Hash16k = body.AsSpan(32, 16).ToArray();
				file.Length = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(48));
				file.Name = DecodeName(body.AsSpan(56));
			}

			private static void ReadIfsc(Par2Set set, byte[] body)
			{
				if (body.Length < 16 || (body.Length - 16) % 20 != 0)
					return;
				Par2File file = GetFile(set, new Guid(body.AsSpan(0, 16)));
				if (file.Slices != null)
					return;
				Par2Slice[] slices = new Par2Slice[(body.Length - 16) / 20];
				for (int i = 0; i < slices.Length; i++)
				{
					int at = 16 + i * 20;
					slices[i].Md5 = body.AsSpan(at, 16).ToArray();
					slices[i].Crc32 = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(at + 16));
				}
				file.Slices = slices;
			}
		}

		/// <summary>
		/// File names are null-padded to a multiple of 4. The spec says ASCII; most tools write UTF-8, and old Windows
		/// tools the ANSI code page, so UTF-8 is used when it's valid and Windows-1252 otherwise. '/' becomes '\'.
		/// </summary>
		internal static string DecodeName(ReadOnlySpan<byte> bytes)
		{
			int end = bytes.IndexOf((byte)0);
			if (end >= 0)
				bytes = bytes.Slice(0, end);

			string name;
			try
			{
				name = new UTF8Encoding(false, true).GetString(bytes);
			}
			catch (DecoderFallbackException)
			{
				name = Encoding.GetEncoding(1252).GetString(bytes);
			}
			return name.Replace('/', '\\');
		}
	}
}
