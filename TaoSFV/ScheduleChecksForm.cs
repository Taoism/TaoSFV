using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaoSFV.Localization;

namespace TaoSFV
{
	/// <summary>
	/// Tools > Schedule Checks: lists the folders checked on a schedule and adds, changes or removes them.
	/// Built in code (no designer file); laid out with table panels so it scales with the font and DPI.
	/// </summary>
	public sealed class ScheduleChecksForm : Form
	{
		private readonly ListView _list = new ListView();
		private readonly TextBox _folder = new TextBox();
		private readonly ComboBox _frequency = new ComboBox();
		private readonly ComboBox _day = new ComboBox();
		private readonly DateTimePicker _time = new DateTimePicker();
		private readonly Label _on = new Label();
		private readonly Button _save = new Button();
		private readonly Button _remove = new Button();
		private List<ScheduledCheck> _checks = new List<ScheduledCheck>();

		public ScheduleChecksForm()
		{
			var p = Language.MainForm;

			Text = p.ScheduleChecks_Title;
			Font = SystemFonts.MessageBoxFont;
			FormBorderStyle = FormBorderStyle.Sizable;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(640, 420);
			MinimumSize = new Size(560, 360);
			Padding = new Padding(10);

			Label intro = new Label { Text = p.ScheduleChecks_Intro, AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 0, 0, 8) };

			_list.View = View.Details;
			_list.FullRowSelect = true;
			_list.MultiSelect = false;
			_list.HideSelection = false;
			_list.Dock = DockStyle.Fill;
			_list.Columns.Add(p.ScheduleChecks_FolderColumn, 230);
			_list.Columns.Add(p.ScheduleChecks_WhenColumn, 170);
			_list.Columns.Add(p.ScheduleChecks_NextRunColumn, 105);
			_list.Columns.Add(p.ScheduleChecks_LastRunColumn, 105);
			_list.SelectedIndexChanged += List_SelectedIndexChanged;

			// folder row
			Button browse = new Button { Text = p.ScheduleChecks_Browse, AutoSize = true };
			browse.Click += Browse_Click;
			_folder.Dock = DockStyle.Fill;
			TableLayoutPanel folderRow = Row(new Label { Text = p.ScheduleChecks_Folder, AutoSize = true, Anchor = AnchorStyles.Left }, _folder, browse);
			folderRow.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);

			// when row
			_frequency.DropDownStyle = ComboBoxStyle.DropDownList;
			_frequency.Items.AddRange(new object[] { p.ScheduleChecks_Daily, p.ScheduleChecks_Weekly, p.ScheduleChecks_Monthly });
			_frequency.SelectedIndexChanged += (sender, e) => FillDays();
			_day.DropDownStyle = ComboBoxStyle.DropDownList;
			_day.Width = 110;
			_on.AutoSize = true;
			_on.Anchor = AnchorStyles.Left;
			_on.Text = p.ScheduleChecks_On;
			_time.Format = DateTimePickerFormat.Custom;
			_time.CustomFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern; // no seconds
			_time.ShowUpDown = true;
			_time.Width = 100;
			_time.Value = DateTime.Today.AddHours(3);
			_save.Text = p.ScheduleChecks_Save;
			_save.AutoSize = true;
			_save.Click += Save_Click;
			TableLayoutPanel whenRow = Row(new Label { Text = p.ScheduleChecks_Every, AutoSize = true, Anchor = AnchorStyles.Left }, _frequency, _on, _day,
				new Label { Text = p.ScheduleChecks_At, AutoSize = true, Anchor = AnchorStyles.Left }, _time, _save);

			// buttons
			_remove.Text = p.ScheduleChecks_Remove;
			_remove.AutoSize = true;
			_remove.Enabled = false;
			_remove.Click += Remove_Click;
			Button reports = new Button { Text = p.ScheduleChecks_Reports, AutoSize = true };
			reports.Click += (sender, e) =>
			{
				Directory.CreateDirectory(ScheduledCheckRun.ReportsPath);
				Process.Start(new ProcessStartInfo(ScheduledCheckRun.ReportsPath) { UseShellExecute = true });
			};
			Button close = new Button { Text = Language.General.CloseButton, AutoSize = true, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right };
			CancelButton = close;
			TableLayoutPanel buttons = Row(_remove, reports, new Label(), close);
			buttons.ColumnStyles[2] = new ColumnStyle(SizeType.Percent, 100);

			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.Controls.Add(intro);
			layout.Controls.Add(_list);
			layout.Controls.Add(folderRow);
			layout.Controls.Add(whenRow);
			layout.Controls.Add(buttons);
			Controls.Add(layout);

			_frequency.SelectedIndex = (int)CheckFrequency.Weekly;
			ThemeHelper.Apply(this);

