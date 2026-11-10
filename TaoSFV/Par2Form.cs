using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaoSFV.Localization;
using TaoSFV.Par2;

namespace TaoSFV
{
	/// <summary>
	/// A PAR2 recovery set: checks its files (damaged, missing, renamed, shifted blocks) and repairs them from the
	/// recovery data. Built in code like ScheduleChecksForm; work runs in the background and can be stopped.
	/// </summary>
	public sealed class Par2Form : Form
	{
		private static readonly Color BadColor = Color.FromArgb(220, 60, 60);
		private static readonly Color RenamedColor = Color.FromArgb(200, 140, 0);

		private readonly string _par2Path;
		private readonly ListView _list = new ListView();
		private readonly Label _info = new Label();
		private readonly Label _summary = new Label();
		private readonly ProgressBar _progress = new ProgressBar();
		private readonly Button _repair = new Button();
		private readonly Button _verify = new Button();
		private readonly Button _close = new Button();
		private readonly System.Windows.Forms.Timer _progressTimer = new System.Windows.Forms.Timer { Interval = 200 };

		private CancellationTokenSource _cancel;
		private Par2Result _result;
		private long _done;
		private long _total;
		private bool _closeWhenStopped;

		public Par2Form(string par2Path)
		{
			_par2Path = Path.GetFullPath(par2Path);
			var p = Language.MainForm;

			Text = string.Format(p.Par2_Title, Path.GetFileName(_par2Path));
			Icon = Owner != null ? Owner.Icon : Icon.ExtractAssociatedIcon(Program.ExecutablePath);
			Font = SystemFonts.MessageBoxFont;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(720, 440);
			MinimumSize = new Size(520, 320);
			Padding = new Padding(10);

			_info.AutoSize = true;
			_info.Margin = new Padding(0, 0, 0, 8);

			_list.View = View.Details;
			_list.FullRowSelect = true;
			_list.HideSelection = false;
			_list.Dock = DockStyle.Fill;
			_list.Columns.Add(p.Par2_FileColumn, 300);
			_list.Columns.Add(p.Par2_SizeColumn, 90, HorizontalAlignment.Right);
			_list.Columns.Add(p.Par2_StatusColumn, 170);
			_list.Columns.Add(p.Par2_BlocksColumn, 110);

			_summary.AutoSize = true;
			_summary.MaximumSize = new Size(690, 0);
			_summary.Margin = new Padding(0, 8, 0, 0);
			_progress.Dock = DockStyle.Fill;
			_progress.Height = 16;
			_progress.Margin = new Padding(0, 6, 0, 0);

			_repair.Text = p.Par2_RepairButton;
			_repair.AutoSize = true;
			_repair.Enabled = false;
			_repair.Click += Repair_Click;
			_verify.Text = p.Par2_VerifyButton;
			_verify.AutoSize = true;
			_verify.Enabled = false;
			_verify.Click += (sender, e) => StartVerify();
			Button folder = new Button { Text = p.Par2_OpenFolderButton, AutoSize = true };
			folder.Click += (sender, e) => Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _par2Path + "\"") { UseShellExecute = true });
			_close.Text = Language.General.CloseButton;
			_close.AutoSize = true;
			_close.Anchor = AnchorStyles.Right;
			_close.Click += Close_Click;
			CancelButton = _close;

			TableLayoutPanel buttons = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
			foreach (Control control in new Control[] { _repair, _verify, folder, new Label(), _close })
			{
				buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				buttons.Controls.Add(control);
			}
			buttons.ColumnStyles[3] = new ColumnStyle(SizeType.Percent, 100);

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.Controls.Add(_info);
			layout.Controls.Add(_list);
			layout.Controls.Add(_summary);
			layout.Controls.Add(_progress);
			layout.Controls.Add(buttons);
			Controls.Add(layout);

			_progressTimer.Tick += (sender, e) => UpdateProgress();
			ThemeHelper.Apply(this);

