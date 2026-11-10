using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TaoSFV.Hash;
using TaoSFV.Model.Settings;
using TaoSFV.Model.Workset;
using TaoSFV.Par2;
using TaoSFV.Verification;
using Xunit;

namespace TaoSFV.Tests
{
	/// <summary>Names in checksum and PAR2 files must never make TaoSFV write outside the set's folder.</summary>
	public class SafePathTests : IDisposable
	{
		private static readonly IReadOnlyDictionary<string, ChecksumFile> NoCache = new Dictionary<string, ChecksumFile>();
		private readonly string _dir;
		private readonly string _setDir;

		public SafePathTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			_setDir = Path.Combine(_dir, "set");
			Directory.CreateDirectory(_setDir);
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		[Theory]
		[InlineData("a.bin")]
		[InlineData(@"CD1\a.bin")]
		[InlineData("CD1/a.bin")]
		[InlineData("a..b.bin")]
		[InlineData("...bin")]
		public void Allows_names_inside_the_folder(string name)
		{
			Assert.True(SafePath.IsInside(_setDir, name));
		}

		[Theory]
		[InlineData(@"..\a.bin")]
		[InlineData("../a.bin")]
		[InlineData(@"CD1\..\..\a.bin")]
		[InlineData(@"C:\Windows\a.bin")]
		[InlineData(@"C:a.bin")]
		[InlineData(@"\a.bin")]
		[InlineData(@"\\server\share\a.bin")]
		[InlineData(@"a.bin:stream")]
		[InlineData("..")]
		[InlineData("")]
		public void Refuses_names_outside_the_folder(string name)
		{
			Assert.False(SafePath.IsInside(_setDir, name));
			Assert.Null(SafePath.Resolve(_setDir, name));
		}

		[Fact]
		public void Verify_never_writes_outside_the_folder()
		{
			// a bad file and a missing file named outside the set: checked, but no .missing marker, rename or delete
			string outside = Path.Combine(_dir, "victim.bin");
			File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });

			ChecksumSet set = new ChecksumSet(Path.Combine(_setDir, "set.sfv"), _setDir, ChecksumType.SFV);
			ChecksumFile bad = new ChecksumFile(set) { FileName = @"..\victim.bin", OriginalChecksum = "00000000", FileInfo = new FileInfo(outside) };
			ChecksumFile missing = new ChecksumFile(set) { FileName = @"..\gone.bin", OriginalChecksum = "00000000" };
			set.Files.Add(bad);
			set.Files.Add(missing);

			VerifyOptions options = new VerifyOptions
			{
				CreateMissingMarkers = true, RenameBadFiles = true, DeleteFailedFiles = true,
				CleanupBadMissing = true, Renaming = CheckRenaming.Lowercase
			};
			VerifyContext context = new VerifyContext(CancellationToken.None);

			Assert.Equal(ChecksumFileState.Bad, FileVerifier.Verify(set, bad, options, NoCache, context));
			Assert.Equal(ChecksumFileState.Missing, FileVerifier.Verify(set, missing, options, NoCache, context));

			Assert.True(File.Exists(outside));
			Assert.False(File.Exists(outside + ".bad"));
			Assert.False(File.Exists(Path.Combine(_dir, "gone.bin.missing")));
		}

		[Fact]
		public void Verify_still_writes_markers_inside_the_folder()
		{
			ChecksumSet set = new ChecksumSet(Path.Combine(_setDir, "set.sfv"), _setDir, ChecksumType.SFV);
			ChecksumFile missing = new ChecksumFile(set) { FileName = "gone.bin", OriginalChecksum = "00000000" };
			set.Files.Add(missing);

			FileVerifier.Verify(set, missing, new VerifyOptions { CreateMissingMarkers = true }, NoCache, new VerifyContext(CancellationToken.None));
			Assert.True(File.Exists(Path.Combine(_setDir, "gone.bin.missing")));
		}

		[Fact]
		public void Par2_set_naming_a_file_outside_its_folder_is_refused()
		{
			File.WriteAllBytes(Path.Combine(_dir, "outside.bin"), new byte[4096]);
			File.WriteAllBytes(Path.Combine(_setDir, "a.bin"), new byte[4096]);
			string par2 = Path.Combine(_setDir, "set.par2");
			Par2TestWriter.Write(par2, _setDir, new[] { "a.bin", @"..\outside.bin" }, 1024, 1);

			InvalidDataException ex = Assert.Throws<InvalidDataException>(() => Par2Reader.Load(par2));
			Assert.Contains("outside its folder", ex.Message);
		}
	}
}
