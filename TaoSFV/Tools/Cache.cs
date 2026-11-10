using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using TaoSFV.Hash;
using TaoSFV.Model.Workset;

namespace TaoSFV
{
	internal static class Cache
	{
		private class PendingEntry
		{
			public ChecksumAlgorithm Algorithm;
			public string FileName;
			public DateTime LastWriteUtc;
			public long Length;
			public string Checksum;
		}

		// one low-priority writer drains this queue, instead of a thread per checked file
		private static readonly BlockingCollection<PendingEntry> _pending = new BlockingCollection<PendingEntry>();
		private static readonly object _writerLock = new object();
		private static Thread _writer;
		private static int _inFlight;

		private static volatile bool _standalone;

		// false when settings aren't loaded (unit tests) or the cache is turned off; always true once opened standalone
		private static bool IsEnabled
		{
			get { return _standalone || (Program.Settings != null && Program.Settings.General.UseCachedResults); }
		}

		/// <summary>Opens a cache file without loading settings (taosfv-cli --use-cache, tests).</summary>
		internal static void OpenStandalone(string cacheFile)
		{
			Program.OpenCacheOnly(cacheFile);
			_standalone = true;
		}

		/// <summary>Stops using a cache opened with OpenStandalone (tests).</summary>
		internal static void CloseStandalone()
		{
			Flush();
			_standalone = false;
		}

		private static void Clear(string tableName)
		{
			string deleteSql = string.Format("delete from {0};", tableName);
			using (SqliteConnection conn = Program.GetOpenCacheConnection())
				Database.Execute(conn, deleteSql);
		}

		private static void Clean(string tableName)
		{
			if (!IsEnabled)
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
			Flush();
			try
			{
				foreach (ChecksumAlgorithm algorithm in ChecksumAlgorithm.All)
					Clean(algorithm.CacheTable);
			}
			catch
			{
			}
		}

		/// <summary>Queues a checksum for the cache; written in the background.</summary>
		public static void Update(ChecksumType type, FileInfo fileInfo, string checksum)
		{
			if (!IsEnabled)
				return;

			EnsureWriter();
			Interlocked.Increment(ref _inFlight);
			_pending.Add(new PendingEntry
			{
				Algorithm = ChecksumAlgorithm.Get(type),
				FileName = fileInfo.FullName,
				LastWriteUtc = fileInfo.LastWriteTimeUtc,
				Length = fileInfo.Length,
				Checksum = checksum
			});
		}

		/// <summary>Waits (briefly) until queued cache writes are on disk.</summary>
		public static void Flush()
		{
			Stopwatch sw = Stopwatch.StartNew();
			while (Volatile.Read(ref _inFlight) > 0 && sw.Elapsed < TimeSpan.FromSeconds(10))
				Thread.Sleep(10);
		}

		private static void EnsureWriter()
		{
			lock (_writerLock)
			{
				if (_writer != null)
					return;

				_writer = new Thread(WriterLoop);
				_writer.IsBackground = true;
				_writer.Priority = ThreadPriority.Lowest;
				_writer.Name = "Cache writer";
				_writer.Start();
			}
		}

		private const int MaxBatch = 500;

		private static void WriterLoop()
		{
			foreach (PendingEntry first in _pending.GetConsumingEnumerable())
			{
				// whatever has queued up goes in one transaction on one connection; a connection and commit per row
				// made checking thousands of small files wait seconds for the cache
				List<PendingEntry> batch = new List<PendingEntry> { first };
				PendingEntry next;
				while (batch.Count < MaxBatch && _pending.TryTake(out next))
					batch.Add(next);

				try
				{
					using (SqliteConnection conn = Program.GetOpenCacheConnection())
					{
						Database.Execute(conn, "begin;");
						try
						{
							foreach (PendingEntry entry in batch)
							{
								string sql = string.Format("insert into {0} (FileName, LastWriteUtc, FileLength, {1}) values(@FileName, @LastWriteUtc, @FileLength, @Checksum);",
									entry.Algorithm.CacheTable, entry.Algorithm.CacheColumn);
								using (SqliteCommand cmd = new SqliteCommand(sql, conn))
								{
									cmd.Parameters.AddWithValue("@FileName", entry.FileName);
									cmd.Parameters.AddWithValue("@LastWriteUtc", entry.LastWriteUtc);
									cmd.Parameters.AddWithValue("@FileLength", entry.Length);
									cmd.Parameters.AddWithValue("@Checksum", entry.Checksum);
									cmd.ExecuteNonQuery();
								}
							}
							Database.Execute(conn, "commit;");
						}
						catch
						{
							Database.Execute(conn, "rollback;");
							throw;
						}
					}
				}
				catch (Exception ex)
				{
					Trace.WriteLine(ex);
				}
				finally
				{
					Interlocked.Add(ref _inFlight, -batch.Count);
				}
			}
		}

		public static List<ChecksumFile> GetCache(ChecksumType checksumType, string baseDirectory)
		{
			if (!IsEnabled)
				return new List<ChecksumFile>();

			ChecksumAlgorithm algorithm = ChecksumAlgorithm.Get(checksumType);

			// names starting with the folder, ignoring ASCII case (as the LIKE this replaces did), as a range the NOCASE
			// index can use: [folder, folder with its last character + 1)
			string from, to;
			GetPrefixRange(baseDirectory, out from, out to);
			string getCacheSql = string.Format("select FileName, LastWriteUtc, FileLength, {1} as Checksum from {0} " +
				"where FileName >= @From collate nocase and FileName < @To collate nocase", algorithm.CacheTable, algorithm.CacheColumn);

			DataTable dt;

			using (SqliteConnection conn = Program.GetOpenCacheConnection())
			using (SqliteCommand cmd = new SqliteCommand(getCacheSql, conn))
			{
				cmd.Parameters.AddWithValue("@From", from);
				cmd.Parameters.AddWithValue("@To", to);
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

		/// <summary>
		/// Bounds for "starts with prefix" under SQLite's NOCASE collation (which only folds ASCII A-Z): the prefix
		/// lowercased, and the same with its last character incremented.
		/// </summary>
		internal static void GetPrefixRange(string prefix, out string from, out string to)
		{
			char[] chars = prefix.ToCharArray();
			for (int i = 0; i < chars.Length; i++)
			{
				if (chars[i] >= 'A' && chars[i] <= 'Z')
					chars[i] = (char)(chars[i] + 32);
			}
			from = new string(chars);

			// an empty prefix, or one ending in U+FFFF, can't be incremented: everything from `from` on
			int last = chars.Length - 1;
			if (last < 0 || chars[last] == char.MaxValue)
			{
				to = new string(char.MaxValue, 2);
				return;
			}
			chars[last]++;
			to = new string(chars);
		}

		internal static void Clear()
		{
			Flush();
			foreach (ChecksumAlgorithm algorithm in ChecksumAlgorithm.All)
				Clear(algorithm.CacheTable);
		}
	}
}
