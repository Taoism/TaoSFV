using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Threading;
using ilSFV.Model.Workset;

namespace ilSFV
{
	internal static class Cache
	{
		private class UpdateSQLData
		{
			public FileInfo FileInfo { get; set; }
			public string Checksum { get; set; }
		}

		private static void Clear(string tableName)
		{
			string deleteSql = string.Format("delete from {0};", tableName);
			using (SqliteConnection conn = Program.GetOpenCacheConnection())
				Database.Execute(conn, deleteSql);
		}

		private static void Clean(string tableName)
		{
			if (!Program.Settings.General.UseCachedResults)
				return;

			string getMaxIDSql = string.Format("select max(ID) from {0};", tableName);
			long? maxID = null;

			using (SqliteConnection conn = Program.GetOpenCacheConnection())
			{
				DataTable dt = Database.GetTable(conn, getMaxIDSql);
				if (dt.Rows.Count == 1)
				{
					if (!DBNull.Value.Equals(dt.Rows[0][0]))
						maxID = Convert.ToInt64(dt.Rows[0][0]);
				}

				if (maxID != null)
				{
					maxID -= Program.Settings.General.CacheSize;
					if (maxID > 0)
					{
						string deleteSql = string.Format("delete from {0} where ID <= {1};", tableName, maxID);
						Database.Execute(conn, deleteSql);
					}
				}
			}
		}

		public static void Clean()
		{
			try
			{
				Clean("CRC32FileCache");
				Clean("MD5FileCache");
				Clean("SHA1FileCache");
			}
			catch
			{
			}
		}

		public static void CleanAsync()
		{
			Thread t = new Thread(Clean);
			t.Priority = Thread.CurrentThread.Priority;
			t.Start();
		}

		private static void UpdateSFVCache(object sqlData)
		{
			try
			{
				FileInfo fileInfo = ((UpdateSQLData)sqlData).FileInfo;
				string crc32 = ((UpdateSQLData)sqlData).Checksum;

				const string updateSql = "insert into CRC32FileCache (FileName, LastWriteUtc, FileLength, CRC32) values(@FileName, @LastWriteUtc, @FileLength, @CRC32);";
				using (SqliteConnection conn = Program.GetOpenCacheConnection())
				using (SqliteCommand cmd = new SqliteCommand(updateSql, conn))
				{
					cmd.Parameters.AddWithValue("@FileName", fileInfo.FullName);
					cmd.Parameters.AddWithValue("@LastWriteUtc", fileInfo.LastWriteTimeUtc);
					cmd.Parameters.AddWithValue("@FileLength", fileInfo.Length);
					cmd.Parameters.AddWithValue("@CRC32", crc32);
					cmd.ExecuteNonQuery();
				}
			}
			catch
			{
			}
		}

		private static void UpdateMD5Cache(object sqlData)
		{
			try
			{
				FileInfo fileInfo = ((UpdateSQLData)sqlData).FileInfo;
				string md5 = ((UpdateSQLData)sqlData).Checksum;

				const string updateSql = "insert into MD5FileCache (FileName, LastWriteUtc, FileLength, MD5) values(@FileName, @LastWriteUtc, @FileLength, @MD5);";
				using (SqliteConnection conn = Program.GetOpenCacheConnection())
				using (SqliteCommand cmd = new SqliteCommand(updateSql, conn))
				{
					cmd.Parameters.AddWithValue("@FileName", fileInfo.FullName);
					cmd.Parameters.AddWithValue("@LastWriteUtc", fileInfo.LastWriteTimeUtc);
					cmd.Parameters.AddWithValue("@FileLength", fileInfo.Length);
					cmd.Parameters.AddWithValue("@MD5", md5);
					cmd.ExecuteNonQuery();
				}
			}
			catch
			{
			}
		}

