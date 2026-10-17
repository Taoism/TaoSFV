using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace ilSFV.Model.Settings
{
	public class CheckSettings : BaseSqlSettings
	{
		public bool AutoFindRenames { get; set; }
		public bool UseRecyleBin { get; set; }
		public bool RenameBadFiles { get; set; }
		public bool CreateMissingMarkers { get; set; }
		public bool CleanupBadMissing { get; set; }
		public bool DeleteFailedFiles { get; set; }
		public bool AutoCloseWhenDoneChecking { get; set; }
		public bool AutoCloseWhenDoneCheckingOnlyIfAllOK { get; set; }
		public CheckRenaming Renaming { get; set; }
		public bool AutoVerify { get; set; }
		public bool PlaySoundOK { get; set; }
		public bool PlaySoundOK_OnlyIfInactive { get; set; }
		public bool PlaySoundError { get; set; }

		public void Save()
		{
			const string updateSql = @"update Checking set
AutoFindRenames = @AutoFindRenames,
AutoVerify = @AutoVerify,
UseRecyleBin = @UseRecyleBin,
RenameBadFiles = @RenameBadFiles,
CreateMissingMarkers = @CreateMissingMarkers,
CleanupBadMissing = @CleanupBadMissing,
DeleteFailedFiles = @DeleteFailedFiles,
AutoCloseWhenDoneChecking = @AutoCloseWhenDoneChecking,
AutoCloseWhenDoneCheckingOnlyIfAllOK = @AutoCloseWhenDoneCheckingOnlyIfAllOK,
Renaming = @Renaming,
PlaySoundOK = @PlaySoundOK,
PlaySoundOK_IfInactive = @PlaySoundOK_IfInactive,
PlaySoundError = @PlaySoundError
";

			using (SqliteCommand cmd = new SqliteCommand(updateSql, Program.GetOpenSettingsConnection()))
			{
				cmd.AddParameter("@AutoFindRenames", AutoFindRenames);
				cmd.AddParameter("@AutoVerify", AutoVerify);
				cmd.AddParameter("@UseRecyleBin", UseRecyleBin);
				cmd.AddParameter("@RenameBadFiles", RenameBadFiles);
				cmd.AddParameter("@CreateMissingMarkers", CreateMissingMarkers);
				cmd.AddParameter("@CleanupBadMissing", CleanupBadMissing);
				cmd.AddParameter("@DeleteFailedFiles", DeleteFailedFiles);
				cmd.AddParameter("@AutoCloseWhenDoneChecking", AutoCloseWhenDoneChecking);
				cmd.AddParameter("@AutoCloseWhenDoneCheckingOnlyIfAllOK", AutoCloseWhenDoneCheckingOnlyIfAllOK);
				cmd.AddParameter("@Renaming", Renaming.ToString());
				cmd.AddParameter("@PlaySoundOK", PlaySoundOK);
				cmd.AddParameter("@PlaySoundOK_IfInactive", PlaySoundOK_OnlyIfInactive);
				cmd.AddParameter("@PlaySoundError", PlaySoundError);

				cmd.ExecuteNonQuery();
			}
		}

		public void Load()
		{
			DataRow dr = GetDataRow("Checking");

			AutoFindRenames = Convert.ToBoolean(dr["AutoFindRenames"]);
			AutoVerify = Convert.ToBoolean(dr["AutoVerify"]);
			UseRecyleBin = Convert.ToBoolean(dr["UseRecyleBin"]);
			RenameBadFiles = Convert.ToBoolean(dr["RenameBadFiles"]);
			CreateMissingMarkers = Convert.ToBoolean(dr["CreateMissingMarkers"]);
			CleanupBadMissing = Convert.ToBoolean(dr["CleanupBadMissing"]);
			DeleteFailedFiles = Convert.ToBoolean(dr["DeleteFailedFiles"]);
			AutoCloseWhenDoneChecking = Convert.ToBoolean(dr["AutoCloseWhenDoneChecking"]);
			AutoCloseWhenDoneCheckingOnlyIfAllOK = Convert.ToBoolean(dr["AutoCloseWhenDoneCheckingOnlyIfAllOK"]);
			Renaming = (CheckRenaming)Enum.Parse(typeof(CheckRenaming), Convert.ToString(dr["Renaming"]));
			PlaySoundOK = Convert.ToBoolean(dr["PlaySoundOK"]);
			PlaySoundOK_OnlyIfInactive = Convert.ToBoolean(dr["PlaySoundOK_IfInactive"]);
			PlaySoundError = Convert.ToBoolean(dr["PlaySoundError"]);
		}
	}

	public enum CheckRenaming
	{
		None,
		MatchSet,
		Lowercase
	}
}
