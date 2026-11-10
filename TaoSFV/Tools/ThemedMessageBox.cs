using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using TaoSFV.Localization;

namespace TaoSFV
{
	/// <summary>
	/// Message boxes that follow the app theme: the Windows message box in light mode, and a dark dialog with the same
	/// layout (icon and text above, buttons on a footer strip) in dark mode, since Windows draws message boxes light only.
	/// Same overloads as MessageBox.Show.
	/// </summary>
	public static class ThemedMessageBox
	{
		private static readonly Color Body = Color.FromArgb(0x20, 0x20, 0x20);
		private static readonly Color Footer = Color.FromArgb(0x2B, 0x2B, 0x2B);
		private static readonly Color Text = Color.FromArgb(0xE4, 0xE4, 0xE4);

		public static DialogResult Show(string text)
		{
			return Show(null, text, "", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
		}

		public static DialogResult Show(string text, string caption)
		{
			return Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
		}

		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
		{
			return Show(null, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
		}

		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
		{
			return Show(null, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
		}

		public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
		{
			return Show(null, text, caption, buttons, icon, defaultButton);
		}

		public static DialogResult Show(IWin32Window owner, string text)
		{
			return Show(owner, text, "", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
		}

		public static DialogResult Show(IWin32Window owner, string text, string caption)
		{
			return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
		}

		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
		{
			return Show(owner, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
		}

		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
		{
			return Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
		}

		public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon,
			MessageBoxDefaultButton defaultButton)
		{
			text = text ?? "";
			caption = caption ?? "";

			// like MessageBox, a box without an owner belongs to the active window
			if (owner == null)
				owner = Form.ActiveForm;
			Control ownerControl = owner as Control;
			if (ownerControl != null && (ownerControl.IsDisposed || !ownerControl.IsHandleCreated || !ownerControl.Visible))
				owner = null;

			if (!Application.IsDarkModeEnabled)
			{
				return owner != null
					? MessageBox.Show(owner, text, caption, buttons, icon, defaultButton)
					: MessageBox.Show(text, caption, buttons, icon, defaultButton);
			}

			using (Form form = BuildDark(text, caption, buttons, icon, defaultButton, owner != null))
			{
				PlaySound(icon);
				return owner != null ? form.ShowDialog(owner) : form.ShowDialog();
			}
		}

		private static Form BuildDark(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, bool hasOwner)
		{
			Form form = new Form
			{
				Text = caption,
				Font = SystemFonts.MessageBoxFont,
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MinimizeBox = false,
				MaximizeBox = false,
				ShowIcon = false,
				ShowInTaskbar = !hasOwner,
				StartPosition = hasOwner ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				BackColor = Body,
				ForeColor = Text,
				KeyPreview = true
			};
			float scale = form.DeviceDpi / 96f;
			int px(int value) => (int)Math.Round(value * scale);
			form.MinimumSize = new Size(px(200), 0);

			TableLayoutPanel root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty, Dock = DockStyle.Fill };

			// icon and text
			TableLayoutPanel body = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(px(24), px(22), px(28), px(22)), BackColor = Body };
			Bitmap image = IconImage(icon, px(32));
			Label label = new Label
			{
				Text = text,
				AutoSize = true,
				MaximumSize = new Size(px(520), 0),
				ForeColor = Text,
				BackColor = Body,
				UseMnemonic = false,
				Margin = new Padding(0, image != null && !text.Contains("\n") ? px(8) : 0, 0, 0)
			};
			if (image != null)
				body.Controls.Add(new PictureBox { Image = image, Size = image.Size, SizeMode = PictureBoxSizeMode.Normal, Margin = new Padding(0, 0, px(12), 0), BackColor = Body });
			body.Controls.Add(label);

			// buttons right-aligned on the footer strip
			FlowLayoutPanel footer = new FlowLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				FlowDirection = FlowDirection.RightToLeft,
				Dock = DockStyle.Fill,
				BackColor = Footer,
				Margin = Padding.Empty,
				Padding = new Padding(px(10), px(11), px(10), px(11)),
				WrapContents = false
			};

			List<Tuple<string, DialogResult>> choices = Choices(buttons);
			Button[] created = new Button[choices.Count];
			for (int i = choices.Count - 1; i >= 0; i--)
			{
				Button button = new Button
				{
					Text = choices[i].Item1,
					DialogResult = choices[i].Item2,
					MinimumSize = new Size(px(88), px(26)),
					AutoSize = true,
					Margin = new Padding(i == 0 ? 0 : px(10), 0, 0, 0),
					AccessibleName = choices[i].Item1.Replace("&", ""),
					UseVisualStyleBackColor = true
				};
				created[i] = button;
				footer.Controls.Add(button);
			}

			int defaultIndex = Math.Min(created.Length - 1, defaultButton == MessageBoxDefaultButton.Button2 ? 1 : defaultButton == MessageBoxDefaultButton.Button3 ? 2 : 0);
			form.AcceptButton = created[defaultIndex];
			Button cancel = created.FirstOrDefault(b => b.DialogResult == DialogResult.Cancel);
			if (cancel != null)
				form.CancelButton = cancel;
			else if (buttons == MessageBoxButtons.OK)
				form.CancelButton = created[0];

			// Yes/No has no Cancel: closing the window means No, as with the Windows box
			form.FormClosing += (sender, e) =>
			{
				if (form.DialogResult == DialogResult.None || (form.DialogResult == DialogResult.Cancel && cancel == null && buttons != MessageBoxButtons.OK))
					form.DialogResult = DefaultResult(buttons);
			};

			// Ctrl+C copies the message, as in the Windows message box
			form.KeyDown += (sender, e) =>
			{
				if (e.Control && e.KeyCode == Keys.C)
				{
					try
					{
						Clipboard.SetText(caption + Environment.NewLine + Environment.NewLine + text);
					}
					catch (System.Runtime.InteropServices.ExternalException)
					{
					}
				}
			};
			form.Shown += (sender, e) => created[defaultIndex].Focus();

			root.Controls.Add(body, 0, 0);
			root.Controls.Add(footer, 0, 1);
			form.Controls.Add(root);
			ThemeHelper.Apply(form);
			return form;
		}

		private static DialogResult DefaultResult(MessageBoxButtons buttons)
		{
			switch (buttons)
			{
				case MessageBoxButtons.OK:
					return DialogResult.OK;
				case MessageBoxButtons.YesNo:
					return DialogResult.No;
				case MessageBoxButtons.AbortRetryIgnore:
					return DialogResult.Abort;
				default:
					return DialogResult.Cancel;
			}
		}

		private static List<Tuple<string, DialogResult>> Choices(MessageBoxButtons buttons)
		{
			var g = Language.General;
			string ok = g != null && !string.IsNullOrEmpty(g.OKButton) ? g.OKButton : "OK";
			string cancel = g != null && !string.IsNullOrEmpty(g.CancelButton) ? g.CancelButton : "Cancel";
			string yes = g != null && !string.IsNullOrEmpty(g.YesButton) ? g.YesButton : "&Yes";
			string no = g != null && !string.IsNullOrEmpty(g.NoButton) ? g.NoButton : "&No";

			switch (buttons)
			{
				case MessageBoxButtons.OKCancel:
					return new List<Tuple<string, DialogResult>> { Tuple.Create(ok, DialogResult.OK), Tuple.Create(cancel, DialogResult.Cancel) };
				case MessageBoxButtons.YesNo:
					return new List<Tuple<string, DialogResult>> { Tuple.Create(yes, DialogResult.Yes), Tuple.Create(no, DialogResult.No) };
				case MessageBoxButtons.YesNoCancel:
					return new List<Tuple<string, DialogResult>> { Tuple.Create(yes, DialogResult.Yes), Tuple.Create(no, DialogResult.No), Tuple.Create(cancel, DialogResult.Cancel) };
				case MessageBoxButtons.RetryCancel:
					return new List<Tuple<string, DialogResult>> { Tuple.Create("&Retry", DialogResult.Retry), Tuple.Create(cancel, DialogResult.Cancel) };
				case MessageBoxButtons.AbortRetryIgnore:
					return new List<Tuple<string, DialogResult>> { Tuple.Create("&Abort", DialogResult.Abort), Tuple.Create("&Retry", DialogResult.Retry), Tuple.Create("&Ignore", DialogResult.Ignore) };
				default:
					return new List<Tuple<string, DialogResult>> { Tuple.Create(ok, DialogResult.OK) };
			}
		}

		private static Bitmap IconImage(MessageBoxIcon icon, int size)
		{
			Icon system;
			switch (icon)
			{
				case MessageBoxIcon.Question:
					system = SystemIcons.Question;
					break;
				case MessageBoxIcon.Warning:
					system = SystemIcons.Warning;
					break;
				case MessageBoxIcon.Error:
					system = SystemIcons.Error;
					break;
				case MessageBoxIcon.Information:
					system = SystemIcons.Information;
					break;
				default:
					return null;
			}
			using (Icon sized = new Icon(system, size, size))
				return sized.ToBitmap();
		}

		private static void PlaySound(MessageBoxIcon icon)
		{
			switch (icon)
			{
				case MessageBoxIcon.Warning:
					SystemSounds.Exclamation.Play();
					break;
				case MessageBoxIcon.Error:
					SystemSounds.Hand.Play();
					break;
				case MessageBoxIcon.Information:
					SystemSounds.Asterisk.Play();
					break;
			}
		}
	}
}
