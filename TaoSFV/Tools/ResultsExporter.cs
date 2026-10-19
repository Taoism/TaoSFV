using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using TaoSFV.Model.Workset;

namespace TaoSFV
{
	public enum ResultsFormat
	{
		Text,
		Csv,
		Html
	}

	/// <summary>The words used in saved results (English by default; the app fills them from the language file).</summary>
	public sealed class ResultsText
	{
		public ResultsText()
		{
			Title = "TaoSFV results, {0:yyyy-MM-dd HH:mm}";
			Summary = "{0:#,0} files in {1:#,0} sets: {2}";
			BadDetail = "expected {0}, got {1}";
			States = new Dictionary<ChecksumFileState, string>
			{
				{ ChecksumFileState.NotProcessed, "Not checked" },
				{ ChecksumFileState.OK, "OK" },
				{ ChecksumFileState.Bad, "Bad" },
				{ ChecksumFileState.Missing, "Missing" },
				{ ChecksumFileState.Unreadable, "Unreadable" }
			};
		}

		/// <summary>{0}: when the results were saved.</summary>
		public string Title { get; set; }

		/// <summary>{0}: files, {1}: sets, {2}: the count of each result.</summary>
		public string Summary { get; set; }

		/// <summary>{0}: checksum in the file, {1}: checksum computed.</summary>
		public string BadDetail { get; set; }

		public Dictionary<ChecksumFileState, string> States { get; private set; }

		public string StateName(ChecksumFileState state)
		{
			string name;
			return States.TryGetValue(state, out name) ? name : state.ToString();
		}
	}

	/// <summary>
	/// File > Save Results: writes what a run found, per set and file, as plain text, CSV (for spreadsheets) or a
	/// standalone HTML page.
	/// </summary>
	public static class ResultsExporter
	{
		/// <summary>The format for a file name's extension (.csv, .htm/.html, anything else text).</summary>
		public static ResultsFormat FormatFromFileName(string fileName)
		{
			string ext = Path.GetExtension(fileName).ToLowerInvariant();
			if (ext == ".csv")
				return ResultsFormat.Csv;
			if (ext == ".htm" || ext == ".html")
				return ResultsFormat.Html;
			return ResultsFormat.Text;
		}

		public static void Save(string fileName, IList<ChecksumSet> sets, ResultsText text, DateTime when)
		{
			ResultsFormat format = FormatFromFileName(fileName);
			// a BOM so Excel reads a CSV's non-ASCII names correctly
			Encoding encoding = new UTF8Encoding(format == ResultsFormat.Csv);
			using (StreamWriter writer = new StreamWriter(fileName, false, encoding))
				Write(writer, sets, format, text, when);
		}

		public static void Write(TextWriter writer, IList<ChecksumSet> sets, ResultsFormat format, ResultsText text, DateTime when)
		{
			switch (format)
			{
				case ResultsFormat.Csv:
					WriteCsv(writer, sets, text);
					break;
				case ResultsFormat.Html:
					WriteHtml(writer, sets, text, when);
					break;
				default:
					WriteText(writer, sets, text, when);
					break;
			}
		}

		/// <summary>"1,234 files in 5 sets: 1,200 OK, 30 bad, ..." (states with no files left out).</summary>
		public static string Summary(IList<ChecksumSet> sets, ResultsText text)
		{
			List<ChecksumFile> files = sets.SelectMany(s => s.Files).ToList();
			IEnumerable<string> counts = Enum.GetValues(typeof(ChecksumFileState)).Cast<ChecksumFileState>()
				.OrderBy(StateOrder)
				.Select(state => new { state, count = files.Count(f => f.State == state) })
				.Where(p => p.count != 0)
				.Select(p => string.Format(CultureInfo.CurrentCulture, "{0:#,0} {1}", p.count, text.StateName(p.state)));
			return string.Format(CultureInfo.CurrentCulture, text.Summary, files.Count, sets.Count, string.Join(", ", counts));
		}

		// OK first, then problems, then files not checked
		private static int StateOrder(ChecksumFileState state)
		{
			return state == ChecksumFileState.NotProcessed ? 99 : (int)state;
		}

		private static string Detail(ChecksumFile file, ResultsText text)
		{
			if (file.State == ChecksumFileState.Bad)
				return string.Format(text.BadDetail, file.OriginalChecksum, file.CurrentChecksum);
			if (file.State == ChecksumFileState.Unreadable)
				return file.ErrorMessage;
			return null;
		}

