using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using TaoSFV.Localization;
using TaoSFV.Model.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace TaoSFV
{
    static class Program
    {
        private static string _dataPath;
        private static string _cacheFile;
        private static string _settingsFile;

        public static string AppDataPath { get { return _dataPath; } }

        private static SqliteConnection _settingsConn;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // checksum files are read/written with legacy code pages, which .NET only provides via this provider
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // the uninstaller runs "TaoSFV.exe /unregister": removes the file types and Explorer menu, nothing else
            if (args != null && args.Length == 1 && args[0].ToLower() == "/unregister")
            {
                try
                {
                    FileTypeRegistration.Unregister();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex);
                }
                return;
            }

            // a scheduled check runs without a window and must not take the single-instance mutex: TaoSFV opened
            // meanwhile would otherwise hand its files to it
            bool scheduled = args != null && args.Length == 2 && args[0].ToLower() == ScheduledCheckRun.Argument;
            bool ok = true;
            Mutex mutex = scheduled ? null : new Mutex(true, "{CE7485F4-1CD3-4201-8CAD-AFE0F80C3061}", out ok);
            bool anotherInstance = !ok;

            try
            {
                _dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaoSFV");
                if (!Directory.Exists(_dataPath))
                {
                    RegisterFileTypes(false);

                    Directory.CreateDirectory(_dataPath);
                }

                // SQLite databases are created on first run; the old SQL Server Compact .sdf files are not migrated
                _cacheFile = Path.Combine(_dataPath, "cache.db");
                _settingsFile = Path.Combine(_dataPath, "settings.db");

                Settings = new ProgramSettings();
                Settings.General.Load();
                Settings.Check.Load();
                Settings.Create.Load();
                Settings.Comments.Load();

                Language.Load(Settings.General.Language);

                if (scheduled)
                {
                    Application.EnableVisualStyles();
                    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                    ScheduledCheckRun.Run(args[1]);
                    return;
                }

                Mutex createMutex = null;
                bool anotherCreateInstance = false;

                bool create = false;
                if (args != null && args.Length != 0 && args[0].ToLower() == "/create")
                {
                    create = true;

                    createMutex = new Mutex(true, string.Format("CE7485F4-1CD3-4201-8CAD-AFE0F80C3061:Create{0}", args[1]), out ok);
                    anotherCreateInstance = !ok;
                }

                try
                {
                    if (anotherInstance && Settings.General.ReuseWindow && !create)
                    {
                        string addPath = Path.Combine(AppDataPath, Guid.NewGuid().ToString().Replace("{", "").Replace("}", "") + ".add");
                        if (args != null && args.Length != 0)
                        {
                            File.WriteAllLines(addPath, args);
                        }
                        else
                        {
                            File.WriteAllBytes(addPath, new byte[0]);
                        }
                        return;
                    }
                    else if (create)
                    {
                        string ext = string.Format(".{0}.create", args[1]);
                        if (anotherCreateInstance)
                        {
                            string addPath = Path.Combine(AppDataPath, Guid.NewGuid().ToString().Replace("{", "").Replace("}", "") + ext);
                            File.WriteAllLines(addPath, args);
                            return;
                        }
                        else
                        {
                            DateTime lastFound = DateTime.Now;
                            while (DateTime.Now - lastFound < TimeSpan.FromMilliseconds(300))
                            {
                                foreach (string file in Directory.GetFiles(AppDataPath, "*" + ext))
                                {
                                    List<string> argsList = new List<string>(args);

                                    string[] lines;
                                    try
                                    {
                                        lines = File.ReadAllLines(file);

                                        for (int i = 2; i < lines.Length; i++)
                                        {
                                            argsList.Add(lines[i]);
                                        }

                                        File.Delete(file);
                                    }
                                    catch (IOException)
                                    {
                                        continue;
                                    }

                                    args = argsList.ToArray();

                                    lastFound = DateTime.Now;
                                }

                                Thread.Sleep(50);
                            }
                        }
                    }

                    foreach (string file in Directory.GetFiles(AppDataPath, "*.add"))
                    {
                        File.Delete(file);
                    }

                    foreach (string file in Directory.GetFiles(AppDataPath, "*.create"))
                    {
                        File.Delete(file);
                    }

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                    ApplyTheme(Settings.General.Theme);

                    // first start next to an existing ilSFV: offer its settings once
                    if (Settings.IsNew && IlsfvImport.IsAvailable(IlsfvImport.DefaultDataPath))
                        OfferIlsfvImport(null, true);
                    Application.ThreadException += Application_ThreadException;
                    Application.Run(new MainForm(args));
                    Cache.Flush();

                    // Save settings
                    Settings.Save();
                    Settings.General.Save();
                    Settings.Check.Save();
                    Settings.Create.Save();
                    Settings.Comments.Save();
                }
                finally
                {
                    if (!anotherCreateInstance && createMutex != null)
                        createMutex.ReleaseMutex();
                }
            }
            catch (PlatformNotSupportedException)
            {
                // TODO: Localize
                MessageBox.Show("This program requires Windows XP SP3, Windows Vista SP2, Windows 7 or later.", "", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            catch (Exception ex)
            {
                ShowException(ex);
            }
            finally
            {
                if (mutex != null && !anotherInstance)
                    mutex.ReleaseMutex();
            }
        }

        public static void RegisterFileTypes(bool throwException)
        {
            try
            {
                FileTypeRegistration.Register(ExecutablePath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);

                if (throwException)
                    throw;
            }
        }

        public static ProgramSettings Settings { get; private set; }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ShowException(e.Exception);
        }

        public static void ShowException(Exception e)
        {
            if (e is FileNotFoundException && e.Message.Contains("System.Core"))
            {
                DialogResult res = MessageBox.Show(Language.Startup.RequireNET35_Message, Language.General.Error, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (res == DialogResult.OK)
                {
                    OpenUrl("https://dotnet.microsoft.com/download/dotnet");
                }

                Environment.Exit(-1);
                return;
            }

            Cursor.Current = Cursors.WaitCursor;
            using (ExceptionForm form = new ExceptionForm(e.Message, e.ToString(), false, false))
            {
                Cursor.Current = Cursors.Default;
                form.ShowDialog();
            }
        }

        /// <summary>
        /// Returns a new open connection; callers dispose it. The cache is written from background threads,
        /// and SQLite connections must not be shared across threads.
        /// </summary>
        public static SqliteConnection GetOpenCacheConnection()
        {
            return Database.Open(_cacheFile);
        }

        /// <summary>Where TaoSFV keeps its checksum cache: %LOCALAPPDATA%\TaoSFV\cache.db.</summary>
        public static string DefaultCacheFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaoSFV", "cache.db"); }
        }

        /// <summary>
        /// Uses only the checksum cache, without loading settings (taosfv-cli). The file must exist: creating
        /// TaoSFV's folder would make the GUI skip its first-run setup.
        /// </summary>
        internal static void OpenCacheOnly(string cacheFile)
        {
            _cacheFile = cacheFile;
            using (SqliteConnection conn = GetOpenCacheConnection())
                Database.UpgradeCache(conn);
        }

        public static SqliteConnection GetOpenSettingsConnection()
        {
            if (_settingsConn == null)
                _settingsConn = Database.Open(_settingsFile);
            return _settingsConn;
        }

        /// <summary>
        /// Path of TaoSFV.exe (Assembly.Location points at TaoSFV.dll on .NET).
        /// </summary>
        public static string ExecutablePath
        {
            get { return Environment.ProcessPath ?? Application.ExecutablePath; }
        }

        public static void ApplyTheme(AppTheme theme)
        {
            switch (theme)
            {
                case AppTheme.Light:
                    Application.SetColorMode(SystemColorMode.Classic);
                    break;
                case AppTheme.Dark:
                    Application.SetColorMode(SystemColorMode.Dark);
                    break;
                default:
                    Application.SetColorMode(SystemColorMode.System);
                    break;
            }
        }

        /// <summary>Asks, imports ilSFV's settings and cache, reloads settings and reports. Returns true if imported.</summary>
        public static bool OfferIlsfvImport(IWin32Window owner, bool firstStart)
        {
            var lang = Language.MainForm;
            string question = firstStart ? lang.ImportIlsfv_FirstStart_Message : lang.ImportIlsfv_Confirm_Message;
            if (MessageBox.Show(owner, question, lang.ImportIlsfv_Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;

            IlsfvImport.Result result;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                Cache.Flush();
                result = IlsfvImport.Import(IlsfvImport.DefaultDataPath, GetOpenSettingsConnection(), GetOpenCacheConnection);
                Settings.Reload();
                Language.Load(Settings.General.Language);
            }
            catch (Exception ex)
            {
                Cursor.Current = Cursors.Default;
                Trace.WriteLine(ex);
                MessageBox.Show(owner, string.Format(lang.ImportIlsfv_Error_Message, ex.Message), lang.ImportIlsfv_Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Cursor.Current = Cursors.Default;
            MessageBox.Show(owner, string.Format(lang.ImportIlsfv_Done_Message, result.FilesChecked, result.RecentFiles, result.CacheEntries),
                lang.ImportIlsfv_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            OfferCommentTemplateUpdate(owner);
            return true;
        }

        /// <summary>
        /// A comments header or footer written in ilSFV that names ilSFV or links to cdtag.com: shows it and offers to
        /// change the name to TaoSFV and the links to TaoSFV's page. (Untouched defaults are replaced without asking.)
        /// </summary>
        private static void OfferCommentTemplateUpdate(IWin32Window owner)
        {
            CommentSettings comments = Settings.Comments;
            if (!CommentTemplate.IsOutdated(comments.Header) && !CommentTemplate.IsOutdated(comments.Footer))
                return;

            string shown = (comments.Header + Environment.NewLine + comments.Footer).Trim();
            var lang = Language.MainForm;
            if (MessageBox.Show(owner, string.Format(lang.UpdateCommentsTemplate_Message, shown), lang.UpdateCommentsTemplate_Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            comments.Header = CommentTemplate.Modernize(comments.Header);
            comments.Footer = CommentTemplate.Modernize(comments.Footer);
            comments.Save();
        }

        public static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        public static bool SafeDelete(string fileName)
        {
            return SafeDelete(fileName, Settings.Check.UseRecyleBin);
        }

        /// <summary>
        /// Deletes a file, or moves it to the Recycle Bin. Returns false if the Recycle Bin wouldn't take it; the file
        /// is then left in place rather than deleted permanently. Throws if a permanent delete fails.
        /// </summary>
        public static bool SafeDelete(string fileName, bool useRecycleBin)
        {
            if (!useRecycleBin)
            {
                File.Delete(fileName);
                return true;
            }

            if (RecycleBin.Recycle(fileName))
                return true;

            Trace.WriteLine(string.Format("Couldn't move {0} to the Recycle Bin", fileName));
            return false;
        }
    }
}
