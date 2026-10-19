using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using TaoSFV.Verification;
using System.Threading.Tasks;
using TaoSFV.Hash;
using TaoSFV.Localization;
using TaoSFV.Model.Settings;
using TaoSFV.Model.Workset;
using TaoSFV.Tools;
using Timer = System.Windows.Forms.Timer;

namespace TaoSFV
{
    public sealed partial class MainForm : Form
    {
        // TODO: PRI 0
        // TODO: comments font selection; see http://discuss.big-o-software.com/viewtopic.php?f=3&t=204
        // TODO: comment template by format (md5/sha1/sfv)
        // TODO: Results file: cSFVse; see http://discuss.big-o-software.com/viewtopic.php?f=3&t=1046 - called "Result Logs", http://discuss.big-o-software.com/viewtopic.php?f=3&t=4632
        // TODO: Support to create checksums of Read-only folders (e.g. CDROM) and save the checksum in a different folder

        // - Drag'n'Drop support for Folders (recursive) and mixed selected folders/files.
        // - Creation of Checksum File via context menu with recursive / subdirectory support
        // - Check for long filenames / folder names and skip those files with correct error msg (at the moment taosfv does not accept drag'n'drop of files with path longer than 250 characters)
        // When you have multiple directory sfv creattion (create file in each subdirectory) and prompt for file name, successive prompts should be appear (so that all files are not named the same)
        // an option (auto replace sfv in directory) would be nice.
        // And a new sfv file created should use the cache for faster creation (in case the same files have already been checked).
        // queue the creation of multiple SFV files.
        // optimze for 64 bit

        /*
double click item to open containing folder
checksum auto formatting.. 00-{0}.sfv where {0} = dir name - http://img4.imageshack.us/img4/9409/clipboard15u.jpg
compare to cSFV http://cruzer.antispam.dk/csfv/versionhistory.php
ensure overwrite +r
export results to text file
         */

        // TODO: PRI 1
        // TODO: upper/lower case checksum, per format
        // TODO: show current cache size
        // TODO: add tick marks to list view items (which items to check)
        // TODO: command line support
        // TODO: empty folder on verify: Something like this when passed: [100% Complete]-[#30 files OK] and when not passed: [80% Complete]-[#27 files OK - #2 Bads - 1 Missing] 
        // TODO: hash in filename (regex?)
        // TODO: utf8/codepage

        // TODO: PRI 2
        // TODO: Shell, get checksum of files without creating .sfv file
        // TODO: Shell extension (allows you to select files in explorer to check, or to create an .SFV or .MD5 for)
        // TODO: Shell, recursively check directories
        // TODO: Shell, allow user to specify if they want shell extensions nested in submenu
        // TODO: Shell terms "Context menu handler", "QueryInfo handler"
        // TODO: restores previous file associations when you uninstall
        // TODO: fix file association when other programs steal it
        // TODO: Explorer Info Tooltip (Files in SFV: 12)
        // TODO: intelligent sorting, ex, Flex9 before Flex10, '9 - abc' before '10 - abc'
        // TODO: md5sum detailed compatibility: http://discuss.big-o-software.com/viewtopic.php?f=2&t=5441 use # for comments, \n only for newline

        // TODO: PRI 3
        // TODO: Multilingual support
        // TODO: PAR/PAR2 (see program FSRaid, avoid mirror and smartpar)


        [DllImport("user32")]
        private static extern bool SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint LVM_SETTEXTBKCOLOR = 0x1026;

        [DllImport("user32.dll")]
        private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private readonly List<ChecksumSet> _sets = new List<ChecksumSet>();
        private bool _workingOnList;
        private bool _queueHideGood;
        private bool _queueStop;

        private readonly bool _initialVerify;
        private readonly bool _initialCreate;
        private readonly ChecksumType _initialCreateChecksumType;
        private readonly Timer _timer;
        private readonly FileSystemWatcher _fsw;

        public void PlayCompleteSound(bool allOK)
        {
            if (allOK)
            {
                if (!Program.Settings.Check.PlaySoundOK)
                    return;

                if (Program.Settings.Check.PlaySoundOK_OnlyIfInactive && GetForegroundWindow() == Handle)
                    return;
            }
            else
            {
                if (!Program.Settings.Check.PlaySoundError)
                    return;
            }

            try
            {
                string shortFileName = string.Format("complete_{0}.wav", allOK ? "ok" : "error");
                string completeWav = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), shortFileName);
                if (File.Exists(completeWav))
                {
                    Sound.PlaySound(completeWav, IntPtr.Zero, Sound.SoundFlags.SND_FILENAME | Sound.SoundFlags.SND_ASYNC);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }

        public MainForm(string[] args)
        {
            InitializeComponent();
            ThemeHelper.Apply(this);

            // state image 4 (Unreadable): a purple square derived from the blue "untested" one
            Bitmap unreadableIcon = MakeUnreadableIcon(Properties.Resources.Bullet_6);
            imageList1.Images.Add(unreadableIcon);
            miFileUnreadable.Image = unreadableIcon;
            lvwFiles.ShowItemToolTips = true;
            lvwFiles.VirtualMode = true;
            lvwFiles.RetrieveVirtualItem += lvwFiles_RetrieveVirtualItem;

            Language.Changed += delegate { SetLanguage(); };
            SetLanguage();

            //miFindRenamedFiles.Visible = false;
            //miTruncateFileNames.Visible = false;
            //toolStripSeparator6.Visible = false;

            _fsw = new FileSystemWatcher(Program.AppDataPath, "*.add");
            _fsw.Created += _fsw_Created;
            _fsw.EnableRaisingEvents = true;

            notifyIcon1.BalloonTipClicked += notifyIcon1_BalloonTipClicked;

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            int major = version.Major;
            int minor = version.Minor;
            int build = version.Build;

            Text = string.Format("TaoSFV {0}.{1}", major, minor, build);

            ResizeStatusBar();

            chkHideGood.Checked = Program.Settings.General.HideGoodFiles;
            miHideGood.Checked = Program.Settings.General.HideGoodFiles;
            miUseCachedResults.Checked = Program.Settings.General.UseCachedResults;
            miFindRenamedFiles.Enabled = false;

            btnPause.Enabled = false;
            btnHide.Enabled = false;

            if (Program.Settings.General.RememberWindowPlacement)
            {
                if (Program.Settings.General.WindowTop == 0 && Program.Settings.General.WindowLeft == 0)
                {
                    StartPosition = FormStartPosition.CenterScreen;
                }
                else
                {
                    StartPosition = FormStartPosition.Manual;
                    Top = Program.Settings.General.WindowTop;
                    Left = Program.Settings.General.WindowLeft;
                }

                Height = Program.Settings.General.WindowHeight;
                Width = Program.Settings.General.WindowWidth;
                WindowState = Program.Settings.General.FormWindowState;
            }

            ToggleCommentsPane();

            if (args != null && args.Length > 0)
            {
                int argStart = 0;
                switch (args[0].ToLower())
                {
                    case "/verify":
                        _initialVerify = true;
                        argStart++;
                        break;

                    case "/create":
                        _initialCreate = true;
                        argStart += 2;
                        break;

                    default:
                        _initialVerify = true;
                        break;
                }

                List<string> files = new List<string>();
                for (int i = argStart; i < args.Length; i++)
                {
                    files.Add(args[i]);
                }

                if (_initialVerify)
                {
                    EngageDropTimer(files);
                }
                else if (_initialCreate)
                {
                    ChecksumAlgorithm createAlgorithm = ChecksumAlgorithm.FromExtension(args[1]);
                    if (createAlgorithm == null)
                        throw new Exception(string.Format("{0} not implemented", args[1]));
                    _initialCreateChecksumType = createAlgorithm.Type;

                    EngageCreateTimer(files);
                }
            }

            _timer = new Timer();
            _timer.Interval = 1;
            _timer.Tick += _timer_Tick;
            _timer.Enabled = true;

            Load += MainForm_Load;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            SendMessage(lvwFiles.Handle, LVM_SETTEXTBKCOLOR, IntPtr.Zero, unchecked((IntPtr)(int)0xFFFFFF));
        }

        private void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            UnhideFromSystray();
        }

        private readonly List<string> _newSets = new List<string>();

