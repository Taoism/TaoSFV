using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TaoSFV.Par2;
using Xunit;

namespace TaoSFV.Tests
{
	/// <summary>
	/// A set made by par2cmdline 1.4.0 (Fixtures\par2cmdline):
	///   par2 create -s4096 -c8 -n2 set.par2 "01 Intro.flac" 02.flac "CD1\03 Ünïcode.flac" tiny.txt
	/// 4 files (41000, 32768, 9001 and 77 bytes), 4 KB blocks, 8 recovery blocks in set.vol0+4.par2 and set.vol4+4.par2.
	/// </summary>
	public class Par2CompatibilityTests : IDisposable
	{
		private static readonly string[] Names = { "01 Intro.flac", "02.flac", @"CD1\03 Ünïcode.flac", "tiny.txt" };
		private readonly string _dir;

		public Par2CompatibilityTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "par2cmdline");
			foreach (string file in Directory.GetFiles(fixture, "*", SearchOption.AllDirectories))
			{
				string target = Path.Combine(_dir, Path.GetRelativePath(fixture, file));
				Directory.CreateDirectory(Path.GetDirectoryName(target));
				File.Copy(file, target);
			}
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		private string Par2
		{
			get { return Path.Combine(_dir, "set.par2"); }
		}

		[Fact]
		public void ReadsTheSet()
		{
			Par2Set set = Par2Reader.Load(Par2);

			Assert.Equal(4096, set.SliceSize);
			Assert.Equal(Names.OrderBy(n => n), set.RecoveryFiles.Select(f => f.Name).OrderBy(n => n));
			Assert.Equal(Enumerable.Range(0, 8), set.RecoverySlices.Keys.OrderBy(e => e));
			Assert.Equal(3, set.Par2Files.Count);
			Assert.StartsWith("Created by par2cmdline", set.Creator);
			Assert.Equal(0, set.DamagedPackets);
			Assert.True(Par2Verifier.Verify(set).AllOk);
		}

		[Fact]
		public void RecoveryDataMatchesPar2cmdline()
		{
			// the strongest check of the maths: same field, constants, file order and exponents give the same bytes
			Par2Set set = Par2Reader.Load(Par2);
			int[] exponents = set.RecoverySlices.Keys.OrderBy(e => e).ToArray();
			byte[][] ours = Par2Encoder.ComputeRecovery(set, exponents);

			for (int i = 0; i < exponents.Length; i++)
			{
				Par2RecoverySlice slice = set.RecoverySlices[exponents[i]];
				byte[] theirs = new byte[set.SliceSize];
				using (FileStream stream = File.OpenRead(slice.FileName))
				{
					stream.Position = slice.Offset;
					stream.ReadExactly(theirs);
				}
				Assert.True(theirs.AsSpan().SequenceEqual(ours[i]), "recovery block " + exponents[i] + " differs");
			}
		}

		[Fact]
		public void RepairsDamageInAPar2cmdlineSet()
		{
			byte[][] originals = Names.Select(n => File.ReadAllBytes(Path.Combine(_dir, n))).ToArray();

			byte[] intro = File.ReadAllBytes(Path.Combine(_dir, Names[0]));
			intro[5000] ^= 1;      // block 1
			intro[40999] ^= 1;     // the last, partial block
			File.WriteAllBytes(Path.Combine(_dir, Names[0]), intro);
			File.Delete(Path.Combine(_dir, Names[2]));                                   // 3 blocks
			File.Move(Path.Combine(_dir, Names[1]), Path.Combine(_dir, "02 (2).flac"));  // renamed
			File.Delete(Path.Combine(_dir, "set.vol0+4.par2"));                          // half the recovery data

			Par2Result verified = Par2Verifier.Verify(Par2Reader.Load(Par2));
			Assert.Equal(5, verified.SlicesNeeded);
			Assert.Equal(4, verified.RecoverySlicesAvailable);
			Assert.False(verified.Repairable);

			// with both volumes back there's enough
			File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "par2cmdline", "set.vol0+4.par2"), Path.Combine(_dir, "set.vol0+4.par2"));
			verified = Par2Verifier.Verify(Par2Reader.Load(Par2));
			Assert.True(verified.Repairable);
			Par2Repairer.Repair(verified);

			for (int i = 0; i < Names.Length; i++)
				Assert.Equal(MD5.HashData(originals[i]), MD5.HashData(File.ReadAllBytes(Path.Combine(_dir, Names[i]))));
		}
	}
}
