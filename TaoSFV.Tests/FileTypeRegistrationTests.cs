using System;
using Microsoft.Win32;
using Xunit;

namespace TaoSFV.Tests
{
	/// <summary>
	/// Runs against a throwaway key under HKCU\Software\TaoSFV.Tests, never the real Software\Classes.
	/// </summary>
	public class FileTypeRegistrationTests : IDisposable
	{
		private const string Exe = @"C:\Program Files\TaoSFV\TaoSFV.exe";

		private readonly string _path = @"Software\TaoSFV.Tests\" + Guid.NewGuid().ToString("N");
		private readonly RegistryKey _classes;

		public FileTypeRegistrationTests()
		{
			_classes = Registry.CurrentUser.CreateSubKey(_path);
		}

		public void Dispose()
		{
			_classes.Dispose();
			Registry.CurrentUser.DeleteSubKeyTree(_path, false);
			using (RegistryKey parent = Registry.CurrentUser.OpenSubKey(@"Software\TaoSFV.Tests", true))
			{
				if (parent != null && parent.SubKeyCount == 0)
					Registry.CurrentUser.DeleteSubKey(@"Software\TaoSFV.Tests", false);
			}
		}

		private string Value(string key, string name = "")
		{
			using (RegistryKey k = _classes.OpenSubKey(key))
				return k == null ? null : k.GetValue(name) as string;
		}

		[Fact]
		public void Register_WritesAssociationsAndMenus()
		{
			FileTypeRegistration.Register(_classes, Exe);

			Assert.True(FileTypeRegistration.IsRegistered(_classes));
			foreach (string ext in FileTypeRegistration.Extensions)
			{
				string progId = FileTypeRegistration.GetProgId(ext);
				Assert.Equal(progId, Value("." + ext));
				Assert.Equal("\"" + Exe + "\" \"%1\"", Value(progId + @"\shell\open\command"));
				Assert.Equal("\"" + Exe + "\",0", Value(progId + @"\DefaultIcon"));
				using (RegistryKey openWith = _classes.OpenSubKey("." + ext + @"\OpenWithProgids"))
					Assert.Contains(progId, openWith.GetValueNames());
			}

			Assert.Equal("TaoSFV.CreateMenu", Value(@"*\shell\TaoSFV", "ExtendedSubCommandsKey"));
			Assert.Equal("TaoSFV.FolderMenu", Value(@"Directory\shell\TaoSFV", "ExtendedSubCommandsKey"));
			Assert.Equal("\"" + Exe + "\" \"%1\"", Value(@"TaoSFV.FolderMenu\shell\0verify\command"));
			Assert.Equal("\"" + Exe + "\" /create sha256 \"%1\"", Value(@"TaoSFV.FolderMenu\shell\sha256\command"));
			Assert.Equal("Create SHA-512...", Value(@"TaoSFV.CreateMenu\shell\sha512", "MUIVerb"));
			Assert.Equal("\"" + Exe + "\" /create sfv \"%1\"", Value(@"TaoSFV.CreateMenu\shell\sfv\command"));
			Assert.Equal("\"" + Exe + "\" /create md5 \"%1\"", Value(@"TaoSFV.CreateMenu\shell\md5\command"));
			Assert.Equal("\"" + Exe + "\" /create sha1 \"%1\"", Value(@"TaoSFV.CreateMenu\shell\sha1\command"));
		}

		[Fact]
		public void Register_IsRepeatable()
		{
			FileTypeRegistration.Register(_classes, @"C:\old\TaoSFV.exe");
			FileTypeRegistration.Register(_classes, Exe);

			Assert.Equal("\"" + Exe + "\" \"%1\"", Value(@"TaoSFV.SFV\shell\open\command"));
		}

		[Fact]
		public void Unregister_RemovesEverythingItWrote()
		{
			FileTypeRegistration.Register(_classes, Exe);
			FileTypeRegistration.Unregister(_classes);

			Assert.False(FileTypeRegistration.IsRegistered(_classes));
			Assert.Equal(0, _classes.SubKeyCount - CountShellParents());
			Assert.Null(_classes.OpenSubKey(@"*\shell\TaoSFV"));
			Assert.Null(_classes.OpenSubKey(@"Directory\shell\TaoSFV"));
		}

		// "*\shell" and "Directory\shell" parents may remain empty; everything else must be gone
		private int CountShellParents()
		{
			int count = 0;
			foreach (string name in new[] { "*", "Directory" })
			{
				if (_classes.OpenSubKey(name) != null)
					count++;
			}
			return count;
		}

		[Fact]
		public void Unregister_LeavesAnotherProgramsAssociation()
		{
			FileTypeRegistration.Register(_classes, Exe);
			using (RegistryKey sfv = _classes.CreateSubKey(".sfv"))
			{
				sfv.SetValue("", "OtherApp.SFV");
				using (RegistryKey openWith = sfv.CreateSubKey("OpenWithProgids"))
					openWith.SetValue("OtherApp.SFV", new byte[0], RegistryValueKind.None);
			}

			FileTypeRegistration.Unregister(_classes);

			Assert.Equal("OtherApp.SFV", Value(".sfv"));
			using (RegistryKey openWith = _classes.OpenSubKey(@".sfv\OpenWithProgids"))
				Assert.Equal(new[] { "OtherApp.SFV" }, openWith.GetValueNames());
			Assert.Null(_classes.OpenSubKey(".md5"));
		}
	}
}
