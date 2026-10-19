using System;
using TaoSFV.Model.Workset;

namespace TaoSFV
{
    public class InvalidChecksumFileException : Exception
    {
        // long enough to recognize the line, short enough for a message box (a binary file has no line breaks)
        private const int MaxLineLength = 200;

        // TODO: Localize "Line"
        public InvalidChecksumFileException(string fileName, ChecksumType checksumType, string[] lines, int lineIndex)
            : base(string.Format("Line {0}: \"{1}\"", lineIndex + 1, Shorten(lines[lineIndex])))
        {
            FileName = fileName;
            ChecksumType = checksumType;
            LineNumber = lineIndex + 1;
        }

        public string FileName { get; private set; }
        public ChecksumType ChecksumType { get; private set; }
        public int LineNumber { get; private set; }

        private static string Shorten(string line)
        {
            return line.Length <= MaxLineLength ? line : line.Substring(0, MaxLineLength) + "...";
        }
    }
}
