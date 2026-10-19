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
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
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
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
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

        private static void RegEnableReflectionKey(RegistryKey key, string path)
        {
            RegistryKey openedKey;
            IntPtr handle = GetRegistryHandle(key, path, out openedKey);
            if (handle != IntPtr.Zero)
            {
                RegEnableReflectionKey(handle);
                openedKey.Close();
            }
        }

        private static IntPtr GetRegistryHandle(RegistryKey key, string path, out RegistryKey openedKey)
        {
            string[] parts = path.Split('\\');

            openedKey = null;
            
            if (parts == null || parts.Length == 0)
            {
                return IntPtr.Zero;
            }

            IntPtr handle = IntPtr.Zero;
            foreach (string part in parts)
            {
                key = _openSubKey(key, part, true, RegWow64Options.KEY_WOW64_32KEY, out handle);
                if (key == null)
                    return IntPtr.Zero;
            }

            return handle;
        }

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(
            [In] IntPtr hProcess,
            [Out] out bool wow64Process
        );

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool InternalCheckIsWow64()
        {
            if ((Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor >= 1) ||
                Environment.OSVersion.Version.Major >= 6)
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    bool retVal;
                    if (!IsWow64Process(p.Handle, out retVal))
                    {
                        return false;
                    }
                    return retVal;
                }
            }
            else
            {
                return false;
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
            return true;
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

        enum RegWow64Options
        {
            None = 0,
            KEY_WOW64_64KEY = 0x0100,
            KEY_WOW64_32KEY = 0x0200
        }

        enum RegistryRights
        {
            ReadKey = 131097,
            WriteKey = 131078
        }

        private static RegistryKey _openSubKey(RegistryKey parentKey, string subKeyName, bool writable, RegWow64Options options, out IntPtr subKeyHandle)
        {
            //Sanity check
            if (parentKey == null || _getRegistryKeyHandle(parentKey) == IntPtr.Zero)
            {
                subKeyHandle = IntPtr.Zero;
                return null;
            }

            //Set rights
            int rights = (int)RegistryRights.ReadKey;
            if (writable)
                rights = (int)RegistryRights.WriteKey;

            //Call the native function >.<
            int x, result = RegOpenKeyEx(_getRegistryKeyHandle(parentKey), subKeyName, 0, rights | (int)options, out x);
            subKeyHandle = (IntPtr)result;

            //If we errored, throw an exception
            if (result != 0)
            {
                throw new System.ComponentModel.Win32Exception("Exception encountered opening registry key.");
            }

            //Get the key represented by the pointer returned by RegOpenKeyEx
            RegistryKey subKey = _pointerToRegistryKey(subKeyHandle, writable, false);
            return subKey;
        }

        /// <summary>
        /// Get a pointer to a registry key.
        /// </summary>
        /// <param name="registryKey">Registry key to obtain the pointer of.</param>
        /// <returns>Pointer to the given registry key.</returns>
        private static IntPtr _getRegistryKeyHandle(RegistryKey registryKey)
        {
            //Get the type of the RegistryKey
            Type registryKeyType = typeof(RegistryKey);
            //Get the FieldInfo of the 'hkey' member of RegistryKey
            System.Reflection.FieldInfo fieldInfo =
            registryKeyType.GetField("hkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            //Get the handle held by hkey
            SafeHandle handle = (SafeHandle)fieldInfo.GetValue(registryKey);
            //Get the unsafe handle
            IntPtr dangerousHandle = handle.DangerousGetHandle();
            return dangerousHandle;
        }

        /// <summary>
        /// Get a registry key from a pointer.
        /// </summary>
        /// <param name="hKey">Pointer to the registry key</param>
        /// <param name="writable">Whether or not the key is writable.</param>
        /// <param name="ownsHandle">Whether or not we own the handle.</param>
        /// <returns>Registry key pointed to by the given pointer.</returns>
        private static RegistryKey _pointerToRegistryKey(IntPtr hKey, bool writable, bool ownsHandle)
        {
            //Get the BindingFlags for private contructors
            System.Reflection.BindingFlags privateConstructors = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            //Get the Type for the SafeRegistryHandle
            Type safeRegistryHandleType = typeof(Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid).Assembly.GetType("Microsoft.Win32.SafeHandles.SafeRegistryHandle");
            //Get the array of types matching the args of the ctor we want
            Type[] safeRegistryHandleCtorTypes = new Type[] { typeof(IntPtr), typeof(bool) };
            //Get the constructorinfo for our object
            System.Reflection.ConstructorInfo safeRegistryHandleCtorInfo = safeRegistryHandleType.GetConstructor(
            privateConstructors, null, safeRegistryHandleCtorTypes, null);
            //Invoke the constructor, getting us a SafeRegistryHandle
            Object safeHandle = safeRegistryHandleCtorInfo.Invoke(new Object[] { hKey, ownsHandle });

            //Get the type of a RegistryKey
            Type registryKeyType = typeof(RegistryKey);
            //Get the array of types matching the args of the ctor we want
            Type[] registryKeyConstructorTypes = new Type[] { safeRegistryHandleType, typeof(bool) };
            //Get the constructorinfo for our object
            System.Reflection.ConstructorInfo registryKeyCtorInfo = registryKeyType.GetConstructor(
            privateConstructors, null, registryKeyConstructorTypes, null);
            //Invoke the constructor, getting us a RegistryKey
            RegistryKey resultKey = (RegistryKey)registryKeyCtorInfo.Invoke(new Object[] { safeHandle, writable });
            //return the resulting key
            return resultKey;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Auto)]
        public static extern int RegOpenKeyEx(IntPtr hKey, string subKey, int ulOptions, int samDesired, out int phkResult);

        [Flags]
        public enum RegOption
        {
            NonVolatile = 0x0,
            Volatile = 0x1,
            CreateLink = 0x2,
            BackupRestore = 0x4,
            OpenLink = 0x8
        }

        [Flags]
        public enum RegSAM
        {
            QueryValue = 0x0001,
            SetValue = 0x0002,
            CreateSubKey = 0x0004,
            EnumerateSubKeys = 0x0008,
            Notify = 0x0010,
            CreateLink = 0x0020,
            WOW64_32Key = 0x0200,
            WOW64_64Key = 0x0100,
            WOW64_Res = 0x0300,
            Read = 0x00020019,
            Write = 0x00020006,
            Execute = 0x00020019,
            AllAccess = 0x000f003f
        }

        public enum RegResult
        {
            CreatedNewKey = 0x00000001,
            OpenedExistingKey = 0x00000002
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern int RegCreateKeyEx(
                    int hKey,
                    string lpSubKey,
                    int Reserved,
                    string lpClass,
                    RegOption dwOptions,
                    RegSAM samDesired,
                    SECURITY_ATTRIBUTES lpSecurityAttributes,
                    out int phkResult,
                    out RegResult lpdwDisposition);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern int RegEnableReflectionKey(IntPtr hBase);
    }
}
