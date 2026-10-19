using System;
using System.IO;
using System.Linq;
using System.Threading;
using TaoSFV.Model.Workset;
using TaoSFV.Verification;
using Xunit;

namespace TaoSFV.Tests
{
	public class ScheduledCheckTests : IDisposable
	{
		private readonly string _dir;

		public ScheduledCheckTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		[Theory]
		[InlineData(@"D:\Music & More\Rock", CheckFrequency.Weekly, DayOfWeek.Sunday, 1, 3, 0)]
		[InlineData(@"E:\", CheckFrequency.Daily, DayOfWeek.Sunday, 1, 23, 45)]
		[InlineData(@"\\nas\share\photos", CheckFrequency.Monthly, DayOfWeek.Sunday, 15, 2, 30)]
		public void TaskXmlRoundTrips(string folder, CheckFrequency frequency, DayOfWeek day, int dayOfMonth, int hour, int minute)
		{
			ScheduledCheck check = new ScheduledCheck { Folder = folder, Frequency = frequency, Day = day, DayOfMonth = dayOfMonth, Time = new TimeSpan(hour, minute, 0) };

			string xml = ScheduledChecks.BuildXml(check, @"C:\Program Files\TaoSFV\TaoSFV.exe", new DateTime(2026, 10, 18, 12, 0, 0));
			ScheduledCheck back = ScheduledChecks.Parse(xml);

			Assert.Equal(folder, back.Folder);
			Assert.Equal(frequency, back.Frequency);
			Assert.Equal(check.Time, back.Time);
			if (frequency == CheckFrequency.Weekly)
				Assert.Equal(day, back.Day);
			if (frequency == CheckFrequency.Monthly)
				Assert.Equal(dayOfMonth, back.DayOfMonth);
			Assert.Contains("<StartWhenAvailable>true</StartWhenAvailable>", xml);
			Assert.Contains("<LogonType>InteractiveToken</LogonType>", xml);
		}

		[Fact]
		public void DriveRootArgumentDoesNotEscapeTheQuote()
		{
			string xml = ScheduledChecks.BuildXml(new ScheduledCheck { Folder = @"E:\", Time = TimeSpan.Zero }, "TaoSFV.exe", DateTime.Today);

			// "E:\\" on the command line is E:\ (a lone backslash before the quote would escape it)
			Assert.Contains("<Arguments>/scheduled &quot;E:\\\\&quot;</Arguments>", xml);
		}

		[Theory]
		[InlineData(@"D:\Music\Rock", "D_ Music Rock")]
		[InlineData(@"D:\Music\Rock\", "D_ Music Rock")]
		[InlineData(@"\\nas\share", "nas share")]
		public void ReportNames(string folder, string name)
		{
			Assert.Equal(name, ScheduledCheckRun.ReportName(folder));
		}

		[Fact]
		public void FolderCheckReadsEveryFileAndReportsProblems()
		{
			File.WriteAllText(Path.Combine(_dir, "a.bin"), "a");
			File.WriteAllText(Path.Combine(_dir, "b.bin"), "b");
			string crcA;
			using (FileStream s = File.OpenRead(Path.Combine(_dir, "a.bin")))
				crcA = TaoSFV.Hash.ChecksumAlgorithm.Sfv.Compute(s);
			File.WriteAllText(Path.Combine(_dir, "set.sfv"), "a.bin " + crcA + "\r\nb.bin 00000000\r\nc.bin 11111111\r\n");

			LoadResult result = FolderCheck.Run(new[] { _dir }, 0, CancellationToken.None);

			Assert.Equal(new[] { ChecksumFileState.OK, ChecksumFileState.Bad, ChecksumFileState.Missing }, result.Sets[0].Files.Select(f => f.State));
			Assert.False(FolderCheck.AllOK(result));
		}

		[Fact]
		public void MissingFolderIsAProblem()
		{
			LoadResult result = FolderCheck.Run(new[] { Path.Combine(_dir, "unplugged") }, 0, CancellationToken.None);

			Assert.False(FolderCheck.AllOK(result));
		}
	}
}
