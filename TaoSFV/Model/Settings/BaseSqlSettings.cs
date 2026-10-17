using System.Data;

namespace TaoSFV.Model.Settings
{
	public abstract class BaseSqlSettings
	{
		public DataRow GetDataRow(string tableName)
		{
			return Database.GetTable(Program.GetOpenSettingsConnection(), "select * from " + tableName).Rows[0];
		}
	}
}