		private static void WriteText(TextWriter writer, IList<ChecksumSet> sets, ResultsText text, DateTime when)
		{
			writer.WriteLine(string.Format(text.Title, when));
			writer.WriteLine(Summary(sets, text));

			int width = Enum.GetValues(typeof(ChecksumFileState)).Cast<ChecksumFileState>().Max(s => text.StateName(s).Length);
			foreach (ChecksumSet set in sets)
			{
				writer.WriteLine();
				writer.WriteLine(set.VerificationFileName);
				foreach (ChecksumFile file in set.Files)
				{
					string detail = Detail(file, text);
					writer.WriteLine("  " + text.StateName(file.State).PadRight(width) + "  " + file.FileName + (detail == null ? "" : "  (" + detail + ")"));
				}
			}
		}

		private static void WriteCsv(TextWriter writer, IList<ChecksumSet> sets, ResultsText text)
		{
			writer.WriteLine("Checksum file,File,Result,Expected,Actual,Size,Error");
			foreach (ChecksumSet set in sets)
			{
				foreach (ChecksumFile file in set.Files)
				{
					writer.WriteLine(string.Join(",", new[]
					{
						Csv(set.VerificationFileName),
						Csv(file.FileName),
						Csv(text.StateName(file.State)),
						Csv(file.OriginalChecksum),
						Csv(file.CurrentChecksum),
						file.FileInfo != null && file.FileInfo.Exists ? file.FileInfo.Length.ToString(CultureInfo.InvariantCulture) : "",
						Csv(file.State == ChecksumFileState.Unreadable ? file.ErrorMessage : null)
					}));
				}
			}
		}

		private static string Csv(string value)
		{
			if (string.IsNullOrEmpty(value))
				return "";
			// quoted when needed; a leading =, +, - or @ is quoted too so spreadsheets don't run it as a formula
			if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || "=+-@".IndexOf(value[0]) >= 0)
				return "\"" + value.Replace("\"", "\"\"") + "\"";
			return value;
		}

		private static void WriteHtml(TextWriter writer, IList<ChecksumSet> sets, ResultsText text, DateTime when)
		{
			string title = string.Format(text.Title, when);
			writer.WriteLine("<!doctype html>");
			writer.WriteLine("<html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
			writer.WriteLine("<title>" + H(title) + "</title>");
			writer.WriteLine("<style>");
			writer.WriteLine(":root { color-scheme: light dark; --bg: #fff; --fg: #1b1b1b; --muted: #666; --line: #ddd; --set: #0b5cad; --ok: #1a7f37; --bad: #c62828; --missing: #b26a00; --unreadable: #7b3fb5; }");
			writer.WriteLine("@media (prefers-color-scheme: dark) { :root { --bg: #1e1e1e; --fg: #e8e8e8; --muted: #a0a0a0; --line: #3a3a3a; --set: #99c8ff; --ok: #6fcf7f; --bad: #ff7a7a; --missing: #f0b050; --unreadable: #c89bf0; } }");
			writer.WriteLine("body { background: var(--bg); color: var(--fg); font: 14px/1.4 \"Segoe UI\", system-ui, sans-serif; margin: 24px; }");
			writer.WriteLine("h1 { font-size: 20px; margin: 0 0 4px; } p.summary { color: var(--muted); margin: 0 0 16px; }");
			writer.WriteLine("table { border-collapse: collapse; width: 100%; } td { padding: 3px 10px 3px 0; border-bottom: 1px solid var(--line); vertical-align: top; overflow-wrap: anywhere; }");
			writer.WriteLine("tr.set td { color: var(--set); font-weight: 600; padding-top: 14px; } td.state { white-space: nowrap; font-weight: 600; } td.detail { color: var(--muted); }");
			writer.WriteLine(".OK { color: var(--ok); } .Bad { color: var(--bad); } .Missing { color: var(--missing); } .Unreadable { color: var(--unreadable); } .NotProcessed { color: var(--muted); }");
			writer.WriteLine("</style></head><body>");
			writer.WriteLine("<h1>" + H(title) + "</h1>");
			writer.WriteLine("<p class=\"summary\">" + H(Summary(sets, text)) + "</p>");
			writer.WriteLine("<table>");
			foreach (ChecksumSet set in sets)
			{
				writer.WriteLine("<tr class=\"set\"><td colspan=\"3\">" + H(set.VerificationFileName) + "</td></tr>");
				foreach (ChecksumFile file in set.Files)
				{
					writer.WriteLine(string.Format("<tr><td class=\"state {0}\">{1}</td><td>{2}</td><td class=\"detail\">{3}</td></tr>",
						file.State, H(text.StateName(file.State)), H(file.FileName), H(Detail(file, text))));
				}
			}
			writer.WriteLine("</table></body></html>");
		}

		private static string H(string text)
		{
			return WebUtility.HtmlEncode(text ?? "");
		}
	}
}
