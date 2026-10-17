using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace ilSFV.Model.Settings
{
	public class CreateSettings : BaseSqlSettings
	{
		public string ExcludeFilesOfType { get; set; }
		public bool SortFiles { get; set; }
		public bool SFV32Compatibility { get; set; }
		public bool MD5SumCompatibility { get; set; }
		public bool PromptForFileName { get; set; }
		public bool AutoCloseWhenDoneCreating { get; set; }
		public bool CreateForEachSubDir { get; set; }

		internal void Save()
		{
			const string updateSql = @"update Creating set
ExcludeFilesOfType = @ExcludeFilesOfType,
SortFiles = @SortFiles,
SFV32Compatibility = @SFV32Compatibility,
MD5SumCompatibility = @MD5SumCompatibility,
PromptForFileName = @PromptForFileName,
AutoCloseWhenDoneCreating = @AutoCloseWhenDoneCreating,
CreateForEachSubDir = @CreateForEachSubDir
";

			using (SqliteCommand cmd = new SqliteCommand(updateSql, Program.GetOpenSettingsConnection()))
			{
				cmd.AddParameter("@ExcludeFilesOfType", ExcludeFilesOfType);
				cmd.AddParameter("@SortFiles", SortFiles);
				cmd.AddParameter("@SFV32Compatibility", SFV32Compatibility);
				cmd.AddParameter("@MD5SumCompatibility", MD5SumCompatibility);
				cmd.AddParameter("@PromptForFileName", PromptForFileName);
				cmd.AddParameter("@AutoCloseWhenDoneCreating", AutoCloseWhenDoneCreating);
				cmd.AddParameter("@CreateForEachSubDir", CreateForEachSubDir);

				cmd.ExecuteNonQuery();
			}
		}

		internal void Load()
		{
			DataRow dr = GetDataRow("Creating");

			ExcludeFilesOfType = Convert.ToString(dr["ExcludeFilesOfType"]);
			SortFiles = Convert.ToBoolean(dr["SortFiles"]);
			SFV32Compatibility = Convert.ToBoolean(dr["SFV32Compatibility"]);
			MD5SumCompatibility = Convert.ToBoolean(dr["MD5SumCompatibility"]);
			PromptForFileName = Convert.ToBoolean(dr["PromptForFileName"]);
			AutoCloseWhenDoneCreating = Convert.ToBoolean(dr["AutoCloseWhenDoneCreating"]);
			CreateForEachSubDir = Convert.ToBoolean(dr["CreateForEachSubDir"]);
		}
	}
}
