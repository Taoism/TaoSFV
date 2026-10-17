using System.Drawing;
using System.Windows.Forms;

namespace TaoSFV
{
	/// <summary>
	/// Touch-ups for controls that WinForms' built-in dark mode leaves light.
	/// </summary>
	internal static class ThemeHelper
	{
		public static void Apply(Control root)
		{
			if (!Application.IsDarkModeEnabled)
				return;

			ApplyRecursive(root);
		}

		private static void ApplyRecursive(Control control)
		{
			// visual-styles tab pages paint the light theme background
			TabPage tabPage = control as TabPage;
			if (tabPage != null)
			{
				tabPage.UseVisualStyleBackColor = false;
				tabPage.BackColor = SystemColors.Control;
			}

			// the 3D border has a white outer edge in dark mode
			TextBoxBase textBox = control as TextBoxBase;
			if (textBox != null && textBox.BorderStyle == BorderStyle.Fixed3D)
				textBox.BorderStyle = BorderStyle.FixedSingle;

			ToolStrip toolStrip = control as ToolStrip;
			if (toolStrip != null)
			{
				foreach (ToolStripItem item in toolStrip.Items)
				{
					// 3D label borders are drawn with ControlLightLight, which is white in dark mode; draw over them
					ToolStripStatusLabel label = item as ToolStripStatusLabel;
					if (label != null && label.BorderSides != ToolStripStatusLabelBorderSides.None)
						label.Paint += StatusLabel_Paint;
				}
			}

			foreach (Control child in control.Controls)
				ApplyRecursive(child);
		}

		private static void StatusLabel_Paint(object sender, PaintEventArgs e)
		{
			ToolStripItem item = (ToolStripItem)sender;
			Rectangle rect = new Rectangle(Point.Empty, item.Size);
			using (Pen back = new Pen(item.Owner.BackColor))
				e.Graphics.DrawRectangle(back, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
			using (Pen border = new Pen(SystemColors.ControlDark))
				e.Graphics.DrawRectangle(border, rect.X + 1, rect.Y + 1, rect.Width - 3, rect.Height - 3);
		}
	}
}
