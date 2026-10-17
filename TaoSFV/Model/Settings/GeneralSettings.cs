using System;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Windows.Forms;

namespace TaoSFV.Model.Settings
{
	public class GeneralSettings : BaseSqlSettings
	{
		public string Language { get; set; }
		public int CacheSize { get; set; }
		public bool AlwaysOnTop { get; set; }
		public bool RememberWindowPlacement { get; set; }
		public bool ReuseWindow { get; set; }
		public bool FlashWindowWhenDone { get; set; }
		public bool AutoScrollFileList { get; set; }
		public bool Recursive { get; set; }
		public bool UseLowPriorityOnHide { get; set; }
        public bool IsRecentFilesSaved { get; set; }

		public bool HideGoodFiles { get; set; }
		public bool ShowCommentsPane { get; set; }
		public bool UseCachedResults { get; set; }
		public int WindowHeight { get; set; }
		public int WindowWidth { get; set; }
		public int WindowLeft { get; set; }
		public int WindowTop { get; set; }
		public FormWindowState FormWindowState { get; set; }
		public AppTheme Theme { get; set; }

		public bool CheckForUpdates { get; set; }
		public DateTime? LastUpdateCheck { get; set; }
		public int UpdateCheckFrequency { get; set; }

		public void Save()
		{
			const string updateSql = @"update General set
CheckForUpdates = @CheckForUpdates,
LastUpdateCheck = @LastUpdateCheck,
UpdateCheckFrequency = @UpdateCheckFrequency,
Language = @Language,
CacheSize = @CacheSize,
AlwaysOnTop = @AlwaysOnTop,
RememberWindowPlacement = @RememberWindowPlacement,
ReuseWindow = @ReuseWindow,
FlashWindowWhenDone = @FlashWindowWhenDone,
AutoScrollFileList = @AutoScrollFileList,
Recursive = @Recursive,
HideGoodFiles = @HideGoodFiles,
ShowCommentsPane = @ShowCommentsPane,
UseCachedResults = @UseCachedResults,
WindowHeight = @WindowHeight,
WindowWidth = @WindowWidth,
WindowLeft = @WindowLeft,
WindowTop = @WindowTop,
FormWindowState = @FormWindowState,
UseLowPriorityOnHide = @UseLowPriorityOnHide,
IsRecentFilesSaved = @IsRecentFilesSaved,
Theme = @Theme
";

			using (SqliteCommand cmd = new SqliteCommand(updateSql, Program.GetOpenSettingsConnection()))
			{
				cmd.AddParameter("@CheckForUpdates", CheckForUpdates);
				cmd.AddParameter("@LastUpdateCheck", LastUpdateCheck);
				cmd.AddParameter("@UpdateCheckFrequency", UpdateCheckFrequency);
				cmd.AddParameter("@Language", Language);
				cmd.AddParameter("@CacheSize", CacheSize);
				cmd.AddParameter("@AlwaysOnTop", AlwaysOnTop);
				cmd.AddParameter("@RememberWindowPlacement", RememberWindowPlacement);
				cmd.AddParameter("@ReuseWindow", ReuseWindow);
				cmd.AddParameter("@FlashWindowWhenDone", FlashWindowWhenDone);
				cmd.AddParameter("@AutoScrollFileList", AutoScrollFileList);
				cmd.AddParameter("@Recursive", Recursive);
				cmd.AddParameter("@HideGoodFiles", HideGoodFiles);
				cmd.AddParameter("@ShowCommentsPane", ShowCommentsPane);
				cmd.AddParameter("@UseCachedResults", UseCachedResults);
				cmd.AddParameter("@WindowHeight", WindowHeight);
				cmd.AddParameter("@WindowWidth", WindowWidth);
				cmd.AddParameter("@WindowLeft", WindowLeft);
				cmd.AddParameter("@WindowTop", WindowTop);
				cmd.AddParameter("@FormWindowState", FormWindowState.ToString());
				cmd.AddParameter("@UseLowPriorityOnHide", UseLowPriorityOnHide);
                cmd.AddParameter("@IsRecentFilesSaved", IsRecentFilesSaved);
				cmd.AddParameter("@Theme", Theme.ToString());

				cmd.ExecuteNonQuery();
			}

            if (!IsRecentFilesSaved)
            {
                using (SqliteCommand cmd = new SqliteCommand("delete from RecentFile", Program.GetOpenSettingsConnection()))
                {
                    cmd.ExecuteNonQuery();
                }
            }
		}

		public void Load()
		{
			DataRow dr = GetDataRow("General");

			Language = Convert.ToString(dr["Language"]);
			CacheSize = Convert.ToInt32(dr["CacheSize"]);
			AlwaysOnTop = Convert.ToBoolean(dr["AlwaysOnTop"]);
			RememberWindowPlacement = Convert.ToBoolean(dr["RememberWindowPlacement"]);
			ReuseWindow = Convert.ToBoolean(dr["ReuseWindow"]);
			FlashWindowWhenDone = Convert.ToBoolean(dr["FlashWindowWhenDone"]);
			AutoScrollFileList = Convert.ToBoolean(dr["AutoScrollFileList"]);
			Recursive = Convert.ToBoolean(dr["Recursive"]);
			HideGoodFiles = Convert.ToBoolean(dr["HideGoodFiles"]);
			ShowCommentsPane = Convert.ToBoolean(dr["ShowCommentsPane"]);
			UseCachedResults = Convert.ToBoolean(dr["UseCachedResults"]);
			WindowHeight = Convert.ToInt32(dr["WindowHeight"]);
			WindowWidth = Convert.ToInt32(dr["WindowWidth"]);
			WindowLeft = Convert.ToInt32(dr["WindowLeft"]);
			WindowTop = Convert.ToInt32(dr["WindowTop"]);
			FormWindowState = (FormWindowState)Enum.Parse(typeof(FormWindowState), Convert.ToString(dr["FormWindowState"]));
			CheckForUpdates = Convert.ToBoolean(dr["CheckForUpdates"]);
			UpdateCheckFrequency = Convert.ToInt32(dr["UpdateCheckFrequency"]);
			UseLowPriorityOnHide = Convert.ToBoolean(dr["UseLowPriorityOnHide"]);
            IsRecentFilesSaved = Convert.ToBoolean(dr["IsRecentFilesSaved"]);

			AppTheme theme;
			Theme = Enum.TryParse(Convert.ToString(dr["Theme"]), out theme) ? theme : AppTheme.System;

			object lastUpdateCheck = dr["LastUpdateCheck"];
			LastUpdateCheck = DBNull.Value.Equals(lastUpdateCheck) ? (DateTime?)null : Convert.ToDateTime(lastUpdateCheck);
		}
	}

	public enum AppTheme
	{
		System,
		Light,
		Dark
	}
}