		private static void UpdateSHA1Cache(object sqlData)
		{
			try
			{
				FileInfo fileInfo = ((UpdateSQLData)sqlData).FileInfo;
				string sha1 = ((UpdateSQLData)sqlData).Checksum;

				const string updateSql = "insert into SHA1FileCache (FileName, LastWriteUtc, FileLength, SHA1) values(@FileName, @LastWriteUtc, @FileLength, @SHA1);";
				using (SqliteConnection conn = Program.GetOpenCacheConnection())
				using (SqliteCommand cmd = new SqliteCommand(updateSql, conn))
				{
					cmd.Parameters.AddWithValue("@FileName", fileInfo.FullName);
					cmd.Parameters.AddWithValue("@LastWriteUtc", fileInfo.LastWriteTimeUtc);
					cmd.Parameters.AddWithValue("@FileLength", fileInfo.Length);
					cmd.Parameters.AddWithValue("@SHA1", sha1);
					cmd.ExecuteNonQuery();
				}
			}
			catch
			{
			}
		}

		public static void UpdateSFVCache(FileInfo fileInfo, string crc32)
		{
			if (!Program.Settings.General.UseCachedResults)
				return;

			Thread t = new Thread(UpdateSFVCache);
			t.Priority = ThreadPriority.Lowest;
			t.Start(new UpdateSQLData { FileInfo = fileInfo, Checksum = crc32 });
		}

		public static void UpdateSHA1Cache(FileInfo fileInfo, string crc32)
		{
			if (!Program.Settings.General.UseCachedResults)
				return;

			Thread t = new Thread(UpdateSHA1Cache);
			t.Priority = ThreadPriority.Lowest;
			t.Start(new UpdateSQLData { FileInfo = fileInfo, Checksum = crc32 });
		}

		public static void UpdateMD5Cache(FileInfo fileInfo, string md5)
		{
			if (!Program.Settings.General.UseCachedResults)
				return;

			Thread t = new Thread(UpdateMD5Cache);
			t.Priority = ThreadPriority.Lowest;
			t.Start(new UpdateSQLData { FileInfo = fileInfo, Checksum = md5 });
		}

		public static List<ChecksumFile> GetCache(ChecksumType checksumType, string baseDirectory)
		{
			if (!Program.Settings.General.UseCachedResults)
				return new List<ChecksumFile>();

			string getCacheSql;
			if (checksumType == ChecksumType.MD5)
				getCacheSql = "select FileName, LastWriteUtc, FileLength, MD5 as Checksum from MD5FileCache where FileName like @Directory || '%'";
			else if (checksumType == ChecksumType.SFV)
				getCacheSql = "select FileName, LastWriteUtc, FileLength, CRC32 as Checksum from CRC32FileCache where FileName like @Directory || '%'";
			else if (checksumType == ChecksumType.SHA1)
				getCacheSql = "select FileName, LastWriteUtc, FileLength, SHA1 as Checksum from SHA1FileCache where FileName like @Directory || '%'";
			else
				throw new Exception(string.Format("{0} not implemented", checksumType));

			DataTable dt;

			using (SqliteConnection conn = Program.GetOpenCacheConnection())
			using (SqliteCommand cmd = new SqliteCommand(getCacheSql, conn))
			{
				cmd.Parameters.AddWithValue("@Directory", baseDirectory);
				dt = Database.GetTable(cmd);
			}

			List<ChecksumFile> files = new List<ChecksumFile>();
			foreach (DataRow dr in dt.Rows)
			{
				ChecksumFile file = new ChecksumFile(null);

				file.FileName = Convert.ToString(dr["FileName"]);
				file.CacheLastWriteUtc = Convert.ToDateTime(dr["LastWriteUtc"]);
				file.CacheLength = Convert.ToInt64(dr["FileLength"]);
				file.OriginalChecksum = Convert.ToString(dr["Checksum"]);

				files.Add(file);
			}

			return files;
		}

		internal static void Clear()
		{
			Clear("CRC32FileCache");
			Clear("MD5FileCache");
			Clear("SHA1FileCache");
		}
	}
}
