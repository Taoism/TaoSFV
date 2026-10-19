using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaoSFV
{
	/// <summary>
	/// Keeps fonts set explicitly in the designer the right size on a monitor with another DPI. WinForms rescales
	/// inherited fonts when a per-monitor-aware window starts on or moves to such a monitor, but not explicit ones.
	/// </summary>
	internal static class DpiFonts
	{
		/// <param name="scaled">Called after the fonts change (e.g. to re-measure text); may be null.</param>
		public static void Track(Form form, Action scaled, params Control[] controls)
		{
			// the DPI the fonts are sized for: the system DPI until the window is placed on a monitor
			int fontDpi = form.DeviceDpi;
			EventHandler rescale = (sender, e) =>
			{
				if (form.DeviceDpi == fontDpi)
					return;

				float factor = (float)form.DeviceDpi / fontDpi;
				fontDpi = form.DeviceDpi;
				foreach (Control control in controls)
					control.Font = new Font(control.Font.FontFamily, control.Font.Size * factor, control.Font.Style);
				if (scaled != null)
					scaled();
			};
			form.HandleCreated += rescale;
			form.DpiChanged += (sender, e) => rescale(sender, e);
		}
	}
}
