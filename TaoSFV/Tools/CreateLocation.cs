using System.Collections.Generic;
using System.IO;

namespace TaoSFV
{
	/// <summary>Where a checksum file created from Explorer (or a drop) goes.</summary>
	internal static class CreateLocation
	{
		/// <summary>
		/// The folder the new checksum file is written to (and named after). A single folder gets the file inside it
		/// (D:\Music\Album -> D:\Music\Album\Album.sfv); files, or several items, get it in the folder they're in.
		/// </summary>
		public static string GetFolder(IList<string> items)
		{
			if (items.Count == 1 && Directory.Exists(items[0]))
				return items[0];

			// a drive root has no parent: keep the root itself
			return Path.GetDirectoryName(items[0]) ?? items[0];
		}
	}
}
