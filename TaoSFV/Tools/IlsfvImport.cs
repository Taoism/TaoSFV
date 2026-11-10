using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlServerCe;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TaoSFV
{
	/// <summary>
	/// One-time import of ilSFV's SQL Server Compact databases (%LOCALAPPDATA%\ilSFV\settings.sdf and cache.sdf).
	/// The .sdf files are copied to a temporary folder and read from there, so ilSFV's own files are never opened.
	/// </summary>
	internal static class IlsfvImport
	{
		public class Result
		{
			public bool Settings;
			public int RecentFiles;
			public long FilesChecked;
			public int CacheEntries;
		}

		public static string DefaultDataPath
		{
			get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ilSFV"); }
		}

		public static bool IsAvailable(string ilsfvDataPath)
		{
			return File.Exists(Path.Combine(ilsfvDataPath, "settings.sdf"));
		}

		/// <summary>Imports into the open TaoSFV databases; reload Program.Settings afterwards.</summary>
		public static Result Import(string ilsfvDataPath, SqliteConnection settings, Func<SqliteConnection> openCache)
		{
			Result result = new Result();
			string temp = Path.Combine(Path.GetTempPath(), "TaoSFV-import-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(temp);
			try
			{
				string settingsCopy = Path.Combine(temp, "settings.sdf");
				File.Copy(Path.Combine(ilsfvDataPath, "settings.sdf"), settingsCopy);
				using (SqlCeConnection source = Open(settingsCopy, temp))
					ImportSettings(source, settings, result);

				string cacheFile = Path.Combine(ilsfvDataPath, "cache.sdf");
				if (File.Exists(cacheFile))
				{
					string cacheCopy = Path.Combine(temp, "cache.sdf");
					File.Copy(cacheFile, cacheCopy);
					using (SqlCeConnection source = Open(cacheCopy, temp))
					using (SqliteConnection cache = openCache())
						result.CacheEntries = ImportCache(source, cache);
				}
			}
			finally
			{
				try
				{
					Directory.Delete(temp, true);
				}
				catch (IOException)
				{
				}
			}

			return result;
		}

		private static SqlCeConnection Open(string file, string tempPath)
		{
			SqlCeConnection conn = new SqlCeConnection(string.Format("Data Source={0};Mode=Read Only;Temp Path={1}", file, tempPath));
			conn.Open();
			return conn;
		}

		private static Dictionary<string, object> ReadRow(SqlCeConnection conn, string table)
		{
			using (SqlCeCommand cmd = new SqlCeCommand("select * from " + table, conn))
			using (DbDataReader reader = cmd.ExecuteReader())
			{
				if (!reader.Read())
					return null;

				Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < reader.FieldCount; i++)
					row[reader.GetName(i)] = reader.GetValue(i);
				return row;
			}
		}

		private static void ImportSettings(SqlCeConnection source, SqliteConnection target, Result result)
		{
			Database.Execute(target, "begin;");
			try
			{
				// Theme is TaoSFV's own, and the old update server is gone, so neither is taken from ilSFV
				CopyColumns(ReadRow(source, "General"), target, "General", new[]
				{
					"Language", "CacheSize", "AlwaysOnTop", "RememberWindowPlacement", "ReuseWindow", "FlashWindowWhenDone",
					"AutoScrollFileList", "Recursive", "HideGoodFiles", "ShowCommentsPane", "UseCachedResults", "WindowHeight",
					"WindowWidth", "WindowLeft", "WindowTop", "FormWindowState", "UseLowPriorityOnHide", "IsRecentFilesSaved"
				});
				CopyColumns(ReadRow(source, "Checking"), target, "Checking", new[]
				{
					"AutoFindRenames", "UseRecyleBin", "RenameBadFiles", "CreateMissingMarkers", "CleanupBadMissing", "DeleteFailedFiles",
					"AutoCloseWhenDoneChecking", "AutoCloseWhenDoneCheckingOnlyIfAllOK", "Renaming", "AutoVerify", "PlaySoundOK",
					"PlaySoundOK_IfInactive", "PlaySoundError"
				});

				Dictionary<string, object> creating = ReadRow(source, "Creating");
				if (creating != null && creating.ContainsKey("ExcludeFilesOfType"))
				{
					// TaoSFV's newer checksum types are checksum files too
					string exclude = Convert.ToString(creating["ExcludeFilesOfType"]);
					foreach (string ext in new[] { ".sha256", ".sha512" })
					{
						if (exclude.IndexOf(ext, StringComparison.OrdinalIgnoreCase) < 0)
							exclude += ";" + ext;
					}
					creating["ExcludeFilesOfType"] = exclude;
				}
				CopyColumns(creating, target, "Creating", new[]
				{
					"ExcludeFilesOfType", "SortFiles", "SFV32Compatibility", "MD5SumCompatibility", "PromptForFileName",
					"AutoCloseWhenDoneCreating", "CreateForEachSubDir"
				});
				// ilSFV's untouched default header says "Using ilSFV" and links to the defunct cdtag.com: take TaoSFV's
				// instead. A header the user wrote is copied as is (TaoSFV offers to update it afterwards).
				Dictionary<string, object> comments = ReadRow(source, "Comments");
				if (comments != null && comments.ContainsKey("Header") && CommentTemplate.IsOldDefault(Convert.ToString(comments["Header"])))
					comments["Header"] = CommentTemplate.DefaultHeader;
				CopyColumns(comments, target, "Comments", new[] { "WriteComments", "Header", "Content", "Footer" });

				// usage statistics carry on from ilSFV's totals
				Dictionary<string, object> stats = ReadRow(source, "UsageStatistics");
				if (stats != null)
				{
					using (SqliteCommand cmd = new SqliteCommand(@"update UsageStatistics set
FilesChecked = FilesChecked + @FilesChecked, SetsChecked = SetsChecked + @SetsChecked, MBChecked = MBChecked + @MBChecked,
GoodFiles = GoodFiles + @GoodFiles, TimeSpent = TimeSpent + @TimeSpent", target))
					{
						foreach (string column in new[] { "FilesChecked", "SetsChecked", "MBChecked", "GoodFiles", "TimeSpent" })
							cmd.AddParameter("@" + column, stats.ContainsKey(column) ? Convert.ToInt64(stats[column]) : 0L);
						cmd.ExecuteNonQuery();
					}
					result.FilesChecked = stats.ContainsKey("FilesChecked") ? Convert.ToInt64(stats["FilesChecked"]) : 0;
				}

				// the newest four, oldest first, so TaoSFV's ID order matches
				List<string> recent = new List<string>();
				try
				{
					using (SqlCeCommand cmd = new SqlCeCommand("select top (4) Path from RecentFile order by ID desc", source))
					using (DbDataReader reader = cmd.ExecuteReader())
					{
						while (reader.Read())
							recent.Add(reader.GetString(0));
					}
				}
				catch (SqlCeException)
				{
					// very old ilSFV versions had no RecentFile table
				}
				if (recent.Count != 0)
				{
					Database.Execute(target, "delete from RecentFile;");
					recent.Reverse();
					foreach (string path in recent)
					{
						using (SqliteCommand cmd = new SqliteCommand("insert into RecentFile (Path) values(@Path)", target))
						{
							cmd.AddParameter("@Path", path);
							cmd.ExecuteNonQuery();
						}
					}
				}
				result.RecentFiles = recent.Count;

				Database.Execute(target, "commit;");
				result.Settings = true;
			}
			catch
			{
				Database.Execute(target, "rollback;");
				throw;
			}
		}

		private static void CopyColumns(Dictionary<string, object> row, SqliteConnection target, string table, string[] columns)
		{
			if (row == null)
				return;

			List<string> assignments = new List<string>();
			using (SqliteCommand cmd = new SqliteCommand())
			{
				cmd.Connection = target;
				foreach (string column in columns)
				{
					object value;
					if (!row.TryGetValue(column, out value) || value is DBNull)
						continue; // older ilSFV databases lack some columns; keep TaoSFV's value

					assignments.Add(column + " = @" + column);
					cmd.AddParameter("@" + column, value is bool ? ((bool)value ? 1 : 0) : value);
				}

				if (assignments.Count == 0)
					return;

				cmd.CommandText = string.Format("update {0} set {1}", table, string.Join(", ", assignments));
				cmd.ExecuteNonQuery();
			}
		}

		private static int ImportCache(SqlCeConnection source, SqliteConnection target)
		{
			int count = 0;
			string[,] tables = { { "CRC32FileCache", "CRC32" }, { "MD5FileCache", "MD5" }, { "SHA1FileCache", "SHA1" } };

			Database.Execute(target, "begin;");
			try
			{
				for (int t = 0; t < tables.GetLength(0); t++)
				{
					string table = tables[t, 0];
					string column = tables[t, 1];

					SqlCeCommand select = new SqlCeCommand(string.Format("select FileName, LastWriteUtc, FileLength, {0} from {1} order by ID", column, table), source);
					DbDataReader reader;
					try
					{
						reader = select.ExecuteReader();
					}
					catch (SqlCeException)
					{
						select.Dispose();
						continue; // SHA1FileCache only exists in later ilSFV versions
					}

					using (select)
					using (reader)
					using (SqliteCommand insert = new SqliteCommand(string.Format(
						"insert into {0} (FileName, LastWriteUtc, FileLength, {1}) values(@FileName, @LastWriteUtc, @FileLength, @Checksum)", table, column), target))
					{
						SqliteParameter fileName = insert.Parameters.Add("@FileName", SqliteType.Text);
						SqliteParameter lastWrite = insert.Parameters.Add("@LastWriteUtc", SqliteType.Text);
						SqliteParameter length = insert.Parameters.Add("@FileLength", SqliteType.Integer);
						SqliteParameter checksum = insert.Parameters.Add("@Checksum", SqliteType.Text);

						while (reader.Read())
						{
							fileName.Value = reader.GetString(0);
							lastWrite.Value = reader.GetDateTime(1);
							length.Value = reader.GetInt64(2);
							checksum.Value = reader.GetString(3);
							insert.ExecuteNonQuery();
							count++;
						}
					}
				}

				Database.Execute(target, "commit;");
			}
			catch
			{
				Database.Execute(target, "rollback;");
				throw;
			}

			return count;
		}
	}
}
