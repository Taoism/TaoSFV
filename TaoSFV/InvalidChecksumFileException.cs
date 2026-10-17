using System;
using TaoSFV.Localization;
using TaoSFV.Model.Workset;

namespace TaoSFV
{
    public class InvalidChecksumFileException : Exception
    {
        // TODO: Localize "Line"
        public InvalidChecksumFileException(string fileName, ChecksumType checksumType, string[] lines, int lineIndex)
            : base(string.Format("Line {0}: \"{1}\"", lineIndex + 1, lines[lineIndex]) + Environment.NewLine + Environment.NewLine +
                   string.Format(Language.MainForm.FileContentsNotRecognized, fileName, checksumType, ChecksumFileFormat.ReadAllText(fileName))
            )
        {
        }
    }
}
