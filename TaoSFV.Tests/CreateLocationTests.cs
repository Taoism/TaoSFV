using System;
using System.IO;
using Xunit;

namespace TaoSFV.Tests
{
	public class CreateLocationTests : IDisposable
	{
		private readonly string _dir;

		public CreateLocationTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(_dir, "Album"));
			Directory.CreateDirectory(Path.Combine(_dir, "Other"));
			File.WriteAllText(Path.Combine(_dir, "Album", "01.flac"), "x");
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		[Fact]
		public void SingleFolderGetsTheFileInsideIt()
		{
			string album = Path.Combine(_dir, "Album");
			Assert.Equal(album, CreateLocation.GetFolder(new[] { album }));
		}

		[Fact]
		public void FilesGetItInTheirFolder()
		{
			Assert.Equal(Path.Combine(_dir, "Album"), CreateLocation.GetFolder(new[] { Path.Combine(_dir, "Album", "01.flac") }));
		}

		[Fact]
		public void SeveralFoldersGetItInTheirParent()
		{
			Assert.Equal(_dir, CreateLocation.GetFolder(new[] { Path.Combine(_dir, "Album"), Path.Combine(_dir, "Other") }));
		}

		[Fact]
		public void DriveRootStaysTheRoot()
		{
			string root = Path.GetPathRoot(_dir);
			Assert.Equal(root, CreateLocation.GetFolder(new[] { root }));
		}
	}
}
