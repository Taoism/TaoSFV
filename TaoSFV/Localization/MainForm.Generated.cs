namespace TaoSFV.Localization
{
    public class MainForm
    {
        ///<summary>&File</summary>
        public string Menu_File { get; set; }

        ///<summary>New &SFV File...</summary>
        public string Menu_File_NewSFVFile { get; set; }

        ///<summary>New &MD5 File...</summary>
        public string Menu_File_NewMD5File { get; set; }

        ///<summary>New SH&A-1 File...</summary>
        public string Menu_File_NewSHA1File { get; set; }

        ///<summary>New SHA-&256 File...</summary>
        public string Menu_File_NewSHA256File { get; set; }

        ///<summary>New SHA-&512 File...</summary>
        public string Menu_File_NewSHA512File { get; set; }

        ///<summary>&Open...</summary>
        public string Menu_File_Open { get; set; }

        ///<summary>Verify &Folder...</summary>
        public string Menu_File_VerifyFolder { get; set; }

        ///<summary>&Preferences...</summary>
        public string Menu_File_Preferences { get; set; }

        ///<summary>Check For &Updates</summary>
        public string Menu_File_CheckForUpdates { get; set; }

        ///<summary>E&xit</summary>
        public string Menu_File_Exit { get; set; }

        ///<summary>&Legend</summary>
        public string Menu_Legend { get; set; }

        ///<summary>File OK</summary>
        public string Menu_Legend_FileOK { get; set; }

        ///<summary>File Bad</summary>
        public string Menu_Legend_FileBad { get; set; }

        ///<summary>File Not Found</summary>
        public string Menu_Legend_FileNotFound { get; set; }

        ///<summary>Untested-Unknown</summary>
        public string Menu_Legend_FileUntestedUnknown { get; set; }

        ///<summary>&Tools</summary>
        public string Menu_Tools { get; set; }

        ///<summary>Find &Renamed Files</summary>
        public string Menu_Tools_FindRenamedFiles { get; set; }

        ///<summary>Use &Cached Results</summary>
        public string Menu_Tools_UseCachedResults { get; set; }

        ///<summary>Find/Delete Duplicate Files Using Checksum...</summary>
        public string Menu_Tools_FindDeleteDuplicateFilesUsingChecksum { get; set; }

        ///<summary>&Truncate File Names...</summary>
        public string Menu_Tools_TruncateFileNames { get; set; }

        ///<summary>Register File Types</summary>
        public string Menu_Tools_RegisterFileTypes { get; set; }

        ///<summary>Unregister File Types</summary>
        public string Menu_Tools_UnregisterFileTypes { get; set; }

        ///<summary>Import ilSFV Settings...</summary>
        public string Menu_Tools_ImportIlsfvSettings { get; set; }

        ///<summary>&View</summary>
        public string Menu_View { get; set; }

        ///<summary>&Hide Good</summary>
        public string Menu_View_HideGood { get; set; }

        ///<summary>&Comment/Result Pane</summary>
        public string Menu_View_CommentResultPane { get; set; }

        ///<summary>&Help</summary>
        public string Menu_Help { get; set; }

        ///<summary>&About</summary>
        public string Menu_Help_About { get; set; }

        ///<summary>File name</summary>
        public string FileNameColumnHeader { get; set; }

        ///<summary>Comments</summary>
        public string CommentsTabHeader { get; set; }

        ///<summary>Sets:</summary>
        public string SetsLabel { get; set; }

        ///<summary>Parts:</summary>
        public string PartsLabel { get; set; }

        ///<summary>Good:</summary>
        public string GoodLabel { get; set; }

        ///<summary>Bad:</summary>
        public string BadLabel { get; set; }

        ///<summary>Missing:</summary>
        public string MissingLabel { get; set; }

        ///<summary>Hide Good</summary>
        public string HideGoodCheckBox { get; set; }

        ///<summary>&Pause</summary>
        public string PauseButton { get; set; }

        ///<summary>&Resume</summary>
        public string ResumeButton { get; set; }

        ///<summary>Hide</summary>
        public string HideButton { get; set; }

        ///<summary>&Go</summary>
        public string GoButton { get; set; }

        ///<summary>&Stop</summary>
        public string StopButton { get; set; }

        ///<summary>A new version of TaoSFV ({0}.{1}.{2}) is available.\n\nWould you like to download it now?</summary>
        public string UpdateAvailable_Message { get; set; }

        ///<summary>Update Available</summary>
        public string UpdateAvailable_Title { get; set; }

        ///<summary>You have the latest version.</summary>
        public string NoUpdateAvailable_Message { get; set; }

        ///<summary>Check For Updates</summary>
        public string NoUpdateAvailable_Title { get; set; }

        ///<summary>File '{0}' does not exist.</summary>
        public string FileNotFound_Message { get; set; }

        ///<summary>File not found</summary>
        public string FileNotFound_Title { get; set; }

        ///<summary>File '{0}' contents not recognized as {1} verification file.\n\n{2}</summary>
        public string FileContentsNotRecognized { get; set; }

        ///<summary>Ready.</summary>
        public string Status_Ready { get; set; }

        ///<summary>Paused.</summary>
        public string Status_Paused { get; set; }

        ///<summary>Renaming '{0}' to '{1}'...</summary>
        public string Status_Renaming { get; set; }

        ///<summary>Getting file list...</summary>
        public string Status_GettingFileList { get; set; }

        ///<summary>Pre-sorting...</summary>
        public string Status_PreSorting { get; set; }

        ///<summary>Getting file info...</summary>
        public string Status_GettingFileInfo { get; set; }

        ///<summary>Getting file info ({0}%)...</summary>
        public string Status_GettingFileInfoPercentage { get; set; }

        ///<summary>Getting file list for {0}...</summary>
        public string Status_GettingFileListForDirectory { get; set; }

        ///<summary>Loading {0}...</summary>
        public string Status_LoadingFile { get; set; }

        ///<summary>Loading {0} ({1}%)...</summary>
        public string Status_LoadingFilePercentage { get; set; }

        ///<summary>Working...</summary>
        public string Status_Working { get; set; }

        ///<summary>Working... ({0}%)</summary>
        public string Status_WorkingPercentage { get; set; }

        ///<summary>Updating cache...</summary>
        public string Status_UpdatingCache { get; set; }

        ///<summary>{0}% | ETA: {1} | Elapsed: {2}</summary>
        public string Status_ETA { get; set; }

        ///<summary>Looking for long file names...</summary>
        public string Status_LookingForLongFileNames { get; set; }

        ///<summary>Finished: {0}% Complete - {1:#,0.0} MB in {2:#,0.0} seconds ({3:#,0.0} MB/s).</summary>
        public string Status_FinishedUnder10Minutes { get; set; }

        ///<summary>Finished: {0}% Complete - {1:#,0.0} MB in {2:00}:{3:00}:{4:00} ({5:#,0.0} MB/s).</summary>
        public string Status_Finished10MinutesOrMore { get; set; }

        ///<summary>Done verifying ({0}% OK).</summary>
        public string SystemTray_DoneVerifying { get; set; }

        ///<summary>Done creating checksum files.</summary>
        public string SystemTray_DoneCreating { get; set; }

        ///<summary>Finding renames in {0}...</summary>
        public string Status_FindingRenamesInFile { get; set; }

        ///<summary>{0:#,0} renamed file(s) found.</summary>
        public string FindRenamedFiles_Message { get; set; }

        ///<summary>Find Renamed Files</summary>
        public string FindRenamedFiles_Title { get; set; }

        ///<summary>The file already exists, would you like to overwrite it?\n\n{0}</summary>
        public string OverwriteFile_Message { get; set; }

        ///<summary>Confirmation Needed</summary>
        public string OverwriteFile_Title { get; set; }

        ///<summary>{0:#,0} existing checksum file(s) will be overwritten. Continue?</summary>
        public string OverwriteMultipleFiles_Message { get; set; }

        ///<summary>Confirmation Needed</summary>
        public string OverwriteMultipleFiles_Title { get; set; }

        ///<summary>{0}</summary>
        public string RegisterFileTypesError_Message { get; set; }

        ///<summary>Register File Types</summary>
        public string RegisterFileTypesError_Title { get; set; }

        ///<summary>TaoSFV now opens .sfv, .md5 and .sha1 files for your account, and adds a TaoSFV menu to files and folders in Explorer.</summary>
        public string RegisterFileTypes_Message { get; set; }

        ///<summary>TaoSFV file associations and Explorer menus were removed for your account.</summary>
        public string UnregisterFileTypes_Message { get; set; }

        ///<summary>Choose a folder. Every checksum file in it and its subfolders will be verified.</summary>
        public string VerifyFolder_Description { get; set; }

        ///<summary>Import ilSFV Settings</summary>
        public string ImportIlsfv_Title { get; set; }

        ///<summary>ilSFV is installed on this computer. Bring its preferences, usage statistics, recent files and checksum cache into TaoSFV?\n\nYou can do this later from Tools > Import ilSFV Settings. ilSFV itself is not changed.</summary>
        public string ImportIlsfv_FirstStart_Message { get; set; }

        ///<summary>Replace TaoSFV's preferences with ilSFV's, and add ilSFV's usage statistics, recent files and checksum cache?\n\nTaoSFV's theme is kept. ilSFV itself is not changed.</summary>
        public string ImportIlsfv_Confirm_Message { get; set; }

        ///<summary>ilSFV settings imported: preferences, usage statistics ({0:#,0} files checked), {1} recent files and {2:#,0} cached checksums.\n\nWindow size and position apply the next time TaoSFV starts.</summary>
        public string ImportIlsfv_Done_Message { get; set; }

        ///<summary>ilSFV's settings couldn't be imported.\n\n{0}</summary>
        public string ImportIlsfv_Error_Message { get; set; }

        ///<summary>Copy File Name(s)</summary>
        public string CopyFileNamesContextMenu { get; set; }

        ///<summary>Copy Path + File Name(s)</summary>
        public string CopyPathAndFileNamesContextMenu { get; set; }

        ///<summary>Copy Current Checksum ({0})</summary>
        public string CopyCurrentChecksumContextMenu { get; set; }

        ///<summary>Copy Original Checksum ({0})</summary>
        public string CopyOriginalChecksumContextMenu { get; set; }

        ///<summary>Max Length</summary>
        public string TruncateFileNames_MaxLength { get; set; }

        ///<summary>Minimum length is 12.</summary>
        public string TruncateFileNames_MinimumLengthIs12 { get; set; }

        ///<summary>{0:#,0} file(s) renamed.</summary>
        public string TruncateFileNames_FilesRenamed_Message { get; set; }

        ///<summary>Select a Folder</summary>
        public string FolderBrowseDialog_Title { get; set; }
    }
}
