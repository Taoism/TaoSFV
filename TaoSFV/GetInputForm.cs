using System.Windows.Forms;
using TaoSFV.Localization;

namespace TaoSFV
{
	public partial class GetInputForm : Form
	{
		private GetInputForm()
		{
			InitializeComponent();

			// buttons anchored to the corner landed on top of each other when the form was rescaled for a monitor
			// with another DPI; a right-to-left flow along the bottom keeps them apart
			FlowLayoutPanel buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Bottom,
				FlowDirection = FlowDirection.RightToLeft,
				AutoSize = true,
				WrapContents = false,
				Padding = new Padding(9, 0, 9, 9)
			};
			Controls.Remove(btnOK);
			Controls.Remove(btnCancel);
			btnOK.Anchor = AnchorStyles.None;
			btnCancel.Anchor = AnchorStyles.None;
			buttons.Controls.Add(btnCancel);
			buttons.Controls.Add(btnOK);
			Controls.Add(buttons);

			ThemeHelper.Apply(this);

            btnOK.Text = Language.General.OKButton;
            btnCancel.Text = Language.General.CancelButton;
		}

		public static bool ShowForm(string windowText, string labelText, out string userInput)
		{
			Cursor.Current = Cursors.WaitCursor;
			using (GetInputForm form = new GetInputForm())
			{
				form.Text = windowText;
				form.label1.Text = labelText;
				Cursor.Current = Cursors.Default;
				if (form.ShowDialog() == DialogResult.OK)
				{
					userInput = form.textBox1.Text.Trim();
					return !string.IsNullOrEmpty(userInput);
				}
				else
				{
					userInput = null;
					return false;
				}
			}
		}

		private void btnOK_Click(object sender, System.EventArgs e)
		{
			DialogResult = DialogResult.OK;
			Close();
		}
	}
}