        private void _fsw_Created(object sender, FileSystemEventArgs e)
        {
            try
            {
                List<string> newSets = new List<string>();

                string path = e.FullPath;
                if (File.Exists(path))
                {
                    Thread.Sleep(500);

                    string[] files = File.ReadAllLines(path);
                    File.Delete(path);

                    foreach (string file in files)
                    {
                        newSets.Add(file);
                    }
                }

                UnhideFromSystray();
                Application.DoEvents();

                if (!_workingOnList)
                {
                    QueueNewSets(newSets);
                }
                else
                {
                    lock (_newSets)
                    {
                        foreach (string set in newSets)
                            _newSets.Add(set);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Language.General.Error, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void QueueNewSets(List<string> newSets)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<List<string>>(QueueNewSets), newSets);
            }
            else
            {
                EngageDropTimer(newSets);
            }
        }

        /// <summary>Adds checksum files and folders opened while checking, so the run checks them too.</summary>
        private async Task LoadNewSetsAsync()
        {
            List<string> newSets = TakeNewSets();
            if (newSets.Count == 0)
                return;

            Activate();
            await LoadSetsAsync(newSets);
        }

        private void _timer_Tick(object sender, EventArgs e)
        {
            _timer.Enabled = false;

            SetAlwaysOnTop();

            if (Program.Settings.General.CheckForUpdates)
            {
                if (Program.Settings.General.LastUpdateCheck + TimeSpan.FromDays(Program.Settings.General.UpdateCheckFrequency) < DateTime.Now)
                {
                    try
                    {
                        CheckForUpdates(false);
                    }
                    catch (Exception ex)
                    {
                        // error checking for updates
                        Trace.WriteLine(ex);
                    }
                }
            }
        }

        private static void CheckForUpdates(bool verbose)
        {
            if (verbose)
                Cursor.Current = Cursors.WaitCursor;

            string content = Encoding.ASCII.GetString(Http.Get("http://cdtag.com/taosfv/version.txt"));
            string[] strparts = content.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int[] parts = strparts.Select(p => p.Length < 5 ? int.Parse(p) : -1).ToArray();
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            int major = version.Major;
            int minor = version.Minor;
            int build = version.Build;

            if (verbose)
                Cursor.Current = Cursors.Default;

            if (major < parts[0] ||
                (major == parts[0] && minor < parts[1]) ||
                (major == parts[0] && minor == parts[1] && build < parts[2]))
            {
                DialogResult res = MessageBox.Show(
                    string.Format(Language.MainForm.UpdateAvailable_Message, parts[0], parts[1], parts[2]),
                    Language.MainForm.UpdateAvailable_Title,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (res == DialogResult.Yes)
                {
                    Program.OpenUrl(strparts[3]);
                }
            }
            else
            {
                if (verbose)
                {
                    MessageBox.Show(Language.MainForm.NoUpdateAvailable_Message, Language.MainForm.NoUpdateAvailable_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            Program.Settings.General.LastUpdateCheck = DateTime.Now;
        }

        /// <summary>
        /// Notifies the user that the application requests attention
        /// by flashing the taskbar if the form is not the current window.
        /// </summary>
        /// <param name="myForm">The form in question.</param>
        public static void FlashWindow(Form myForm)
        {
            // if the current foreground window isn't this window, flash this window in task bar once every 1 second
            // ReSharper disable EmptyGeneralCatchClause
            try
            {
                if (GetForegroundWindow() != myForm.Handle)
                    FlashWindow(myForm.Handle, true);
            }
            catch
            {
            }
            // ReSharper restore EmptyGeneralCatchClause
        }

        private long _totalSizeOfSets;
        private void GetWorkset(string fileName, string directoryName, ChecksumType setType, List<string> filesAndFolders, out int overwriteCount)
        {
            SetStatusText(Language.MainForm.Status_GettingFileList);
            Application.DoEvents();

            IDictionary<string, List<string>> files;

            if (filesAndFolders == null)
            {
                if (Program.Settings.General.Recursive)
                {
                    files = GetFilesAllDirectories(directoryName);
                }
                else
                {
                    files = new Dictionary<string, List<string>>();
                    files.Add(directoryName, Directory.GetFiles(directoryName, "*.*", SearchOption.TopDirectoryOnly).ToList());
                }
            }
            else
            {
                files = new Dictionary<string, List<string>>();
                foreach (string item in filesAndFolders)
                {
                    if (Directory.Exists(item))
                    {
                        if (Program.Settings.General.Recursive)
                        {
                            foreach (var kvp in GetFilesAllDirectories(item))
                            {
                                files.Add(kvp.Key, kvp.Value);
                            }
                        }
                        else
                        {
                            files.Add(item, Directory.GetFiles(item, "*.*", SearchOption.TopDirectoryOnly).ToList());
                        }
                    }
                    else
                    {
                        List<string> topFiles;
                        if (!files.TryGetValue(directoryName, out topFiles))
                        {
                            topFiles = new List<string>();
                            files.Add(directoryName, topFiles);
                        }
                        topFiles.Add(item);
                    }
                }
            }

            if (Program.Settings.Create.SortFiles)
            {
                SetStatusText(Language.MainForm.Status_PreSorting);
                Application.DoEvents();

                files = new SortedDictionary<string, List<string>>(files, StringComparer.InvariantCultureIgnoreCase);
                foreach (KeyValuePair<string, List<string>> kvp in new Dictionary<string, List<string>>(files))
                {
                    files[kvp.Key] = kvp.Value.OrderBy(p => p).ToList();
                }
            }

            SetStatusText(Language.MainForm.Status_GettingFileInfo);
            Application.DoEvents();

            List<string> excludeExt = new List<string>();
            if (!string.IsNullOrEmpty(Program.Settings.Create.ExcludeFilesOfType))
            {
                string[] exts = Program.Settings.Create.ExcludeFilesOfType.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string ext in exts)
                {
                    string extMod = ext.ToLower();
                    if (extMod.StartsWith("*"))
                        extMod = extMod.Substring(1, extMod.Length - 1);
                    if (extMod.StartsWith("."))
                    {
                        if (!excludeExt.Contains(extMod))
                            excludeExt.Add(extMod);
                    }
                }
            }

            _sets.Clear();

            ChecksumSet set = new ChecksumSet(fileName, directoryName, setType);
            string checksumExt = Path.GetExtension(fileName);

            if (!Program.Settings.Create.CreateForEachSubDir)
                _sets.Add(set);

            int trimLength = directoryName.Length;
            if (!directoryName.EndsWith("\\"))
                trimLength++;

            int filesProcessed = 0;
            int lastPercent = 0;
            int totalFiles = 0;

            foreach (KeyValuePair<string, List<string>> kvp in files)
            {
                totalFiles += kvp.Value.Count;
            }

            foreach (KeyValuePair<string, List<string>> kvp in files)
            {
                if (Program.Settings.Create.CreateForEachSubDir)
                {
                    string tmpFileName;
                    if (Program.Settings.Create.PromptForFileName)
                    {
                        tmpFileName = Path.Combine(kvp.Key, Path.GetFileName(fileName));
                    }
                    else
                    {
                        tmpFileName = Path.GetFileName(kvp.Key) + checksumExt;
                        tmpFileName = Path.Combine(kvp.Key, tmpFileName);
                    }
                    set = new ChecksumSet(tmpFileName, kvp.Key, setType);
                    _sets.Add(set);

                    trimLength = kvp.Key.Length;
                    if (!kvp.Key.EndsWith("\\"))
                        trimLength++;
                }

                long directoryTotalSize = 0;
                foreach (string file in kvp.Value)
                {
                    filesProcessed++;
                    int percent = (filesProcessed * 100 / totalFiles);

                    if (percent / 5 > lastPercent / 5)
                    {
                        SetStatusText(string.Format(Language.MainForm.Status_GettingFileInfoPercentage, (percent / 5) * 5));
                        Application.DoEvents();

                        lastPercent = percent;
                    }

                    bool ok = true;

                    foreach (string ext in excludeExt)
                    {
                        if (string.Compare(Path.GetExtension(file), ext, true) == 0)
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok)
                    {
                        FileInfo fileInfo = TryGetNewFileInfo(file);
                        if ((fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                            continue;

                        ChecksumFile checksumFile = new ChecksumFile(set);
                        checksumFile.FileInfo = fileInfo;
                        checksumFile.State = ChecksumFileState.NotProcessed;
                        checksumFile.FileName = file.Substring(trimLength, file.Length - trimLength);

                        set.Files.Add(checksumFile);

                        directoryTotalSize += fileInfo.Length;
                    }
                }

                set.TotalSize += directoryTotalSize;
            }

            overwriteCount = 0;
            foreach (ChecksumSet setx in _sets)
            {
                if (File.Exists(setx.VerificationFileName))
                    overwriteCount++;
            }
        }

        private static FileInfo TryGetNewFileInfo(string file)
        {
            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(file);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("'{0}': {1}", file, ex.Message), ex);
            }

            return fileInfo;
        }

        private Dictionary<string, List<string>> GetFilesAllDirectories(string directoryName)
        {
            Dictionary<string, List<string>> dict = new Dictionary<string, List<string>>();
            GetFilesAllDirectories(directoryName, dict);
            return dict;
        }

        private void GetFilesAllDirectories(string directoryName, IDictionary<string, List<string>> dictionary)
        {
            SetStatusText(string.Format(Language.MainForm.Status_GettingFileListForDirectory, directoryName));
            Application.DoEvents();

            // Files
            try
            {
                string[] files = Directory.GetFiles(directoryName, "*.*", SearchOption.TopDirectoryOnly);

                if (files.Length != 0)
                    dictionary.Add(directoryName, files.ToList());
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }

            // Directories
            try
            {
                string[] directories = Directory.GetDirectories(directoryName, "*.*", SearchOption.TopDirectoryOnly);
                foreach (string directory in directories)
                {
                    DirectoryInfo di = new DirectoryInfo(directory);
                    if ((di.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) // skip hidden directories
                        continue;
                    GetFilesAllDirectories(directory, dictionary);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }

        private void miAbout_Click(object sender, EventArgs e)
        {
            ShowPreferences(true);
        }

        private void miPreferences_Click(object sender, EventArgs e)
        {
            ShowPreferences(false);
        }

        private void ShowPreferences(bool showAbout)
        {
            Cursor.Current = Cursors.WaitCursor;
            using (PreferencesForm form = new PreferencesForm(showAbout))
            {
                Cursor.Current = Cursors.Default;
                form.ShowDialog();
            }

            Language.Load(Program.Settings.General.Language);

            if (!Program.Settings.General.IsRecentFilesSaved)
            {
                using (SqliteCommand cmd = new SqliteCommand("delete from RecentFile", Program.GetOpenSettingsConnection()))
                {
                    cmd.ExecuteNonQuery();
                }
            }

            SetAlwaysOnTop();
        }

        private void SetAlwaysOnTop()
        {
            if (Program.Settings.General.AlwaysOnTop)
                FormHelper.MakeTopMost(this);
            else
                FormHelper.MakeNormal(this);
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            SaveFormSizeAndPosition();

            ResizeStatusBar();

            progressBar1.Width = chkHideGood.Left - 16;
        }

        private void SaveFormSizeAndPosition()
        {
            if (IsHandleCreated)
            {
                if (WindowState != FormWindowState.Minimized)
                {
                    if (WindowState != FormWindowState.Maximized)
                    {
                        Program.Settings.General.WindowTop = Top;
                        Program.Settings.General.WindowLeft = Left;
                        Program.Settings.General.WindowHeight = Height;
                        Program.Settings.General.WindowWidth = Width;
                    }
                    Program.Settings.General.FormWindowState = WindowState;
                }
            }
        }

        private void ResizeStatusBar()
        {
            int statusWidth = statusStrip1.Width / 2 + 5;
            lblStatus.Width = statusWidth;

            int partCount = lblUnreadable.Visible ? 6 : 5;
            int partsWidth = (statusStrip1.Width - statusWidth) / partCount - 1;
            lblSets.Width = partsWidth;
            lblParts.Width = partsWidth;
            lblGood.Width = partsWidth;
            lblBad.Width = partsWidth;
            lblMissing.Width = partsWidth;
            lblUnreadable.Width = partsWidth;
        }

        private void SetStatusText(string text)
        {
            lblStatus.Text = text;

            if (text.Length >= 64)
                text = text.Substring(0, 60) + "...";
            notifyIcon1.Text = text;
        }

        private static List<string> GetChecksumFiles(bool multiSelect)
        {
            string[] fileNames;

            Cursor.Current = Cursors.WaitCursor;
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = ChecksumAlgorithm.DialogPattern + "|" + ChecksumAlgorithm.DialogPattern;
                ofd.Multiselect = multiSelect;
                Cursor.Current = Cursors.Default;
                DialogResult res = ofd.ShowDialog();
                if (res != DialogResult.OK)
                    return null;
                fileNames = ofd.FileNames;
            }

            return fileNames.ToList();
        }

        private static string FirstLine(string text)
        {
            int index = text.IndexOfAny(new[] { '\r', '\n' });
            return index < 0 ? text : text.Substring(0, index);
        }

        private void miVerifyFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Language.MainForm.VerifyFolder_Description;
                dialog.UseDescriptionForTitle = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                // a folder goes through the same path as a dropped folder: every checksum file in it and its subfolders
                EngageDropTimer(new List<string> { dialog.SelectedPath });
            }
        }

        private void miOpen_Click(object sender, EventArgs e)
        {
            List<string> fileNames = GetChecksumFiles(true);
            if (fileNames == null)
                return;

            EngageDropTimer(fileNames);
        }

        private CancellationTokenSource _loadCancel;

        /// <summary>
        /// Reads checksum files and folders on the thread pool and appends their sets to the list, so a big folder
        /// doesn't freeze the window. Skipped checksum files are listed at the end. Returns false if stopped.
        /// </summary>
        private async Task<bool> LoadSetsAsync(List<string> paths)
        {
            _loadCancel = new CancellationTokenSource();
            CancellationToken cancel = _loadCancel.Token;

            // the status bar is updated at most 10 times a second however many files there are
            long nextStatus = 0;
            Action<string> loading = path =>
            {
                long now = Environment.TickCount64;
                if (now < Interlocked.Read(ref nextStatus))
                    return;
                Interlocked.Exchange(ref nextStatus, now + 100);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (_loadCancel != null && !IsDisposed)
                            SetStatusText(string.Format(Language.MainForm.Status_LoadingFile, path));
                    }));
                }
                catch (InvalidOperationException)
                {
                    // the window is closing
                }
            };

            LoadResult loaded;
            try
            {
                SetStatusText(Language.MainForm.Status_GettingFileList);
                loaded = await Task.Run(() => ChecksumSetLoader.LoadAll(paths, loading, cancel));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _loadCancel.Dispose();
                _loadCancel = null;
            }

            if (IsDisposed)
                return false;

            ListViewBeginUpdate();
            try
            {
                foreach (ChecksumSet set in loaded.Sets)
                {
                    AppendSetRows(set);
                    _totalSizeOfSets += set.TotalSize;
                    _files_parts += set.Files.Count;
                    _sets.Add(set);
                }
                ShowRows();
            }
            finally
            {
                ListViewEndUpdate();
            }
            UpdateStatusBar();

            HashSet<string> skippedPaths = new HashSet<string>(loaded.Skipped.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (!skippedPaths.Contains(path) && (Directory.Exists(path) || ChecksumAlgorithm.IsChecksumFile(path)))
                    Program.Settings.AddRecentFile(path);
            }

            ShowSkipped(loaded.Skipped);
            return true;
        }

