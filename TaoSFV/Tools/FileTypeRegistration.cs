using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaoSFV
{
	/// <summary>
	/// Per-user (HKCU\Software\Classes) file associations and Explorer context menus; no administrator rights needed.
	/// </summary>
	internal static class FileTypeRegistration
	{
		public static readonly string[] Extensions = { "sfv", "md5", "sha1" };

		private const string ClassesPath = @"Software\Classes";
		private const string CreateMenuKey = "TaoSFV.CreateMenu";
		private const string MenuVerb = "TaoSFV";
		private static readonly string[] MenuTargets = { "*", "Directory" };

		private const int SHCNE_ASSOCCHANGED = 0x08000000;
		private const int SHCNF_IDLIST = 0x0;

		[DllImport("shell32.dll")]
		private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

		public static string GetProgId(string extension)
		{
			return "TaoSFV." + extension.ToUpperInvariant();
		}

		public static void Register(string exePath)
		{
			using (RegistryKey classes = Registry.CurrentUser.CreateSubKey(ClassesPath))
				Register(classes, exePath);
			SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
		}

		public static void Unregister()
		{
			using (RegistryKey classes = Registry.CurrentUser.CreateSubKey(ClassesPath))
				Unregister(classes);
			SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
		}

		public static bool IsRegistered()
		{
			using (RegistryKey classes = Registry.CurrentUser.OpenSubKey(ClassesPath))
				return classes != null && IsRegistered(classes);
		}

		internal static void Register(RegistryKey classes, string exePath)
		{
			string open = string.Format("\"{0}\" \"%1\"", exePath);

			foreach (string extension in Extensions)
			{
				string progId = GetProgId(extension);

				using (RegistryKey key = classes.CreateSubKey(progId))
				{
					key.SetValue("", string.Format("{0} File", extension.ToUpperInvariant()));
					using (RegistryKey icon = key.CreateSubKey("DefaultIcon"))
						icon.SetValue("", string.Format("\"{0}\",0", exePath));
					using (RegistryKey command = key.CreateSubKey(@"shell\open\command"))
						command.SetValue("", open);
				}

				using (RegistryKey ext = classes.CreateSubKey("." + extension))
				{
					ext.SetValue("", progId);
					using (RegistryKey openWith = ext.CreateSubKey("OpenWithProgids"))
						openWith.SetValue(progId, new byte[0], RegistryValueKind.None);
				}
			}

			// "TaoSFV > Create SFV/MD5/SHA-1..." on files and folders. ExtendedSubCommandsKey is the
			// per-user counterpart of the HKLM CommandStore that ilSFV used.
			string[,] createVerbs = { { "sfv", "Create SFV..." }, { "md5", "Create MD5..." }, { "sha1", "Create SHA-1..." } };
			for (int i = 0; i < createVerbs.GetLength(0); i++)
			{
				using (RegistryKey verb = classes.CreateSubKey(string.Format(@"{0}\shell\{1}", CreateMenuKey, createVerbs[i, 0])))
				{
					verb.SetValue("MUIVerb", createVerbs[i, 1]);
					verb.SetValue("MultiSelectModel", "Player");
					using (RegistryKey command = verb.CreateSubKey("command"))
						command.SetValue("", string.Format("\"{0}\" /create {1} \"%1\"", exePath, createVerbs[i, 0]));
				}
			}

			foreach (string target in MenuTargets)
			{
				using (RegistryKey verb = classes.CreateSubKey(string.Format(@"{0}\shell\{1}", target, MenuVerb)))
				{
					verb.SetValue("MUIVerb", "TaoSFV");
					verb.SetValue("Icon", string.Format("\"{0}\",0", exePath));
					verb.SetValue("MultiSelectModel", "Player");
					verb.SetValue("ExtendedSubCommandsKey", CreateMenuKey);
				}
			}
		}

		/// <summary>Removes only what Register wrote; another program's association of an extension is left alone.</summary>
		internal static void Unregister(RegistryKey classes)
		{
			foreach (string extension in Extensions)
			{
				string progId = GetProgId(extension);
				classes.DeleteSubKeyTree(progId, false);

				using (RegistryKey ext = classes.OpenSubKey("." + extension, true))
				{
					if (ext == null)
						continue;

					if (string.Equals(ext.GetValue("") as string, progId, StringComparison.OrdinalIgnoreCase))
						ext.DeleteValue("", false);

					using (RegistryKey openWith = ext.OpenSubKey("OpenWithProgids", true))
					{
						if (openWith != null)
						{
							openWith.DeleteValue(progId, false);
							if (openWith.ValueCount == 0 && openWith.SubKeyCount == 0)
								ext.DeleteSubKey("OpenWithProgids", false);
						}
					}
				}

				DeleteIfEmpty(classes, "." + extension);
			}

			classes.DeleteSubKeyTree(CreateMenuKey, false);
			foreach (string target in MenuTargets)
				classes.DeleteSubKeyTree(string.Format(@"{0}\shell\{1}", target, MenuVerb), false);
		}

		internal static bool IsRegistered(RegistryKey classes)
		{
			foreach (string extension in Extensions)
			{
				using (RegistryKey ext = classes.OpenSubKey("." + extension))
				{
					if (ext == null || !string.Equals(ext.GetValue("") as string, GetProgId(extension), StringComparison.OrdinalIgnoreCase))
						return false;
				}
			}
			return true;
		}

		private static void DeleteIfEmpty(RegistryKey parent, string name)
		{
			using (RegistryKey key = parent.OpenSubKey(name))
			{
				if (key == null || key.ValueCount != 0 || key.SubKeyCount != 0)
					return;
			}
			parent.DeleteSubKey(name, false);
		}
	}
}
