using System;
using System.IO;
using TaoSFV.Model.Workset;
using Xunit;

namespace TaoSFV.Tests
{
	public class ResultsExporterTests
	{
		private static readonly DateTime When = new DateTime(2026, 10, 18, 21, 30, 0);

		private static ChecksumSet Sample()
		{
			ChecksumSet set = new ChecksumSet(@"D:\Music\Album\album.sfv", @"D:\Music\Album", ChecksumType.SFV);
			set.Files.Add(new ChecksumFile(set) { FileName = "01.flac", OriginalChecksum = "AAAA0001", CurrentChecksum = "aaaa0001", State = ChecksumFileState.OK });
			set.Files.Add(new ChecksumFile(set) { FileName = "02, \"live\".flac", OriginalChecksum = "AAAA0002", CurrentChecksum = "bbbb0002", State = ChecksumFileState.Bad });
			set.Files.Add(new ChecksumFile(set) { FileName = "03.flac", OriginalChecksum = "AAAA0003", State = ChecksumFileState.Missing });
			set.Files.Add(new ChecksumFile(set) { FileName = "=04.flac", OriginalChecksum = "AAAA0004", State = ChecksumFileState.Unreadable, ErrorMessage = "locked" });
			return set;
		}

		private static string Write(ResultsFormat format)
		{
			StringWriter writer = new StringWriter();
			ResultsExporter.Write(writer, new[] { Sample() }, format, new ResultsText(), When);
			return writer.ToString();
		}

		[Fact]
		public void Summary()
		{
			Assert.Equal("4 files in 1 sets: 1 OK, 1 Bad, 1 Missing, 1 Unreadable", ResultsExporter.Summary(new[] { Sample() }, new ResultsText()));
		}

		[Fact]
		public void Text()
		{
			string text = Write(ResultsFormat.Text);

			Assert.StartsWith("TaoSFV results, 2026-10-18 21:30" + Environment.NewLine, text);
			Assert.Contains(@"D:\Music\Album\album.sfv", text);
			Assert.Contains("  Bad          02, \"live\".flac  (expected aaaa0002, got bbbb0002)", text);
			Assert.Contains("  Unreadable   =04.flac  (locked)", text);
		}

		[Fact]
		public void CsvQuotesCommasQuotesAndFormulaCharacters()
		{
			string[] lines = Write(ResultsFormat.Csv).Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

			Assert.Equal("Checksum file,File,Result,Expected,Actual,Size,Error", lines[0]);
			Assert.Equal(@"D:\Music\Album\album.sfv,""02, """"live"""".flac"",Bad,aaaa0002,bbbb0002,,", lines[2]);
			Assert.Equal(@"D:\Music\Album\album.sfv,""=04.flac"",Unreadable,aaaa0004,,,locked", lines[4]);
		}

		[Fact]
		public void HtmlIsEscaped()
		{
			string html = Write(ResultsFormat.Html);

			Assert.StartsWith("<!doctype html>", html);
			Assert.Contains("<td class=\"state Bad\">Bad</td><td>02, &quot;live&quot;.flac</td>", html);
			Assert.Contains("prefers-color-scheme: dark", html);
		}

		[Theory]
		[InlineData("r.csv", ResultsFormat.Csv)]
		[InlineData("r.HTML", ResultsFormat.Html)]
		[InlineData("r.htm", ResultsFormat.Html)]
		[InlineData("r.txt", ResultsFormat.Text)]
		public void FormatFromExtension(string name, ResultsFormat format)
		{
			Assert.Equal(format, ResultsExporter.FormatFromFileName(name));
		}
	}
}
