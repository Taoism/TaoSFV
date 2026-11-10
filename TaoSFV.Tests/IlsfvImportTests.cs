using System;
using System.Collections.Generic;
using System.Data.SqlServerCe;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TaoSFV.Tests
{
	/// <summary>Builds small ilSFV-style .sdf databases and imports them into fresh TaoSFV SQLite databases.</summary>
	public class IlsfvImportTests : IDisposable
	{
		private readonly string _dir;
		private readonly string _ilsfv;

		public IlsfvImportTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			_ilsfv = Path.Combine(_dir, "ilSFV");
			Directory.CreateDirectory(_ilsfv);
		}

		public void Dispose()
		{
			SqliteConnection.ClearAllPools();
			Directory.Delete(_dir, true);
		}

		private static void Exec(SqlCeConnection conn, string sql)
		{
			using (SqlCeCommand cmd = new SqlCeCommand(sql, conn))
				cmd.ExecuteNonQuery();
		}

		private static SqlCeConnection CreateSdf(string path)
		{
			string cs = "Data Source=" + path;
			using (SqlCeEngine engine = new SqlCeEngine(cs))
				engine.CreateDatabase();
			SqlCeConnection conn = new SqlCeConnection(cs);
			conn.Open();
			return conn;
		}

		// the ilSFV 1.11 schema (settings version 9), with non-default values
		private void CreateIlsfvSettings(string header = " My header")
		{
			using (SqlCeConnection c = CreateSdf(Path.Combine(_ilsfv, "settings.sdf")))
			{
				Exec(c, "create table DatabaseSetting (Version int not null)");
				Exec(c, "insert into DatabaseSetting (Version) values(9)");
				Exec(c, "create table UsageStatistics (FilesChecked bigint not null, SetsChecked bigint not null, MBChecked bigint not null, GoodFiles bigint not null, TimeSpent bigint not null)");
				Exec(c, "insert into UsageStatistics values(72646, 5086, 2525362, 71126, 9488)");
				Exec(c, @"create table General (Language nvarchar(50) not null, CacheSize bigint not null, AlwaysOnTop bit not null, RememberWindowPlacement bit not null,
ReuseWindow bit not null, FlashWindowWhenDone bit not null, AutoScrollFileList bit not null, Recursive bit not null, HideGoodFiles bit not null,
ShowCommentsPane bit not null, UseCachedResults bit not null, WindowHeight bigint not null, WindowWidth bigint not null, WindowLeft bigint not null,
WindowTop bigint not null, FormWindowState nvarchar(50) not null, CheckForUpdates bit not null, LastUpdateCheck datetime not null,
UpdateCheckFrequency int not null, UseLowPriorityOnHide bit not null, IsRecentFilesSaved bit not null)");
				Exec(c, "insert into General values('Deutsch', 35000, 1, 1, 0, 1, 1, 1, 1, 0, 1, 901, 976, 1363, 347, 'Maximized', 1, '2026-10-06', 7, 0, 1)");
				Exec(c, @"create table Checking (AutoFindRenames bit not null, UseRecyleBin bit not null, RenameBadFiles bit not null, CreateMissingMarkers bit not null,
CleanupBadMissing bit not null, DeleteFailedFiles bit not null, AutoCloseWhenDoneChecking bit not null, AutoCloseWhenDoneCheckingOnlyIfAllOK bit not null,
Renaming nvarchar(50) not null, AutoVerify bit not null, PlaySoundOK bit not null, PlaySoundOK_IfInactive bit not null, PlaySoundError bit not null)");
				Exec(c, "insert into Checking values(1, 1, 0, 1, 1, 0, 1, 1, 'Lowercase', 1, 1, 1, 0)");
				Exec(c, @"create table Creating (ExcludeFilesOfType nvarchar(512) not null, SortFiles bit not null, SFV32Compatibility bit not null,
MD5SumCompatibility bit not null, PromptForFileName bit not null, AutoCloseWhenDoneCreating bit not null, CreateForEachSubDir bit not null)");
				Exec(c, "insert into Creating values('.sfv;.md5;.sha1;.par;.bad;.missing', 1, 1, 1, 1, 1, 1)");
				Exec(c, "create table Comments (ID int primary key identity, WriteComments bit not null, Header nvarchar(2048) not null, Content nvarchar(512) not null, Footer nvarchar(2048) not null)");
				using (SqlCeCommand cmd = new SqlCeCommand("insert into Comments (WriteComments, Header, Content, Footer) values(1, @Header, '{2}', ' footer')", c))
				{
					cmd.Parameters.AddWithValue("@Header", header);
					cmd.ExecuteNonQuery();
				}
				Exec(c, "create table RecentFile (ID int primary key identity, Path nvarchar(1024) not null)");
				foreach (string path in new[] { @"K:\a\one.sfv", @"K:\b\two.sfv", @"K:\c\three.sfv", @"K:\d\four.sfv", @"K:\e\five.sfv" })
					Exec(c, "insert into RecentFile (Path) values('" + path + "')");
			}
		}

		private void CreateIlsfvCache()
		{
			using (SqlCeConnection c = CreateSdf(Path.Combine(_ilsfv, "cache.sdf")))
			{
				Exec(c, "create table CRC32FileCache (ID bigint identity primary key, FileName nvarchar(1024) not null, LastWriteUtc datetime not null, FileLength bigint not null, CRC32 nvarchar(8) not null)");
				Exec(c, "create table MD5FileCache (ID bigint identity primary key, FileName nvarchar(1024) not null, LastWriteUtc datetime not null, FileLength bigint not null, MD5 nvarchar(32) not null)");
				Exec(c, "insert into CRC32FileCache (FileName, LastWriteUtc, FileLength, CRC32) values('K:\\a\\01.flac', '2026-01-02 03:04:05', 1234, 'AF67A815')");
				Exec(c, "insert into CRC32FileCache (FileName, LastWriteUtc, FileLength, CRC32) values('K:\\a\\02.flac', '2026-01-02 03:04:06', 5678, '00000000')");
				Exec(c, "insert into MD5FileCache (FileName, LastWriteUtc, FileLength, MD5) values('K:\\b\\x.bin', '2026-02-03 04:05:06', 10, 'd41d8cd98f00b204e9800998ecf8427e')");
				// no SHA1FileCache table, as in older ilSFV versions
			}
		}

		private SqliteConnection NewTaoSfvDatabase(string name, bool settings)
		{
			SqliteConnection conn = Database.Open(Path.Combine(_dir, name));
			if (settings)
				Database.UpgradeSettings(conn);
			else
				Database.UpgradeCache(conn);
			return conn;
		}

		private static Dictionary<string, object> Row(SqliteConnection conn, string sql)
		{
			using (SqliteCommand cmd = new SqliteCommand(sql, conn))
			using (SqliteDataReader r = cmd.ExecuteReader())
			{
				Assert.True(r.Read());
				Dictionary<string, object> row = new Dictionary<string, object>();
				for (int i = 0; i < r.FieldCount; i++)
					row[r.GetName(i)] = r.GetValue(i);
				return row;
			}
		}

		[Fact]
		public void Import_CopiesSettingsStatisticsRecentFilesAndCache()
		{
			CreateIlsfvSettings();
			CreateIlsfvCache();
			string cachePath = Path.Combine(_dir, "cache.db");
			using (NewTaoSfvDatabase("cache.db", false))
			using (SqliteConnection settings = NewTaoSfvDatabase("settings.db", true))
			{
				Database.Execute(settings, "update General set Theme = 'Dark'");
				Database.Execute(settings, "update UsageStatistics set FilesChecked = 10");

				IlsfvImport.Result result = IlsfvImport.Import(_ilsfv, settings, () => Database.Open(cachePath));

				Assert.True(result.Settings);
				Assert.Equal(4, result.RecentFiles);
				Assert.Equal(3, result.CacheEntries);

				Dictionary<string, object> general = Row(settings, "select * from General");
				Assert.Equal("Deutsch", general["Language"]);
				Assert.Equal(35000L, general["CacheSize"]);
				Assert.Equal(1L, general["AlwaysOnTop"]);
				Assert.Equal(0L, general["ReuseWindow"]);
				Assert.Equal(1L, general["HideGoodFiles"]);
				Assert.Equal("Maximized", general["FormWindowState"]);
				Assert.Equal(1363L, general["WindowLeft"]);
				Assert.Equal("Dark", general["Theme"]);           // kept
				Assert.Equal(0L, general["CheckForUpdates"]);     // not taken from ilSFV

				Dictionary<string, object> checking = Row(settings, "select * from Checking");
				Assert.Equal("Lowercase", checking["Renaming"]);
				Assert.Equal(1L, checking["CreateMissingMarkers"]);
				Assert.Equal(0L, checking["PlaySoundError"]);

				Dictionary<string, object> creating = Row(settings, "select * from Creating");
				Assert.Equal(".sfv;.md5;.sha1;.par;.bad;.missing;.sha256;.sha512;.xxh128;.blake3", creating["ExcludeFilesOfType"]);
				Assert.Equal(1L, creating["CreateForEachSubDir"]);

				Assert.Equal(" My header", Row(settings, "select Header from Comments")["Header"]);
				Assert.Equal(72656L, Row(settings, "select FilesChecked from UsageStatistics")["FilesChecked"]);

				List<string> recent = new List<string>();
				using (SqliteCommand cmd = new SqliteCommand("select Path from RecentFile order by ID desc", settings))
				using (SqliteDataReader r = cmd.ExecuteReader())
					while (r.Read())
						recent.Add(r.GetString(0));
				Assert.Equal(new[] { @"K:\e\five.sfv", @"K:\d\four.sfv", @"K:\c\three.sfv", @"K:\b\two.sfv" }, recent);
			}

			using (SqliteConnection cache = Database.Open(cachePath))
			{
				Dictionary<string, object> crc = Row(cache, "select * from CRC32FileCache order by ID limit 1");
				Assert.Equal(@"K:\a\01.flac", crc["FileName"]);
				Assert.Equal(1234L, crc["FileLength"]);
				Assert.Equal("AF67A815", crc["CRC32"]);
				Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), Convert.ToDateTime(crc["LastWriteUtc"]));
				Assert.Equal(1L, Row(cache, "select count(*) as n from MD5FileCache")["n"]);
			}

			// ilSFV's files are untouched (the import reads copies)
			Assert.True(File.Exists(Path.Combine(_ilsfv, "settings.sdf")));
			Assert.Equal(new[] { "cache.sdf", "settings.sdf" }, Array.ConvertAll(Directory.GetFiles(_ilsfv), Path.GetFileName));
		}

		[Fact]
		public void Import_ReplacesIlsfvsDefaultCommentsHeader()
		{
			CreateIlsfvSettings(" Using ilSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}\r\n http://cdtag.com/ilsfv");
			using (SqliteConnection settings = NewTaoSfvDatabase("settings.db", true))
			{
				IlsfvImport.Import(_ilsfv, settings, () => { throw new InvalidOperationException("cache shouldn't be opened"); });
				Assert.Equal(CommentTemplate.DefaultHeader, Row(settings, "select Header from Comments")["Header"]);
			}
		}

		[Fact]
		public void Import_WithoutCacheFile()
		{
			CreateIlsfvSettings();
			using (SqliteConnection settings = NewTaoSfvDatabase("settings.db", true))
			{
				IlsfvImport.Result result = IlsfvImport.Import(_ilsfv, settings, () => { throw new InvalidOperationException("cache shouldn't be opened"); });
				Assert.True(result.Settings);
				Assert.Equal(0, result.CacheEntries);
			}
		}

		[Fact]
		public void IsAvailable_NeedsSettingsFile()
		{
			Assert.False(IlsfvImport.IsAvailable(_ilsfv));
			CreateIlsfvSettings();
			Assert.True(IlsfvImport.IsAvailable(_ilsfv));
		}
	}
}
