using System;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using TaoSFV.Hash;
using TaoSFV.Model.Workset;
using TaoSFV.Verification;
using Xunit;

namespace TaoSFV.Tests
{
	public class SharedCacheTests : IDisposable
	{
		private readonly string _dir;
		private readonly string _cacheFile;

		public SharedCacheTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
			_cacheFile = Path.Combine(_dir, "cache.db");
		}

		public void Dispose()
		{
			Cache.CloseStandalone();
			SqliteConnection.ClearAllPools();
			Directory.Delete(_dir, true);
		}

		private ChecksumSet MakeSet(string path, string checksum)
		{
			ChecksumSet set = new ChecksumSet(Path.Combine(_dir, "set.sfv"), _dir, ChecksumType.SFV);
			set.Files.Add(new ChecksumFile(set) { FileName = Path.GetFileName(path), OriginalChecksum = checksum, FileInfo = new FileInfo(path) });
			return set;
		}

		private static ChecksumFileState Check(ChecksumSet set, bool useCache)
		{
			VerifyOptions options = VerifyOptions.ReadOnly(useCache);
			return FileVerifier.Verify(set, set.Files[0], options, FileVerifier.LoadCache(set, options), new VerifyContext(CancellationToken.None));
		}

		[Fact]
		public void MissingCacheFileIsNotCreated()
		{
			Assert.False(SharedCache.TryOpen(_cacheFile));
			Assert.False(File.Exists(_cacheFile));
		}

		[Fact]
		public void UnchangedFileIsTakenFromTheCacheAndNewResultsAreStored()
		{
			File.WriteAllBytes(_cacheFile, new byte[0]); // an empty file is a valid, empty SQLite database
			Assert.True(SharedCache.TryOpen(_cacheFile));

			string path = Path.Combine(_dir, "a.bin");
			byte[] data = new byte[50000];
			new Random(7).NextBytes(data);
			File.WriteAllBytes(path, data);
			string crc;
			using (FileStream stream = File.OpenRead(path))
				crc = ChecksumAlgorithm.Sfv.Compute(stream);

			// first check reads the file and adds it to the cache
			Assert.Equal(ChecksumFileState.OK, Check(MakeSet(path, crc), true));
			SharedCache.Flush();

			// change the content but keep size and modified time: the cache can't tell, so it still says OK...
			DateTime written = File.GetLastWriteTimeUtc(path);
			data[100] ^= 0xFF;
			File.WriteAllBytes(path, data);
			File.SetLastWriteTimeUtc(path, written);

			Assert.Equal(ChecksumFileState.OK, Check(MakeSet(path, crc), true));
			// ...while reading the file finds the damage
			Assert.Equal(ChecksumFileState.Bad, Check(MakeSet(path, crc), false));

			// a changed modified time makes it read the file again
			File.SetLastWriteTimeUtc(path, written.AddMinutes(1));
			Assert.Equal(ChecksumFileState.Bad, Check(MakeSet(path, crc), true));
		}
	}
}
