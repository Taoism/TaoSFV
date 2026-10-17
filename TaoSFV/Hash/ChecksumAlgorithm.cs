using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TaoSFV.Model.Workset;

namespace TaoSFV.Hash
{
	/// <summary>
	/// Everything that differs between the supported checksum file types.
	/// </summary>
	public sealed class ChecksumAlgorithm
	{
		public static readonly ChecksumAlgorithm Sfv = new ChecksumAlgorithm(ChecksumType.SFV, "sfv", "SFV", 8, null, "CRC32FileCache", "CRC32", s => CRC32.Calculate(s));
		public static readonly ChecksumAlgorithm Md5 = new ChecksumAlgorithm(ChecksumType.MD5, "md5", "MD5", 32, "MD5", "MD5FileCache", "MD5", s => MD5.Calculate(s));
		public static readonly ChecksumAlgorithm Sha1 = new ChecksumAlgorithm(ChecksumType.SHA1, "sha1", "SHA-1", 40, "SHA1", "SHA1FileCache", "SHA1", s => SHA1.Calculate(s));
		public static readonly ChecksumAlgorithm Sha256 = new ChecksumAlgorithm(ChecksumType.SHA256, "sha256", "SHA-256", 64, "SHA256", "SHA256FileCache", "SHA256", s => Hex(HashAlgorithmName.SHA256, s));
		public static readonly ChecksumAlgorithm Sha512 = new ChecksumAlgorithm(ChecksumType.SHA512, "sha512", "SHA-512", 128, "SHA512", "SHA512FileCache", "SHA512", s => Hex(HashAlgorithmName.SHA512, s));

		public static readonly IReadOnlyList<ChecksumAlgorithm> All = new[] { Sfv, Md5, Sha1, Sha256, Sha512 };

		private readonly Func<Stream, string> _compute;

		private ChecksumAlgorithm(ChecksumType type, string extension, string displayName, int digestLength, string bsdTag,
			string cacheTable, string cacheColumn, Func<Stream, string> compute)
		{
			Type = type;
			Extension = extension;
			DisplayName = displayName;
			DigestLength = digestLength;
			BsdTag = bsdTag;
			CacheTable = cacheTable;
			CacheColumn = cacheColumn;
			_compute = compute;
		}

		public ChecksumType Type { get; private set; }

		/// <summary>Lowercase, without the dot ("sha256").</summary>
		public string Extension { get; private set; }

		public string DisplayName { get; private set; }

		/// <summary>Length of the checksum in hex characters.</summary>
		public int DigestLength { get; private set; }

		/// <summary>Tag used by BSD-style lines ("SHA256 (name) = digest"); null for SFV.</summary>
		public string BsdTag { get; private set; }

		/// <summary>True for md5sum-style files (digest, then name); false for SFV (name, then CRC).</summary>
		public bool DigestFirst
		{
			get { return Type != ChecksumType.SFV; }
		}

		internal string CacheTable { get; private set; }
		internal string CacheColumn { get; private set; }

		/// <summary>Hashes the whole stream from the start.</summary>
		public string Compute(Stream stream)
		{
			return _compute(stream);
		}

		public static ChecksumAlgorithm Get(ChecksumType type)
		{
			foreach (ChecksumAlgorithm algorithm in All)
			{
				if (algorithm.Type == type)
					return algorithm;
			}
			throw new NotSupportedException(string.Format("{0} not implemented", type));
		}

		/// <summary>Accepts "sfv", ".sfv" or a path; returns null for anything else.</summary>
		public static ChecksumAlgorithm FromExtension(string extensionOrPath)
		{
			if (string.IsNullOrEmpty(extensionOrPath))
				return null;

			string ext = extensionOrPath.Contains(".") ? Path.GetExtension(extensionOrPath) : extensionOrPath;
			ext = ext.TrimStart('.');
			foreach (ChecksumAlgorithm algorithm in All)
			{
				if (string.Equals(algorithm.Extension, ext, StringComparison.OrdinalIgnoreCase))
					return algorithm;
			}
			return null;
		}

		public static bool IsChecksumFile(string path)
		{
			return FromExtension(Path.GetExtension(path)) != null;
		}

		/// <summary>".sfv", ".md5", ...</summary>
		public static string[] DottedExtensions
		{
			get { return All.Select(a => "." + a.Extension).ToArray(); }
		}

		/// <summary>"*.sfv;*.md5;..." for file dialogs.</summary>
		public static string DialogPattern
		{
			get { return string.Join(";", All.Select(a => "*." + a.Extension)); }
		}

		private static string Hex(HashAlgorithmName name, Stream stream)
		{
			stream.Position = 0;
			return Convert.ToHexStringLower(StreamHasher.Hash(name, stream));
		}

		public override string ToString()
		{
			return DisplayName;
		}
	}
}
