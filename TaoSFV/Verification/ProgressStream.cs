using System;
using System.IO;
using System.Threading;

namespace TaoSFV.Verification
{
	/// <summary>
	/// Read-only wrapper that reports bytes read, stops on cancellation and blocks while paused.
	/// </summary>
	internal sealed class ProgressStream : Stream
	{
		private readonly Stream _inner;
		private readonly Action<int> _onRead;
		private readonly CancellationToken _cancel;
		private readonly PauseGate _pause;

		public ProgressStream(Stream inner, Action<int> onRead, CancellationToken cancel, PauseGate pause)
		{
			_inner = inner;
			_onRead = onRead;
			_cancel = cancel;
			_pause = pause;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_pause != null)
				_pause.WaitWhilePaused(_cancel);
			_cancel.ThrowIfCancellationRequested();

			int read = _inner.Read(buffer, offset, count);
			if (read > 0 && _onRead != null)
				_onRead(read);
			return read;
		}

		public override bool CanRead
		{
			get { return true; }
		}

		public override bool CanSeek
		{
			get { return _inner.CanSeek; }
		}

		public override bool CanWrite
		{
			get { return false; }
		}

		public override long Length
		{
			get { return _inner.Length; }
		}

		public override long Position
		{
			get { return _inner.Position; }
			set { _inner.Position = value; }
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			return _inner.Seek(offset, origin);
		}

		public override void Flush()
		{
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_inner.Dispose();
			base.Dispose(disposing);
		}
	}

	/// <summary>Pause/resume for hashing threads.</summary>
	internal sealed class PauseGate
	{
		private readonly ManualResetEventSlim _running = new ManualResetEventSlim(true);

		public bool IsPaused
		{
			get { return !_running.IsSet; }
		}

		public void Pause()
		{
			_running.Reset();
		}

		public void Resume()
		{
			_running.Set();
		}

		public void WaitWhilePaused(CancellationToken cancel)
		{
			_running.Wait(cancel);
		}
	}
}
