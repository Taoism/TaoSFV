using System.Windows.Forms;

namespace TaoSFV
{
	/// <summary>
	/// The file list: a double-buffered ListView, so the virtual list's row redraws during a run don't flicker.
	/// </summary>
	internal class FastListView : ListView
	{
		public FastListView()
		{
			DoubleBuffered = true;
		}
	}
}
