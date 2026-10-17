using System;
using System.Windows.Forms;
using TaoSFV.Localization;

namespace TaoSFV
{
	public partial class ReleaseNotesForm : Form
	{
		public ReleaseNotesForm()
		{
			InitializeComponent();
			ThemeHelper.Apply(this);

            Text = Language.ReleaseNotesForm.Title;
            btnOK.Text = Language.General.OKButton;
		}
	}
}
