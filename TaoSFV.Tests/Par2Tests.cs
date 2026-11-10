using System;
using System.IO;
using System.Linq;
using TaoSFV.Par2;
using Xunit;

namespace TaoSFV.Tests
{
	public class Par2Tests : IDisposable
	{
		private const int SliceSize = 4096;
		private readonly string _dir;

		public Par2Tests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(_dir, "CD1"));
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		private string Write(string name, int length, int seed)
		{
			byte[] data = new byte[length];
			new Random(seed).NextBytes(data);
			File.WriteAllBytes(Path.Combine(_dir, name), data);
			return Path.Combine(_dir, name);
		}

		// three files: 10 slices + a partial one, exactly 2 slices, and a small file in a subfolder
		private string MakeSet(int recoverySlices)
		{
			Write("a.bin", SliceSize * 10 + 123, 1);
			Write("b.bin", SliceSize * 2, 2);
			Write(@"CD1\c.bin", 100, 3);
			string par2 = Path.Combine(_dir, "set.par2");
			Par2TestWriter.Write(par2, _dir, new[] { "a.bin", "b.bin", @"CD1\c.bin" }, SliceSize, recoverySlices);
			return par2;
		}

		private static Par2FileResult Result(Par2Result result, string name)
		{
			return result.Files.Single(f => f.File.Name == name);
		}

		[Fact]
		public void ReadsTheSet()
		{
			Par2Set set = Par2Reader.Load(MakeSet(3));

			Assert.Equal(SliceSize, set.SliceSize);
			Assert.Equal(3, set.RecoveryFileIds.Count);
			Assert.Equal(new[] { "a.bin", "b.bin", @"CD1\c.bin" }, set.RecoveryFiles.Select(f => f.Name).OrderBy(n => n));
			Assert.Equal(11, set.Files.Values.Single(f => f.Name == "a.bin").Slices.Length);
			Assert.Equal(3, set.RecoverySlices.Count);
			Assert.Equal("TaoSFV tests", set.Creator);
			Assert.Equal(0, set.DamagedPackets);
		}

		[Fact]
		public void IntactFilesAreOk()
		{
			Par2Result result = Par2Verifier.Verify(Par2Reader.Load(MakeSet(0)));

			Assert.True(result.AllOk);
			Assert.Equal(0, result.SlicesNeeded);
			Assert.True(result.Repairable);
		}

		[Fact]
		public void DamagedSlicesAreCounted()
		{
			string par2 = MakeSet(2);
			string a = Path.Combine(_dir, "a.bin");
			byte[] data = File.ReadAllBytes(a);
			data[SliceSize * 3 + 5] ^= 0xFF;   // slice 3
			data[SliceSize * 10 + 7] ^= 0xFF;  // the partial last slice
			File.WriteAllBytes(a, data);

			Par2Result result = Par2Verifier.Verify(Par2Reader.Load(par2));
			Par2FileResult file = Result(result, "a.bin");

			Assert.Equal(Par2FileState.Damaged, file.State);
			Assert.Equal(11, file.TotalSlices);
			Assert.Equal(9, file.GoodSlices);
			Assert.False(file.SliceOk[3]);
			Assert.False(file.SliceOk[10]);
			Assert.Equal(2, result.SlicesNeeded);
			Assert.True(result.Repairable);       // 2 needed, 2 available
		}

		[Fact]
		public void TruncatedFileLosesItsLastSlices()
		{
			string par2 = MakeSet(1);
			string a = Path.Combine(_dir, "a.bin");
			using (FileStream stream = new FileStream(a, FileMode.Open))
				stream.SetLength(SliceSize * 8);   // slices 8, 9 and 10 gone

			Par2Result result = Par2Verifier.Verify(Par2Reader.Load(par2));

			Assert.Equal(8, Result(result, "a.bin").GoodSlices);
			Assert.Equal(3, result.SlicesNeeded);
			Assert.False(result.Repairable);      // 3 needed, 1 available
		}

		[Fact]
		public void MissingAndRenamedFiles()
		{
			string par2 = MakeSet(0);
			File.Move(Path.Combine(_dir, "b.bin"), Path.Combine(_dir, "b (1).bin"));
			File.Delete(Path.Combine(_dir, "CD1", "c.bin"));

			Par2Result result = Par2Verifier.Verify(Par2Reader.Load(par2));

			Par2FileResult b = Result(result, "b.bin");
			Assert.Equal(Par2FileState.Renamed, b.State);
			Assert.Equal(Path.Combine(_dir, "b (1).bin"), b.FoundAs);
			Assert.Equal(Par2FileState.Missing, Result(result, @"CD1\c.bin").State);
			Assert.Equal(1, result.SlicesNeeded);
		}

		[Fact]
		public void VolumeFilesBelongToTheSet()
		{
			string par2 = MakeSet(0);
			// recovery slices in a separate volume file, as par2cmdline writes them
			string volume = Path.Combine(_dir, "set.vol00+02.par2");
			Par2TestWriter.Write(volume, _dir, new[] { "a.bin", "b.bin", @"CD1\c.bin" }, SliceSize, 2);
			Write("other.par2", 10, 9);

			Assert.Equal(new[] { "set.par2", "set.vol00+02.par2" }, Par2Reader.FindSetFiles(par2).Select(Path.GetFileName));
			Assert.Equal(2, Par2Reader.Load(par2).RecoverySlices.Count);
			Assert.Equal("set", Par2Reader.GetBaseName("set.vol123+456.par2"));
			Assert.Equal("my.vol.set", Par2Reader.GetBaseName("my.vol.set.par2"));
		}

		[Fact]
		public void DamagedPar2DataIsSkipped()
		{
			string par2 = MakeSet(4);
			byte[] bytes = File.ReadAllBytes(par2);

			// garbage before the first packet, and one recovery packet's data corrupted
			int lastPacket = LastIndexOf(bytes, Par2Reader.Magic);
			bytes[lastPacket + 100] ^= 0xFF;
			byte[] damaged = new byte[] { 1, 2, 3, 4, 5 }.Concat(bytes).ToArray();
			File.WriteAllBytes(par2, damaged);

			Par2Set set = Par2Reader.Load(par2);

			Assert.Equal(3, set.Files.Count);
			Assert.Equal(3, set.RecoverySlices.Count);
			Assert.Equal(1, set.DamagedPackets);
			Assert.True(Par2Verifier.Verify(set).AllOk);
		}

		[Fact]
		public void NoMainPacketIsAnError()
		{
			string path = Write("junk.par2", 1000, 4);
			Assert.Throws<InvalidDataException>(() => Par2Reader.Load(path));
		}

		[Fact]
		public void NamesAreUtf8OrWindows1252()
		{
			Assert.Equal(@"Disc 1\Ä.flac", Par2Reader.DecodeName(System.Text.Encoding.UTF8.GetBytes("Disc 1/Ä.flac\0\0")));
			Assert.Equal("Ä.flac", Par2Reader.DecodeName(new byte[] { 0xC4, (byte)'.', (byte)'f', (byte)'l', (byte)'a', (byte)'c' }));
		}

		private static int LastIndexOf(byte[] haystack, byte[] needle)
		{
			return haystack.AsSpan().LastIndexOf(needle);
		}
	}
}
