using System;
using System.IO;
using Xunit;

namespace TaoSFV.Tests
{
	// Recycling a real file isn't tested here so test runs don't fill the Recycle Bin.
	public class RecycleBinTests : IDisposable
	{
		private readonly string _dir;

		public RecycleBinTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		[Fact]
		public void LongPathIsLeftInPlaceNotDeleted()
		{
			// the shell would delete this permanently instead of recycling it
			string dir = Path.Combine(_dir, new string('d', 120), new string('e', 120));
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, "long path test.bin");
			Assert.True(path.Length > RecycleBin.MaxPathLength);
			File.WriteAllBytes(path, new byte[] { 1 });

			Assert.False(RecycleBin.Recycle(path));
			Assert.True(File.Exists(path));
		}

		[Fact]
		public void MissingFileReturnsFalse()
		{
			Assert.False(RecycleBin.Recycle(Path.Combine(_dir, "not there.bin")));
		}
	}
}
