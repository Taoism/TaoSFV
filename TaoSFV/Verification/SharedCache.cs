using System.IO;

namespace TaoSFV.Verification
{
	/// <summary>
	/// TaoSFV's checksum cache for other front ends (taosfv-cli --use-cache). A cached checksum is used when the
	/// file's size and last-write time are unchanged and it matches the checksum file; new results are added.
	/// </summary>
	public static class SharedCache
	{
		/// <summary>TaoSFV's own cache file.</summary>
		public static string DefaultFile
		{
			get { return Program.DefaultCacheFile; }
		}

		/// <summary>Opens an existing cache file; false (nothing opened) if it doesn't exist.</summary>
		public static bool TryOpen(string cacheFile)
		{
			if (!File.Exists(cacheFile))
				return false;
			Cache.OpenStandalone(cacheFile);
			return true;
		}

		/// <summary>Waits (up to 10 s) until new results are written.</summary>
		public static void Flush()
		{
			Cache.Flush();
		}
	}
}
