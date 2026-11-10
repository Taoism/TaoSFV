using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaoSFV.Hash;

namespace TaoSFV.Model.Workset
{
	/// <summary>
	/// Reading, writing and line parsing for .sfv, .md5, .sha1, .sha256 and .sha512 files.
	/// </summary>
	public static class ChecksumFileFormat
	{
		/// <summary>Windows-1252, used by hkSFV/ilSFV and most older tools.</summary>
		public const int LegacyCodePage = 1252;

		private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
		private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

		static ChecksumFileFormat()
		{
			// .NET only has Windows-1252 once the code pages provider is registered; the apps do this at startup
			// too, but anything else using this class (tools, test harnesses) would otherwise fail to write
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}

		private static Encoding Legacy
		{
			get { return Encoding.GetEncoding(LegacyCodePage); }
		}

		/// <summary>
		/// Picks the encoding of a checksum file: a byte order mark if present, otherwise UTF-8 when the
		/// bytes are valid UTF-8 (which includes plain ASCII), otherwise Windows-1252.
		/// </summary>
		public static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
		{
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
			{
				preambleLength = 3;
				return Encoding.UTF8;
			}
			if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
			{
				preambleLength = 4;
				return Encoding.UTF32;
			}
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			{
				preambleLength = 2;
				return Encoding.Unicode;
			}
			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
			{
				preambleLength = 2;
				return Encoding.BigEndianUnicode;
			}

			preambleLength = 0;
			try
			{
				StrictUtf8.GetCharCount(bytes);
				return Utf8NoBom;
			}
			catch (DecoderFallbackException)
			{
				return Legacy;
			}
		}

		public static string Decode(byte[] bytes)
		{
			int preambleLength;
			Encoding encoding = DetectEncoding(bytes, out preambleLength);
			return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
		}

		public static string ReadAllText(string path)
		{
			return Decode(File.ReadAllBytes(path));
		}

		/// <summary>Splits like File.ReadAllLines (on \r\n, \n or \r).</summary>
		public static string[] ReadAllLines(string path)
		{
			List<string> lines = new List<string>();
			using (StringReader reader = new StringReader(ReadAllText(path)))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
					lines.Add(line);
			}
			return lines.ToArray();
		}

