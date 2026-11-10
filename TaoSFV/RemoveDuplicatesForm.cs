using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TaoSFV.Localization;
using TaoSFV.Hash;
using TaoSFV.Model.Workset;

namespace TaoSFV
{
    public partial class RemoveDuplicatesForm : Form
    {
        private readonly ChecksumSet _set;
        private List<List<ChecksumFile>> _dupeSet;

        public RemoveDuplicatesForm(ChecksumSet set)
        {
            InitializeComponent();
            ThemeHelper.Apply(this);

            _set = set;

            // a virtual list: thousands of real rows took seconds to create when the window opened
            lvwGroupings.VirtualMode = true;
            lvwGroupings.RetrieveVirtualItem += lvwGroupings_RetrieveVirtualItem;
            Load += (sender, e) => SelectFirstGroup();

            InitializeFormVariables();

            SetLanguage();
        }

        private void SetLanguage()
        {
            var p = Language.RemoveDuplicatesForm;

            Text = p.Title;
            colGroupingsDuplicates.Text = p.DuplicatesColumnHeader;
            colGroupingsCount.Text = p.CountColumnHeader;

            lblSelectFileToKeep.Text = p.SelectFileToKeep;
            colFileName.Text = p.FileNameColumnHeader;

            lblOptionalSelectDirectories.Text = p.OptionalSelectDirsToDeleteAllDuplicatesFrom;
            colBadDirsDirectory.Text = p.DirectoryColumnHeader;
            colBadDirsCount.Text = p.CountColumnHeader;

            btnRemoveDuplicates.Text = p.DeleteDuplicatesButton;

            btnOK.Text = Language.General.CloseButton;
        }

        private void InitializeFormVariables()
        {
            // files no longer on disk aren't duplicates to choose between
            string dir = _set.Directory;
            foreach (List<ChecksumFile> group in DuplicateFinder.GetDuplicates(_set))
            {
                foreach (ChecksumFile file in group)
                {
                    if (!File.Exists(Path.Combine(dir, file.FileName)))
                        file.IsDeleted = true;
                }
            }

            RebuildGroupings();
        }

        private void RebuildGroupings()
        {
            _dupeSet = DuplicateFinder.GetDuplicates(_set);

            lvwGroupings.SelectedIndices.Clear();
            lvwGroupings.VirtualListSize = _dupeSet.Count;
            lvwGroupings.Invalidate();

            if (lvwGroupings.IsHandleCreated)
                SelectFirstGroup();
        }

        private void SelectFirstGroup()
        {
            if (_dupeSet.Count > 0)
                lvwGroupings.SelectedIndices.Add(0);
            else
                lvwGroupings_SelectedIndexChanged(lvwGroupings, EventArgs.Empty);
        }

        private void lvwGroupings_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            ListViewItem item = new ListViewItem(string.Format("{0:#,0} / {1:#,0}", e.ItemIndex + 1, _dupeSet.Count));
            item.SubItems.Add(_dupeSet[e.ItemIndex].Count.ToString());
            e.Item = item;
        }

