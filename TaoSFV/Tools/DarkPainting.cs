using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace TaoSFV
{
	/// <summary>
	/// Paints the parts WinForms' dark mode leaves white or light gray: push buttons, check box and radio button
	/// glyphs, and progress bar borders, all in dark grey. Hooked once per control; each painter checks the current
	/// mode, so switching themes needs no unhooking.
	/// </summary>
	internal static class DarkPainting
	{
		public static readonly Color Face = Color.FromArgb(0x33, 0x33, 0x33);
		public static readonly Color FaceHover = Color.FromArgb(0x3D, 0x3D, 0x3D);
		public static readonly Color FacePressed = Color.FromArgb(0x2A, 0x2A, 0x2A);
		public static readonly Color FaceDisabled = Color.FromArgb(0x2B, 0x2B, 0x2B);
		public static readonly Color Border = Color.FromArgb(0x4A, 0x4A, 0x4A);
		public static readonly Color BorderStrong = Color.FromArgb(0x6E, 0x6E, 0x6E);   // default or focused button, glyph outlines
		public static readonly Color Text = Color.FromArgb(0xE4, 0xE4, 0xE4);
		public static readonly Color TextDisabled = Color.FromArgb(0x7A, 0x7A, 0x7A);
		public static readonly Color GlyphFill = Color.FromArgb(0x2B, 0x2B, 0x2B);
		public static readonly Color Accent = Color.FromArgb(0x4C, 0x8B, 0xD6);

		private sealed class ButtonState
		{
			public bool Hover;
			public bool Pressed;
		}

		private static readonly ConditionalWeakTable<Control, object> _hooked = new ConditionalWeakTable<Control, object>();
		private static readonly ConditionalWeakTable<Button, ButtonState> _states = new ConditionalWeakTable<Button, ButtonState>();

		public static void Hook(Control control)
		{
			object existing;
			if (_hooked.TryGetValue(control, out existing))
				return;

			Button button = control as Button;
			if (button != null)
			{
				ButtonState state = _states.GetOrCreateValue(button);
				button.MouseEnter += (s, e) => { state.Hover = true; button.Invalidate(); };
				button.MouseLeave += (s, e) => { state.Hover = false; state.Pressed = false; button.Invalidate(); };
				button.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { state.Pressed = true; button.Invalidate(); } };
				button.MouseUp += (s, e) => { state.Pressed = false; button.Invalidate(); };
				button.GotFocus += (s, e) => button.Invalidate();
				button.LostFocus += (s, e) => button.Invalidate();
				button.Paint += PaintButton;
				_hooked.Add(control, null);
				return;
			}

			CheckBox checkBox = control as CheckBox;
			RadioButton radio = control as RadioButton;
			if ((checkBox != null && checkBox.Appearance == Appearance.Normal) || (radio != null && radio.Appearance == Appearance.Normal))
			{
				control.Paint += PaintGlyph;
				_hooked.Add(control, null);
				return;
			}

			ProgressBar progress = control as ProgressBar;
			if (progress != null)
			{
				BorderPainter painter = new BorderPainter(progress);
				progress.HandleCreated += (s, e) => painter.Attach();
				if (progress.IsHandleCreated)
					painter.Attach();
				_hooked.Add(control, painter);
				return;
			}

			GroupBox group = control as GroupBox;
			if (group != null)
			{
				group.Paint += PaintGroupBox;
				_hooked.Add(control, null);
				return;
			}

			// controls whose window border (non-client area) Windows draws light gray in dark mode
			if (control is TextBoxBase || control is ListView || control is ListBox || control is TreeView || control is DateTimePicker)
			{
				FrameRepainter frame = new FrameRepainter(control);
				control.HandleCreated += (s, e) => frame.Attach();
				if (control.IsHandleCreated)
					frame.Attach();
				_hooked.Add(control, frame);
			}
		}

		// ------------------------------------------------------------------ group boxes

		private static void PaintGroupBox(object sender, PaintEventArgs e)
		{
			if (!Application.IsDarkModeEnabled)
				return;

			GroupBox group = (GroupBox)sender;
			Graphics g = e.Graphics;
			Size text = TextRenderer.MeasureText(g, group.Text, group.Font, Size.Empty, TextFormatFlags.SingleLine);
			int top = text.Height / 2;

			// the frame GroupBox drew in white: repaint the whole box (child controls paint themselves)
			using (SolidBrush back = new SolidBrush(group.BackColor))
				g.FillRectangle(back, group.ClientRectangle);
			using (Pen pen = new Pen(BorderStrong))
				g.DrawRectangle(pen, 0, top, group.Width - 1, group.Height - top - 1);
			if (!string.IsNullOrEmpty(group.Text))
			{
				Rectangle label = new Rectangle(6, 0, text.Width, text.Height);
				using (SolidBrush back = new SolidBrush(group.BackColor))
					g.FillRectangle(back, label);
				TextRenderer.DrawText(g, group.Text, group.Font, label, group.Enabled ? group.ForeColor : TextDisabled, TextFormatFlags.SingleLine);
			}
		}

		// ------------------------------------------------------------------ window borders

		/// <summary>Repaints a control's window border (1 or 2 pixels in the non-client area) in dark grey after Windows draws it.</summary>
		private sealed class FrameRepainter : NativeWindow
		{
			private const int WM_NCPAINT = 0x0085;
			private const int WM_PAINT = 0x000F;
			private const int WM_SETFOCUS = 0x0007;
			private const int WM_KILLFOCUS = 0x0008;
			private const int WM_NCDESTROY = 0x0082;
			private readonly Control _control;

			public FrameRepainter(Control control)
			{
				_control = control;
			}

			public void Attach()
			{
				if (Handle != IntPtr.Zero)
					ReleaseHandle();
				AssignHandle(_control.Handle);
			}

			protected override void WndProc(ref Message m)
			{
				base.WndProc(ref m);
				if (m.Msg == WM_NCDESTROY)
				{
					ReleaseHandle();
					return;
				}
				if ((m.Msg == WM_NCPAINT || m.Msg == WM_PAINT || m.Msg == WM_SETFOCUS || m.Msg == WM_KILLFOCUS) && Application.IsDarkModeEnabled)
					PaintFrame();
			}

			private void PaintFrame()
			{
				WINDOWINFO info = new WINDOWINFO();
				info.cbSize = (uint)Marshal.SizeOf(typeof(WINDOWINFO));
				if (!GetWindowInfo(Handle, ref info))
					return;
				// cxWindowBorders is 0 for a control with only a client edge; the client area's offset is the border
				int border = Math.Min(info.rcClient.Left - info.rcWindow.Left, info.rcClient.Top - info.rcWindow.Top);
				if (border > 4)
					return;
				if (border <= 0)
				{
					// dark-mode text boxes and date pickers draw their border inside the client area: repaint its edge
					TextBoxBase textBox = _control as TextBoxBase;
					if (textBox != null && textBox.BorderStyle == BorderStyle.None)
						return;
					using (Graphics g = Graphics.FromHwnd(Handle))
					using (Pen pen = new Pen(Border))
						g.DrawRectangle(pen, 0, 0, _control.ClientSize.Width - 1, _control.ClientSize.Height - 1);
					return;
				}

				int width = info.rcWindow.Right - info.rcWindow.Left;
				int height = info.rcWindow.Bottom - info.rcWindow.Top;
				IntPtr dc = GetWindowDC(Handle);
				if (dc == IntPtr.Zero)
					return;
				try
				{
					using (Graphics g = Graphics.FromHdc(dc))
					{
						// outer ring dark grey, any inner ring the control's background
						using (Pen outer = new Pen(Border))
							g.DrawRectangle(outer, 0, 0, width - 1, height - 1);
						using (Pen inner = new Pen(_control.BackColor))
						{
							for (int i = 1; i < border; i++)
								g.DrawRectangle(inner, i, i, width - 1 - 2 * i, height - 1 - 2 * i);
						}
					}
				}
				finally
				{
					ReleaseDC(Handle, dc);
				}
			}

			[StructLayout(LayoutKind.Sequential)]
			private struct RECT
			{
				public int Left, Top, Right, Bottom;
			}

			[StructLayout(LayoutKind.Sequential)]
			private struct WINDOWINFO
			{
				public uint cbSize;
				public RECT rcWindow;
				public RECT rcClient;
				public uint dwStyle;
				public uint dwExStyle;
				public uint dwWindowStatus;
				public uint cxWindowBorders;
				public uint cyWindowBorders;
				public ushort atomWindowType;
				public ushort wCreatorVersion;
			}

			[DllImport("user32.dll")]
			private static extern bool GetWindowInfo(IntPtr hwnd, ref WINDOWINFO info);

			[DllImport("user32.dll")]
			private static extern IntPtr GetWindowDC(IntPtr hwnd);

			[DllImport("user32.dll")]
			private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
		}

		// ------------------------------------------------------------------ buttons

		private static void PaintButton(object sender, PaintEventArgs e)
		{
			if (!Application.IsDarkModeEnabled)
				return;

			Button button = (Button)sender;
			ButtonState state = _states.GetOrCreateValue(button);
			Form form = button.FindForm();
			bool strong = button.Enabled && (button.Focused || (form != null && form.AcceptButton == button));

			Rectangle rect = button.ClientRectangle;
			Graphics g = e.Graphics;
			using (SolidBrush back = new SolidBrush(button.Parent != null ? button.Parent.BackColor : SystemColors.Control))
				g.FillRectangle(back, rect);

			Color face = !button.Enabled ? FaceDisabled : state.Pressed ? FacePressed : state.Hover ? FaceHover : Face;
			using (SolidBrush brush = new SolidBrush(face))
				g.FillRectangle(brush, rect);
			using (Pen pen = new Pen(!button.Enabled ? Border : strong ? BorderStrong : Border))
				g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

			if (button.Image != null)
			{
				Image image = button.Image;
				Point at = new Point(rect.X + (rect.Width - image.Width) / 2, rect.Y + (rect.Height - image.Height) / 2);
				if (button.Enabled)
					g.DrawImage(image, at);
				else
					ControlPaint.DrawImageDisabled(g, image, at.X, at.Y, face);
			}

			TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
			// underline the access key only when Windows shows keyboard cues (after Alt is pressed)
			if (!SystemInformation.MenuAccessKeysUnderlined)
				flags |= TextFormatFlags.HidePrefix;
			if (!string.IsNullOrEmpty(button.Text))
				TextRenderer.DrawText(g, button.Text, button.Font, Rectangle.Inflate(rect, -4, 0), button.Enabled ? Text : TextDisabled, flags);
		}

		// ------------------------------------------------------------------ check boxes and radio buttons

		private static void PaintGlyph(object sender, PaintEventArgs e)
		{
			if (!Application.IsDarkModeEnabled)
				return;

			ButtonBase control = (ButtonBase)sender;
			CheckBox checkBox = control as CheckBox;
			RadioButton radio = control as RadioButton;
			System.Drawing.ContentAlignment align = checkBox != null ? checkBox.CheckAlign : radio.CheckAlign;
			if (align != System.Drawing.ContentAlignment.MiddleLeft && align != System.Drawing.ContentAlignment.TopLeft)
				return;

			Graphics g = e.Graphics;
			Size size = checkBox != null
				? CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.UncheckedNormal)
				: RadioButtonRenderer.GetGlyphSize(g, RadioButtonState.UncheckedNormal);
			int top = align == System.Drawing.ContentAlignment.TopLeft ? control.Padding.Top + 1 : (control.Height - size.Height) / 2;
			Rectangle glyph = new Rectangle(control.Padding.Left, top, size.Width, size.Height);

			// cover what WinForms drew (a little wider: its dark glyph isn't always the same size)
			using (SolidBrush back = new SolidBrush(control.BackColor))
				g.FillRectangle(back, Rectangle.Inflate(glyph, 1, 1));

			bool on = checkBox != null ? checkBox.CheckState != CheckState.Unchecked : radio.Checked;
			Color fill = !control.Enabled ? FaceDisabled : on ? Accent : GlyphFill;
			Color outline = !control.Enabled ? Border : on ? Accent : BorderStrong;
			Rectangle box = new Rectangle(glyph.X, glyph.Y, glyph.Width - 1, glyph.Height - 1);

			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (SolidBrush brush = new SolidBrush(fill))
			using (Pen pen = new Pen(outline))
			{
				if (radio != null)
				{
					g.FillEllipse(brush, box);
					g.DrawEllipse(pen, box);
					if (on)
					{
						Rectangle dot = Rectangle.Inflate(box, -box.Width / 3, -box.Height / 3);
						using (SolidBrush white = new SolidBrush(Color.White))
							g.FillEllipse(white, dot);
					}
				}
				else
				{
					using (GraphicsPath path = Rounded(box, Math.Max(2, box.Width / 5)))
					{
						g.FillPath(brush, path);
						g.DrawPath(pen, path);
					}
					if (checkBox.CheckState == CheckState.Checked)
					{
						float w = box.Width, h = box.Height;
						PointF[] tick =
						{
							new PointF(box.X + w * 0.22f, box.Y + h * 0.52f),
							new PointF(box.X + w * 0.42f, box.Y + h * 0.72f),
							new PointF(box.X + w * 0.78f, box.Y + h * 0.30f)
						};
						using (Pen white = new Pen(control.Enabled ? Color.White : TextDisabled, Math.Max(1.5f, w / 8f)))
						{
							white.LineJoin = LineJoin.Round;
							white.StartCap = LineCap.Round;
							white.EndCap = LineCap.Round;
							g.DrawLines(white, tick);
						}
					}
					else if (checkBox.CheckState == CheckState.Indeterminate)
					{
						using (SolidBrush white = new SolidBrush(Color.White))
							g.FillRectangle(white, Rectangle.Inflate(box, -box.Width / 4, -box.Height / 2 + 1));
					}
				}
			}
			g.SmoothingMode = SmoothingMode.Default;
		}

		private static GraphicsPath Rounded(Rectangle r, int radius)
		{
			int d = radius * 2;
			GraphicsPath path = new GraphicsPath();
			path.AddArc(r.X, r.Y, d, d, 180, 90);
			path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
			path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
			path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
			path.CloseFigure();
			return path;
		}

		// ------------------------------------------------------------------ progress bars

		/// <summary>Redraws a progress bar's light outer edge in dark grey after every paint of the native control.</summary>
		private sealed class BorderPainter : NativeWindow
		{
			private const int WM_PAINT = 0x000F;
			private const int PBM_SETPOS = 0x0402;
			private readonly ProgressBar _bar;

			public BorderPainter(ProgressBar bar)
			{
				_bar = bar;
			}

			public void Attach()
			{
				if (Handle != IntPtr.Zero)
					ReleaseHandle();
				AssignHandle(_bar.Handle);
			}

			protected override void WndProc(ref Message m)
			{
				base.WndProc(ref m);
				if ((m.Msg == WM_PAINT || m.Msg == PBM_SETPOS) && Application.IsDarkModeEnabled && _bar.IsHandleCreated)
				{
					using (Graphics g = Graphics.FromHwnd(_bar.Handle))
					using (Pen pen = new Pen(Border))
						g.DrawRectangle(pen, 0, 0, _bar.ClientSize.Width - 1, _bar.ClientSize.Height - 1);
				}
				else if (m.Msg == 0x0082) // WM_NCDESTROY
					ReleaseHandle();
			}
		}
	}
}
