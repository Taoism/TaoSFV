using System;
using System.Text.RegularExpressions;

namespace TaoSFV
{
	/// <summary>
	/// The comments header written at the top of new checksum files, and bringing headers from ilSFV (or from TaoSFV
	/// before it was renamed) up to date.
	/// </summary>
	internal static class CommentTemplate
	{
		/// <summary>TaoSFV's page (the repository is published there).</summary>
		public const string Url = "https://github.com/Taoism/TaoSFV";

		public static readonly string DefaultHeader = " Using TaoSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}" + Environment.NewLine + " " + Url;

		// ilSFV 1.x's default, TaoSFV's own before the rename, and TaoSFV's linking to the original ilSFV repository
		private static readonly string[] OldDefaultHeaders =
		{
			" Using ilSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}\n http://cdtag.com/ilsfv",
			" Using ilSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}\n https://github.com/judwhite/ilSFV",
			" Using TaoSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}\n https://github.com/judwhite/ilSFV"
		};

		// a cdtag.com link (the site is gone), and "ilSFV" as a name rather than inside a link such as .../judwhite/ilSFV
		private static readonly Regex CdtagLink = new Regex(@"(https?://)?(www\.)?cdtag\.com(/[^\s""'<>]*)?", RegexOptions.IgnoreCase);
		private static readonly Regex IlsfvName = new Regex(@"(?<![\w./-])ilSFV\b");

		/// <summary>True for a header nobody changed: one of the old defaults (line endings and trailing spaces aside).</summary>
		public static bool IsOldDefault(string header)
		{
			string normalized = Normalize(header);
			foreach (string old in OldDefaultHeaders)
			{
				if (normalized == old)
					return true;
			}
			return false;
		}

		/// <summary>The text with "ilSFV" as a name changed to "TaoSFV" and cdtag.com links pointed at TaoSFV's page.</summary>
		public static string Modernize(string text)
		{
			if (string.IsNullOrEmpty(text))
				return text;
			string result = CdtagLink.Replace(text, Url);
			return IlsfvName.Replace(result, "TaoSFV");
		}

		/// <summary>True if Modernize would change the text.</summary>
		public static bool IsOutdated(string text)
		{
			return Modernize(text) != text;
		}

		private static string Normalize(string text)
		{
			if (text == null)
				return "";
			string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			for (int i = 0; i < lines.Length; i++)
				lines[i] = lines[i].TrimEnd();
			return string.Join("\n", lines).TrimEnd('\n');
		}
	}
}