        /// <summary>
        /// Says which checksum files couldn't be loaded: one opened on its own gets its own message, anything more
        /// a list. A bad checksum file is the user's problem to fix, not a crash to report.
        /// </summary>
        private void ShowSkipped(List<SkippedChecksumFile> skipped)
        {
            foreach (SkippedChecksumFile file in skipped)
                Trace.WriteLine(file.Path + ": " + file.Error);

            if (skipped.Count == 0)
                return;

            if (skipped.Count == 1 && !skipped[0].InFolder)
            {
                SkippedChecksumFile file = skipped[0];
                InvalidChecksumFileException invalid = file.Error as InvalidChecksumFileException;
                if (invalid != null)
                    MessageBox.Show(this, string.Format(Language.MainForm.FileContentsNotRecognized, file.Path, invalid.ChecksumType, invalid.Message),
                        Language.MainForm.UnreadableChecksumFile_Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (file.Error is FileNotFoundException)
                    MessageBox.Show(this, string.Format(Language.MainForm.FileNotFound_Message, file.Path),
                        Language.MainForm.FileNotFound_Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    MessageBox.Show(this, string.Format(Language.MainForm.UnreadableChecksumFile_Message, file.Path, file.Error.Message),
                        Language.MainForm.UnreadableChecksumFile_Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string list = string.Join(Environment.NewLine, skipped.Take(10).Select(p => p.Path + Environment.NewLine + "    " +
                (p.Error is FileNotFoundException ? Language.MainForm.FileNotFound_Title : FirstLine(p.Error.Message))));
            if (skipped.Count > 10)
                list += Environment.NewLine + "...";
            MessageBox.Show(this, string.Format(Language.MainForm.SkippedChecksumFiles_Message, skipped.Count, list),
                Language.MainForm.SkippedChecksumFiles_Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Replaces the list with one checksum file, without checking it (for Find Duplicate Files).</summary>
        private bool LoadSingleFile(string fileName)
        {
            LoadResult loaded;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                loaded = ChecksumSetLoader.LoadAll(new[] { fileName }, null, CancellationToken.None);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }

            ShowSkipped(loaded.Skipped);
            if (loaded.Sets.Count != 1)
                return false;

            ChecksumSet set = loaded.Sets[0];
            Program.Settings.AddRecentFile(fileName);

            ClearFileItems();
            _sets.Clear();
            _sets.Add(set);
            _totalSizeOfSets = set.TotalSize;
            _files_parts = set.Files.Count;
            AddSetRows(set);
            return true;
        }

        private List<string> TakeNewSets()
        {
            lock (_newSets)
            {
                List<string> newSets = new List<string>(_newSets);
                _newSets.Clear();
                return newSets;
            }
        }

        private void btnRightPane_Click(object sender, EventArgs e)
        {
            Program.Settings.General.ShowCommentsPane = !Program.Settings.General.ShowCommentsPane;
            ToggleCommentsPane();
        }

        private void miCommentResultPane_Click(object sender, EventArgs e)
        {
            Program.Settings.General.ShowCommentsPane = !Program.Settings.General.ShowCommentsPane;
            ToggleCommentsPane();
        }

        private void ToggleCommentsPane()
        {
            bool show = Program.Settings.General.ShowCommentsPane;

            splitContainer1.Panel2Collapsed = !show;

            // Buttons and Menu Items
            btnRightPane.Text = show ? "<<" : ">>";
            miCommentResultPane.Checked = show;

            progressBar1.Width = chkHideGood.Left - 16;
        }

        private void miHideGood_Click(object sender, EventArgs e)
        {
            chkHideGood.Checked = !miHideGood.Checked;
        }

        private void chkHideGood_CheckedChanged(object sender, EventArgs e)
        {
            miHideGood.Checked = chkHideGood.Checked;
            Program.Settings.General.HideGoodFiles = miHideGood.Checked;

            if (_workingOnList)
                _queueHideGood = true;
            else
                ApplyHideGood();
        }

        private void ApplyHideGood()
        {
            _queueHideGood = false;

            if (_sets.Count == 0)
                return;

            RebuildRows();

            lvwFiles.Focus();
        }

        private void btnGo_Click(object sender, EventArgs e)
        {
            if (!_workingOnList)
            {
                foreach (ChecksumSet set in _sets)
                {
                    foreach (ChecksumFile file in set.Files)
                        file.State = ChecksumFileState.NotProcessed;
                }

                ApplyHideGood();

                Verify();
            }
            else
            {
                StopWork();
            }
        }

        private int _files_parts;
        private int _files_ok;
        private int _files_missing;
        private int _files_unreadable;
        private int _files_bad;
        private int _set_index;

        private CancellationTokenSource _verifyCancel;
        private readonly PauseGate _pauseGate = new PauseGate();

        private async void Verify()
        {
            await VerifyAsync();
        }

        /// <summary>
        /// Checks every queued set. Files are hashed on the thread pool (several at once on SSDs) while
        /// this method, on the UI thread, starts work in order and applies results as they finish.
        /// </summary>
        private async Task VerifyAsync()
        {
            if (_sets.Count == 0)
                return;

            DateTime start = DateTime.Now;
            _workingOnList = true;
            _verifyCancel = new CancellationTokenSource();
            _pauseGate.Resume();
            VerifyContext context = new VerifyContext(_verifyCancel.Token, _pauseGate);
            VerifyOptions options = VerifyOptions.FromSettings(Program.Settings);

            _currentRunFile = null;
            Timer progressTimer = CreateProgressTimer(context, start, () => _currentRunFile);

            try
            {
                SetStatusText(Language.MainForm.Status_Working);
                btnPause.Enabled = true;
                btnHide.Enabled = true;

                ToggleMenuItemsEnabled(false, false);

                btnGo.Text = Language.MainForm.StopButton;
                _queueStop = false;
                _pause = false;

                progressBar1.Value = 0;
                progressBar2.Value = 0;

                _files_ok = 0;
                _files_bad = 0;
                _files_missing = 0;
                _files_unreadable = 0;

                progressTimer.Start();

                for (_set_index = 0; _set_index < _sets.Count; _set_index++)
                {
                    if (_queueStop)
                        break;

                    // the progress timer redraws it; status bar layout per set was most of the cost of small sets
                    _statusBarDirty = true;

                    ChecksumSet set = _sets[_set_index];
                    if (txtComments.Text != set.Comments)
                        txtComments.Text = set.Comments;

                    lvwFiles.Focus();

                    IReadOnlyDictionary<string, ChecksumFile> cache = await Task.Run(() => FileVerifier.LoadCache(set, options));

                    await ProcessFilesAsync(set, DiskInfo.GetParallelism(set.Directory), context.Cancel,
                        file => FileVerifier.Verify(set, file, options, cache, context),
                        ApplyFileResult);

                    if (_queueStop)
                        break;

                    progressBar1.Value = 100;

                    // only this set's missing files: _files_missing is a run total
                    if (Program.Settings.Check.AutoFindRenames && set.Files.Any(p => p.State == ChecksumFileState.Missing))
                        await FindRenamedFilesAsync(set, context);

                    if (_queueHideGood)
                        ApplyHideGood();

                    await LoadNewSetsAsync();
                }

                progressTimer.Stop();
                UpdateProgressText(context.BytesProcessed, start);

                SetStatusText(Language.MainForm.Status_UpdatingCache);
                TimeSpan timeSpent = DateTime.Now - start;
                Program.Settings.Statistics.AddStats(_files_ok + _files_bad + _files_missing + _files_unreadable, _set_index, context.BytesChecked / 1024 / 1024, _files_ok, timeSpent);
                await Task.Run(() => Cache.Clean());

                if (Program.Settings.Check.AutoCloseWhenDoneChecking)
                {
                    if (_files_parts == _files_ok || !Program.Settings.Check.AutoCloseWhenDoneCheckingOnlyIfAllOK)
                    {
                        Close();
                    }
                }

                if (Program.Settings.General.FlashWindowWhenDone)
                    FlashWindow(this);
            }
            finally
            {
                progressTimer.Stop();
                progressTimer.Dispose();
                if (!IsDisposed)
                    FlushStatusBar();

                long bytesProcessed = context.BytesChecked;
                TimeSpan totalTime = DateTime.Now - start;
                decimal totalMB = bytesProcessed / 1024.0m / 1024.0m;
                int percentGood = _files_parts == 0 ? 0 : (_files_ok * 100) / _files_parts;
                decimal mbPerSecond = totalTime.TotalSeconds > 0 ? totalMB / (decimal)totalTime.TotalSeconds : totalMB;
                string statusText;
                if (totalTime.TotalSeconds < 600)
                    statusText = string.Format(Language.MainForm.Status_FinishedUnder10Minutes, percentGood, totalMB, totalTime.TotalSeconds, mbPerSecond);
                else
                    statusText = string.Format(Language.MainForm.Status_Finished10MinutesOrMore, percentGood, totalMB, totalTime.Hours, totalTime.Minutes, totalTime.Seconds, mbPerSecond);

                if (!IsDisposed)
                {
                    ToolTipIcon icon = percentGood == 100 ? ToolTipIcon.Info : ToolTipIcon.Warning;
                    notifyIcon1.ShowBalloonTip(3000, "TaoSFV", string.Format(Language.MainForm.SystemTray_DoneVerifying, percentGood), icon);

                    if (_pause)
                        Unpause();
                    btnPause.Enabled = false;
                    btnHide.Enabled = false;
                    SetStatusText(statusText);
                    PlayCompleteSound(_files_parts == _files_ok);

                    ToggleMenuItemsEnabled(true, (_files_missing != 0));

                    btnGo.Text = Language.MainForm.GoButton;
                }

                _workingOnList = false;
                _queueStop = false;
                _verifyCancel.Dispose();
                _verifyCancel = null;
            }
        }

        // the file most recently started, for the progress timer to select (written by the producer thread)
        private volatile ChecksumFile _currentRunFile;

        /// <summary>
        /// Runs `work` for each file of a set on the thread pool, up to `parallelism` at a time, started in list order.
        /// A background producer starts files and queues results; this method (on the UI thread) applies them in
        /// batches 20 times a second, so the UI does a fixed amount of work however many files there are.
        /// A file that can't be read becomes Unreadable; any other error stops the run and is rethrown.
        /// </summary>
        private async Task ProcessFilesAsync(ChecksumSet set, int parallelism, CancellationToken cancel, Action<ChecksumFile> work, Action<ChecksumFile> onDone)
        {
            ConcurrentQueue<KeyValuePair<ChecksumFile, Exception>> finished = new ConcurrentQueue<KeyValuePair<ChecksumFile, Exception>>();
            List<ChecksumFile> files = new List<ChecksumFile>(set.Files);

            Task producer = Task.Run(async () =>
            {
                List<Task> running = new List<Task>();
                using (SemaphoreSlim slots = new SemaphoreSlim(parallelism))
                {
                    foreach (ChecksumFile file in files)
                    {
                        try
                        {
                            _pauseGate.WaitWhilePaused(cancel); // don't start files while paused
                            await slots.WaitAsync(cancel);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        _currentRunFile = file;
                        running.Add(Task.Run(() =>
                        {
                            Exception error = null;
                            try
                            {
                                work(file);
                            }
                            catch (Exception ex)
                            {
                                error = ex;
                            }
                            finally
                            {
                                slots.Release();
                            }
                            finished.Enqueue(new KeyValuePair<ChecksumFile, Exception>(file, error));
                        }));
                    }

                    await Task.WhenAll(running);
                }
            });

            Exception fatal = null;
            while (true)
            {
                bool producerDone = producer.IsCompleted;

                KeyValuePair<ChecksumFile, Exception> result;
                while (finished.TryDequeue(out result))
                {
                    ChecksumFile file = result.Key;
                    Exception error = result.Value;

                    if (error is OperationCanceledException)
                        continue; // stopped before it finished; stays unprocessed

                    if (error is IOException || error is UnauthorizedAccessException)
                    {
                        file.State = ChecksumFileState.Unreadable;
                        file.ErrorMessage = error.Message;
                    }
                    else if (error != null)
                    {
                        if (fatal == null)
                            fatal = error;
                        StopWork();
                        continue;
                    }

                    onDone(file);
                }

                if (producerDone)
                    break;

                if (_queueHideGood)
                    ApplyHideGood();

                // wakes early when the set is done: a fixed 50 ms here cost every set that much, even with nothing to read
                await Task.WhenAny(producer, Task.Delay(50));
            }

            await producer;
            if (fatal != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fatal).Throw();
        }

        /// <summary>Counts a finished file and shows its state.</summary>
        private void ApplyFileResult(ChecksumFile file)
        {
            if (file.State == ChecksumFileState.OK)
                _files_ok++;
            else if (file.State == ChecksumFileState.Bad)
                _files_bad++;
            else if (file.State == ChecksumFileState.Missing)
                _files_missing++;
            else if (file.State == ChecksumFileState.Unreadable)
                _files_unreadable++;

            // the progress timer redraws the status bar; doing it per file made runs over many small files UI-bound
            _statusBarDirty = true;

            if (Program.Settings.General.HideGoodFiles && file.State == ChecksumFileState.OK)
                _rowsDirty = true; // the progress timer rebuilds the rows
            else
                RedrawFile(file);
        }

        private bool _statusBarDirty;

        /// <summary>
        /// Ten times a second while files are being read: progress bars, ETA, status bar counts, and selecting the
        /// file being read. Per-file UI updates are batched here so many small files don't make the run UI-bound.
        /// </summary>
        private Timer CreateProgressTimer(VerifyContext context, DateTime start, Func<ChecksumFile> currentFile)
        {
            ChecksumFile shownFile = null;
            Timer timer = new Timer { Interval = 100 };
            timer.Tick += (sender, e) =>
            {
                FlushStatusBar();

                if (_pause)
                    return;

                UpdateProgressText(context.BytesProcessed, start);

                ChecksumFile file = currentFile();
                if (file != null && file != shownFile)
                {
                    shownFile = file;
                    SelectFile(file);
                }

                double? fileProgress = file == null ? null : context.GetFileProgress(file);
                progressBar1.Value = fileProgress == null ? 0 : (int)(fileProgress.Value * 100);
            };
            return timer;
        }

        private void FlushStatusBar()
        {
            if (_rowsDirty)
            {
                _rowsDirty = false;
                RebuildRows();
            }

            if (!_statusBarDirty)
                return;
            _statusBarDirty = false;
            UpdateStatusBar();
        }

        // The file list is a virtual ListView over _rows. Inserting thousands of real ListViewItems took seconds (far
        // longer when accessibility / UI Automation clients are listening, as on many machines); a virtual list only
        // builds the rows on screen. Virtual lists can't have groups, so each set gets a header row instead.
        private sealed class FileRow
        {
            public ChecksumSet Set;
            public ChecksumFile File; // null for the set's header row
        }

        private readonly List<FileRow> _rows = new List<FileRow>();
        private readonly Dictionary<ChecksumFile, int> _rowIndex = new Dictionary<ChecksumFile, int>();
        private bool _rowsDirty;
        private int _fileNameColumnWidth;
        private Font _headerFont;

        /// <summary>Appends a header row and the set's file rows (good files left out when Hide Good is on).</summary>
        private void AddSetRows(ChecksumSet set)
        {
            AppendSetRows(set);
            ShowRows();
        }

        private void AppendSetRows(ChecksumSet set)
        {
            _rows.Add(new FileRow { Set = set });

            bool hideGood = Program.Settings.General.HideGoodFiles;
            foreach (ChecksumFile file in set.Files)
            {
                if (hideGood && file.State == ChecksumFileState.OK)
                    continue;

                _rowIndex[file] = _rows.Count;
                _rows.Add(new FileRow { Set = set, File = file });
            }
        }

        /// <summary>Rebuilds the rows for every set shown or queued (after Hide Good changes).</summary>
        private void RebuildRows()
        {
            // sets already shown, in order, plus any queued sets not shown yet
            List<ChecksumSet> sets = new List<ChecksumSet>();
            HashSet<ChecksumSet> shown = new HashSet<ChecksumSet>();
            foreach (FileRow row in _rows)
            {
                if (row.File == null && shown.Add(row.Set))
                    sets.Add(row.Set);
            }
            foreach (ChecksumSet set in _sets)
            {
                if (shown.Add(set))
                    sets.Add(set);
            }

            _rows.Clear();
            _rowIndex.Clear();
            _measuredRows = 0;
            foreach (ChecksumSet set in sets)
                AppendSetRows(set);
            ShowRows();
        }

        private void ClearFileItems()
        {
            _rows.Clear();
            _rowIndex.Clear();
            _measuredRows = 0;
            _fileNameColumnWidth = 0;
            ShowRows();
        }

        private void ShowRows()
        {
            MeasureNewRows();
            lvwFiles.VirtualListSize = _rows.Count;
            lvwFiles.Invalidate();
            if (_rows.Count != 0 && lvwFiles.Columns[0].Width != _fileNameColumnWidth)
                lvwFiles.Columns[0].Width = _fileNameColumnWidth;
        }

        // rows before this have been measured for the file name column's width
        private int _measuredRows;

        // the most names of each kind measured per batch
        private const int MeasuredNames = 50;

        /// <summary>
        /// Widens the file name column for rows added since the last call. Measuring every name took seconds for
        /// 80,000 rows, so only the longest few are measured: in practice the widest name is among them.
        /// </summary>
        private void MeasureNewRows()
        {
            if (_measuredRows >= _rows.Count)
                return;

            List<string> headers = new List<string>();
            List<string> names = new List<string>();
            for (int i = _measuredRows; i < _rows.Count; i++)
            {
                FileRow row = _rows[i];
                if (row.File == null)
                    headers.Add(Path.GetFileName(row.Set.VerificationFileName));
                else
                    names.Add(row.File.FileName);
            }
            _measuredRows = _rows.Count;

            if (_headerFont == null)
                _headerFont = new Font(lvwFiles.Font, FontStyle.Bold);

            using (Graphics graphics = lvwFiles.CreateGraphics())
            {
                foreach (string header in headers.OrderByDescending(p => p.Length).Take(MeasuredNames))
                    MeasureFileName(graphics, header, _headerFont);
                foreach (string name in names.OrderByDescending(p => p.Length).Take(MeasuredNames))
                    MeasureFileName(graphics, name, lvwFiles.Font);
            }
        }

        private void MeasureFileName(Graphics graphics, string text, Font font)
        {
            // room for the state icon and some padding
            int width = TextRenderer.MeasureText(graphics, text, font).Width + 32;
            if (width > _fileNameColumnWidth)
                _fileNameColumnWidth = width;
        }

        private int GetFileRow(ChecksumFile file)
        {
            int index;
            return _rowIndex.TryGetValue(file, out index) ? index : -1;
        }

        private void RedrawFile(ChecksumFile file)
        {
            int index = GetFileRow(file);
            if (index >= 0 && index < lvwFiles.VirtualListSize)
                lvwFiles.RedrawItems(index, index, true);
        }

        private void SelectFile(ChecksumFile file)
        {
            int index = GetFileRow(file);
            if (index < 0 || index >= lvwFiles.VirtualListSize)
                return;

            lvwFiles.SelectedIndices.Clear();
            lvwFiles.SelectedIndices.Add(index);
            if (Program.Settings.General.AutoScrollFileList)
                lvwFiles.EnsureVisible(index);
        }

        /// <summary>Selected files (header rows aren't files).</summary>
        private List<ChecksumFile> GetSelectedFiles()
        {
            List<ChecksumFile> files = new List<ChecksumFile>();
            foreach (int index in lvwFiles.SelectedIndices)
            {
                if (index < _rows.Count && _rows[index].File != null)
                    files.Add(_rows[index].File);
            }
            return files;
        }

        private void lvwFiles_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            FileRow row = e.ItemIndex < _rows.Count ? _rows[e.ItemIndex] : null;
            if (row == null)
            {
                e.Item = new ListViewItem(new[] { string.Empty, string.Empty });
                return;
            }

            if (row.File == null)
            {
                ListViewItem header = new ListViewItem(new[] { Path.GetFileName(row.Set.VerificationFileName), string.Empty }, -1);
                header.ForeColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x99, 0xC8, 0xFF) : SystemColors.HotTrack;
                header.Font = _headerFont;
                e.Item = header;
                return;
            }

            ListViewItem item = new ListViewItem(new[] { row.File.FileName, string.Empty }, (int)row.File.State);
            item.ToolTipText = row.File.ErrorMessage ?? string.Empty;
            item.Tag = row.File;
            e.Item = item;
        }

        private void StopWork()
        {
            _queueStop = true;
            if (_verifyCancel != null)
                _verifyCancel.Cancel();
            if (_loadCancel != null)
                _loadCancel.Cancel();
        }

        private void UpdateProgressBarsAndText(long bytesProcessed, DateTime start)
        {
            UpdateProgressText(bytesProcessed, start);
            Application.DoEvents();
        }

        private void UpdateProgressText(long bytesProcessed, DateTime start)
        {
            int filesLeft = _files_parts - _files_ok - _files_bad - _files_missing - _files_unreadable;
            decimal totalPercent = _totalSizeOfSets == 0 ? 100.0m : (bytesProcessed * 100.0m) / _totalSizeOfSets;
            if (totalPercent > 100)
                totalPercent = 100;
            progressBar2.Value = (int)totalPercent;

            TimeSpan elapsed = DateTime.Now - start;
            string strElapsed = string.Format("{0:0}:{1:00}:{2:00}", elapsed.Hours, elapsed.Minutes, elapsed.Seconds);
            TimeSpan eta = (double)totalPercent <= double.Epsilon ? TimeSpan.Zero : TimeSpan.FromSeconds(elapsed.TotalSeconds * 100.0 / (double)totalPercent - elapsed.TotalSeconds + 1.0);
            if (eta < TimeSpan.Zero)
                eta = TimeSpan.Zero;
            else
                eta += TimeSpan.FromSeconds(0.1 * filesLeft);
            string strETA = string.Format("{0:0}:{1:00}:{2:00}", eta.Hours, eta.Minutes, eta.Seconds);

            string statusText = string.Format(Language.MainForm.Status_ETA, (int)totalPercent, strETA, strElapsed);
            SetStatusText(statusText);
        }

        private void ToggleMenuItemsEnabled(bool enabled, bool findRenamedEnabled)
        {
            miNewSFV.Enabled = enabled;
            miNewMD5.Enabled = enabled;
            miNewSHA1.Enabled = enabled;
            miNewSHA256.Enabled = enabled;
            miNewSHA512.Enabled = enabled;
            miPreferences.Enabled = enabled;
            miFindRenamedFiles.Enabled = findRenamedEnabled;
            miUseCachedResults.Enabled = enabled;
            miFindDuplicateFiles.Enabled = enabled;
            miAbout.Enabled = enabled;
            lvwFiles.MultiSelect = enabled;
        }

        private static Bitmap MakeUnreadableIcon(Image source)
        {
            // R' = 0.3R + 0.9B, G' = 0.5G, B' = B turns the blue bullet purple
            ColorMatrix purple = new ColorMatrix(new[]
            {
                new float[] { 0.3f, 0, 0, 0, 0 },
                new float[] { 0, 0.5f, 0, 0, 0 },
                new float[] { 0.9f, 0, 1, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });

            Bitmap result = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(result))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(purple);
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }

        private void UpdateStatusBar()
        {
            int currentSet = _set_index + 1;
            if (currentSet > _sets.Count)
                currentSet = _sets.Count;

            lblSets.Text = string.Format(Language.MainForm.SetsLabel + " {0:#,0}/{1:#,0}", currentSet, _sets.Count);
            lblParts.Text = string.Format(Language.MainForm.PartsLabel + " {0:#,0}/{1:#,0}", _files_ok + _files_missing + _files_bad + _files_unreadable, _files_parts);
            lblGood.Text = string.Format(Language.MainForm.GoodLabel + " {0:#,0}", _files_ok);
            lblMissing.Text = string.Format(Language.MainForm.MissingLabel + " {0:#,0}", _files_missing);
            lblBad.Text = string.Format(Language.MainForm.BadLabel + " {0:#,0}", _files_bad);
            lblUnreadable.Text = string.Format(Language.MainForm.UnreadableLabel + " {0:#,0}", _files_unreadable);
            if (lblUnreadable.Visible != (_files_unreadable != 0))
            {
                lblUnreadable.Visible = _files_unreadable != 0;
                ResizeStatusBar();
            }
        }

        private void miUseCachedResults_Click(object sender, EventArgs e)
        {
            Program.Settings.General.UseCachedResults = !Program.Settings.General.UseCachedResults;
            miUseCachedResults.Checked = Program.Settings.General.UseCachedResults;
        }

        private async void miFindRenamedFiles_Click(object sender, EventArgs e)
        {
            try
            {
                miFindRenamedFiles.Enabled = false;

                VerifyContext context = new VerifyContext(CancellationToken.None);
                int totalNewOK = 0;
                foreach (ChecksumSet set in _sets.ToList())
                {
                    int newOK = await FindRenamedFilesAsync(set, context);
                    totalNewOK += newOK;

                    if (newOK != 0)
                    {
                        Program.Settings.Statistics.AddStats(0, 0, 0, newOK, TimeSpan.Zero);
                    }
                }

                SetStatusText(Language.MainForm.Status_Ready);

                MessageBox.Show(string.Format(Language.MainForm.FindRenamedFiles_Message, totalNewOK), Language.MainForm.FindRenamedFiles_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                miFindRenamedFiles.Enabled = (_files_missing != 0);
            }
        }

        /// <summary>Looks for the set's missing files under other names (hashing in the background); returns how many were found.</summary>
        private async Task<int> FindRenamedFilesAsync(ChecksumSet set, VerifyContext context)
        {
            Guard.ArgumentNotNull(set, "set");

            // during a run the progress timer owns the status bar
            bool duringRun = _verifyCancel != null;
            if (!duringRun)
                SetStatusText(string.Format(Language.MainForm.Status_FindingRenamesInFile, set.VerificationFileName));

            bool lowercase = Program.Settings.Check.Renaming == CheckRenaming.Lowercase;
            List<ChecksumFile> found;
            try
            {
                found = await Task.Run(() => RenamedFileFinder.Find(set, lowercase, context));
            }
            catch (OperationCanceledException)
            {
                return 0;
            }

            foreach (ChecksumFile file in found)
            {
                _files_ok++;
                _files_missing--;

                RedrawFile(file);
            }

            if (duringRun)
            {
                _statusBarDirty = true;
                if (found.Count != 0 && Program.Settings.General.HideGoodFiles)
                    _rowsDirty = true;
            }
            else
            {
                UpdateStatusBar();
                if (found.Count != 0)
                    ApplyHideGood();
            }

            return found.Count;
        }

        private bool GetCreateSet(string ext, List<string> files)
        {
            string path;
            if (files == null)
            {
                if (folderBrowserDialog1.ShowDialog() != DialogResult.OK)
                    return false;

                path = folderBrowserDialog1.SelectedPath;
            }
            else
            {
                path = Path.GetDirectoryName(files[0]);
            }

            string fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName))
                fileName = path[0].ToString();
            fileName = string.Format("{0}.{1}", fileName, ext);

            bool checkExists = !Program.Settings.Create.CreateForEachSubDir || !Program.Settings.General.Recursive;

            if (Program.Settings.Create.PromptForFileName)
            {
                saveFileDialog1.DefaultExt = ext;
                saveFileDialog1.FileName = fileName;
                saveFileDialog1.Filter = string.Format("*.{0}|*.{0}", ext);
                saveFileDialog1.InitialDirectory = path;
                saveFileDialog1.OverwritePrompt = checkExists;

                if (saveFileDialog1.ShowDialog() != DialogResult.OK)
                    return false;

                fileName = saveFileDialog1.FileName;
            }
            else
            {
                fileName = Path.Combine(path, fileName);

                if (checkExists && File.Exists(fileName))
                {
                    string message = string.Format(Language.MainForm.OverwriteFile_Message, fileName);
                    if (MessageBox.Show(message, Language.MainForm.OverwriteFile_Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return false;
                }
            }

            ChecksumAlgorithm algorithm = ChecksumAlgorithm.FromExtension(ext);
            if (algorithm == null)
                return false;
            ChecksumType setType = algorithm.Type;

            int overwriteCount;
            GetWorkset(fileName, path, setType, files, out overwriteCount);

            if (!checkExists && overwriteCount != 0)
            {
                string message = string.Format(Language.MainForm.OverwriteMultipleFiles_Message, overwriteCount);
                if (MessageBox.Show(message, Language.MainForm.OverwriteMultipleFiles_Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    SetStatusText(Language.MainForm.Status_Ready);
                    _sets.Clear();
                    return false;
                }
            }

            return true;
        }

        private void miNewSFV_Click(object sender, EventArgs e)
        {
            if (!GetCreateSet("sfv", sender as List<string>))
                return;

            CreateSet();
        }

        private void miNewMD5_Click(object sender, EventArgs e)
        {
            if (!GetCreateSet("md5", sender as List<string>))
                return;

            CreateSet();
        }

        private void miNewSHA1_Click(object sender, EventArgs e)
        {
            if (!GetCreateSet("sha1", sender as List<string>))
                return;

            CreateSet();
        }

        private void miNewSHA256_Click(object sender, EventArgs e)
        {
            if (!GetCreateSet("sha256", sender as List<string>))
                return;

            CreateSet();
        }

        private void miNewSHA512_Click(object sender, EventArgs e)
        {
            if (!GetCreateSet("sha512", sender as List<string>))
                return;

            CreateSet();
        }

        private async void CreateSet()
        {
            await CreateSetAsync();
        }

        /// <summary>
        /// Hashes the files of every queued set (on the thread pool, several at once on SSDs) and writes each set's
        /// checksum file when all of its files were read.
        /// </summary>
        private async Task CreateSetAsync()
        {
            DateTime start = DateTime.Now;
            _workingOnList = true;
            _verifyCancel = new CancellationTokenSource();
            _pauseGate.Resume();
            VerifyContext context = new VerifyContext(_verifyCancel.Token, _pauseGate);

            _currentRunFile = null;
            Timer progressTimer = CreateProgressTimer(context, start, () => _currentRunFile);

            try
            {
                SetStatusText(Language.MainForm.Status_Working);
                btnPause.Enabled = true;
                btnHide.Enabled = true;

                ToggleMenuItemsEnabled(false, false);

                btnGo.Text = Language.MainForm.StopButton;
                _queueStop = false;
                _pause = false;

                progressBar1.Value = 0;
                progressBar2.Value = 0;

                _files_parts = 0;
                _files_ok = 0;
                _files_bad = 0;
                _files_missing = 0;
                _files_unreadable = 0;
                _totalSizeOfSets = 0;
                foreach (ChecksumSet set in _sets)
                {
                    _files_parts += set.Files.Count;
                    _totalSizeOfSets += set.TotalSize;
                }
                UpdateStatusBar();

                ClearFileItems();
                foreach (ChecksumSet set in _sets)
                    AppendSetRows(set);
                ShowRows();

                txtComments.Text = string.Empty;

                lvwFiles.Focus();
                Application.DoEvents();

                progressTimer.Start();

                for (_set_index = 0; _set_index < _sets.Count; _set_index++)
                {
                    ChecksumSet set = _sets[_set_index];
                    Program.Settings.AddRecentFile(set.VerificationFileName);
                    ChecksumAlgorithm setAlgorithm = ChecksumAlgorithm.Get(set.Type);

                    int setOKCount = 0;
                    await ProcessFilesAsync(set, DiskInfo.GetParallelism(set.Directory), context.Cancel,
                        file =>
                        {
                            if (!File.Exists(file.FileInfo.FullName))
                            {
                                file.State = ChecksumFileState.Missing;
                                return;
                            }

                            file.CurrentChecksum = FileVerifier.HashFile(setAlgorithm, file, context);
                            file.OriginalChecksum = file.CurrentChecksum;
                            file.State = ChecksumFileState.OK;
                        },
                        file =>
                        {
                            if (file.State == ChecksumFileState.OK)
                                setOKCount++;
                            else if (file.State == ChecksumFileState.Missing)
                                StopWork(); // as before: a file disappeared while creating, so stop

                            ApplyFileResult(file);
                        });

                    progressBar1.Value = 100;
                    progressBar2.Value = 100;

                    if (_queueHideGood)
                        ApplyHideGood();

                    // a set with a missing or unreadable file isn't written: its checksum file would be incomplete
                    if (set.Files.Count == setOKCount)
                    {
                        StringBuilder shabang = new StringBuilder();
                        ChecksumAlgorithm algorithm = ChecksumAlgorithm.Get(set.Type);
                        bool md5Sum = Program.Settings.Create.MD5SumCompatibility;
                        string newLine = ChecksumFileFormat.GetNewLine(algorithm, md5Sum);

                        if (set.Type == ChecksumType.SFV && Program.Settings.Create.SFV32Compatibility)
                        {
                            shabang.Append("; Generated by WIN-SFV32 v1 [added for sfv32 compatibility]").Append(newLine);
                        }

                        if (Program.Settings.Comments.WriteComments)
                        {
                            string[] header = Program.Settings.Comments.Header.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                            string[] content = Program.Settings.Comments.Content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                            string[] footer = Program.Settings.Comments.Footer.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                            DateTime createDateTime = DateTime.Now;
                            foreach (string line in header)
                            {
                                shabang.Append(ChecksumFileFormat.FormatComment(algorithm, md5Sum, string.Format(line, createDateTime))).Append(newLine);
                            }

                            foreach (ChecksumFile file in set.Files)
                            {
                                foreach (string line in content)
                                {
                                    shabang.Append(ChecksumFileFormat.FormatComment(algorithm, md5Sum, string.Format(line, file.FileInfo.Length, GetLastWriteTime(file.FileInfo), file.FileName))).Append(newLine);
                                }
                            }

                            foreach (string line in footer)
                            {
                                shabang.Append(ChecksumFileFormat.FormatComment(algorithm, md5Sum, string.Format(line, createDateTime))).Append(newLine);
                            }
                        }

                        foreach (ChecksumFile file in set.Files)
                        {
                            shabang.Append(ChecksumFileFormat.FormatEntry(algorithm, md5Sum, file.FileName, file.CurrentChecksum)).Append(newLine);
                        }

                        string strShaBang = shabang.ToString();

                        ChecksumFileFormat.WriteAllText(set.VerificationFileName, strShaBang);

                        // md5sum-style files use LF; the text box needs CRLF to show line breaks
                        txtComments.Text = strShaBang.Replace("\r\n", "\n").Replace("\n", "\r\n");
                        set.Comments = strShaBang;
                    }

                    if (_queueStop)
                        break;
                }

                progressTimer.Stop();

                TimeSpan timeSpent = DateTime.Now - start;
                Program.Settings.Statistics.AddStats(_files_parts, _set_index, context.BytesChecked / 1024 / 1024, _files_ok, timeSpent);
                await Task.Run(() => Cache.Clean());

                if (Program.Settings.Create.AutoCloseWhenDoneCreating)
                {
                    Close();
                }

                if (Program.Settings.General.FlashWindowWhenDone)
                    FlashWindow(this);
            }
            finally
            {
                progressTimer.Stop();
                progressTimer.Dispose();
                if (!IsDisposed)
                    FlushStatusBar();

                long bytesProcessed = context.BytesChecked;
                TimeSpan totalTime = DateTime.Now - start;
                decimal totalMB = bytesProcessed / 1024.0m / 1024.0m;
                int percentGood = _files_parts == 0 ? 0 : (_files_ok * 100) / _files_parts;
                decimal mbPerSecond = totalTime.TotalSeconds > 0 ? totalMB / (decimal)totalTime.TotalSeconds : totalMB;

                string statusText;
                if (totalTime.TotalSeconds < 600)
                    statusText = string.Format(Language.MainForm.Status_FinishedUnder10Minutes, percentGood, totalMB, totalTime.TotalSeconds, mbPerSecond);
                else
                    statusText = string.Format(Language.MainForm.Status_Finished10MinutesOrMore, percentGood, totalMB, totalTime.Hours, totalTime.Minutes, totalTime.Seconds, mbPerSecond);

                if (!IsDisposed)
                {
                    notifyIcon1.ShowBalloonTip(3000, "TaoSFV", Language.MainForm.SystemTray_DoneCreating, ToolTipIcon.Info);

                    if (_pause)
                        Unpause();
                    btnPause.Enabled = false;
                    btnHide.Enabled = false;
                    SetStatusText(statusText);

                    ToggleMenuItemsEnabled(true, false);

                    btnGo.Text = Language.MainForm.GoButton;
                }

                _workingOnList = false;
                _queueStop = false;
                _verifyCancel.Dispose();
                _verifyCancel = null;
            }
        }

        private void btnHide_Click(object sender, EventArgs e)
        {
            notifyIcon1.Visible = true;
            Visible = false;
            if (Program.Settings.General.UseLowPriorityOnHide)
            {
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            UnhideFromSystray();
        }

        private void UnhideFromSystray()
        {
            Visible = true;
            notifyIcon1.Visible = false;
            Thread.CurrentThread.Priority = ThreadPriority.Normal;
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
            Activate();
            if (WindowState == FormWindowState.Minimized)
                WindowState = Program.Settings.General.FormWindowState;
        }

        private bool _pause;
        private void btnPause_Click(object sender, EventArgs e)
        {
            if (_pause)
                Unpause();
            else
                Pause();
        }

        private void Pause()
        {
            btnPause.Text = Language.MainForm.ResumeButton;
            _pause = true;
            _pauseGate.Pause();
            SetStatusText(Language.MainForm.Status_Paused);
            btnHide.Enabled = false;
        }

        private void Unpause()
        {
            btnPause.Text = Language.MainForm.PauseButton;
            _pause = false;
            _pauseGate.Resume();
            btnHide.Enabled = true;
        }

        private void miExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            StopWork();
        }

        private void MainForm_Move(object sender, EventArgs e)
        {
            SaveFormSizeAndPosition();
        }

        private void lvwFiles_DragOver(object sender, DragEventArgs e)
        {
            List<FileDrop> list = FileDrop.GetList(e.Data, ChecksumAlgorithm.DottedExtensions, true);
            if (list.Count > 0)
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private readonly Timer _dropTimer = new Timer();
        private void lvwFiles_DragDrop(object sender, DragEventArgs e)
        {
            List<FileDrop> list = FileDrop.GetList(e.Data, ChecksumAlgorithm.DottedExtensions, true);
            if (list.Count > 0)
            {
                EngageDropTimer(list.Select(p => p.Path).ToList());
            }
        }

        private void EngageDropTimer(List<string> files)
        {
            _dropTimer.Tick -= _dropTimer_Tick;
            _dropTimer.Tick += _dropTimer_Tick;
            _dropTimer.Interval = 1;
            _dropTimer.Tag = files;
            _dropTimer.Enabled = true;
        }

        private readonly Timer _createTimer = new Timer();
        private void EngageCreateTimer(List<string> files)
        {
            _createTimer.Tick -= _createTimer_Tick;
            _createTimer.Tick += _createTimer_Tick;
            _createTimer.Interval = 1;
            _createTimer.Tag = files;
            _createTimer.Enabled = true;
        }

        private void _createTimer_Tick(object sender, EventArgs e)
        {
            _createTimer.Enabled = false;

            List<string> files = (List<string>)_createTimer.Tag;

            if (!GetCreateSet(ChecksumAlgorithm.Get(_initialCreateChecksumType).Extension, files))
                return;

            CreateSet();
        }

        private async void _dropTimer_Tick(object sender, EventArgs e)
        {
            _dropTimer.Enabled = false;

            List<string> files = (List<string>)_dropTimer.Tag;
            if (files.Count == 0)
                return;

            if (_workingOnList)
            {
                // a run in progress picks these up after its current set; a load in progress, when it finishes
                lock (_newSets)
                    _newSets.AddRange(files);
                return;
            }

            _workingOnList = true;
            try
            {
                ClearFileItems();
                _totalSizeOfSets = 0;
                _files_parts = 0;
                _sets.Clear();

                progressBar1.Value = 0;
                progressBar2.Value = 0;
                _set_index = 0;
                _files_ok = 0;
                _files_bad = 0;
                _files_missing = 0;
                _files_unreadable = 0;
                UpdateStatusBar();

                // Go becomes Stop while loading, as while checking
                btnGo.Text = Language.MainForm.StopButton;
                ToggleMenuItemsEnabled(false, false);
                _queueStop = false;
                bool loaded;
                try
                {
                    do
                    {
                        loaded = await LoadSetsAsync(files);
                        files = TakeNewSets();
                    } while (loaded && files.Count != 0);
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        btnGo.Text = Language.MainForm.GoButton;
                        ToggleMenuItemsEnabled(true, false);
                    }
                }

                if (IsDisposed)
                    return;

                SetStatusText(Language.MainForm.Status_Ready);
                if (loaded && Program.Settings.Check.AutoVerify)
                    await VerifyAsync();
            }
            finally
            {
                _workingOnList = false;
                _queueStop = false;
            }
        }

        private void mnuFile_DropDownOpening(object sender, EventArgs e)
        {
            List<string> paths = Program.Settings.GetRecentFiles().ToList();

            miDocumentSeparator.Visible = false;
            miDocument1.Visible = false;
            miDocument2.Visible = false;
            miDocument3.Visible = false;
            miDocument4.Visible = false;

            if (paths.Count >= 1)
            {
                miDocumentSeparator.Visible = true;
                miDocument1.Text = "&1 " + paths[0];
                miDocument1.Tag = paths[0];
                miDocument1.Visible = true;
            }
            if (paths.Count >= 2)
            {
                miDocument2.Text = "&2 " + paths[1];
                miDocument2.Tag = paths[1];
                miDocument2.Visible = true;
            }
            if (paths.Count >= 3)
            {
                miDocument3.Text = "&3 " + paths[2];
                miDocument3.Tag = paths[2];
                miDocument3.Visible = true;
            }
            if (paths.Count >= 4)
            {
                miDocument4.Text = "&4 " + paths[3];
                miDocument4.Tag = paths[3];
                miDocument4.Visible = true;
            }
        }

        private void miDocument1_Click(object sender, EventArgs e)
        {
            EngageDropTimer(new List<string> { ((ToolStripMenuItem)sender).Tag.ToString() });
        }

        private void miCheckForUpdates_Click(object sender, EventArgs e)
        {
            CheckForUpdates(true);
        }

        private void miRegisterFileTypes_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                Program.RegisterFileTypes(true);
            }
            catch (Exception ex)
            {
                Cursor.Current = Cursors.Default;

                MessageBox.Show(string.Format(Language.MainForm.RegisterFileTypesError_Message, ex.Message), Language.MainForm.RegisterFileTypesError_Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Cursor.Current = Cursors.Default;
            MessageBox.Show(Language.MainForm.RegisterFileTypes_Message, Language.MainForm.RegisterFileTypesError_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void miImportIlsfv_Click(object sender, EventArgs e)
        {
            if (Program.OfferIlsfvImport(this, false))
            {
                SetAlwaysOnTop();
                SetLanguage();
            }
        }

        private void miUnregisterFileTypes_Click(object sender, EventArgs e)
        {
            try
            {
                FileTypeRegistration.Unregister();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Language.MainForm.RegisterFileTypesError_Message, ex.Message), Language.MainForm.Menu_Tools_UnregisterFileTypes, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(Language.MainForm.UnregisterFileTypes_Message, Language.MainForm.Menu_Tools_UnregisterFileTypes, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void miCopyFileNames_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            StringBuilder sb = new StringBuilder();
            List<ChecksumFile> files = GetSelectedFiles();
            foreach (ChecksumFile file in files)
            {
                if (files.Count == 1)
                    sb.Append(Path.GetFileName(file.FileName));
                else
                    sb.AppendLine(Path.GetFileName(file.FileName));
            }
            Clipboard.SetText(sb.ToString());
            Cursor.Current = Cursors.WaitCursor;
        }

        private void miCopyPathAndFileNames_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            StringBuilder sb = new StringBuilder();
            List<ChecksumFile> files = GetSelectedFiles();
            foreach (ChecksumFile file in files)
            {
                if (files.Count == 1)
                    sb.Append(Path.Combine(file.Set.Directory, file.FileName));
                else
                    sb.AppendLine(Path.Combine(file.Set.Directory, file.FileName));
            }
            Clipboard.SetText(sb.ToString());
            Cursor.Current = Cursors.WaitCursor;
        }

        private void miCopyCurrentChecksum_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            StringBuilder sb = new StringBuilder();
            List<ChecksumFile> files = GetSelectedFiles();
            foreach (ChecksumFile file in files)
            {
                if (files.Count == 1)
                    sb.Append(file.CurrentChecksum);
                else
                    sb.AppendLine(file.CurrentChecksum);
            }
            Clipboard.SetText(sb.ToString());
            Cursor.Current = Cursors.WaitCursor;
        }

        private void miCopyOriginalChecksum_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            StringBuilder sb = new StringBuilder();
            List<ChecksumFile> files = GetSelectedFiles();
            foreach (ChecksumFile file in files)
            {
                if (files.Count == 1)
                    sb.Append(file.OriginalChecksum);
                else
                    sb.AppendLine(file.OriginalChecksum);
            }
            Clipboard.SetText(sb.ToString());
            Cursor.Current = Cursors.WaitCursor;
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            List<ChecksumFile> selected = GetSelectedFiles();
            if (_workingOnList || selected.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            miCopyCurrentChecksum.Visible = false;
            miCopyOriginalChecksum.Visible = false;

            bool showSep = false;
            if (selected.Count == 1)
            {
                ChecksumFile file = selected[0];

                if (!string.IsNullOrEmpty(file.CurrentChecksum))
                {
                    miCopyCurrentChecksum.Text = string.Format(Language.MainForm.CopyCurrentChecksumContextMenu, file.CurrentChecksum);
                    miCopyCurrentChecksum.Visible = true;
                    showSep = true;
                }

                if (!string.IsNullOrEmpty(file.OriginalChecksum))
                {
                    miCopyOriginalChecksum.Text = string.Format(Language.MainForm.CopyOriginalChecksumContextMenu, file.OriginalChecksum);
                    miCopyOriginalChecksum.Visible = true;
                    showSep = true;
                }
            }

            miContextMenuSeparator.Visible = showSep;
        }

        private void lvwFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_workingOnList)
                return;

            // a single file or set header shows its set's comments
            if (lvwFiles.SelectedIndices.Count == 1 && lvwFiles.SelectedIndices[0] < _rows.Count)
            {
                txtComments.Text = _rows[lvwFiles.SelectedIndices[0]].Set.Comments;
            }
            else
            {
                txtComments.Text = string.Empty;
            }
        }

        private int _listViewUpdate;
        private void ListViewBeginUpdate()
        {
            if (_listViewUpdate == 0)
                lvwFiles.BeginUpdate();
            _listViewUpdate++;
        }

        private void ListViewEndUpdate()
        {
            _listViewUpdate--;
            if (_listViewUpdate == 0)
                lvwFiles.EndUpdate();
        }

        private static DateTime GetLastWriteTime(FileSystemInfo fileInfo)
        {
            try
            {
                return fileInfo.LastWriteTime;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                return DateTime.Now;
            }
        }

        private static DateTime GetLastWriteTimeUtc(FileSystemInfo fileInfo)
        {
            try
            {
                return fileInfo.LastWriteTimeUtc;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                return DateTime.Now;
            }
        }

        private void miFindDuplicateFiles_Click(object sender, EventArgs e)
        {
            bool ok = false;
            try
            {
                List<string> fileNames = GetChecksumFiles(false);
                if (fileNames == null || fileNames.Count != 1)
                    return;

                if (!LoadSingleFile(fileNames[0]))
                    return;

                if (_sets.Count != 1)
                    return;

                ok = true;
            }
            finally
            {
                if (!ok)
                {
                    Cursor.Current = Cursors.WaitCursor;
                    _sets.Clear();
                    lvwFiles.BeginUpdate();
                    try
                    {
                        ClearFileItems();
                    }
                    finally
                    {
                        lvwFiles.EndUpdate();
                    }
                    Cursor.Current = Cursors.Default;
                }
                SetStatusText(Language.MainForm.Status_Ready);
            }

            Cursor.Current = Cursors.WaitCursor;
            ChecksumSet set = _sets[0];
            using (RemoveDuplicatesForm form = new RemoveDuplicatesForm(set))
            {
                Cursor.Current = Cursors.Default;
                form.ShowDialog();
            }

            Cursor.Current = Cursors.WaitCursor;
            _sets.Clear();
            lvwFiles.BeginUpdate();
            try
            {
                ClearFileItems();
            }
            finally
            {
                lvwFiles.EndUpdate();
            }
            Cursor.Current = Cursors.Default;
        }

        private void miTruncateFileNames_Click(object sender, EventArgs e)
        {
            string strMaxLength;
            if (!GetInputForm.ShowForm(Language.MainForm.TruncateFileNames_MaxLength, Language.MainForm.TruncateFileNames_MaxLength, out strMaxLength))
                return;

            int maxLength;
            if (!int.TryParse(strMaxLength, out maxLength))
                return;

            if (maxLength < FileNameTruncator.MinimumLength)
            {
                MessageBox.Show(Language.MainForm.TruncateFileNames_MinimumLengthIs12, "", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (folderBrowserDialog1.ShowDialog() != DialogResult.OK)
                return;

            SetStatusText(Language.MainForm.Status_LookingForLongFileNames);
            TruncateResult result;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                result = FileNameTruncator.Truncate(folderBrowserDialog1.SelectedPath, maxLength, null);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                SetStatusText(Language.MainForm.Status_Ready);
            }

            string message = string.Format(Language.MainForm.TruncateFileNames_FilesRenamed_Message, result.Renamed);
            if (result.NotRenamed.Count != 0)
            {
                string list = string.Join(Environment.NewLine, result.NotRenamed.Take(10).Select(p =>
                    p.Key + Environment.NewLine + "    " + (p.Value ?? Language.MainForm.TruncateFileNames_NoRoom)));
                if (result.NotRenamed.Count > 10)
                    list += Environment.NewLine + "...";
                message += Environment.NewLine + Environment.NewLine +
                    string.Format(Language.MainForm.TruncateFileNames_NotRenamed_Message, result.NotRenamed.Count, list);
            }

            MessageBox.Show(message, "", MessageBoxButtons.OK, result.NotRenamed.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void SetLanguage()
        {
            var p = Language.MainForm;

            // File
            mnuFile.Text = p.Menu_File;
            miNewSFV.Text = p.Menu_File_NewSFVFile;
            miNewMD5.Text = p.Menu_File_NewMD5File;
            miNewSHA1.Text = p.Menu_File_NewSHA1File;
            miNewSHA256.Text = p.Menu_File_NewSHA256File;
            miNewSHA512.Text = p.Menu_File_NewSHA512File;
            miOpen.Text = p.Menu_File_Open;
            miVerifyFolder.Text = p.Menu_File_VerifyFolder;
            miPreferences.Text = p.Menu_File_Preferences;
            miCheckForUpdates.Text = p.Menu_File_CheckForUpdates;
            miExit.Text = p.Menu_File_Exit;

            // Legend
            mnuLegend.Text = p.Menu_Legend;
            miFileOK.Text = p.Menu_Legend_FileOK;
            miFileBad.Text = p.Menu_Legend_FileBad;
            miFileNotFound.Text = p.Menu_Legend_FileNotFound;
            miFileUnreadable.Text = p.Menu_Legend_FileUnreadable;
            miFileUntested.Text = p.Menu_Legend_FileUntestedUnknown;

            // Tools
            mnuTools.Text = p.Menu_Tools;
            miFindRenamedFiles.Text = p.Menu_Tools_FindRenamedFiles;
            miUseCachedResults.Text = p.Menu_Tools_UseCachedResults;
            miFindDuplicateFiles.Text = p.Menu_Tools_FindDeleteDuplicateFilesUsingChecksum;
            miTruncateFileNames.Text = p.Menu_Tools_TruncateFileNames;
            miRegisterFileTypes.Text = p.Menu_Tools_RegisterFileTypes;
            miUnregisterFileTypes.Text = p.Menu_Tools_UnregisterFileTypes;
            miImportIlsfv.Text = p.Menu_Tools_ImportIlsfvSettings;
            miImportIlsfv.Visible = IlsfvImport.IsAvailable(IlsfvImport.DefaultDataPath);

            // View
            mnuView.Text = p.Menu_View;
            miHideGood.Text = p.Menu_View_HideGood;
            miCommentResultPane.Text = p.Menu_View_CommentResultPane;

            // Help
            mnuHelp.Text = p.Menu_Help;
            miAbout.Text = p.Menu_Help_About;

            // Headers
            colFilename.Text = p.FileNameColumnHeader;
            tpComments.Text = p.CommentsTabHeader;

            // Status bar labels
            lblSets.Text = p.SetsLabel;
            lblParts.Text = p.PartsLabel;
            lblGood.Text = p.GoodLabel;
            lblBad.Text = Language.MainForm.BadLabel;
            lblMissing.Text = p.MissingLabel;

            // Buttons
            chkHideGood.Text = p.HideGoodCheckBox;
            btnPause.Text = p.PauseButton;
            btnHide.Text = p.HideButton;
            btnGo.Text = p.GoButton;

            // Dialogs
            folderBrowserDialog1.Description = p.FolderBrowseDialog_Title;

            SetStatusText(p.Status_Ready);

            Application.DoEvents();

            progressBar1.Width = chkHideGood.Left - 16;
        }
    }
}
