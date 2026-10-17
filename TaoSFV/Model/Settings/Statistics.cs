using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace TaoSFV.Model.Settings
{
	public class Statistics : BaseSqlSettings
	{
		public long FilesChecked { get; set; }
		public long SetsChecked { get; set; }
		public long MBChecked { get; set; }
		public long GoodFiles { get; set; }
		public TimeSpan TimeSpent { get; set; }

		internal void Clear()
		{
			const string updateSql = @"update UsageStatistics set
FilesChecked = 0,
SetsChecked = 0,
MBChecked = 0,
GoodFiles = 0,
TimeSpent = 0";

			using (SqliteCommand cmd = new SqliteCommand(updateSql, Program.GetOpenSettingsConnection()))
			{
				cmd.ExecuteNonQuery();
			}

			Load();
		}

		internal void Load()
		{
			DataRow dr = GetDataRow("UsageStatistics");

			FilesChecked = Convert.ToInt64(dr["FilesChecked"]);
			SetsChecked = Convert.ToInt64(dr["SetsChecked"]);
			MBChecked = Convert.ToInt64(dr["MBChecked"]);
			GoodFiles = Convert.ToInt64(dr["GoodFiles"]);
			TimeSpent = TimeSpan.FromSeconds(Convert.ToInt64(dr["TimeSpent"]));
		}

		public void AddStats(long filesChecked, long setsChecked, long mbChecked, long goodFiles, TimeSpan timeSpan)
		{
			const string updateSql = @"update UsageStatistics set
FilesChecked = FilesChecked + @FilesChecked,
SetsChecked = SetsChecked + @SetsChecked,
MBChecked = MBChecked + @MBChecked,
GoodFiles = GoodFiles + @GoodFiles,
TimeSpent = TimeSpent + @TimeSpent";

			using (SqliteCommand cmd = new SqliteCommand(updateSql, Program.GetOpenSettingsConnection()))
			{
				cmd.AddParameter("@FilesChecked", filesChecked);
				cmd.AddParameter("@SetsChecked", setsChecked);
				cmd.AddParameter("@MBChecked", mbChecked);
				cmd.AddParameter("@GoodFiles", goodFiles);
				cmd.AddParameter("@TimeSpent", (long)timeSpan.TotalSeconds);

				cmd.ExecuteNonQuery();
			}
		}
	}
}
