using System;
using System.IO;
using System.Threading;
using TaoSFV.Hash;
using TaoSFV.Model.Workset;
using TaoSFV.Verification;
using Xunit;

namespace TaoSFV.Tests
{
	public class RenamedAndUnreadableTests : IDisposable
	{
		private readonly string _dir;

		public RenamedAndUnreadableTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		private string Write(string name, int length, int seed)
		{
			byte[] data = new byte[length];
			new Random(seed).NextBytes(data);
			string path = Path.Combine(_dir, name);
			File.WriteAllBytes(path, data);
			return path;
		}

		private static string Crc(string path)
		{
			using (FileStream stream = File.OpenRead(path))
				return ChecksumAlgorithm.Sfv.Compute(stream);
		}

		private ChecksumFile Missing(ChecksumSet set, string name, string checksum)
		{
			ChecksumFile file = new ChecksumFile(set) { FileName = name, OriginalChecksum = checksum, State = ChecksumFileState.Missing };
			set.Files.Add(file);
			return file;
		}

		[Fact]
		public void FindsRenamedFilesAndRenamesThemBack()
		{
			string renamed = Write("Track 01 (1).flac", 5000, 1);   // was "01.flac"
			string noExt = Write("02", 6000, 2);                      // was "02.flac", lost its extension
			Write("other.flac", 7000, 3);                              // unrelated
			string crc1 = Crc(renamed), crc2 = Crc(noExt);

			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile one = Missing(set, "01.flac", crc1);
			ChecksumFile two = Missing(set, "02.flac", crc2);
			ChecksumFile gone = Missing(set, "03.flac", "12345678");

			var found = RenamedFileFinder.Find(set, false, new VerifyContext(CancellationToken.None));

			Assert.Equal(new[] { one, two }, found);
			Assert.Equal(ChecksumFileState.OK, one.State);
			Assert.Equal(ChecksumFileState.Missing, gone.State);
			Assert.True(File.Exists(Path.Combine(_dir, "01.flac")));
			Assert.True(File.Exists(Path.Combine(_dir, "02.flac")));
			Assert.False(File.Exists(renamed));
			Assert.True(File.Exists(Path.Combine(_dir, "other.flac")));
		}

		[Fact]
		public void LowercaseRenamingAppliesToFoundFiles()
		{
			string renamed = Write("x.flac", 1000, 4);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			Missing(set, "MiXeD.flac", Crc(renamed));

			RenamedFileFinder.Find(set, true, new VerifyContext(CancellationToken.None));

			Assert.Equal(new[] { "mixed.flac" }, Array.ConvertAll(Directory.GetFiles(_dir), Path.GetFileName));
		}

		[Fact]
		public void LockedFileThrowsIOExceptionAndLeavesNoBytesInFlight()
		{
			string path = Write("locked.flac", 100000, 5);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile file = new ChecksumFile(set) { FileName = "locked.flac", OriginalChecksum = "00000000", FileInfo = new FileInfo(path) };
			set.Files.Add(file);
			VerifyContext context = new VerifyContext(CancellationToken.None);

			using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
			{
				Assert.ThrowsAny<IOException>(() =>
					FileVerifier.Verify(set, file, VerifyOptions.ReadOnly(false), new System.Collections.Generic.Dictionary<string, ChecksumFile>(), context));
			}

			Assert.Null(context.GetFileProgress(file));       // not left "in progress"
			Assert.Equal(100000, context.BytesProcessed);     // counted as done, so progress still reaches 100%
			Assert.Equal(0, context.BytesChecked);            // but not as checked, so speed and "MB checked" aren't inflated
		}

		[Fact]
		public void CheckedBytesIncludeCachedAndHashedFiles()
		{
			string path = Write("a.bin", 300000, 7);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile file = new ChecksumFile(set) { FileName = "a.bin", OriginalChecksum = Crc(path), FileInfo = new FileInfo(path) };
			set.Files.Add(file);
			VerifyContext context = new VerifyContext(CancellationToken.None);

			FileVerifier.Verify(set, file, VerifyOptions.ReadOnly(false), new System.Collections.Generic.Dictionary<string, ChecksumFile>(), context);

			Assert.Equal(300000, context.BytesChecked);
		}

		[Fact]
		public void HashFileCountsTowardProgress()
		{
			string path = Write("a.bin", 300000, 6);
			VerifyContext context = new VerifyContext(CancellationToken.None);

			string crc = FileVerifier.HashFile(ChecksumAlgorithm.Sfv, new FileInfo(path), context);

			Assert.Equal(Crc(path), crc);
			Assert.Equal(300000, context.BytesProcessed);
		}
	}
}
