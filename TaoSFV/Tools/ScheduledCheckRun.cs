using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TaoSFV.Localization;
using TaoSFV.Model.Workset;
using TaoSFV.Verification;

namespace TaoSFV
{
	/// <summary>
	/// "TaoSFV.exe /scheduled folder", started by Task Scheduler: checks the folder with no window, saves an HTML
	/// report, and only if something is wrong shows a notification that opens it.
	/// </summary>
	internal static class ScheduledCheckRun
	{
		public const string Argument = "/scheduled";

		// reports kept per folder
		private const int ReportsKept = 20;

		public static string ReportsPath
		{
			get { return Path.Combine(Program.AppDataPath, "Scheduled checks"); }
		}

		public static void Run(string folder)
		{
			LoadResult result = FolderCheck.Run(new[] { folder }, Program.Settings.Check.ParallelReads, CancellationToken.None);
			string report = SaveReport(folder, result);

			if (!FolderCheck.AllOK(result))
				Notify(folder, result, report);
		}

		/// <summary>The words for saved results, from the current language.</summary>
		public static ResultsText LocalizedResultsText()
		{
			var p = Language.MainForm;
			ResultsText text = new ResultsText
			{
				Title = p.SaveResults_Title,
				Summary = p.SaveResults_Summary,
				BadDetail = p.SaveResults_BadDetail,
				NotRead = p.SaveResults_NotRead
			};
			text.States[ChecksumFileState.NotProcessed] = p.SaveResults_NotChecked;
			text.States[ChecksumFileState.OK] = p.SaveResults_OK;
			text.States[ChecksumFileState.Bad] = p.SaveResults_Bad;
			text.States[ChecksumFileState.Missing] = p.SaveResults_Missing;
			text.States[ChecksumFileState.Unreadable] = p.SaveResults_Unreadable;
			return text;
		}

		private static string SaveReport(string folder, LoadResult result)
		{
			Directory.CreateDirectory(ReportsPath);

			string name = ReportName(folder);
			DateTime now = DateTime.Now;
			string report = Path.Combine(ReportsPath, string.Format("{0} {1:yyyy-MM-dd HHmmss}.html", name, now));
			ResultsExporter.Save(report, result.Sets, LocalizedResultsText(), now, result.Skipped);

			// the names sort by date, so the oldest come first
			foreach (string old in Directory.GetFiles(ReportsPath, name + " ????-??-?? ??????.html").OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).Skip(ReportsKept))
			{
				try
				{
					File.Delete(old);
				}
				catch (IOException)
				{
				}
			}
			return report;
		}

		/// <summary>A file-name-safe name for a folder's reports, e.g. "D_ Music Rock" for D:\Music\Rock.</summary>
		internal static string ReportName(string folder)
		{
			string name = folder.TrimEnd('\\', '/');
			foreach (char c in Path.GetInvalidFileNameChars())
				name = name.Replace(c, c == '\\' || c == '/' ? ' ' : '_');
			if (name.Length > 80)
				name = name.Substring(name.Length - 80);
			return name.Trim();
		}

		private static void Notify(string folder, LoadResult result, string report)
		{
			var p = Language.MainForm;
			string summary = ResultsExporter.Summary(result.Sets, LocalizedResultsText());
			if (result.Skipped.Count != 0)
				summary += Environment.NewLine + string.Format(p.ScheduledCheck_NotRead, result.Skipped.Count);

			using (NotifyIcon icon = new NotifyIcon())
			using (System.Windows.Forms.Timer timeout = new System.Windows.Forms.Timer())
			{
				icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Program.ExecutablePath);
				icon.Text = "TaoSFV";
				icon.Visible = true;
				icon.BalloonTipClicked += (sender, e) =>
				{
					Process.Start(new ProcessStartInfo(report) { UseShellExecute = true });
					Application.ExitThread();
				};
				icon.Click += (sender, e) =>
				{
					Process.Start(new ProcessStartInfo(report) { UseShellExecute = true });
					Application.ExitThread();
				};

				// the notification stays in the Action Center; the tray icon goes after a minute
				timeout.Interval = 60000;
				timeout.Tick += (sender, e) => Application.ExitThread();
				timeout.Start();

				icon.ShowBalloonTip(30000, string.Format(p.ScheduledCheck_Failed_Title, folder), summary + Environment.NewLine + p.ScheduledCheck_ClickForReport, ToolTipIcon.Warning);
				Application.Run();
				icon.Visible = false;
			}
		}
	}
}
