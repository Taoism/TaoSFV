using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TaoSFV.Tests
{
	public class DatabaseTests : IDisposable
	{
		private readonly string _dir;

		public DatabaseTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose()
		{
			SqliteConnection.ClearAllPools();
			Directory.Delete(_dir, true);
		}

		[Fact]
		public void NewSettingsHaveAutomaticParallelReads()
		{
			using (SqliteConnection conn = Database.Open(Path.Combine(_dir, "settings.db")))
			{
				Assert.True(Database.UpgradeSettings(conn));
				DataTable dt = Database.GetTable(conn, "select ParallelReads from Checking;");
				Assert.Equal(0L, dt.Rows[0][0]);
				Assert.False(Database.UpgradeSettings(conn)); // already current
			}
		}

		[Fact]
		public void CacheHasCaseInsensitiveFileNameIndexes()
		{
			using (SqliteConnection conn = Database.Open(Path.Combine(_dir, "cache.db")))
			{
				Database.UpgradeCache(conn);
				DataTable dt = Database.GetTable(conn, "select name from sqlite_master where type = 'index' and name like '%_NoCase';");
				Assert.Equal(5, dt.Rows.Count);
			}
		}
	}
}
