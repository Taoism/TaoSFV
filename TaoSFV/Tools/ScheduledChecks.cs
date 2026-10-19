using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace TaoSFV
{
	public enum CheckFrequency
	{
		Daily,
		Weekly,
		Monthly
	}

	/// <summary>A folder checked on a schedule by a task in Task Scheduler's TaoSFV folder.</summary>
	public sealed class ScheduledCheck
	{
		public string Folder { get; set; }
		public CheckFrequency Frequency { get; set; }

		/// <summary>Weekly: the day of the week.</summary>
		public DayOfWeek Day { get; set; }

		/// <summary>Monthly: the day of the month (1-28).</summary>
		public int DayOfMonth { get; set; }

		public TimeSpan Time { get; set; }

		// from Task Scheduler, for listing
		public string TaskName { get; set; }
		public DateTime? NextRun { get; set; }
		public DateTime? LastRun { get; set; }
	}

	/// <summary>
	/// Creates, lists and removes scheduled checks through the Task Scheduler COM API. Tasks run as the signed-in
	/// user (no password or admin rights needed), so they run while that user is signed in; one missed while the
	/// PC was off runs when it can.
	/// </summary>
	public static class ScheduledChecks
	{
		private const string TaskFolder = "TaoSFV";
		private const int TASK_CREATE_OR_UPDATE = 6;
		private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

		public static List<ScheduledCheck> List()
		{
			List<ScheduledCheck> result = new List<ScheduledCheck>();
			dynamic folder = GetFolder(false);
			if (folder == null)
				return result;

			foreach (dynamic task in folder.GetTasks(0))
			{
				ScheduledCheck check = Parse((string)task.Xml);
				if (check == null)
					continue;
				check.TaskName = (string)task.Name;
				check.NextRun = ValidDate((DateTime)task.NextRunTime);
				check.LastRun = ValidDate((DateTime)task.LastRunTime);
				result.Add(check);
			}
			return result.OrderBy(c => c.Folder, StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>Adds or replaces the scheduled check for a folder.</summary>
		public static void Save(ScheduledCheck check, string executable)
		{
			dynamic folder = GetFolder(true);
			folder.RegisterTask(TaskName(check.Folder), BuildXml(check, executable, DateTime.Now), TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
		}

		public static void Remove(string taskName)
		{
			dynamic folder = GetFolder(false);
			if (folder != null)
				folder.DeleteTask(taskName, 0);
		}

		/// <summary>One task per folder: "Check D_ Music" and so on.</summary>
		public static string TaskName(string folder)
		{
			return "Check " + ScheduledCheckRun.ReportName(folder);
		}

		private static dynamic GetFolder(bool create)
		{
			dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
			service.Connect();
			dynamic root = service.GetFolder("\\");
			try
			{
				return root.GetFolder(TaskFolder);
			}
			catch (COMException)
			{
				return create ? root.CreateFolder(TaskFolder) : null;
			}
			catch (System.IO.FileNotFoundException)
			{
				return create ? root.CreateFolder(TaskFolder) : null;
			}
		}

		private static DateTime? ValidDate(DateTime value)
		{
			// Task Scheduler reports "never" as 1899-12-30
			return value.Year < 2000 ? (DateTime?)null : value;
		}

		/// <summary>The task definition: a calendar trigger at the next matching time, running "exe /scheduled folder".</summary>
		public static string BuildXml(ScheduledCheck check, string executable, DateTime now)
		{
			string schedule;
			switch (check.Frequency)
			{
				case CheckFrequency.Daily:
					schedule = "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>";
					break;
				case CheckFrequency.Weekly:
					schedule = "<ScheduleByWeek><DaysOfWeek><" + check.Day + " /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>";
					break;
				default:
					schedule = "<ScheduleByMonth><DaysOfMonth><Day>" + check.DayOfMonth + "</Day></DaysOfMonth><Months>" +
						string.Concat(CultureInfo.InvariantCulture.DateTimeFormat.MonthNames.Take(12).Select(m => "<" + m + " />")) +
						"</Months></ScheduleByMonth>";
					break;
			}

			// the trigger starts today at the chosen time; Task Scheduler works out the first matching day
			DateTime start = now.Date + check.Time;

			StringBuilder xml = new StringBuilder();
			xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
			xml.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
			xml.AppendLine("  <RegistrationInfo><Description>" + SecurityElement.Escape("TaoSFV checks " + check.Folder + " and notifies you only if a file is bad, missing or unreadable.") + "</Description></RegistrationInfo>");
			xml.AppendLine("  <Triggers><CalendarTrigger><StartBoundary>" + start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "</StartBoundary><Enabled>true</Enabled>" + schedule + "</CalendarTrigger></Triggers>");
			xml.AppendLine("  <Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>");
			xml.AppendLine("  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
				"<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" +
				"<Priority>7</Priority><Enabled>true</Enabled></Settings>");
			// a backslash right before the closing quote would escape it: "D:\" is written "D:\\"
			string folderArgument = check.Folder.TrimEnd('\\');
			if (folderArgument.EndsWith(":"))
				folderArgument += "\\\\";
			xml.AppendLine("  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(executable) + "</Command><Arguments>" +
				SecurityElement.Escape(ScheduledCheckRun.Argument + " \"" + folderArgument + "\"") + "</Arguments></Exec></Actions>");
			xml.AppendLine("</Task>");
			return xml.ToString();
		}

		/// <summary>Reads a check back from a task definition made by BuildXml; null for anything else.</summary>
		public static ScheduledCheck Parse(string xml)
		{
			Match args = Regex.Match(xml, "<Arguments>" + Regex.Escape(ScheduledCheckRun.Argument) + " &quot;(.*?)&quot;</Arguments>|<Arguments>" + Regex.Escape(ScheduledCheckRun.Argument) + " \"(.*?)\"</Arguments>");
			Match start = Regex.Match(xml, "<StartBoundary>[^<]*T(\\d\\d):(\\d\\d)");
			if (!args.Success || !start.Success)
				return null;

			string folder = Unescape(args.Groups[1].Success ? args.Groups[1].Value : args.Groups[2].Value);
			if (folder.EndsWith(":\\\\"))
				folder = folder.Substring(0, folder.Length - 1);

			ScheduledCheck check = new ScheduledCheck
			{
				Folder = folder,
				Time = new TimeSpan(int.Parse(start.Groups[1].Value), int.Parse(start.Groups[2].Value), 0),
				DayOfMonth = 1
			};

			Match week = Regex.Match(xml, "<DaysOfWeek>\\s*<(\\w+)\\s*/>");
			Match month = Regex.Match(xml, "<DaysOfMonth>\\s*<Day>(\\d+)</Day>");
			if (week.Success)
			{
				check.Frequency = CheckFrequency.Weekly;
				check.Day = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), week.Groups[1].Value);
			}
			else if (month.Success)
			{
				check.Frequency = CheckFrequency.Monthly;
				check.DayOfMonth = int.Parse(month.Groups[1].Value);
			}
			else
			{
				check.Frequency = CheckFrequency.Daily;
			}
			return check;
		}

		private static string Unescape(string text)
		{
			return text.Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
		}
	}
}
