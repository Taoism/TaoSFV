using System;
using System.Collections.Generic;

namespace TaoSFV.Par2
{
	/// <summary>One file described by a PAR2 recovery set (File Description and IFSC packets).</summary>
	public sealed class Par2File
	{
		public Par2File(Guid id)
		{
			Id = id;
		}

		public Guid Id { get; private set; }

		/// <summary>Relative name as stored in the set, with '\' separators.</summary>
		public string Name { get; internal set; }
		public long Length { get; internal set; }
		public byte[] Hash { get; internal set; }        // MD5 of the whole file
		public byte[] Hash16k { get; internal set; }     // MD5 of the first 16 KB

		/// <summary>MD5 and CRC32 of each slice (the last one zero-padded); null until an IFSC packet is read.</summary>
		public Par2Slice[] Slices { get; internal set; }

		public bool HasDescription
		{
			get { return Name != null; }
		}

		public int SliceCount(long sliceSize)
		{
			return (int)((Length + sliceSize - 1) / sliceSize);
		}
	}

	public struct Par2Slice
	{
		public byte[] Md5;
		public uint Crc32;
	}

	/// <summary>Where a recovery slice is stored: its exponent and the bytes in a .par2 file.</summary>
	public sealed class Par2RecoverySlice
	{
		public int Exponent { get; internal set; }
		public string FileName { get; internal set; }
		public long Offset { get; internal set; }
	}

	/// <summary>A PAR2 recovery set: everything read from the .par2 files that share a base name.</summary>
	public sealed class Par2Set
	{
		internal Par2Set()
		{
			RecoveryFileIds = new List<Guid>();
			NonRecoveryFileIds = new List<Guid>();
			Files = new Dictionary<Guid, Par2File>();
			RecoverySlices = new Dictionary<int, Par2RecoverySlice>();
			Par2Files = new List<string>();
		}

		/// <summary>The folder the protected files' names are relative to (the .par2 file's folder).</summary>
		public string Directory { get; internal set; }

		public Guid SetId { get; internal set; }
		public long SliceSize { get; internal set; }

		/// <summary>Files protected by the recovery data, in the order the Main packet lists them (that order matters for repair).</summary>
		public List<Guid> RecoveryFileIds { get; private set; }

		/// <summary>Files that are only described (checked, but not repairable).</summary>
		public List<Guid> NonRecoveryFileIds { get; private set; }

		public Dictionary<Guid, Par2File> Files { get; private set; }

		/// <summary>Valid recovery slices by exponent (one per exponent is enough).</summary>
		public Dictionary<int, Par2RecoverySlice> RecoverySlices { get; private set; }

		public string Creator { get; internal set; }

		/// <summary>The .par2 files that were read.</summary>
		public List<string> Par2Files { get; private set; }

		/// <summary>Packets skipped because their MD5 didn't match (damaged .par2 data).</summary>
		public int DamagedPackets { get; internal set; }

		public IEnumerable<Par2File> RecoveryFiles
		{
			get
			{
				foreach (Guid id in RecoveryFileIds)
				{
					Par2File file;
					if (Files.TryGetValue(id, out file))
						yield return file;
				}
			}
		}
	}
}
