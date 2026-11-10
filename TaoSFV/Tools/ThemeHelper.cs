using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TaoSFV.Model.Settings;

namespace TaoSFV
{
	/// <summary>
	/// Touch-ups for controls that WinForms' built-in dark mode leaves light, and switching theme while windows are open.
	/// </summary>
	internal static class ThemeHelper
	{
		// what a control looked like before the dark touch-ups, to put back when switching to light
		private sealed class Original
		{
			public bool UseVisualStyleBackColor;
			public BorderStyle BorderStyle;
			public FlatStyle FlatStyle;
			public Color BackColor;
			public Color ForeColor;
			public Color BorderColor;
			public Color MouseOverBackColor;
			public Color MouseDownBackColor;
		}

		private static readonly ConditionalWeakTable<Control, Original> _touched = new ConditionalWeakTable<Control, Original>();
		private static readonly ConditionalWeakTable<ToolStripItem, object> _paintHooked = new ConditionalWeakTable<ToolStripItem, object>();

		/// <summary>Applies the dark touch-ups in dark mode, or undoes earlier ones in light mode.</summary>
		public static void Apply(Control root)
		{
			ApplyRecursive(root, Application.IsDarkModeEnabled);
		}

		/// <summary>
		/// Changes the theme of the running app: every open window is re-themed and its window handles recreated, since
		/// WinForms applies dark or light styling to native controls only when they're created.
		/// </summary>
		public static void Switch(AppTheme theme)
		{
			Program.ApplyTheme(theme);

			// menus and status bars draw with a renderer whose colors were read once; a new one reads them again
			ToolStripManager.Renderer = new ToolStripProfessionalRenderer();

			MethodInfo recreateHandle = typeof(Control).GetMethod("RecreateHandle", BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (Form form in Application.OpenForms.Cast<Form>().ToList())
			{
				Apply(form);
				RefreshButtons(form);
				RefreshToolStrips(form);
				RefreshSystemColors(form);
				if (form.IsHandleCreated && recreateHandle != null)
					recreateHandle.Invoke(form, null);
				SetDarkTitleBar(form, Application.IsDarkModeEnabled);
				form.Invalidate(true);
				form.Update();
			}
		}

		// the title bar is drawn by Windows; a rebuilt window doesn't get the dark one on its own
		private static void SetDarkTitleBar(Form form, bool dark)
		{
			if (!form.IsHandleCreated)
				return;
			int value = dark ? 1 : 0;
			DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
		}

		private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

		// a control keeps the brush it made from a system color (e.g. Window) through a handle rebuild; setting the
		// color again makes a new one in the current mode's shade
		private static void RefreshSystemColors(Control control)
		{
			Color back = control.BackColor;
			if (back.IsSystemColor && !(control is Form))
			{
				control.BackColor = back == SystemColors.Window ? SystemColors.Control : SystemColors.Window;
				control.BackColor = back;
			}

			foreach (Control child in control.Controls)
				RefreshSystemColors(child);
		}

		private static void RefreshToolStrips(Control control)
		{
			ToolStrip strip = control as ToolStrip;
			if (strip != null && strip.RenderMode == ToolStripRenderMode.ManagerRenderMode)
				strip.Renderer = ToolStripManager.Renderer;

			foreach (Control child in control.Controls)
				RefreshToolStrips(child);
		}

		// buttons pick their renderer (light or dark) once; changing the style makes them pick again
		private static void RefreshButtons(Control control)
		{
			ButtonBase button = control as ButtonBase;
			if (button != null)
			{
				FlatStyle style = button.FlatStyle;
				button.FlatStyle = style == FlatStyle.Flat ? FlatStyle.Standard : FlatStyle.Flat;
				button.FlatStyle = style;
			}

			foreach (Control child in control.Controls)
				RefreshButtons(child);
		}

		// dark grey buttons (about the shades of Windows 11's dark buttons)
		private static readonly Color ButtonFace = Color.FromArgb(0x33, 0x33, 0x33);
		private static readonly Color ButtonHover = Color.FromArgb(0x3D, 0x3D, 0x3D);
		private static readonly Color ButtonPressed = Color.FromArgb(0x2A, 0x2A, 0x2A);
		private static readonly Color ButtonBorder = Color.FromArgb(0x4A, 0x4A, 0x4A);
		private static readonly Color DefaultButtonBorder = Color.FromArgb(0x6E, 0x6E, 0x6E);
		private static readonly Color ButtonText = Color.FromArgb(0xE4, 0xE4, 0xE4);

		private static void ApplyRecursive(Control control, bool dark)
		{
			Original original;
			bool touched = _touched.TryGetValue(control, out original);

			if (dark && !touched)
			{
				original = new Original();
				bool changed = false;

				// visual-styles tab pages paint the light theme background
				TabPage tabPage = control as TabPage;
				if (tabPage != null)
				{
					original.UseVisualStyleBackColor = tabPage.UseVisualStyleBackColor;
					tabPage.UseVisualStyleBackColor = false;
					tabPage.BackColor = SystemColors.Control;
					changed = true;
				}

				// the 3D border has a white outer edge in dark mode
				TextBoxBase textBox = control as TextBoxBase;
				if (textBox != null && textBox.BorderStyle == BorderStyle.Fixed3D)
				{
					original.BorderStyle = textBox.BorderStyle;
					textBox.BorderStyle = BorderStyle.FixedSingle;
					changed = true;
				}

				// drop-down lists keep a light gray face in dark mode unless they're flat
				ComboBox comboBox = control as ComboBox;
				if (comboBox != null)
				{
					original.FlatStyle = comboBox.FlatStyle;
					comboBox.FlatStyle = FlatStyle.Flat;
					comboBox.BackColor = Color.Black;
					comboBox.ForeColor = Color.White;
					changed = true;
				}

				// push buttons: WinForms' dark buttons have white or light gray outlines; flat ones in dark grey blend in
				Button button = control as Button;
				if (button != null)
				{
					original.FlatStyle = button.FlatStyle;
					original.UseVisualStyleBackColor = button.UseVisualStyleBackColor;
					original.BackColor = button.BackColor;
					original.ForeColor = button.ForeColor;
					original.BorderColor = button.FlatAppearance.BorderColor;
					original.MouseOverBackColor = button.FlatAppearance.MouseOverBackColor;
					original.MouseDownBackColor = button.FlatAppearance.MouseDownBackColor;

					Form form = button.FindForm();
					bool isDefault = form != null && form.AcceptButton == button;
					button.FlatStyle = FlatStyle.Flat;
					button.UseVisualStyleBackColor = false;
					button.BackColor = ButtonFace;
					button.ForeColor = ButtonText;
					button.FlatAppearance.BorderColor = isDefault ? DefaultButtonBorder : ButtonBorder;
					button.FlatAppearance.MouseOverBackColor = ButtonHover;
					button.FlatAppearance.MouseDownBackColor = ButtonPressed;
					changed = true;
				}

				if (changed)
					_touched.Add(control, original);
			}
			else if (!dark && touched)
			{
				TabPage tabPage = control as TabPage;
				if (tabPage != null)
				{
					tabPage.ResetBackColor();
					tabPage.UseVisualStyleBackColor = original.UseVisualStyleBackColor;
				}

				TextBoxBase textBox = control as TextBoxBase;
				if (textBox != null)
					textBox.BorderStyle = original.BorderStyle;

				ComboBox comboBox = control as ComboBox;
				if (comboBox != null)
				{
					comboBox.FlatStyle = original.FlatStyle;
					comboBox.ResetBackColor();
					comboBox.ResetForeColor();
				}

				Button button = control as Button;
				if (button != null)
				{
					button.FlatStyle = original.FlatStyle;
					button.BackColor = original.BackColor;
					button.ForeColor = original.ForeColor;
					button.UseVisualStyleBackColor = original.UseVisualStyleBackColor;
					button.FlatAppearance.BorderColor = original.BorderColor;
					button.FlatAppearance.MouseOverBackColor = original.MouseOverBackColor;
					button.FlatAppearance.MouseDownBackColor = original.MouseDownBackColor;
				}

				_touched.Remove(control);
			}

			// buttons, check boxes, radio buttons and progress bars: dark grey instead of white outlines
			DarkPainting.Hook(control);

			ToolStrip toolStrip = control as ToolStrip;
			if (toolStrip != null)
			{
				foreach (ToolStripItem item in toolStrip.Items)
				{
					// 3D label borders are drawn with ControlLightLight, which is white in dark mode; draw over them
					// (the handler checks the mode, so it's hooked once and left)
					ToolStripStatusLabel label = item as ToolStripStatusLabel;
					object hooked;
					if (label != null && label.BorderSides != ToolStripStatusLabelBorderSides.None && !_paintHooked.TryGetValue(label, out hooked))
					{
						label.Paint += StatusLabel_Paint;
						_paintHooked.Add(label, null);
					}
				}
				toolStrip.Invalidate();
			}

			foreach (Control child in control.Controls)
				ApplyRecursive(child, dark);
		}

		private static void StatusLabel_Paint(object sender, PaintEventArgs e)
		{
			if (!Application.IsDarkModeEnabled)
				return;

			ToolStripItem item = (ToolStripItem)sender;
			Rectangle rect = new Rectangle(Point.Empty, item.Size);
			using (Pen back = new Pen(item.Owner.BackColor))
				e.Graphics.DrawRectangle(back, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
			// ControlDark is a light gray in dark mode: a fixed dark grey instead
			using (Pen border = new Pen(DarkPainting.Border))
				e.Graphics.DrawRectangle(border, rect.X + 1, rect.Y + 1, rect.Width - 3, rect.Height - 3);
		}
	}
}
