using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TaoSFV.Hash;
using TaoSFV.Model.Workset;
using TaoSFV.Verification;

namespace TaoSFV.Cli
{
	internal static class Program
	{
		private const int ExitOk = 0;
		private const int ExitFailed = 1;
		private const int ExitError = 2;
		private const int ExitCancelled = 130;

		private const string Usage = @"TaoSFV command line - verify and create SFV, MD5, SHA-1, SHA-256 and SHA-512 files.

Usage:
  taosfv-cli verify <file-or-folder>... [options]
  taosfv-cli create <sfv|md5|sha1|sha256|sha512> <output-file> <file-or-folder>... [options]

verify
  Checks each checksum file given. A folder is searched (with subfolders) for
  .sfv, .md5, .sha1, .sha256 and .sha512 files. Nothing is renamed, deleted or
  created. Prints 'name: OK', 'name: FAILED', 'name: MISSING' or
  'name: READ ERROR', then a summary.

create
  Hashes the given files (folders include their subfolders) and writes the
  checksum file. Paths are stored relative to the output file's folder, so
  every input must be inside it. md5sum-style files use '/' separators and
  are readable by md5sum -c / sha256sum -c.

Options:
  -q, --quiet       verify: print only problems and the summary
  -s, --status      verify: print nothing; use the exit code
  -t, --threads N   files to read at once (default: 4 on SSDs, 1 on hard
                    drives and network shares)
  -h, --help        show this help

Exit codes:
  0  every file OK
  1  a file was bad, missing or unreadable
  2  bad arguments, or a checksum file couldn't be read or parsed
  130  cancelled (Ctrl+C)
";

		private static readonly CancellationTokenSource _cancel = new CancellationTokenSource();
		private static readonly bool _showProgress = !Console.IsErrorRedirected;

		private static int Main(string[] args)
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			Console.OutputEncoding = new UTF8Encoding(false);
			Console.CancelKeyPress += (sender, e) =>
			{
				e.Cancel = true;
				_cancel.Cancel();
			};

			try
			{
				Options options;
				string error;
				if (!Options.TryParse(args, out options, out error))
				{
					if (error != null)
						Console.Error.WriteLine("taosfv-cli: " + error + Environment.NewLine);
					Console.Error.Write(Usage);
					return error == null ? ExitOk : ExitError;
				}

				return options.Command == "create" ? Create(options) : Verify(options);
			}
			catch (Exception ex) when (ex is OperationCanceledException ||
				(ex is AggregateException && ((AggregateException)ex).Flatten().InnerExceptions.All(e => e is OperationCanceledException)))
			{
				ClearProgress();
				Console.Error.WriteLine("taosfv-cli: cancelled");
				return ExitCancelled;
			}
		}

		// ------------------------------------------------------------------ verify

		private static int Verify(Options options)
		{
			List<string> checksumFiles = new List<string>();
			foreach (string input in options.Inputs)
			{
				string path = Path.GetFullPath(input);
				if (Directory.Exists(path))
				{
					List<string> found = ChecksumSetLoader.FindChecksumFiles(path);
					if (found.Count == 0)
						Console.Error.WriteLine("taosfv-cli: no checksum files under " + path);
					checksumFiles.AddRange(found);
				}
				else if (File.Exists(path))
				{
					if (!ChecksumAlgorithm.IsChecksumFile(path))
						return Fail("not a checksum file (.sfv, .md5, .sha1, .sha256, .sha512): " + path);
					checksumFiles.Add(path);
				}
				else
				{
					return Fail("not found: " + path);
				}
			}

			if (checksumFiles.Count == 0)
				return ExitError;

			List<ChecksumSet> sets = new List<ChecksumSet>();
			foreach (string checksumFile in checksumFiles)
			{
				try
				{
					ChecksumSet set = ChecksumSetLoader.Parse(checksumFile);
					ChecksumSetLoader.ResolveFiles(set, null);
					sets.Add(set);
				}
				catch (InvalidChecksumFileException ex)
				{
					return Fail("unrecognized line in " + checksumFile + Environment.NewLine + FirstLine(ex.Message));
				}
				catch (IOException ex)
				{
					return Fail("can't read " + checksumFile + ": " + ex.Message);
				}
			}

			Stopwatch stopwatch = Stopwatch.StartNew();
			VerifyContext context = new VerifyContext(_cancel.Token);
			VerifyOptions verifyOptions = VerifyOptions.ReadOnly(false);
			long totalBytes = sets.Sum(s => s.TotalSize);
			int ok = 0, bad = 0, missing = 0, unreadable = 0;

			foreach (ChecksumSet set in sets)
			{
				Dictionary<ChecksumFile, string> errors = new Dictionary<ChecksumFile, string>();
				int parallelism = options.Threads ?? DiskInfo.GetParallelism(set.Directory);

				using (Timer progress = StartProgress(context, totalBytes))
				{
					Parallel.ForEach(set.Files, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = _cancel.Token }, file =>
					{
						try
						{
							FileVerifier.Verify(set, file, verifyOptions, null, context);
						}
						catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
						{
							lock (errors)
								errors[file] = ex.Message;
						}
					});
				}
				ClearProgress();

				List<string> lines = new List<string>();
				foreach (ChecksumFile file in set.Files)
				{
					string error;
					string result;
					if (errors.TryGetValue(file, out error))
					{
						unreadable++;
						result = "READ ERROR (" + error + ")";
					}
					else if (file.State == ChecksumFileState.OK)
					{
						ok++;
						result = "OK";
					}
					else if (file.State == ChecksumFileState.Missing)
					{
						missing++;
						result = "MISSING";
					}
					else
					{
						bad++;
						result = "FAILED";
					}

					if (!(options.Quiet && result == "OK"))
						lines.Add(file.FileName + ": " + result);
				}

				// with several sets, a header says which file the lines belong to (in --quiet, only for sets with problems)
				if (!options.Status && lines.Count != 0)
				{
					if (sets.Count > 1)
						Console.WriteLine("== " + set.VerificationFileName);
					foreach (string line in lines)
						Console.WriteLine(line);
				}
			}

