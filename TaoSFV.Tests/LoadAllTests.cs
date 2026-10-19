using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TaoSFV.Model.Workset;
using Xunit;

namespace TaoSFV.Tests
{
	public class LoadAllTests : IDisposable
	{
		private readonly string _dir;

		public LoadAllTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		private string Write(string relativePath, string contents)
		{
			string path = Path.Combine(_dir, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, contents);
			return path;
		}

		[Fact]
		public void LoadsFoldersRecursivelyInPathOrderAndResolvesFiles()
		{
			Write(@"b\b.sfv", "x.bin 00000000\r\n");
			Write(@"a\deeper\c.md5", "d41d8cd98f00b204e9800998ecf8427e *y.bin\r\n");
			Write(@"a\a.sfv", "z.bin 00000000\r\n");
			Write(@"a\z.bin", "12345");
			Write(@"a\notes.txt", "not a checksum file");

			LoadResult result = ChecksumSetLoader.LoadAll(new[] { _dir }, null, CancellationToken.None);

			Assert.Empty(result.Skipped);
			Assert.Equal(new[] { @"a\a.sfv", @"a\deeper\c.md5", @"b\b.sfv" },
				result.Sets.Select(s => s.VerificationFileName.Substring(_dir.Length + 1)));
			Assert.Equal(5, result.Sets[0].TotalSize);
			Assert.NotNull(result.Sets[0].Files[0].FileInfo);
			Assert.Null(result.Sets[2].Files[0].FileInfo);
		}

		[Fact]
		public void SkipsBadFilesInFoldersAndKeepsGoing()
		{
			Write(@"good1.sfv", "x.bin 00000000\r\n");
			string bad = Write(@"sub\bad.sfv", "this is not a checksum line\r\n");
			Write(@"sub\good2.sfv", "y.bin 00000000\r\n");

			LoadResult result = ChecksumSetLoader.LoadAll(new[] { _dir }, null, CancellationToken.None);

			Assert.Equal(2, result.Sets.Count);
			SkippedChecksumFile skipped = Assert.Single(result.Skipped);
			Assert.Equal(bad, skipped.Path);
			Assert.True(skipped.InFolder);
			Assert.IsType<InvalidChecksumFileException>(skipped.Error);
		}

		[Fact]
		public void ReportsMissingPathsAndIgnoresOtherFiles()
		{
			string good = Write("good.sfv", "x.bin 00000000\r\n");
			string text = Write("readme.txt", "hello");
			string missing = Path.Combine(_dir, "gone.sfv");

			LoadResult result = ChecksumSetLoader.LoadAll(new[] { good, text, missing }, null, CancellationToken.None);

			Assert.Equal(good, Assert.Single(result.Sets).VerificationFileName);
			SkippedChecksumFile skipped = Assert.Single(result.Skipped);
			Assert.Equal(missing, skipped.Path);
			Assert.False(skipped.InFolder);
			Assert.IsType<FileNotFoundException>(skipped.Error);
		}

		[Fact]
		public void ReportsEachFolderAndFileItLoads()
		{
			string file = Write(@"sub\a.sfv", "x.bin 00000000\r\n");
			List<string> reported = new List<string>();

			ChecksumSetLoader.LoadAll(new[] { _dir }, reported.Add, CancellationToken.None);

			Assert.Equal(new[] { _dir, Path.Combine(_dir, "sub"), file }, reported);
		}

		[Fact]
		public void StopsWhenCancelled()
		{
			Write("a.sfv", "x.bin 00000000\r\n");
			Write("b.sfv", "x.bin 00000000\r\n");
			CancellationTokenSource cancel = new CancellationTokenSource();

			Assert.Throws<OperationCanceledException>(() =>
				ChecksumSetLoader.LoadAll(new[] { _dir }, path => { if (path.EndsWith("a.sfv")) cancel.Cancel(); }, cancel.Token));
		}
	}
}
