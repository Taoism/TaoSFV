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
		public void VectorPathsMatchScalar()
		{
			Random random = new Random(8);
			foreach (int length in new[] { 0, 2, 30, 32, 62, 64, 66, 96, 128, 1000, 4096, 65538 })
			{
				byte[] source = new byte[length];
				byte[] start = new byte[length];
				random.NextBytes(source);
				random.NextBytes(start);
				foreach (ushort factor in new ushort[] { 0, 1, 2, 0x100B, 0xFFFF, (ushort)random.Next(65536), (ushort)random.Next(65536) })
				{
					byte[] expected = (byte[])start.Clone();
					Galois16.MultiplyAddScalar(expected, source, factor);

					byte[] actual = (byte[])start.Clone();
					Galois16.MultiplyAdd(actual, source, factor);
					Assert.Equal(expected, actual);

					if (System.Runtime.Intrinsics.X86.Ssse3.IsSupported && factor > 1)
					{
						actual = (byte[])start.Clone();
						int done = Galois16.MultiplyAddSsse3(actual, source, factor);
						Galois16.MultiplyAddScalar(actual.AsSpan(done), source.AsSpan(done), factor);
						Assert.Equal(expected, actual);
					}
					if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && factor > 1)
					{
						actual = (byte[])start.Clone();
						int done = Galois16.MultiplyAddAvx2(actual, source, factor);
						Galois16.MultiplyAddScalar(actual.AsSpan(done), source.AsSpan(done), factor);
						Assert.Equal(expected, actual);
					}
				}
			}
		}

		[Fact]
		public void Benchmark()
		{
			// set TAOSFV_BENCH=1 to run: prints MB/s for the scalar and vector block maths
			if (Environment.GetEnvironmentVariable("TAOSFV_BENCH") != "1")
				return;
			byte[] source = new byte[64 * 1024 * 1024], destination = new byte[source.Length];
			new Random(1).NextBytes(source);
			foreach (string name in new[] { "scalar", "auto" })
			{
				System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
				for (int i = 0; i < 4; i++)
				{
					if (name == "scalar")
						Galois16.MultiplyAddScalar(destination, source, (ushort)(0x1234 + i));
					else
						Galois16.MultiplyAdd(destination, source, (ushort)(0x1234 + i));
				}
				File.AppendAllText(Path.Combine(Path.GetTempPath(), "taosfv-bench.txt"),
					string.Format("{0}: {1:0} MB/s{2}", name, 4 * 64 / sw.Elapsed.TotalSeconds, Environment.NewLine));
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

		[Theory]
		[InlineData(37)]    // bytes inserted in slice 2: slices 3..10 are shifted forward
		[InlineData(-37)]   // bytes removed from slice 2: shifted back
		public void FindsShiftedSlicesAfterInsertedOrRemovedBytes(int change)
		{
			string par2 = MakeSet(1);
			byte[] data = File.ReadAllBytes(Path_("a.bin"));
			int at = SliceSize * 2 + 1000;
			byte[] changed = change > 0
				? data.Take(at).Concat(Enumerable.Repeat((byte)0xEE, change)).Concat(data.Skip(at)).ToArray()
				: data.Take(at).Concat(data.Skip(at - change)).ToArray();
			File.WriteAllBytes(Path_("a.bin"), changed);

			Par2Result verified = Par2Verifier.Verify(Par2Reader.Load(par2));
			Par2FileResult a = verified.Files.Single(f => f.File.Name == "a.bin");

			Assert.Equal(Par2FileState.Damaged, a.State);
			Assert.Equal(10, a.GoodSlices);          // all but slice 2
			Assert.Equal(8, a.ShiftedSlices);        // slices 3..9 and the partial last slice
			Assert.False(a.SliceOk[2]);
			Assert.Equal(1, verified.SlicesNeeded);  // one recovery slice is enough

			Par2Repairer.Repair(verified);
			AssertAllRestored();
		}

		[Fact]
		public void ShiftedScanSkipsIntactSlicesAndFindsNothingInRandomData()
		{
			string par2 = MakeSet(2);
			Corrupt("a.bin", SliceSize * 4 + 9);   // one slice damaged in place, nothing shifted

			Par2Result verified = Par2Verifier.Verify(Par2Reader.Load(par2));
			Par2FileResult a = verified.Files.Single(f => f.File.Name == "a.bin");
			Assert.Equal(10, a.GoodSlices);
			Assert.Equal(0, a.ShiftedSlices);
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