			stopwatch.Stop();
			if (!options.Status)
			{
				int total = ok + bad + missing + unreadable;
				string summary = string.Format("{0} file{1} in {2} set{3}: {4} OK", total, total == 1 ? "" : "s", sets.Count, sets.Count == 1 ? "" : "s", ok);
				if (bad != 0)
					summary += ", " + bad + " failed";
				if (missing != 0)
					summary += ", " + missing + " missing";
				if (unreadable != 0)
					summary += ", " + unreadable + " unreadable";
				summary += string.Format(" - {0} in {1:0.0} s", FormatBytes(context.BytesProcessed), stopwatch.Elapsed.TotalSeconds);
				Console.WriteLine(summary);
			}

			return bad + missing + unreadable == 0 ? ExitOk : ExitFailed;
		}

		// ------------------------------------------------------------------ create

		private static int Create(Options options)
		{
			ChecksumAlgorithm algorithm = ChecksumAlgorithm.FromExtension(options.CreateType);
			if (algorithm == null)
				return Fail("unknown checksum type '" + options.CreateType + "' (use sfv, md5, sha1, sha256 or sha512)");

			string output = Path.GetFullPath(options.Output);
			string baseDirectory = Path.GetDirectoryName(output);
			if (!Directory.Exists(baseDirectory))
				return Fail("folder not found: " + baseDirectory);

			SortedSet<string> files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string input in options.Inputs)
			{
				string path = Path.GetFullPath(input);
				if (Directory.Exists(path))
					CollectFilesToHash(path, files);
				else if (File.Exists(path))
					files.Add(path);
				else
					return Fail("not found: " + path);
			}

			files.Remove(output);
			files.RemoveWhere(ChecksumAlgorithm.IsChecksumFile);
			if (files.Count == 0)
				return Fail("no files to hash");

			string prefix = baseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string outside = files.FirstOrDefault(f => !f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			if (outside != null)
				return Fail("every file must be inside the output file's folder (" + baseDirectory + "): " + outside);

			List<string> ordered = files.ToList();
			string[] checksums = new string[ordered.Count];
			Dictionary<string, string> errors = new Dictionary<string, string>();
			long totalBytes = ordered.Sum(f => new FileInfo(f).Length);
			VerifyContext context = new VerifyContext(_cancel.Token);
			int parallelism = options.Threads ?? DiskInfo.GetParallelism(baseDirectory);
			Stopwatch stopwatch = Stopwatch.StartNew();

			using (Timer progress = StartProgress(context, totalBytes))
			{
				Parallel.For(0, ordered.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = _cancel.Token }, i =>
				{
					try
					{
						checksums[i] = FileVerifier.ComputeChecksum(algorithm, new FileInfo(ordered[i]), context);
					}
					catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
					{
						lock (errors)
							errors[ordered[i]] = ex.Message;
					}
				});
			}
			ClearProgress();

			if (errors.Count != 0)
			{
				foreach (KeyValuePair<string, string> error in errors)
					Console.Error.WriteLine("taosfv-cli: can't read " + error.Key + ": " + error.Value);
				Console.Error.WriteLine("taosfv-cli: " + output + " was not written");
				return ExitFailed;
			}

			// md5sum-style output (readable by md5sum -c / sha256sum -c); SFV keeps hkSFV's conventions
			string newLine = ChecksumFileFormat.GetNewLine(algorithm, true);
			StringBuilder text = new StringBuilder();
			text.Append(ChecksumFileFormat.FormatComment(algorithm, true, string.Format(" Generated by TaoSFV on {0:yyyy-MM-dd} at {0:HH:mm:ss}", DateTime.Now))).Append(newLine);
			for (int i = 0; i < ordered.Count; i++)
				text.Append(ChecksumFileFormat.FormatEntry(algorithm, true, ordered[i].Substring(prefix.Length), checksums[i])).Append(newLine);

			ChecksumFileFormat.WriteAllText(output, text.ToString());
			stopwatch.Stop();
			Console.WriteLine(string.Format("{0}: {1} file{2}, {3} in {4:0.0} s", output, ordered.Count, ordered.Count == 1 ? "" : "s",
				FormatBytes(totalBytes), stopwatch.Elapsed.TotalSeconds));
			return ExitOk;
		}

		private static void CollectFilesToHash(string directory, ISet<string> files)
		{
			foreach (string file in Directory.GetFiles(directory))
			{
				if ((File.GetAttributes(file) & FileAttributes.Hidden) == 0)
					files.Add(file);
			}

			foreach (string subdirectory in Directory.GetDirectories(directory))
			{
				if ((new DirectoryInfo(subdirectory).Attributes & FileAttributes.Hidden) == 0)
					CollectFilesToHash(subdirectory, files);
			}
		}

		// ------------------------------------------------------------------ helpers

		private static Timer StartProgress(VerifyContext context, long totalBytes)
		{
			if (!_showProgress || totalBytes == 0)
				return null;

			Stopwatch stopwatch = Stopwatch.StartNew();
			return new Timer(_ =>
			{
				long done = context.BytesProcessed;
				double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
				Console.Error.Write(string.Format("\r{0,3}%  {1} of {2}  {3}/s   ", Math.Min(100, done * 100 / totalBytes),
					FormatBytes(done), FormatBytes(totalBytes), FormatBytes((long)(done / seconds))));
			}, null, 500, 250);
		}

		private static void ClearProgress()
		{
			if (_showProgress)
				Console.Error.Write("\r" + new string(' ', Math.Max(0, Console.WindowWidth - 1)) + "\r");
		}

		private static string FormatBytes(long bytes)
		{
			if (bytes >= 1L << 30)
				return string.Format("{0:0.0} GB", bytes / (double)(1L << 30));
			if (bytes >= 1L << 20)
				return string.Format("{0:0.0} MB", bytes / (double)(1L << 20));
			if (bytes >= 1L << 10)
				return string.Format("{0:0.0} KB", bytes / (double)(1L << 10));
			return bytes + " bytes";
		}

		private static string FirstLine(string text)
		{
			int index = text.IndexOfAny(new[] { '\r', '\n' });
			return index < 0 ? text : text.Substring(0, index);
		}

		private static int Fail(string message)
		{
			Console.Error.WriteLine("taosfv-cli: " + message);
			return ExitError;
		}

		private sealed class Options
		{
			public string Command;
			public string CreateType;
			public string Output;
			public readonly List<string> Inputs = new List<string>();
			public bool Quiet;
			public bool Status;
			public int? Threads;

			/// <summary>False with a null error means help was requested.</summary>
			public static bool TryParse(string[] args, out Options options, out string error)
			{
				options = new Options();
				error = null;
				List<string> positional = new List<string>();

				for (int i = 0; i < args.Length; i++)
				{
					string arg = args[i];
					switch (arg)
					{
						case "-h":
						case "--help":
						case "/?":
							return false;
						case "-q":
						case "--quiet":
							options.Quiet = true;
							break;
						case "-s":
						case "--status":
							options.Status = true;
							break;
						case "-t":
						case "--threads":
							int threads;
							if (i + 1 >= args.Length || !int.TryParse(args[++i], out threads) || threads < 1 || threads > 64)
							{
								error = "--threads needs a number from 1 to 64";
								return false;
							}
							options.Threads = threads;
							break;
						default:
							if (arg.StartsWith("--") || (arg.StartsWith("-") && arg.Length > 1))
							{
								error = "unknown option " + arg;
								return false;
							}
							positional.Add(arg);
							break;
					}
				}

				if (positional.Count == 0)
				{
					error = "missing command";
					return false;
				}

				options.Command = positional[0].ToLowerInvariant();
				if (options.Command == "verify" || options.Command == "check" || options.Command == "-c")
				{
					options.Command = "verify";
					options.Inputs.AddRange(positional.Skip(1));
					if (options.Inputs.Count == 0)
					{
						error = "verify needs at least one checksum file or folder";
						return false;
					}
				}
				else if (options.Command == "create")
				{
					if (positional.Count < 4)
					{
						error = "create needs a type, an output file and at least one file or folder";
						return false;
					}
					options.CreateType = positional[1];
					options.Output = positional[2];
					options.Inputs.AddRange(positional.Skip(3));
				}
				else
				{
					error = "unknown command '" + positional[0] + "'";
					return false;
				}

				return true;
			}
		}
	}
}
