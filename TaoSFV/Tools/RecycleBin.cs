using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TaoSFV
{
	/// <summary>
	/// Moves files to the Recycle Bin with the shell's IFileOperation.
	/// </summary>
	/// <remarks>
	/// The Recycle Bin can't hold a path of 260 characters or more: the shell silently deletes such a file
	/// permanently instead (seen on Windows 11 with LongPathsEnabled and a longPathAware manifest, and also when
	/// given the 8.3 short name, which it expands). So long paths are never passed to it.
	/// </remarks>
	internal static class RecycleBin
	{
		public const int MaxPathLength = 259;

		/// <summary>Returns false if the file couldn't be recycled; it's then left in place.</summary>
		public static bool Recycle(string fileName)
		{
			if (fileName.Length > MaxPathLength)
				return false;

			// IFileOperation needs a single-threaded apartment; checking runs on the thread pool
			if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
				return RecycleOnThisThread(fileName);

			bool result = false;
			Thread thread = new Thread(() => result = RecycleOnThisThread(fileName));
			thread.SetApartmentState(ApartmentState.STA);
			thread.IsBackground = true;
			thread.Start();
			thread.Join();
			return result;
		}

		private static bool RecycleOnThisThread(string fileName)
		{
			IFileOperation operation = null;
			IShellItem item = null;
			try
			{
				operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_FileOperation));
				operation.SetOperationFlags(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);

				Guid iidShellItem = typeof(IShellItem).GUID;
				int hr = SHCreateItemFromParsingName(fileName, IntPtr.Zero, ref iidShellItem, out item);
				if (hr != 0)
					return false;

				operation.DeleteItem(item, IntPtr.Zero);
				operation.PerformOperations();
				return !operation.GetAnyOperationsAborted();
			}
			catch (COMException)
			{
				return false;
			}
			finally
			{
				if (item != null)
					Marshal.ReleaseComObject(item);
				if (operation != null)
					Marshal.ReleaseComObject(operation);
			}
		}

		private const uint FOF_SILENT = 0x0004;
		private const uint FOF_NOCONFIRMATION = 0x0010;
		private const uint FOF_ALLOWUNDO = 0x0040;
		private const uint FOF_NOERRORUI = 0x0400;

		private static readonly Guid CLSID_FileOperation = new Guid("3ad05575-8857-4850-9277-11b85bdb8e09");

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

		[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IShellItem
		{
		}

		// methods in vtable order; only the ones used have real signatures
		[ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IFileOperation
		{
			void Advise(IntPtr sink, out uint cookie);
			void Unadvise(uint cookie);
			void SetOperationFlags(uint flags);
			void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
			void SetProgressDialog(IntPtr dialog);
			void SetProperties(IntPtr properties);
			void SetOwnerWindow(IntPtr owner);
			void ApplyPropertiesToItem(IShellItem item);
			void ApplyPropertiesToItems(IntPtr items);
			void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);
			void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
			void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);
			void MoveItems(IntPtr items, IShellItem destination);
			void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);
			void CopyItems(IntPtr items, IShellItem destination);
			void DeleteItem(IShellItem item, IntPtr sink);
			void DeleteItems(IntPtr items);
			void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
				[MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
			void PerformOperations();
			[return: MarshalAs(UnmanagedType.Bool)]
			bool GetAnyOperationsAborted();
		}
	}
}