			// pixel sizes set above are for 96 DPI; scale them for this monitor, and again if the window moves
			ScaleFixedSizes(intro, DeviceDpi / 96f);
			DpiChanged += (sender, e) => ScaleFixedSizes(intro, (float)e.DeviceDpiNew / e.DeviceDpiOld);
			Load += (sender, e) => Reload(null);
		}

		private void ScaleFixedSizes(Label intro, float factor)
		{
			if (Math.Abs(factor - 1) < 0.01)
				return;
			intro.MaximumSize = new Size((int)(intro.MaximumSize.Width * factor), 0);
			foreach (ColumnHeader column in _list.Columns)
				column.Width = (int)(column.Width * factor);
			_day.Width = (int)(_day.Width * factor);
			_time.Width = (int)(_time.Width * factor);
		}

		private static TableLayoutPanel Row(params Control[] controls)
		{
			TableLayoutPanel row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = controls.Length, RowCount = 1, Margin = new Padding(0, 6, 0, 0) };
			foreach (Control control in controls)
			{
				row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				row.Controls.Add(control);
			}
			return row;
		}

		private void FillDays()
		{
			int keep = _day.SelectedIndex;
			_day.Items.Clear();
			CheckFrequency frequency = (CheckFrequency)_frequency.SelectedIndex;
			if (frequency == CheckFrequency.Weekly)
				_day.Items.AddRange(CultureInfo.CurrentCulture.DateTimeFormat.DayNames);
			else if (frequency == CheckFrequency.Monthly)
				_day.Items.AddRange(Enumerable.Range(1, 28).Select(d => (object)d.ToString(CultureInfo.CurrentCulture)).ToArray());

			bool hasDay = _day.Items.Count != 0;
			_day.Visible = hasDay;
			_on.Visible = hasDay;
			if (hasDay)
				_day.SelectedIndex = keep >= 0 && keep < _day.Items.Count ? keep : 0;
		}

		/// <summary>"Every Sunday at 3:00 AM" and the like.</summary>
		private static string Describe(ScheduledCheck check)
		{
			var p = Language.MainForm;
			DateTime time = DateTime.Today + check.Time;
			switch (check.Frequency)
			{
				case CheckFrequency.Daily:
					return string.Format(p.ScheduleChecks_DailyAt, time);
				case CheckFrequency.Weekly:
					return string.Format(p.ScheduleChecks_WeeklyAt, time, CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(check.Day));
				default:
					return string.Format(p.ScheduleChecks_MonthlyAt, time, check.DayOfMonth);
			}
		}

		private static string When(DateTime? value)
		{
			return value == null ? "-" : value.Value.ToString("g", CultureInfo.CurrentCulture);
		}

		private void Reload(string selectFolder)
		{
			try
			{
				_checks = ScheduledChecks.List();
			}
			catch (Exception ex)
			{
				Trace.WriteLine(ex);
				_checks = new List<ScheduledCheck>();
			}

			_list.BeginUpdate();
			_list.Items.Clear();
			foreach (ScheduledCheck check in _checks)
			{
				ListViewItem item = new ListViewItem(new[] { check.Folder, Describe(check), When(check.NextRun), When(check.LastRun) });
				item.Tag = check;
				_list.Items.Add(item);
				if (selectFolder != null && string.Equals(check.Folder, selectFolder, StringComparison.OrdinalIgnoreCase))
					item.Selected = true;
			}
			_list.EndUpdate();
			_remove.Enabled = _list.SelectedItems.Count != 0;
		}

		private void List_SelectedIndexChanged(object sender, EventArgs e)
		{
			_remove.Enabled = _list.SelectedItems.Count != 0;
			if (_list.SelectedItems.Count == 0)
				return;

			ScheduledCheck check = (ScheduledCheck)_list.SelectedItems[0].Tag;
			_folder.Text = check.Folder;
			_frequency.SelectedIndex = (int)check.Frequency;
			FillDays();
			if (check.Frequency == CheckFrequency.Weekly)
				_day.SelectedIndex = (int)check.Day;
			else if (check.Frequency == CheckFrequency.Monthly)
				_day.SelectedIndex = Math.Min(Math.Max(check.DayOfMonth, 1), 28) - 1;
			_time.Value = DateTime.Today + check.Time;
		}

		private void Browse_Click(object sender, EventArgs e)
		{
			using (FolderBrowserDialog dialog = new FolderBrowserDialog())
			{
				dialog.SelectedPath = _folder.Text;
				if (dialog.ShowDialog(this) == DialogResult.OK)
					_folder.Text = dialog.SelectedPath;
			}
		}

		private void Save_Click(object sender, EventArgs e)
		{
			var p = Language.MainForm;
			string folder = _folder.Text.Trim();
			if (folder.Length == 0 || !Directory.Exists(folder))
			{
				MessageBox.Show(this, string.Format(p.ScheduleChecks_FolderNotFound, folder), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			folder = Path.GetFullPath(folder);

			ScheduledCheck check = new ScheduledCheck
			{
				Folder = folder,
				Frequency = (CheckFrequency)_frequency.SelectedIndex,
				Day = _frequency.SelectedIndex == (int)CheckFrequency.Weekly ? (DayOfWeek)_day.SelectedIndex : DayOfWeek.Sunday,
				DayOfMonth = _frequency.SelectedIndex == (int)CheckFrequency.Monthly ? _day.SelectedIndex + 1 : 1,
				Time = new TimeSpan(_time.Value.Hour, _time.Value.Minute, 0)
			};

			try
			{
				ScheduledChecks.Save(check, Program.ExecutablePath);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, string.Format(p.ScheduleChecks_Error, ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			Reload(folder);
		}

		private void Remove_Click(object sender, EventArgs e)
		{
			if (_list.SelectedItems.Count == 0)
				return;

			ScheduledCheck check = (ScheduledCheck)_list.SelectedItems[0].Tag;
			try
			{
				ScheduledChecks.Remove(check.TaskName);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, string.Format(Language.MainForm.ScheduleChecks_Error, ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			Reload(null);
		}
	}
}
