using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaoSFV
{
	/// <summary>
	/// SplitContainer whose layout survives a failed splitter repaint.
	/// </summary>
	/// <remarks>
	/// During layout SplitContainer repaints its splitter through CreateGraphics(). While the window is first being
	/// shown that GDI+ call can fail with "A generic error occurred in GDI+", which surfaced as an error dialog and an
	/// invisible main window on some launches. The panels are already sized by then, and the splitter is drawn
	/// again on the next WM_PAINT, so the failure is safe to ignore.
	/// </remarks>
	internal class SafeSplitContainer : SplitContainer
	{
		protected override void OnLayout(LayoutEventArgs e)
		{
			try
			{
				base.OnLayout(e);
			}
			catch (ExternalException ex)
			{
				Trace.WriteLine(ex);
				Invalidate();
			}
		}
	}
}