		/// <summary>
		/// Windows-1252 when every character fits, for compatibility with older tools; otherwise UTF-8
		/// without a BOM (a BOM would become part of the first file name for md5sum/sha1sum).
		/// </summary>
		public static Encoding ChooseWriteEncoding(string text)
		{
			Encoding strictLegacy = Encoding.GetEncoding(LegacyCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
			try
			{
				strictLegacy.GetByteCount(text);
				return Legacy;
			}
			catch (EncoderFallbackException)
			{
				return Utf8NoBom;
			}
		}

		public static void WriteAllText(string path, string text)
		{
			File.WriteAllText(path, text, ChooseWriteEncoding(text));
		}

		/// <summary>
		/// md5sum compatibility applies to md5sum-style files only: "digest *name" with '/' separators, LF line
		/// endings and '#' comments, which md5sum -c / sha256sum -c accept. Everything else keeps the hkSFV
		/// conventions ('\', CRLF, ';').
		/// </summary>
		public static bool IsMd5SumStyle(ChecksumAlgorithm algorithm, bool md5SumCompatibility)
		{
			return (md5SumCompatibility && algorithm.DigestFirst) || IsNewTool(algorithm);
		}

		/// <summary>
		/// XXH128 and BLAKE3 files are always written the way xxhsum and b3sum write them ('hash  name', '/', LF),
		/// since those are the only other tools that read them; b3sum -c rejects the '*' binary marker.
		/// </summary>
		private static bool IsNewTool(ChecksumAlgorithm algorithm)
		{
			return algorithm.Type == ChecksumType.XXH128 || algorithm.Type == ChecksumType.BLAKE3;
		}

		/// <summary>
		/// False for XXH128 and BLAKE3: xxhsum -c and b3sum -c don't document comment lines, so TaoSFV writes none
		/// there (it still reads '#' and ';' comments in them).
		/// </summary>
		public static bool SupportsComments(ChecksumAlgorithm algorithm)
		{
			return !IsNewTool(algorithm);
		}

		public static string GetNewLine(ChecksumAlgorithm algorithm, bool md5SumCompatibility)
		{
			return IsMd5SumStyle(algorithm, md5SumCompatibility) ? "\n" : "\r\n";
		}

		public static string FormatComment(ChecksumAlgorithm algorithm, bool md5SumCompatibility, string comment)
		{
			return (IsMd5SumStyle(algorithm, md5SumCompatibility) ? "#" : ";") + comment;
		}

		/// <summary>One entry line, without the line ending. fileName is relative to the checksum file, with '\' separators.</summary>
		public static string FormatEntry(ChecksumAlgorithm algorithm, bool md5SumCompatibility, string fileName, string checksum)
		{
			if (!algorithm.DigestFirst)
				return fileName + " " + checksum;
			if (IsNewTool(algorithm))
				return checksum + "  " + fileName.Replace('\\', '/');
			if (md5SumCompatibility)
				return checksum + " *" + fileName.Replace('\\', '/');
			return checksum + " " + fileName;
		}

		/// <summary>
		/// True for comment lines: ';' in any file, '#' in md5sum-style files. Expects a trimmed line.
		/// </summary>
		public static bool IsComment(string line, ChecksumType type)
		{
			return line.StartsWith(";") ||
				(line.StartsWith("#") && ChecksumAlgorithm.Get(type).DigestFirst);
		}

		/// <summary>
		/// Parses one trimmed, non-comment line. Returns false when the line isn't in a recognized format.
		/// File names come back with '/' converted to '\'.
		/// </summary>
		public static bool TryParseLine(string line, ChecksumType type, out string fileName, out string checksum)
		{
			fileName = null;
			checksum = null;

			ChecksumAlgorithm algorithm = ChecksumAlgorithm.Get(type);
			bool ok = algorithm.DigestFirst
				? TryParseDigestLine(line, algorithm.BsdTag + " (", algorithm.DigestLength, out fileName, out checksum)
				: TryParseSfvLine(line, out fileName, out checksum);

			if (ok)
				fileName = fileName.Replace('/', '\\');
			return ok;
		}

		// md5sum/sha1sum ("<digest> [*]name"), reversed ("name <digest>") and BSD ("MD5 (name) = <digest>") styles
		private static bool TryParseDigestLine(string line, string bsdPrefix, int digestLength, out string fileName, out string digest)
		{
			fileName = null;
			digest = null;

			if (line.StartsWith(bsdPrefix))
			{
				int idx = line.LastIndexOf(' ');
				if (idx == -1)
					return false;
				int nameLength = idx - bsdPrefix.Length - 3; // strip ") ="
				if (nameLength < 0)
					return false;
				fileName = line.Substring(bsdPrefix.Length, nameLength);
				digest = line.Substring(idx + 1);
				if (digest.Length != digestLength)
					return false;
			}
			else
			{
				int idx = line.IndexOf(' ');
				if (idx == -1)
					return false;

				if (idx == digestLength)
				{
					digest = line.Substring(0, idx);
					fileName = line.Substring(idx + 1);

					// md5sum/sha1sum put a mode character after the separator: '*' binary, ' ' text (the GNU default)
					if (fileName.StartsWith(" "))
						fileName = fileName.Substring(1);
				}
				else
				{
					idx = line.LastIndexOf(' ');
					fileName = line.Substring(0, idx);
					digest = line.Substring(idx + 1);
					if (digest.Length != digestLength)
						return false;
				}
			}

			if (fileName.StartsWith("*")) // md5sum binary mode
				fileName = fileName.Substring(1);

			return true;
		}

		private static bool TryParseSfvLine(string line, out string fileName, out string crc)
		{
			fileName = null;
			crc = null;

			int idx = line.LastIndexOf(' ');
			if (idx == -1)
				return false;

			crc = line.Substring(idx + 1);
			fileName = line.Substring(0, idx);

			if (crc.StartsWith("$")) // remove leading $
				crc = crc.Substring(1);
			if (crc.StartsWith("0x") || crc.StartsWith("0X")) // remove leading 0x
				crc = crc.Substring(2);

			return crc.Length == 8;
		}
	}
}
