using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TaoSFV.Par2;
using Xunit;

namespace TaoSFV.Tests
{
	public class Galois16Tests
	{
		[Fact]
		public void FieldArithmetic()
		{
			Random random = new Random(5);
			for (int i = 0; i < 2000; i++)
			{
				ushort a = (ushort)random.Next(1, 65536), b = (ushort)random.Next(1, 65536);
				ushort product = Galois16.Multiply(a, b);
				Assert.Equal(a, Galois16.Divide(product, b));
				Assert.Equal(Galois16.Multiply(b, a), product);
				Assert.Equal(Galois16.Multiply(a, Galois16.Multiply(a, a)), Galois16.Power(a, 3));
			}
			Assert.Equal(0, Galois16.Multiply(0, 1234));
			Assert.Equal(1, Galois16.Power(0, 0));
		}

		[Fact]
		public void InputConstantsFollowTheSpec()
		{
			// 2^n for n = 1, 2, 4, 7, 8, 11, 13, 14, 16, ... (n not divisible by 3, 5, 17 or 257);
			// 2^16 reduced by the generator 0x1100B is 0x100B
			Assert.Equal(new ushort[] { 2, 4, 16, 128, 256, 2048, 8192, 16384, 0x100B }, Galois16.InputConstants(9));
		}

		[Fact]
		public void MultiplyAddMatchesWordByWord()
		{
			byte[] source = new byte[1000];
			new Random(6).NextBytes(source);
			byte[] destination = new byte[1000];
			Galois16.MultiplyAdd(destination, source, 0x1234);
			for (int i = 0; i < 1000; i += 2)
			{
				ushort expected = Galois16.Multiply(0x1234, (ushort)(source[i] | source[i + 1] << 8));
				Assert.Equal(expected, (ushort)(destination[i] | destination[i + 1] << 8));
			}
		}

		[Fact]
		public void InvertGivesTheIdentity()
		{
			Random random = new Random(7);
			ushort[,] matrix = new ushort[6, 6];
			for (int r = 0; r < 6; r++)
				for (int c = 0; c < 6; c++)
					matrix[r, c] = (ushort)random.Next(65536);
			ushort[,] original = (ushort[,])matrix.Clone();

			Assert.True(Galois16.Invert(matrix));
			for (int r = 0; r < 6; r++)
				for (int c = 0; c < 6; c++)
				{
					ushort sum = 0;
					for (int k = 0; k < 6; k++)
						sum ^= Galois16.Multiply(original[r, k], matrix[k, c]);
					Assert.Equal(r == c ? 1 : 0, sum);
				}

			Assert.False(Galois16.Invert(new ushort[,] { { 1, 2 }, { 2, 4 } }));
		}
	}

	public class Par2RepairTests : IDisposable
	{
		private const int SliceSize = 4096;
		private readonly string _dir;
		private readonly string[] _names = { "a.bin", "b.bin", @"CD1\c.bin" };
		private byte[][] _originals;

