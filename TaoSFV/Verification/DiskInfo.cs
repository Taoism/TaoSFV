using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TaoSFV.Verification
{
	/// <summary>
	/// Picks how many files to read at once: parallel reads speed up SSDs but make spinning disks seek.
	/// </summary>
	public static class DiskInfo
	{
		public const int MaxParallelFiles = 4;

		private static readonly ConcurrentDictionary<string, int> _byRoot = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		public static int GetParallelism(string directory)
		{
			string root;
			try
			{
				root = Path.GetPathRoot(Path.GetFullPath(directory));
			}
			catch (Exception)
			{
				return 1;
			}

			if (string.IsNullOrEmpty(root))
				return 1;

			return _byRoot.GetOrAdd(root, r =>
			{
				bool? seekPenalty = HasSeekPenalty(r);
				return seekPenalty == false ? Math.Max(1, Math.Min(MaxParallelFiles, Environment.ProcessorCount)) : 1;
			});
		}

		/// <summary>True for spinning disks, false for SSDs, null when unknown (network shares, odd drivers).</summary>
		internal static bool? HasSeekPenalty(string root)
		{
			// only local drive letters ("C:\"); UNC paths are treated as unknown
			if (root.Length < 2 || root[1] != ':')
				return null;

			string volume = @"\\.\" + root.Substring(0, 2);
			using (SafeFileHandle handle = CreateFile(volume, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
			{
				if (handle.IsInvalid)
					return null;

				STORAGE_PROPERTY_QUERY query = new STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceSeekPenaltyProperty, QueryType = PropertyStandardQuery };
				DEVICE_SEEK_PENALTY_DESCRIPTOR result;
				uint returned;
				if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, ref query, (uint)Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
					out result, (uint)Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(), out returned, IntPtr.Zero))
					return null;

				return result.IncursSeekPenalty != 0;
			}
		}

		private const uint FILE_SHARE_READ = 0x1;
		private const uint FILE_SHARE_WRITE = 0x2;
		private const uint OPEN_EXISTING = 3;
		private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
		private const int StorageDeviceSeekPenaltyProperty = 7;
		private const int PropertyStandardQuery = 0;

		[StructLayout(LayoutKind.Sequential)]
		private struct STORAGE_PROPERTY_QUERY
		{
			public int PropertyId;
			public int QueryType;
			public byte AdditionalParameters;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
		{
			public uint Version;
			public uint Size;
			public byte IncursSeekPenalty;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
			uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, ref STORAGE_PROPERTY_QUERY lpInBuffer, uint nInBufferSize,
			out DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);
	}
}