        private List<ChecksumFile> SelectedGroup
        {
            get { return lvwGroupings.SelectedIndices.Count == 1 ? _dupeSet[lvwGroupings.SelectedIndices[0]] : null; }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void lvwGroupings_SelectedIndexChanged(object sender, EventArgs e)
        {
            lvwDuplicateFiles.Items.Clear();
            lvwBadDirectories.Items.Clear();
            btnRemoveDuplicates.Enabled = false;

            List<ChecksumFile> group = SelectedGroup;
            if (group != null)
            {
                foreach (ChecksumFile file in DuplicateFinder.Remaining(group))
                {
                    ListViewItem item = new ListViewItem();
                    item.Text = file.FileName;
                    item.Tag = file;
                    lvwDuplicateFiles.Items.Add(item);
                }
            }

            if (lvwDuplicateFiles.Items.Count > 0)
                lvwDuplicateFiles.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
        }

        private void lvwDuplicateFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvwDuplicateFiles.SelectedItems.Count == 1)
            {
                btnRemoveDuplicates.Enabled = true;
                ListViewItem selectedItem = lvwDuplicateFiles.SelectedItems[0];

                lvwBadDirectories.Items.Clear();
                foreach (ListViewItem item in lvwDuplicateFiles.Items)
                {
                    if (item == selectedItem)
                        continue;

                    ChecksumFile file = (ChecksumFile)item.Tag;

                    ListViewGroup group = new ListViewGroup(file.FileName);

                    // the other copy's folders, up to (not including) one the kept file is also in
                    string dir = Path.GetDirectoryName(file.FileName);
                    while (!string.IsNullOrEmpty(dir))
                    {
                        string dirWithSlash = dir + @"\";
                        if (selectedItem.Text.StartsWith(dirWithSlash, StringComparison.OrdinalIgnoreCase))
                            break;

                        ListViewItem badDirItem = new ListViewItem();
                        badDirItem.Text = dirWithSlash;
                        badDirItem.Group = group;
                        badDirItem.SubItems.Add(string.Format("{0:#,0}", DuplicateFinder.GetDeletableIn(_dupeSet, dirWithSlash).Count));
                        lvwBadDirectories.Items.Add(badDirItem);

                        dir = Path.GetDirectoryName(dir);
                    }

                    if (lvwBadDirectories.Items.Count > 0)
                    {
                        int colWidth = lvwBadDirectories.Columns[0].Width;
                        lvwBadDirectories.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
                        if (lvwBadDirectories.Columns[0].Width < colWidth)
                            lvwBadDirectories.Columns[0].Width = colWidth;
                    }
                }
            }
            else
            {
                btnRemoveDuplicates.Enabled = false;
            }
        }

        private void btnRemoveDuplicates_Click(object sender, EventArgs e)
        {
            var lang = Language.RemoveDuplicatesForm;

            if (lvwDuplicateFiles.SelectedItems.Count != 1)
                return;

            ListViewItem selectedItem = lvwDuplicateFiles.SelectedItems[0];
            ChecksumFile file = (ChecksumFile)selectedItem.Tag;

            string sourceFullPath = Path.Combine(_set.Directory, file.FileName);
            if (!File.Exists(sourceFullPath))
            {
                ThemedMessageBox.Show(
                    string.Format(lang.SourceFileDoesNotExist_Message, sourceFullPath),
                    lang.SourceFileDoesNotExist_Title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            string confirmMessage = string.Format(lang.ConfirmSourceFile_Message, file.FileName);
            DialogResult res = ThemedMessageBox.Show(confirmMessage, lang.ConfirmSourceFile_Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res != DialogResult.Yes)
                return;

            List<string> failed = new List<string>();

            Cursor.Current = Cursors.WaitCursor;
            List<ChecksumFile> group = SelectedGroup;
            DeleteFiles(DuplicateFinder.Remaining(group).Where(p => p != file), failed);
            Cursor.Current = Cursors.Default;

            foreach (ListViewItem badDirItem in lvwBadDirectories.SelectedItems)
            {
                string dir = badDirItem.Text;
                List<ChecksumFile> deleteFiles = DuplicateFinder.GetDeletableIn(_dupeSet, dir);
                if (deleteFiles.Count == 0)
                    continue;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Format(lang.ConfirmDelete_Message, deleteFiles.Count, dir));
                if (deleteFiles.Count <= 50)
                {
                    sb.AppendLine();
                    sb.AppendLine(lang.ConfirmDelete_Message_FileListHeader);
                    sb.AppendLine();
                    foreach (ChecksumFile deleteFile in deleteFiles)
                        sb.AppendLine(deleteFile.FileName);
                }

                DialogResult res2 = ThemedMessageBox.Show(sb.ToString(), lang.ConfirmDelete_Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res2 == DialogResult.Yes)
                {
                    Cursor.Current = Cursors.WaitCursor;
                    DeleteFiles(deleteFiles, failed);
                    Cursor.Current = Cursors.Default;
                }
            }

            if (failed.Count != 0)
            {
                string list = string.Join(Environment.NewLine, failed.Take(10));
                if (failed.Count > 10)
                    list += Environment.NewLine + "...";
                ThemedMessageBox.Show(string.Format(lang.DeleteFailed_Message, failed.Count, list), lang.ConfirmDelete_Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Cursor.Current = Cursors.WaitCursor;
            RebuildGroupings();
            Cursor.Current = Cursors.Default;
        }

        /// <summary>
        /// Deletes the files (Recycle Bin per Preferences) and marks them deleted. One that couldn't be deleted stays
        /// in the checksum file and is added to `failed` with the reason.
        /// </summary>
        private void DeleteFiles(IEnumerable<ChecksumFile> files, List<string> failed)
        {
            foreach (ChecksumFile removeFile in files.ToList())
            {
                string fullPath = Path.Combine(_set.Directory, removeFile.FileName);
                if (!removeFile.IsInsideSet)
                {
                    // the checksum file names it outside its own folder: never delete it from here
                    failed.Add(fullPath + Environment.NewLine + "    " + Language.RemoveDuplicatesForm.OutsideFolder);
                    continue;
                }
                try
                {
                    if (File.Exists(fullPath) && !Program.SafeDelete(fullPath))
                    {
                        failed.Add(fullPath + Environment.NewLine + "    " + Language.RemoveDuplicatesForm.NotRecycled);
                        continue;
                    }
                    removeFile.IsDeleted = true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    failed.Add(fullPath + Environment.NewLine + "    " + ex.Message);
                }
            }
        }

        private void RemoveDuplicatesForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            var lang = Language.RemoveDuplicatesForm;

            if (e.CloseReason == CloseReason.UserClosing)
            {
                Cursor.Current = Cursors.WaitCursor;
                int deleteCount = 0;
                foreach (ChecksumFile file in _set.Files)
                {
                    if (file.IsDeleted)
                        deleteCount++;
                }
                Cursor.Current = Cursors.Default;

                if (deleteCount > 0)
                {
                    string msg = string.Format(lang.RewriteChecksum_Message, deleteCount);
                    DialogResult res = ThemedMessageBox.Show(msg, lang.RewriteChecksum_Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                    if (res == DialogResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }

                    if (res == DialogResult.Yes)
                    {
                        Cursor.Current = Cursors.WaitCursor;
                        StringBuilder shabang = new StringBuilder();
                        ChecksumAlgorithm algorithm = ChecksumAlgorithm.Get(_set.Type);
                        bool md5Sum = Program.Settings.Create.MD5SumCompatibility;
                        string newLine = ChecksumFileFormat.GetNewLine(algorithm, md5Sum);

                        if (_set.Type == ChecksumType.SFV && Program.Settings.Create.SFV32Compatibility)
                        {
                            shabang.Append("; Generated by WIN-SFV32 v1 [added for sfv32 compatibility]").Append(newLine);
                        }

                        /*string[] comments = _set.Comments.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                        foreach (string line in comments)
                        {
                            shabang.AppendLine(";" + line);
                        }*/

                        foreach (ChecksumFile file in _set.Files)
                        {
                            if (file.IsDeleted)
                                continue;

                            shabang.Append(ChecksumFileFormat.FormatEntry(algorithm, md5Sum, file.FileName, file.OriginalChecksum)).Append(newLine);
                        }

                        string strShaBang = shabang.ToString();

                        ChecksumFileFormat.WriteAllText(_set.VerificationFileName, strShaBang);
                        Cursor.Current = Cursors.Default;
                    }
                }
            }
        }
    }
}
