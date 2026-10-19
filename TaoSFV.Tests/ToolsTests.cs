using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaoSFV.Model.Workset;
using Xunit;

namespace TaoSFV.Tests
{
	public class DuplicateFinderTests
	{
		private static ChecksumSet Set(params string[] nameAndChecksum)
		{
			ChecksumSet set = new ChecksumSet(@"C:\music\set.md5", @"C:\music", ChecksumType.MD5);
			for (int i = 0; i < nameAndChecksum.Length; i += 2)
				set.Files.Add(new ChecksumFile(set) { FileName = nameAndChecksum[i], OriginalChecksum = nameAndChecksum[i + 1] });
			return set;
		}

		[Fact]
		public void GroupsByChecksumIgnoringCase()
		{
			ChecksumSet set = Set(@"a\1.flac", "AAAA", @"b\1.flac", "aaaa", @"a\2.flac", "BBBB", @"c\3.flac", "CCCC", @"d\3.flac", "CCCC");

			List<List<ChecksumFile>> groups = DuplicateFinder.GetDuplicates(set);

			Assert.Equal(new[] { new[] { @"a\1.flac", @"b\1.flac" }, new[] { @"c\3.flac", @"d\3.flac" } },
				groups.Select(g => g.Select(f => f.FileName).ToArray()).ToArray());
		}

		[Fact]
		public void DeletedFilesDontCount()
		{
			ChecksumSet set = Set(@"a\1.flac", "AAAA", @"b\1.flac", "AAAA");
			set.Files[1].IsDeleted = true;

			Assert.Empty(DuplicateFinder.GetDuplicates(set));
		}

		[Fact]
		public void DeletableInFolderAlwaysKeepsOneCopy()
		{
			ChecksumSet set = Set(
				@"Copy\1.flac", "AAAA", @"Album\1.flac", "AAAA",   // a copy outside "Copy": deletable
				@"Copy\2.flac", "BBBB", @"copy\sub\2.flac", "BBBB", // every copy is in "Copy": kept
				@"Album\3.flac", "CCCC", @"Album\x\3.flac", "CCCC"); // none in "Copy"
			List<List<ChecksumFile>> groups = DuplicateFinder.GetDuplicates(set);

			List<ChecksumFile> deletable = DuplicateFinder.GetDeletableIn(groups, @"Copy\");

			Assert.Equal(new[] { @"Copy\1.flac" }, deletable.Select(f => f.FileName));
		}

		[Fact]
		public void FolderPrefixMatchesWholeFolderNames()
		{
			ChecksumSet set = Set(@"Copy 2\1.flac", "AAAA", @"Album\1.flac", "AAAA");

			Assert.Empty(DuplicateFinder.GetDeletableIn(DuplicateFinder.GetDuplicates(set), @"Copy\"));
		}
	}

	public class FileNameTruncatorTests : IDisposable
	{
		private readonly string _dir;

		public FileNameTruncatorTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		private string Touch(string relativePath)
		{
			string path = Path.Combine(_dir, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, relativePath);
			return path;
		}

		private string[] Names()
		{
			return Directory.GetFiles(_dir, "*", SearchOption.AllDirectories)
				.Select(p => p.Substring(_dir.Length + 1)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
		}

		[Theory]
		[InlineData("Track 01 - A Long Title.flac", 16, 1, "Track 01 -.flac")]      // trailing space dropped
		[InlineData("Track 01 - A Long Title.flac", 16, 2, "Track 01~2.flac")]
		[InlineData("Track 01 - A Long Title.flac", 16, 12, "Track 01~12.flac")]
		[InlineData("abcdefghijklmnopqrstuvwxyz", 12, 1, "abcdefghijkl")]
		[InlineData("Track 1.  more words.mp3", 13, 1, "Track 1.mp3")]           // no trailing period or space
		[InlineData("short.averyveryverylongextension", 20, 1, null)]       // no room
		public void Shorten(string name, int maxLength, int attempt, string expected)
		{
			string result = FileNameTruncator.Shorten(name, maxLength, attempt);
			Assert.Equal(expected, result);
			if (result != null)
				Assert.True(result.Length <= maxLength);
		}

		[Fact]
		public void ShortenDoesNotSplitASurrogatePair()
		{
			string name = "abcdefghij\U0001F3B5xyz.mp3"; // musical note at positions 10-11

			Assert.Equal("abcdefghij.mp3", FileNameTruncator.Shorten(name, 15, 1));
		}

		[Fact]
		public void RenamesLongNamesInSubfoldersAndAvoidsClashes()
		{
			Touch(@"short.flac");
			Touch(@"disc 1\01 - Long Song Title Number One.flac");
			Touch(@"disc 1\01 - Long Song Title Number Two.flac");
			Touch(@"disc 1\01 - Long Song.flac"); // already the shortened name of both

			TruncateResult result = FileNameTruncator.Truncate(_dir, 20, null);

			Assert.Equal(2, result.Renamed);
			Assert.Empty(result.NotRenamed);
			Assert.Equal(new[] { @"disc 1\01 - Long Song.flac", @"disc 1\01 - Long Son~2.flac", @"disc 1\01 - Long Son~3.flac", "short.flac" }, Names());
			Assert.Equal(@"disc 1\01 - Long Song Title Number One.flac", File.ReadAllText(Path.Combine(_dir, @"disc 1\01 - Long Son~2.flac")));
		}

		[Fact]
		public void ReportsFilesItCantRenameAndCarriesOn()
		{
			string noRoom = Touch(@"a.thisextensionistoolong");
			string locked = Touch(@"locked file with a long name.txt");
			Touch(@"another file with a long name.txt");

			TruncateResult result;
			using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
				result = FileNameTruncator.Truncate(_dir, 14, null);

			Assert.Equal(1, result.Renamed);
			Assert.Equal(2, result.NotRenamed.Count);
			Assert.Contains(result.NotRenamed, p => p.Key == noRoom && p.Value == null);
			Assert.Contains(result.NotRenamed, p => p.Key == locked && p.Value != null);
			Assert.Contains("another fi.txt", Names());
		}
	}
}
