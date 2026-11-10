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

        ///<summary>New &XXH128 File...</summary>
        public string Menu_File_NewXXH128File { get; set; }

        ///<summary>New &BLAKE3 File...</summary>
        public string Menu_File_NewBLAKE3File { get; set; }

        ///<summary>&Open...</summary>
        public string Menu_File_Open { get; set; }

        ///<summary>Verify &Folder...</summary>
        public string Menu_File_VerifyFolder { get; set; }

        ///<summary>&Save Results...</summary>
        public string Menu_File_SaveResults { get; set; }

        ///<summary>&Preferences...</summary>
        public string Menu_File_Preferences { get; set; }

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

        ///<summary>File Unreadable</summary>
        public string Menu_Legend_FileUnreadable { get; set; }

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

        ///<summary>Unreadable:</summary>
        public string UnreadableLabel { get; set; }

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

        ///<summary>Update Comments</summary>
        public string UpdateCommentsTemplate_Title { get; set; }

        ///<summary>The comments written at the top of new checksum files still mention ilSFV:\n\n{0}\n\nChange ilSFV to TaoSFV and cdtag.com links to TaoSFV's page?</summary>
        public string UpdateCommentsTemplate_Message { get; set; }

        ///<summary>{0} - TaoSFV</summary>
        public string Par2_Title { get; set; }

        ///<summary>File</summary>
        public string Par2_FileColumn { get; set; }

        ///<summary>Size</summary>
        public string Par2_SizeColumn { get; set; }

        ///<summary>Status</summary>
        public string Par2_StatusColumn { get; set; }

        ///<summary>Blocks OK</summary>
        public string Par2_BlocksColumn { get; set; }

        ///<summary>Reading the PAR2 files...</summary>
        public string Par2_Loading { get; set; }

        ///<summary>Checking files...</summary>
        public string Par2_Verifying { get; set; }

        ///<summary>Repairing...</summary>
        public string Par2_Repairing { get; set; }

        ///<summary>{0} files, {1} blocks of {2}, {3} recovery blocks</summary>
        public string Par2_SetInfo { get; set; }

        ///<summary>OK</summary>
        public string Par2_OK { get; set; }

        ///<summary>OK, found as {0}</summary>
        public string Par2_FoundAs { get; set; }

        ///<summary>Damaged</summary>
        public string Par2_Damaged { get; set; }

        ///<summary>Missing</summary>
        public string Par2_Missing { get; set; }

        ///<summary>{0} (no recovery data)</summary>
        public string Par2_NotProtected { get; set; }

        ///<summary>{0} of {1}</summary>
        public string Par2_BlocksCount { get; set; }

        ///<summary>({0} shifted)</summary>
        public string Par2_Shifted { get; set; }

        ///<summary>All files are OK.</summary>
        public string Par2_AllOk { get; set; }

        ///<summary>{0} blocks damaged or missing, {1} recovery blocks available: repair is possible.</summary>
        public string Par2_Repairable { get; set; }

        ///<summary>{0} blocks damaged or missing, {1} recovery blocks available: {2} more needed to repair.</summary>
        public string Par2_NotRepairable { get; set; }

        ///<summary>A damaged file isn't covered by the recovery data, so it can't be repaired.</summary>
        public string Par2_UnprotectedDamage { get; set; }

        ///<summary>{0} damaged PAR2 packets were skipped.</summary>
        public string Par2_DamagedPackets { get; set; }

        ///<summary>&Repair</summary>
        public string Par2_RepairButton { get; set; }

        ///<summary>&Check Again</summary>
        public string Par2_VerifyButton { get; set; }

        ///<summary>Open &Folder</summary>
        public string Par2_OpenFolderButton { get; set; }

        ///<summary>&Stop</summary>
        public string Par2_StopButton { get; set; }

        ///<summary>Rebuild {0} file(s) from the recovery data?\n\nA damaged original is kept as name.bad, and files found under another name are moved back.</summary>
        public string Par2_RepairConfirm { get; set; }

        ///<summary>Repair complete: {0} file(s) rebuilt, {1} moved back to their name.</summary>
        public string Par2_Repaired { get; set; }

        ///<summary>Repair failed: {0}</summary>
        public string Par2_RepairFailed { get; set; }

        ///<summary>{0} couldn't be read as a PAR2 file.\n\n{1}</summary>
        public string Par2_ReadError { get; set; }

        ///<summary>Stopped.</summary>
        public string Par2_Cancelled { get; set; }

        ///<summary>Check with TaoSFV</summary>
        public string Par2_ExplorerVerb { get; set; }

        ///<summary>Checksum Files Skipped</summary>
        public string SkippedChecksumFiles_Title { get; set; }

        ///<summary>{0} checksum file(s) couldn't be read and were skipped. The others are checked as usual.\n\n{1}</summary>
        public string SkippedChecksumFiles_Message { get; set; }

        ///<summary>Text file (*.txt)|*.txt|CSV for spreadsheets (*.csv)|*.csv|Web page (*.html)|*.html</summary>
        public string SaveResults_Filter { get; set; }

        ///<summary>TaoSFV results, {0:yyyy-MM-dd HH:mm}</summary>
        public string SaveResults_Title { get; set; }

        ///<summary>{0:#,0} files in {1:#,0} sets: {2}</summary>
        public string SaveResults_Summary { get; set; }

        ///<summary>expected {0}, got {1}</summary>
        public string SaveResults_BadDetail { get; set; }

        ///<summary>Not checked</summary>
        public string SaveResults_NotChecked { get; set; }

        ///<summary>OK</summary>
        public string SaveResults_OK { get; set; }

        ///<summary>Bad</summary>
        public string SaveResults_Bad { get; set; }

        ///<summary>Missing</summary>
        public string SaveResults_Missing { get; set; }

        ///<summary>Unreadable</summary>
        public string SaveResults_Unreadable { get; set; }

        ///<summary>Sc&amp;hedule Checks...</summary>
        public string Menu_Tools_ScheduleChecks { get; set; }

        ///<summary>Checksum file not read</summary>
        public string SaveResults_NotRead { get; set; }

        ///<summary>TaoSFV found problems in {0}</summary>
        public string ScheduledCheck_Failed_Title { get; set; }

        ///<summary>{0:#,0} checksum file(s) couldn&apos;t be read.</summary>
        public string ScheduledCheck_NotRead { get; set; }

        ///<summary>Click to see the report.</summary>
        public string ScheduledCheck_ClickForReport { get; set; }

        ///<summary>Scheduled Checks</summary>
        public string ScheduleChecks_Title { get; set; }

        ///<summary>TaoSFV checks these folders on a schedule while you&apos;re signed in, reading every file, and only notifies you if one is bad, missing or unreadable. A check missed while the PC was off runs as soon as it can.</summary>
        public string ScheduleChecks_Intro { get; set; }

        ///<summary>Folder</summary>
        public string ScheduleChecks_FolderColumn { get; set; }

        ///<summary>When</summary>
        public string ScheduleChecks_WhenColumn { get; set; }

        ///<summary>Next check</summary>
        public string ScheduleChecks_NextRunColumn { get; set; }

        ///<summary>Last check</summary>
        public string ScheduleChecks_LastRunColumn { get; set; }

        ///<summary>Folder:</summary>
        public string ScheduleChecks_Folder { get; set; }

        ///<summary>Browse...</summary>
        public string ScheduleChecks_Browse { get; set; }

        ///<summary>Every</summary>
        public string ScheduleChecks_Every { get; set; }

        ///<summary>day</summary>
        public string ScheduleChecks_Daily { get; set; }

        ///<summary>week</summary>
        public string ScheduleChecks_Weekly { get; set; }

        ///<summary>month</summary>
        public string ScheduleChecks_Monthly { get; set; }

        ///<summary>on</summary>
        public string ScheduleChecks_On { get; set; }

        ///<summary>at</summary>
        public string ScheduleChecks_At { get; set; }

        ///<summary>&amp;Add / Update</summary>
        public string ScheduleChecks_Save { get; set; }

        ///<summary>&amp;Remove</summary>
        public string ScheduleChecks_Remove { get; set; }

        ///<summary>Open Reports Folder</summary>
        public string ScheduleChecks_Reports { get; set; }

        ///<summary>Every day at {0:t}</summary>
        public string ScheduleChecks_DailyAt { get; set; }

        ///<summary>Every {1} at {0:t}</summary>
        public string ScheduleChecks_WeeklyAt { get; set; }

        ///<summary>Day {1} of every month at {0:t}</summary>
        public string ScheduleChecks_MonthlyAt { get; set; }

        ///<summary>The folder &apos;{0}&apos; doesn&apos;t exist.</summary>
        public string ScheduleChecks_FolderNotFound { get; set; }

        ///<summary>Task Scheduler couldn&apos;t save the change:\n\n{0}</summary>
        public string ScheduleChecks_Error { get; set; }

        ///<summary>Failed Files Not Deleted</summary>
        public string NotRecycled_Title { get; set; }

        ///<summary>{0:#,0} failed file(s) weren't deleted: the Recycle Bin can't hold paths of 260 characters or more, so they were left in place. ...\n\n{1}</summary>
        public string NotRecycled_Message { get; set; }

        ///<summary>Checksum File Not Read</summary>
        public string UnreadableChecksumFile_Title { get; set; }

        ///<summary>File '{0}' couldn't be read.\n\n{1}</summary>
        public string UnreadableChecksumFile_Message { get; set; }

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

        ///<summary>{0:#,0} file(s) couldn't be renamed:\n\n{1}</summary>
        public string TruncateFileNames_NotRenamed_Message { get; set; }

        ///<summary>The extension leaves no room for a shorter name.</summary>
        public string TruncateFileNames_NoRoom { get; set; }

        ///<summary>{0:#,0} checksum file(s) in the folder updated to the new names.</summary>
        public string TruncateFileNames_ChecksumFilesUpdated { get; set; }

        ///<summary>Select a Folder</summary>
        public string FolderBrowseDialog_Title { get; set; }
    }
}
