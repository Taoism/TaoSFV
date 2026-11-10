using System;
using System.IO;

namespace TaoSFV.Model.Workset
{
	public class ChecksumFile
	{
		private string _originalChecksum;
		private string _currentChecksum;

		public string FileName { get; set; }
		public FileInfo FileInfo { get; set; }
		public ChecksumFileState State { get; set; }
		public bool IsDeleted { get; set; }

		/// <summary>
		/// False when the checksum file names this file outside its own folder (an absolute path or one with ".."). Such
		/// a file is still checked, but TaoSFV never creates, renames or deletes anything for it.
		/// </summary>
		public bool IsInsideSet
		{
			get { return Set != null && SafePath.IsInside(Set.Directory, FileName); }
		}

		public string OriginalChecksum
		{
			get { return _originalChecksum; }
			set { _originalChecksum = value == null ? null : value.ToLower(); }
		}

		public string CurrentChecksum
		{
			get { return _currentChecksum; }
			set { _currentChecksum = value == null ? null : value.ToLower(); }
		}

		public DateTime CacheLastWriteUtc { get; set; }

		/// <summary>Why the file couldn't be read, when State is Unreadable.</summary>
		public string ErrorMessage { get; set; }
		public long CacheLength { get; set; }
		public string Guid { get; private set; }
		public ChecksumSet Set { get; private set; }

		public ChecksumFile(ChecksumSet set)
		{
			State = ChecksumFileState.NotProcessed;
			Guid = System.Guid.NewGuid().ToString();
			Set = set;
		}
	}
}
