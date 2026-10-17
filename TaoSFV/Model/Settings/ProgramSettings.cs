using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Linq;

namespace TaoSFV.Model.Settings
{
	public class ProgramSettings
	{
		public readonly GeneralSettings General = new GeneralSettings();
		public readonly CheckSettings Check = new CheckSettings();
		public readonly CreateSettings Create = new CreateSettings();
		public readonly Statistics Statistics = new Statistics();
		public readonly CommentSettings Comments = new CommentSettings();

		private List<string> _recentFiles;
		public IEnumerable<string> GetRecentFiles()
		{
            if (!General.IsRecentFilesSaved)
            {
                _recentFiles = null;
                return new List<string>();
            }

			if (_recentFiles == null)
			{
				_recentFiles = new List<string>();
				DataTable dt = Database.GetTable(Program.GetOpenSettingsConnection(), "select Path from RecentFile order by ID desc");
				foreach (DataRow dr in dt.Rows)
				{
					_recentFiles.Add(Convert.ToString(dr[0]));
					if (_recentFiles.Count >= 4)
						break;
				}
			}

			return _recentFiles;
		}

		public void AddRecentFile(string path)
		{
			GetRecentFiles();

            if (!General.IsRecentFilesSaved)
                return;

			List<string> found = _recentFiles.Where(p => p.ToLower() == path.ToLower()).ToList();
			if (found.Count >= 1)
			{
				_recentFiles.Remove(found[0]);
			}
			else
			{
				while (_recentFiles.Count >= 4)
					_recentFiles.RemoveAt(3);
			}

			_recentFiles.Insert(0, path);
		}

		public void Save()
		{
			if (_recentFiles == null)
				return;

			using (SqliteCommand cmd = new SqliteCommand("delete from RecentFile", Program.GetOpenSettingsConnection()))
			{
				cmd.ExecuteNonQuery();
			}

			List<string> list = new List<string>(_recentFiles);
			list.Reverse();

			foreach (string path in list)
			{
				using (SqliteCommand cmd = new SqliteCommand("insert into RecentFile (Path) values(@Path)", Program.GetOpenSettingsConnection()))
				{
					cmd.AddParameter("@Path", path);
					cmd.ExecuteNonQuery();
				}
			}
		}

		public ProgramSettings()
		{
			Database.UpgradeSettings(Program.GetOpenSettingsConnection());
			using (SqliteConnection conn = Program.GetOpenCacheConnection())
				Database.UpgradeCache(conn);
		}
	}
}
