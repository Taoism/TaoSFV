using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using System.Net;
using System.Reflection;
using TaoSFV.Localization;

namespace TaoSFV
{
	/// <summary>
	/// Exception form.
	/// </summary>
	public partial class ExceptionForm : Form
	{
		/// <summary>
		/// Exception form constructor.
		/// </summary>
		/// <param name="message">The error message.</param>
		/// <param name="stackTrace">The stack trace.</param>
		/// <param name="isTopMost">if <c>true</c>, set TopMost = true.</param>
		/// <param name="occuredDuringStartup">set to <c>true</c> if the error occured during application startup.</param>
		public ExceptionForm(string message, string stackTrace, bool isTopMost, bool occuredDuringStartup)
		{
			InitializeComponent();
			ThemeHelper.Apply(this);

			if (isTopMost)
			{
				StartPosition = FormStartPosition.CenterScreen;
				TopMost = true;
			}

            var lang = Language.ExceptionForm;
            Text = lang.Title;
            tabPage1.Text = lang.ErrorMessageTab;
            tabPage2.Text = lang.StackTraceTab;
            btnReportBug.Text = lang.ReportBugButton;
            btnIgnore.Text = lang.IgnoreButton;
            lblMainLabel.Text = lang.AnErrorHasOccurred;

			if (occuredDuringStartup)
			{
				txtErrorMessage.Text = string.Format("{0}\r\n\r\n{1}", lang.ErrorOccurredDuringStartup, message);
			}
			else
			{
				txtErrorMessage.Text = message;
			}

			txtStackTrace.Text = stackTrace;
		}

		private void btnReportBug_Click(object sender, EventArgs e)
		{
			try
			{
				SubmitError();
				Close();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, Language.ExceptionForm.SubmitBugError_Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// <summary>
		/// Opens a prefilled GitHub issue (the original bug endpoint on cdtag.com is gone).
		/// </summary>
		private void SubmitError()
		{
			string stackTrace = txtStackTrace.Text;
			if (stackTrace.Length > 4000)
				stackTrace = stackTrace.Substring(0, 4000) + Environment.NewLine + "...";

			string body = string.Format("**Version:** {0}\n**OS:** {1}\n\n```\n{2}\n```\n",
				Assembly.GetExecutingAssembly().GetName().Version, Environment.OSVersion, stackTrace);

			Program.OpenUrl(string.Format("https://github.com/judwhite/ilSFV/issues/new?title={0}&body={1}",
				Uri.EscapeDataString(txtErrorMessage.Text), Uri.EscapeDataString(body)));
		}

		private void ExceptionForm_Activated(object sender, EventArgs e)
		{
			txtErrorMessage.Focus();
		}

		private void btnIgnore_Click(object sender, EventArgs e)
		{
			Close();
		}
	}
}
