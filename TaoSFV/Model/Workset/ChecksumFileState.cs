namespace TaoSFV.Model.Workset
{
    public enum ChecksumFileState
    {
        NotProcessed = 0,
        OK = 1,
        Bad = 2,
        Missing = 3,
        /// <summary>The file exists but couldn't be read (locked, no access, I/O error); see ChecksumFile.ErrorMessage.</summary>
        Unreadable = 4
    }
}