		public Par2RepairTests()
		{
			_dir = Path.Combine(Path.GetTempPath(), "TaoSFV.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(_dir, "CD1"));
		}

		public void Dispose()
		{
			Directory.Delete(_dir, true);
		}

		private string Path_(string name)
		{
			return Path.Combine(_dir, name);
		}

		private string MakeSet(int recoverySlices)
		{
			int[] lengths = { SliceSize * 10 + 123, SliceSize * 2, 100 };
			_originals = new byte[3][];
			for (int i = 0; i < 3; i++)
			{
				_originals[i] = new byte[lengths[i]];
				new Random(i + 1).NextBytes(_originals[i]);
				File.WriteAllBytes(Path_(_names[i]), _originals[i]);
			}
			string par2 = Path_("set.par2");
			Par2TestWriter.Write(par2, _dir, _names, SliceSize, recoverySlices, realRecovery: true);
			return par2;
		}

		private void Corrupt(string name, params int[] offsets)
		{
			byte[] data = File.ReadAllBytes(Path_(name));
			foreach (int offset in offsets)
				data[offset] ^= 0x5A;
			File.WriteAllBytes(Path_(name), data);
		}

		private void AssertAllRestored()
		{
			for (int i = 0; i < 3; i++)
				Assert.Equal(MD5.HashData(_originals[i]), MD5.HashData(File.ReadAllBytes(Path_(_names[i]))));
		}

		private static Par2RepairResult VerifyAndRepair(string par2, long memoryLimit = 256L * 1024 * 1024)
		{
			Par2Result verified = Par2Verifier.Verify(Par2Reader.Load(par2));
			Assert.True(verified.Repairable);
			Par2RepairResult result = Par2Repairer.Repair(verified, memoryLimit: memoryLimit);
			Assert.True(Par2Verifier.Verify(Par2Reader.Load(par2)).AllOk);
			return result;
		}

		[Fact]
		public void RepairsDamagedSlices()
		{
			string par2 = MakeSet(3);
			Corrupt("a.bin", SliceSize * 2 + 1, SliceSize * 10 + 50); // a middle slice and the partial last one

			Par2RepairResult result = VerifyAndRepair(par2);

			AssertAllRestored();
			Assert.Equal(new[] { Path_("a.bin") }, result.Repaired);
			Assert.Equal(new[] { Path_("a.bin.bad") }, result.KeptDamaged);   // the damaged original is kept
			Assert.False(File.Exists(Path_("a.bin.taosfv-repair")));
		}

		[Fact]
		public void RebuildsAMissingFileAndRenamesOneBack()
		{
			string par2 = MakeSet(4);
			File.Delete(Path_("b.bin"));                       // 2 slices
			File.Move(Path_(@"CD1\c.bin"), Path_(@"CD1\c (1).bin"));
			Corrupt("a.bin", 7);                               // 1 slice

			Par2RepairResult result = VerifyAndRepair(par2);

			AssertAllRestored();
			Assert.Contains(Path_("b.bin"), result.Repaired);
			Assert.Equal(new[] { Path_(@"CD1\c.bin") }, result.Renamed);
			Assert.False(File.Exists(Path_(@"CD1\c (1).bin")));
		}

		[Fact]
		public void UsesExactlyAsManyRecoverySlicesAsNeeded()
		{
			string par2 = MakeSet(5);
			Corrupt("a.bin", 0, SliceSize * 1, SliceSize * 3, SliceSize * 5, SliceSize * 7);  // 5 slices, 5 recovery slices

			VerifyAndRepair(par2);
			AssertAllRestored();
		}

		[Fact]
		public void WorksInPartsWithLittleMemory()
		{
			string par2 = MakeSet(3);
			Corrupt("a.bin", 10, SliceSize * 4 + 4000);
			Corrupt("b.bin", SliceSize + 2);

			VerifyAndRepair(par2, memoryLimit: 3000);   // 1000 bytes per rebuilt slice at a time
			AssertAllRestored();
		}

		[Fact]
		public void SkipsMissingRecoverySlices()
		{
			// the lowest exponents are lost (e.g. a deleted volume file); higher ones still do the job
			string par2 = MakeSet(4);
			Par2Set set = Par2Reader.Load(par2);
			set.RecoverySlices.Remove(0);
			set.RecoverySlices.Remove(1);
			Corrupt("b.bin", 1, SliceSize + 1);

			Par2Repairer.Repair(Par2Verifier.Verify(set));
			AssertAllRestored();
		}

		[Fact]
		public void RefusesWithoutEnoughRecoveryData()
		{
			string par2 = MakeSet(1);
			Corrupt("a.bin", 1, SliceSize + 1);
			Par2Result verified = Par2Verifier.Verify(Par2Reader.Load(par2));

			Assert.False(verified.Repairable);
			Assert.Throws<InvalidOperationException>(() => Par2Repairer.Repair(verified));
			Assert.False(File.Exists(Path_("a.bin.bad")));
		}
	}
}
