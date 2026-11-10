using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TaoSFV.Tests
{
	public class CommentTemplateTests : IDisposable
	{
		private const string RenamedDefault = " Using ilSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}\r\n https://github.com/judwhite/ilSFV";
		private const string IlsfvDefault = " Using ilSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}\r\n http://cdtag.com/ilsfv";

		private readonly string _dir;

		public CommentTemplateTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose()
		{
			SqliteConnection.ClearAllPools();
			Directory.Delete(_dir, true);
		}

		[Theory]
		[InlineData(RenamedDefault)]
		[InlineData(IlsfvDefault)]
		[InlineData(" Using TaoSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}\r\n https://github.com/judwhite/ilSFV")] // before the TaoSFV repository
		[InlineData(" Using ilSFV on {0:yyyy-MM-dd} at {0:HH:mm.ss}\n http://cdtag.com/ilsfv  \n")] // LF, trailing spaces
		public void RecognizesOldDefaults(string header)
		{
			Assert.True(CommentTemplate.IsOldDefault(header));
		}

		[Theory]
		[InlineData(" Using ilSFV on {0:yyyy-MM-dd}")]                      // edited
		[InlineData(" My own header")]
		[InlineData("")]
		[InlineData(null)]
		public void KeepsOtherHeaders(string header)
		{
			Assert.False(CommentTemplate.IsOldDefault(header));
		}

		[Fact]
		public void CurrentDefaultIsUpToDate()
		{
			Assert.False(CommentTemplate.IsOldDefault(CommentTemplate.DefaultHeader));
			Assert.False(CommentTemplate.IsOutdated(CommentTemplate.DefaultHeader));
		}

		[Theory]
		[InlineData(" Made with ilSFV by me", " Made with TaoSFV by me")]
		[InlineData(" see http://www.cdtag.com/ilsfv for more", " see https://github.com/Taoism/TaoSFV for more")]
		[InlineData(" cdtag.com", " https://github.com/Taoism/TaoSFV")]
		[InlineData(" https://github.com/judwhite/ilSFV", " https://github.com/judwhite/ilSFV")] // a link, not the name
		[InlineData(" ilSFV.exe and ilSFV", " TaoSFV.exe and TaoSFV")]
		[InlineData(" nothing to change", " nothing to change")]
		public void Modernize(string text, string expected)
		{
			Assert.Equal(expected, CommentTemplate.Modernize(text));
			Assert.Equal(text != expected, CommentTemplate.IsOutdated(text));
		}

		[Theory]
		[InlineData(RenamedDefault, true)]
		[InlineData(" My ilSFV header", false)] // the user's own text is only changed when they agree
		public void SettingsUpgradeReplacesTheOldDefaultHeader(string header, bool replaced)
		{
			using (SqliteConnection conn = Database.Open(Path.Combine(_dir, "settings.db")))
			{
				Database.UpgradeSettings(conn);
				// as a version 3 database (before this upgrade) with the given header
				Database.Execute(conn, "update DatabaseSetting set Version = 3;");
				using (SqliteCommand cmd = new SqliteCommand("update Comments set Header = @Header;", conn))
				{
					cmd.Parameters.AddWithValue("@Header", header);
					cmd.ExecuteNonQuery();
				}

				Assert.False(Database.UpgradeSettings(conn)); // not a new database

				string now = Convert.ToString(Database.GetTable(conn, "select Header from Comments;").Rows[0][0]);
				Assert.Equal(replaced ? CommentTemplate.DefaultHeader : header, now);
			}
		}
	}
}
