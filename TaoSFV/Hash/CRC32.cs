using System;
using System.Buffers;
using System.IO;

namespace TaoSFV.Hash
{
    /// <summary>
    /// Calculates a 32-bit Cyclic Redundancy Checksum (CRC) using the
    /// same polynomial used by Zip (CRC-32/ISO-HDLC, as used in SFV files).
    /// </summary>
    public static class CRC32
    {
        // large reads keep the vectorized System.IO.Hashing.Crc32 busy; 8 KB reads were the bottleneck
        private const int BUFFER_SIZE = 1024 * 1024;

        /// <summary>
        /// Returns the CRC32 Checksum of a specified file as a string.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <returns>CRC32 Checksum as a string.</returns>
        public static string Calculate(FileInfo file)
        {
            if (file == null)
                throw new ArgumentNullException("file");

            return string.Format("{0:X8}", CalculateInt32(file));
        }

        /// <summary>
        /// Returns the CRC32 Checksum of an input stream as a string.
        /// </summary>
        /// <param name="stream">Input stream.</param>
        /// <returns>CRC32 Checksum as a string.</returns>
        public static string Calculate(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            return string.Format("{0:X8}", CalculateInt32(stream));
        }

        /// <summary>
        /// Returns the CRC32 Checksum of a byte array as a string.
        /// </summary>
        /// <param name="data">The byte array.</param>
        /// <returns>CRC32 Checksum as a string.</returns>
        public static string Calculate(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            return string.Format("{0:X8}", CalculateInt32(data));
        }

        /// <summary>
        /// Returns the CRC32 Checksum of a specified file as a four byte signed integer (Int32).
        /// </summary>
        /// <param name="file">The file.</param>
        /// <returns>CRC32 Checksum as a four byte signed integer (Int32).</returns>
        public static uint CalculateInt32(FileInfo file)
        {
            if (file == null)
                throw new ArgumentNullException("file");

            using (FileStream fileStream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return CalculateInt32(fileStream);
            }
        }

        /// <summary>
        /// Returns the CRC32 Checksum of an input stream as a four byte signed integer (Int32).
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>CRC32 Checksum as a four byte signed integer (Int32).</returns>
        public static uint CalculateInt32(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            stream.Position = 0;
            System.IO.Hashing.Crc32 crc32 = new System.IO.Hashing.Crc32();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
            try
            {
                int count;
                while ((count = stream.Read(buffer, 0, BUFFER_SIZE)) > 0)
                    crc32.Append(new ReadOnlySpan<byte>(buffer, 0, count));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return crc32.GetCurrentHashAsUInt32();
        }

        /// <summary>
        /// Returns the CRC32 Checksum of a byte array as a four byte signed integer (Int32).
        /// </summary>
        /// <param name="data">The byte array.</param>
        /// <returns>CRC32 Checksum as a four byte signed integer (Int32).</returns>
        public static uint CalculateInt32(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            using (MemoryStream memoryStream = new MemoryStream(data))
            {
                return CalculateInt32(memoryStream);
            }
        }
    }
}