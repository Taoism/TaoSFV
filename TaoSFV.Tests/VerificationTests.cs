using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaoSFV.Hash;
using TaoSFV.Model.Workset;
using TaoSFV.Verification;
using Xunit;

namespace TaoSFV.Tests
{
	public class VerificationTests : IDisposable
	{
		private static readonly IReadOnlyDictionary<string, ChecksumFile> NoCache = new Dictionary<string, ChecksumFile>();
		private readonly string _dir;

		public VerificationTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		private string WriteFile(string name, int length, int seed)
		{
			byte[] data = new byte[length];
			new Random(seed).NextBytes(data);
			string path = Path.Combine(_dir, name);
			File.WriteAllBytes(path, data);
			return path;
		}

		private ChecksumFile AddFile(ChecksumSet set, string name, string expected)
		{
			ChecksumFile file = new ChecksumFile(set) { FileName = name, OriginalChecksum = expected };
			string path = Path.Combine(_dir, name);
			if (File.Exists(path))
				file.FileInfo = new FileInfo(path);
			set.Files.Add(file);
			return file;
		}

		private static string Hash(ChecksumAlgorithm algorithm, string path)
		{
			using (FileStream stream = File.OpenRead(path))
				return algorithm.Compute(stream);
		}

		[Theory]
		[InlineData(ChecksumType.SFV)]
		[InlineData(ChecksumType.MD5)]
		[InlineData(ChecksumType.SHA1)]
		[InlineData(ChecksumType.SHA256)]
		[InlineData(ChecksumType.SHA512)]
		public void Verify_ReportsOkBadAndMissing(ChecksumType type)
		{
			ChecksumAlgorithm algorithm = ChecksumAlgorithm.Get(type);
			string good = WriteFile("good.bin", 300000, 1);
			string bad = WriteFile("bad.bin", 1000, 2);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set." + algorithm.Extension), _dir, type);

			ChecksumFile okFile = AddFile(set, "good.bin", Hash(algorithm, good));
			ChecksumFile badFile = AddFile(set, "bad.bin", new string('0', algorithm.DigestLength));
			ChecksumFile missingFile = AddFile(set, "missing.bin", new string('0', algorithm.DigestLength));

			VerifyContext context = new VerifyContext(CancellationToken.None);
			VerifyOptions options = VerifyOptions.ReadOnly(false);

			Assert.Equal(ChecksumFileState.OK, FileVerifier.Verify(set, okFile, options, NoCache, context));
			Assert.Equal(ChecksumFileState.Bad, FileVerifier.Verify(set, badFile, options, NoCache, context));
			Assert.Equal(ChecksumFileState.Missing, FileVerifier.Verify(set, missingFile, options, NoCache, context));
			Assert.Equal(300000 + 1000, context.BytesProcessed);

			// read-only: nothing renamed or created
			Assert.True(File.Exists(bad));
			Assert.False(File.Exists(bad + ".bad"));
			Assert.False(File.Exists(Path.Combine(_dir, "missing.bin.missing")));
		}

		[Fact]
		public void Verify_HonorsBadFileAndMarkerOptions()
		{
			string bad = WriteFile("bad.bin", 1000, 2);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile badFile = AddFile(set, "bad.bin", "00000000");
			ChecksumFile missingFile = AddFile(set, "missing.bin", "00000000");
			VerifyOptions options = new VerifyOptions { RenameBadFiles = true, CreateMissingMarkers = true };
			VerifyContext context = new VerifyContext(CancellationToken.None);

			FileVerifier.Verify(set, badFile, options, NoCache, context);
			FileVerifier.Verify(set, missingFile, options, NoCache, context);

			Assert.False(File.Exists(bad));
			Assert.True(File.Exists(bad + ".bad"));
			Assert.True(File.Exists(Path.Combine(_dir, "missing.bin.missing")));
		}

		[Fact]
		public void Verify_RestoresBadFileThatNowMatches()
		{
			string path = WriteFile("song.flac", 5000, 3);
			string crc = Hash(ChecksumAlgorithm.Sfv, path);
			File.Move(path, path + ".bad");
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile file = AddFile(set, "song.flac", crc);

			Assert.Equal(ChecksumFileState.OK, FileVerifier.Verify(set, file, VerifyOptions.ReadOnly(false), NoCache, new VerifyContext(CancellationToken.None)));
			Assert.True(File.Exists(path));
		}

