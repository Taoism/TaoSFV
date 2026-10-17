using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace TaoSFV
{
	/// <summary>
	/// SQLite storage for settings and the checksum cache (replaces the SQL Server Compact .sdf files).
	/// </summary>
	internal static class Database
	{
		public const int SettingsVersion = 1;
		public const int CacheVersion = 1;

		public static SqliteConnection Open(string fileName)
		{
			SqliteConnectionStringBuilder csb = new SqliteConnectionStringBuilder();
			csb.DataSource = fileName;
			csb.Mode = SqliteOpenMode.ReadWriteCreate;
			csb.DefaultTimeout = 30;

			SqliteConnection conn = new SqliteConnection(csb.ToString());
			conn.Open();
			Execute(conn, "pragma journal_mode = wal;");
			return conn;
		}

		public static int Execute(SqliteConnection conn, string sql)
		{
			using (SqliteCommand cmd = new SqliteCommand(sql, conn))
				return cmd.ExecuteNonQuery();
		}

		/// <summary>Adds a parameter, mapping null to DBNull (Microsoft.Data.Sqlite rejects null values).</summary>
		public static void AddParameter(this SqliteCommand cmd, string name, object value)
		{
			cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
		}

		public static DataTable GetTable(SqliteCommand cmd)
		{
			DataTable table = new DataTable();
			using (SqliteDataReader reader = cmd.ExecuteReader())
			{
				for (int i = 0; i < reader.FieldCount; i++)
					table.Columns.Add(reader.GetName(i), typeof(object));

				object[] values = new object[reader.FieldCount];
				while (reader.Read())
				{
					reader.GetValues(values);
					table.Rows.Add(values);
				}
			}
			return table;
		}

		public static DataTable GetTable(SqliteConnection conn, string sql)
		{
			using (SqliteCommand cmd = new SqliteCommand(sql, conn))
				return GetTable(cmd);
		}

		private static int GetVersion(SqliteConnection conn)
		{
			DataTable exists = GetTable(conn, "select name from sqlite_master where type = 'table' and name = 'DatabaseSetting';");
			if (exists.Rows.Count == 0)
				return 0;

			DataTable dt = GetTable(conn, "select Version from DatabaseSetting;");
			return dt.Rows.Count == 0 ? 0 : Convert.ToInt32(dt.Rows[0][0]);
		}

		private static void SetVersion(SqliteConnection conn, int version)
		{
			Execute(conn, "delete from DatabaseSetting;");
			using (SqliteCommand cmd = new SqliteCommand("insert into DatabaseSetting (Version) values(@Version);", conn))
			{
				cmd.AddParameter("@Version", version);
				cmd.ExecuteNonQuery();
			}
		}

		public static void UpgradeSettings(SqliteConnection conn)
		{
			int version = GetVersion(conn);
			if (version >= SettingsVersion)
				return;

			// raw begin/commit: Microsoft.Data.Sqlite would otherwise require cmd.Transaction on every command
			Execute(conn, "begin;");
			try
			{
				if (version < 1)
				{
					Execute(conn, @"
create table if not exists DatabaseSetting (Version integer not null primary key);

create table UsageStatistics
(
	FilesChecked integer not null,
	SetsChecked integer not null,
	MBChecked integer not null,
	GoodFiles integer not null,
	TimeSpent integer not null
);
insert into UsageStatistics (FilesChecked, SetsChecked, MBChecked, GoodFiles, TimeSpent) values(0, 0, 0, 0, 0);

create table General
(
	Language text not null,
	CacheSize integer not null,
	AlwaysOnTop integer not null,
	RememberWindowPlacement integer not null,
	ReuseWindow integer not null,
	FlashWindowWhenDone integer not null,
	AutoScrollFileList integer not null,
	Recursive integer not null,
	HideGoodFiles integer not null,
	ShowCommentsPane integer not null,
	UseCachedResults integer not null,
	WindowHeight integer not null,
	WindowWidth integer not null,
	WindowLeft integer not null,
	WindowTop integer not null,
	FormWindowState text not null,
	CheckForUpdates integer not null default 0,
	LastUpdateCheck text null,
	UpdateCheckFrequency integer not null default 7,
	UseLowPriorityOnHide integer not null default 1,
	IsRecentFilesSaved integer not null default 1,
	Theme text not null default 'System'
);
insert into General (Language, CacheSize, AlwaysOnTop, RememberWindowPlacement, ReuseWindow, FlashWindowWhenDone, AutoScrollFileList,
	Recursive, HideGoodFiles, ShowCommentsPane, UseCachedResults, WindowHeight, WindowWidth, WindowLeft, WindowTop, FormWindowState)
values('English', 5000, 0, 1, 1, 1, 1, 1, 0, 1, 1, 770, 1200, 0, 0, 'Normal');

create table Checking
(
	AutoFindRenames integer not null,
	UseRecyleBin integer not null,
	RenameBadFiles integer not null,
	CreateMissingMarkers integer not null,
	CleanupBadMissing integer not null,
	DeleteFailedFiles integer not null,
	AutoCloseWhenDoneChecking integer not null,
	AutoCloseWhenDoneCheckingOnlyIfAllOK integer not null,
	Renaming text not null,
	AutoVerify integer not null default 1,
	PlaySoundOK integer not null default 1,
	PlaySoundOK_IfInactive integer not null default 1,
	PlaySoundError integer not null default 1
);
insert into Checking (AutoFindRenames, UseRecyleBin, RenameBadFiles, CreateMissingMarkers, CleanupBadMissing, DeleteFailedFiles,
	AutoCloseWhenDoneChecking, AutoCloseWhenDoneCheckingOnlyIfAllOK, Renaming)
values(1, 1, 1, 0, 1, 0, 0, 1, 'None');

create table Creating
(
	ExcludeFilesOfType text not null,
	SortFiles integer not null,
	SFV32Compatibility integer not null,
	MD5SumCompatibility integer not null,
	PromptForFileName integer not null,
	AutoCloseWhenDoneCreating integer not null,
	CreateForEachSubDir integer not null default 0
);
insert into Creating (ExcludeFilesOfType, SortFiles, SFV32Compatibility, MD5SumCompatibility, PromptForFileName, AutoCloseWhenDoneCreating)
values('.sfv;.par;.md5;.nfo;.m3u;.pls;.jpg;.jpeg;.gif;.avi;.divx;.mpg;.mpeg;.ini;.bad;.missing;.sha1', 1, 0, 1, 0, 0);

create table Comments
(
	ID integer primary key autoincrement,
	WriteComments integer not null,
	Header text not null,
	Content text not null,
	Footer text not null
);

create table RecentFile
(
	ID integer primary key autoincrement,
	Path text not null
);
");
					using (SqliteCommand cmd = new SqliteCommand("insert into Comments (WriteComments, Header, Content, Footer) values(1, @Header, @Content, '');", conn))
					{
						cmd.AddParameter("@Header", " Using TaoSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}" + Environment.NewLine + " https://github.com/judwhite/ilSFV");
						cmd.AddParameter("@Content", "{0,13:#,0}  {1:HH:mm.ss} {1:yyyy-MM-dd} {2}");
						cmd.ExecuteNonQuery();
					}
				}

				SetVersion(conn, SettingsVersion);
				Execute(conn, "commit;");
			}
			catch
			{
				Execute(conn, "rollback;");
				throw;
			}
		}

		public static void UpgradeCache(SqliteConnection conn)
		{
			int version = GetVersion(conn);
			if (version >= CacheVersion)
				return;

			// raw begin/commit: Microsoft.Data.Sqlite would otherwise require cmd.Transaction on every command
			Execute(conn, "begin;");
			try
			{
				if (version < 1)
				{
					Execute(conn, @"
create table if not exists DatabaseSetting (Version integer not null primary key);

create table CRC32FileCache (ID integer primary key autoincrement, FileName text not null, LastWriteUtc text not null, FileLength integer not null, CRC32 text not null);
create index IX_CRC32FileCache_FileName on CRC32FileCache (FileName);

create table MD5FileCache (ID integer primary key autoincrement, FileName text not null, LastWriteUtc text not null, FileLength integer not null, MD5 text not null);
create index IX_MD5FileCache_FileName on MD5FileCache (FileName);

create table SHA1FileCache (ID integer primary key autoincrement, FileName text not null, LastWriteUtc text not null, FileLength integer not null, SHA1 text not null);
create index IX_SHA1FileCache_FileName on SHA1FileCache (FileName);
");
				}

				SetVersion(conn, CacheVersion);
				Execute(conn, "commit;");
			}
			catch
			{
				Execute(conn, "rollback;");
				throw;
			}
		}
	}
}