			// pixel sizes are for 96 DPI
			ScaleFixedSizes(DeviceDpi / 96f);
			DpiChanged += (sender, e) => ScaleFixedSizes((float)e.DeviceDpiNew / e.DeviceDpiOld);
			Shown += (sender, e) => StartVerify();
			FormClosed += (sender, e) => _progressTimer.Dispose();
		}

		/// <summary>Opens the window for a .par2 file, or brings an open one for the same set to the front.</summary>
		public static void Show(IWin32Window owner, string par2Path)
		{
			string baseName = Par2Reader.GetBaseName(par2Path);
			string folder = Path.GetDirectoryName(Path.GetFullPath(par2Path));
			Par2Form open = Application.OpenForms.OfType<Par2Form>().FirstOrDefault(f =>
				string.Equals(Path.GetDirectoryName(f._par2Path), folder, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(Par2Reader.GetBaseName(f._par2Path), baseName, StringComparison.OrdinalIgnoreCase));
			if (open != null)
			{
				open.Activate();
				return;
			}

			Par2Form form = new Par2Form(par2Path);
			form.Show(owner);
		}

		private void ScaleFixedSizes(float factor)
		{
			if (Math.Abs(factor - 1) < 0.01)
				return;
			foreach (ColumnHeader column in _list.Columns)
				column.Width = (int)(column.Width * factor);
			_summary.MaximumSize = new Size((int)(_summary.MaximumSize.Width * factor), 0);
		}

		// ------------------------------------------------------------------ work

		private bool Busy
		{
			get { return _cancel != null; }
		}

		private void SetBusy(string status, long total)
		{
			_cancel = new CancellationTokenSource();
			_done = 0;
			_total = Math.Max(1, total);
			_progress.Value = 0;
			_summary.Text = status;
			_summary.ForeColor = ForeColor;
			_repair.Enabled = false;
			_verify.Enabled = false;
			_close.Text = Language.MainForm.Par2_StopButton;
			_progressTimer.Start();
		}

		private void SetIdle()
		{
			_progressTimer.Stop();
			_cancel.Dispose();
			_cancel = null;
			_progress.Value = 0;
			_close.Text = Language.General.CloseButton;
			_verify.Enabled = true;
			_repair.Enabled = _result != null && !_result.AllOk && _result.Repairable;
			if (_closeWhenStopped)
				Close();
		}

		private void UpdateProgress()
		{
			_progress.Value = (int)Math.Min(100, Interlocked.Read(ref _done) * 100 / _total);
		}

		private async void StartVerify()
		{
			var p = Language.MainForm;
			SetBusy(p.Par2_Loading, 1);
			CancellationToken cancel = _cancel.Token;
			try
			{
				Par2Set set = await Task.Run(() => Par2Reader.Load(_par2Path), cancel);
				_total = Math.Max(1, set.Files.Values.Where(f => f.HasDescription).Sum(f => f.Length));
				_summary.Text = p.Par2_Verifying;
				ShowSetInfo(set);
				_result = await Task.Run(() => Par2Verifier.Verify(set, cancel, n => Interlocked.Add(ref _done, n)), cancel);
				ShowResult(_result, null);
			}
			catch (OperationCanceledException)
			{
				_summary.Text = p.Par2_Cancelled;
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
			{
				_summary.Text = string.Format(p.Par2_ReadError, Path.GetFileName(_par2Path), ex.Message).Replace("\n\n", " ");
				_summary.ForeColor = BadColor;
			}
			SetIdle();
		}

		private async void Repair_Click(object sender, EventArgs e)
		{
			var p = Language.MainForm;
			Par2Result result = _result;
			int rebuild = result.Files.Count(f => f.Protected && f.State != Par2FileState.OK);
			if (ThemedMessageBox.Show(this, string.Format(p.Par2_RepairConfirm, rebuild), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				return;

			SetBusy(p.Par2_Repairing, result.Set.SliceSize * (result.Set.RecoveryFiles.Sum(f => (long)f.SliceCount(result.Set.SliceSize)) + result.SlicesNeeded));
			CancellationToken cancel = _cancel.Token;
			string message;
			try
			{
				Par2RepairResult repaired = await Task.Run(() => Par2Repairer.Repair(result, cancel, n => Interlocked.Add(ref _done, n)), cancel);
				message = string.Format(p.Par2_Repaired, repaired.Repaired.Count, repaired.Renamed.Count);

				// check the outcome from scratch
				_done = 0;
				_summary.Text = p.Par2_Verifying;
				Par2Set set = await Task.Run(() => Par2Reader.Load(_par2Path), cancel);
				_total = Math.Max(1, set.Files.Values.Where(f => f.HasDescription).Sum(f => f.Length));
				_result = await Task.Run(() => Par2Verifier.Verify(set, cancel, n => Interlocked.Add(ref _done, n)), cancel);
				ShowResult(_result, message);
			}
			catch (OperationCanceledException)
			{
				_summary.Text = p.Par2_Cancelled;
				_result = null;
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is InvalidOperationException)
			{
				_summary.Text = string.Format(p.Par2_RepairFailed, ex.Message);
				_summary.ForeColor = BadColor;
				_result = null;
			}
			SetIdle();
		}

		private void Close_Click(object sender, EventArgs e)
		{
			if (Busy)
			{
				_cancel.Cancel();
				return;
			}
			Close();
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			// stop the work first; the window closes when it has
			if (Busy)
			{
				_closeWhenStopped = true;
				_cancel.Cancel();
				e.Cancel = true;
				return;
			}
			base.OnFormClosing(e);
		}

		// ------------------------------------------------------------------ display

		private void ShowSetInfo(Par2Set set)
		{
			int files = set.RecoveryFileIds.Count + set.NonRecoveryFileIds.Count;
			_info.Text = string.Format(Language.MainForm.Par2_SetInfo, files, set.Files.Values.Sum(f => f.HasDescription ? f.SliceCount(set.SliceSize) : 0),
				FormatSize(set.SliceSize), set.RecoverySlices.Count) + (string.IsNullOrEmpty(set.Creator) ? "" : " - " + set.Creator);
		}

		private void ShowResult(Par2Result result, string prefix)
		{
			var p = Language.MainForm;
			_list.BeginUpdate();
			_list.Items.Clear();
			foreach (Par2FileResult file in result.Files)
			{
				string status;
				Color color = _list.ForeColor;
				switch (file.State)
				{
					case Par2FileState.OK:
						status = p.Par2_OK;
						break;
					case Par2FileState.Renamed:
						status = string.Format(p.Par2_FoundAs, Path.GetFileName(file.FoundAs));
						color = RenamedColor;
						break;
					case Par2FileState.Damaged:
						status = p.Par2_Damaged;
						color = BadColor;
						break;
					default:
						status = p.Par2_Missing;
						color = BadColor;
						break;
				}
				if (!file.Protected)
					status = string.Format(p.Par2_NotProtected, status);

				string blocks = string.Format(p.Par2_BlocksCount, file.GoodSlices, file.TotalSlices);
				if (file.ShiftedSlices != 0)
					blocks += " " + string.Format(p.Par2_Shifted, file.ShiftedSlices);

				ListViewItem item = new ListViewItem(new[] { file.File.Name, FormatSize(file.File.Length), status, blocks });
				item.ForeColor = color;
				_list.Items.Add(item);
			}
			_list.EndUpdate();

			string summary;
			if (result.AllOk)
				summary = p.Par2_AllOk;
			else if (result.Files.Any(f => !f.Protected && f.State != Par2FileState.OK))
				summary = p.Par2_UnprotectedDamage;
			else if (result.Repairable)
				summary = string.Format(p.Par2_Repairable, result.SlicesNeeded, result.RecoverySlicesAvailable);
			else
				summary = string.Format(p.Par2_NotRepairable, result.SlicesNeeded, result.RecoverySlicesAvailable, result.SlicesNeeded - result.RecoverySlicesAvailable);
			if (result.Set.DamagedPackets != 0)
				summary += " " + string.Format(p.Par2_DamagedPackets, result.Set.DamagedPackets);
			_summary.Text = string.IsNullOrEmpty(prefix) ? summary : prefix + " " + summary;
			_summary.ForeColor = result.AllOk ? ForeColor : (result.Repairable ? RenamedColor : BadColor);
		}

		private static string FormatSize(long bytes)
		{
			if (bytes >= 1L << 30)
				return string.Format("{0:0.0} GB", bytes / (double)(1L << 30));
			if (bytes >= 1L << 20)
				return string.Format("{0:0.0} MB", bytes / (double)(1L << 20));
			if (bytes >= 1L << 10)
				return string.Format("{0:0.0} KB", bytes / (double)(1L << 10));
			return bytes + " B";
		}
	}
}
