using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace ilSFV.Model.Settings
{
	public class CommentSettings : BaseSqlSettings
	{
		public bool WriteComments { get; set; }
		public string Header { get; set; }
		public string Content { get; set; }
		public string Footer { get; set; }

		public void Save()
		{
			const string updateSql = @"update Comments set
WriteComments = @WriteComments,
Header = @Header,
Content = @Content,
Footer = @Footer
";

			using (SqliteCommand cmd = new SqliteCommand(updateSql, Program.GetOpenSettingsConnection()))
			{
				cmd.AddParameter("@WriteComments", WriteComments);
				cmd.AddParameter("@Header", Header);
				cmd.AddParameter("@Content", Content);
				cmd.AddParameter("@Footer", Footer);

				cmd.ExecuteNonQuery();
			}
		}

		public void Load()
		{
			DataRow dr = GetDataRow("Comments");

			WriteComments = Convert.ToBoolean(dr["WriteComments"]);
			Header = Convert.ToString(dr["Header"]);
			Content = Convert.ToString(dr["Content"]);
			Footer = Convert.ToString(dr["Footer"]);
		}
	}
}