		[Fact]
		public void Verify_UsesMatchingCacheEntryWithoutReading()
		{
			string path = WriteFile("cached.bin", 100000, 4);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile file = AddFile(set, "cached.bin", "12345678");
			FileInfo info = new FileInfo(path);
			Dictionary<string, ChecksumFile> cache = new Dictionary<string, ChecksumFile>(StringComparer.OrdinalIgnoreCase)
			{
				{ path.ToUpperInvariant(), new ChecksumFile(null) { FileName = path, CacheLength = info.Length, CacheLastWriteUtc = info.LastWriteTimeUtc, OriginalChecksum = "12345678" } }
			};
			VerifyOptions options = VerifyOptions.ReadOnly(true);

			// the cached CRC is "wrong" for the real bytes, so OK proves the file wasn't read
			Assert.Equal(ChecksumFileState.OK, FileVerifier.Verify(set, file, options, cache, new VerifyContext(CancellationToken.None)));
		}

		[Fact]
		public void Verify_StaleCacheEntryIsIgnored()
		{
			string path = WriteFile("stale.bin", 1000, 5);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile file = AddFile(set, "stale.bin", "12345678");
			Dictionary<string, ChecksumFile> cache = new Dictionary<string, ChecksumFile>(StringComparer.OrdinalIgnoreCase)
			{
				{ path, new ChecksumFile(null) { FileName = path, CacheLength = 1000, CacheLastWriteUtc = DateTime.UtcNow.AddDays(-1), OriginalChecksum = "12345678" } }
			};

			Assert.Equal(ChecksumFileState.Bad, FileVerifier.Verify(set, file, VerifyOptions.ReadOnly(true), cache, new VerifyContext(CancellationToken.None)));
		}

		[Fact]
		public void Verify_CancelledRunThrows()
		{
			string path = WriteFile("big.bin", 4 * 1024 * 1024, 6);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile file = AddFile(set, "big.bin", "00000000");
			CancellationTokenSource cts = new CancellationTokenSource();
			cts.Cancel();

			Assert.ThrowsAny<OperationCanceledException>(() =>
				FileVerifier.Verify(set, file, VerifyOptions.ReadOnly(false), NoCache, new VerifyContext(cts.Token)));
		}

		[Fact]
		public void Verify_PauseBlocksReadingUntilResumed()
		{
			string path = WriteFile("big.bin", 8 * 1024 * 1024, 7);
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			ChecksumFile file = AddFile(set, "big.bin", Hash(ChecksumAlgorithm.Sfv, path));
			PauseGate gate = new PauseGate();
			VerifyContext context = new VerifyContext(CancellationToken.None, gate);
			gate.Pause();

			Task<ChecksumFileState> task = Task.Run(() => FileVerifier.Verify(set, file, VerifyOptions.ReadOnly(false), NoCache, context));

			Assert.False(task.Wait(300));
			Assert.Equal(0, context.BytesProcessed);

			gate.Resume();
			Assert.True(task.Wait(10000));
			Assert.Equal(ChecksumFileState.OK, task.Result);
		}

		[Fact]
		public void Verify_ParallelFilesGiveSameResults()
		{
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sha256"), _dir, ChecksumType.SHA256);
			List<string> expected = new List<string>();
			for (int i = 0; i < 24; i++)
			{
				string path = WriteFile("f" + i + ".bin", 50000 + i * 1000, 100 + i);
				bool corrupt = i % 5 == 0;
				AddFile(set, "f" + i + ".bin", corrupt ? new string('0', 64) : Hash(ChecksumAlgorithm.Sha256, path));
				expected.Add(corrupt ? "Bad" : "OK");
			}

			VerifyContext context = new VerifyContext(CancellationToken.None);
			Parallel.ForEach(set.Files, new ParallelOptions { MaxDegreeOfParallelism = 4 },
				f => FileVerifier.Verify(set, f, VerifyOptions.ReadOnly(false), NoCache, context));

			Assert.Equal(expected, set.Files.Select(f => f.State.ToString()).ToList());
			Assert.Equal(set.Files.Sum(f => f.FileInfo.Length), context.BytesProcessed);
		}

		[Fact]
		public void DiskInfo_SystemDriveIsDetectedOrUnknown()
		{
			string root = Path.GetPathRoot(Environment.SystemDirectory);
			int parallelism = DiskInfo.GetParallelism(root);
			Assert.InRange(parallelism, 1, DiskInfo.MaxParallelFiles);
			Assert.Equal(1, DiskInfo.GetParallelism(@"\\server\share\folder"));
		}
	}
}
