using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CoolMac
{
    public sealed class FolderRegistryValue
    {
        public int View { get; set; }
        public bool KeyExisted { get; set; }
        public string Name { get; set; }
        public bool Existed { get; set; }
        public string Value { get; set; }
        public int Kind { get; set; }
    }
    public sealed class GlobalFolderBackup
    {
        public string Format { get; set; }
        public string Theme { get; set; }
        public string ManagedValue { get; set; }
        public List<FolderRegistryValue> Values { get; set; }
    }
    public sealed class FolderThemeResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
    public sealed class CommonFolderTheme
    {
        public string Folder { get; set; }
        public string BackupId { get; set; }
    }
    internal static class GlobalFolderTheme
    {
        internal const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
        internal static readonly string MachineFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CoolMaconWindows", "FolderTheme");
        internal static readonly string BackupPath = Path.Combine(MachineFolder, "original-icons.json");
        private static readonly string CommonPath = Path.Combine(SettingsStore.Folder, "global-common-folders.json");
        [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint change, uint flags, IntPtr first, IntPtr second);

        internal static GlobalFolderBackup Read()
        {
            if (!File.Exists(BackupPath)) return null;
            GlobalFolderBackup backup = new JavaScriptSerializer().Deserialize<GlobalFolderBackup>(File.ReadAllText(BackupPath));
            if (backup == null || backup.Format != "CoolMaconWindows.GlobalFolders.v1" || backup.Values == null || backup.Values.Count != 4 || backup.Values.Select(value => value.View + ":" + value.Name).Distinct().Count() != 4
                || backup.Values.Any(value => (value.View != 32 && value.View != 64) || (value.Name != "3" && value.Name != "4") || (value.Existed && value.Kind != (int)RegistryValueKind.String && value.Kind != (int)RegistryValueKind.ExpandString)))
                throw new InvalidDataException("The saved Windows icon backup is invalid. No registry values were changed.");
            return backup;
        }
        internal static bool Enabled()
        {
            GlobalFolderBackup backup = Read(); if (backup == null) return false;
            return backup.Values.All(value => String.Equals(Current(value), backup.ManagedValue, StringComparison.OrdinalIgnoreCase));
        }
        private static string Current(FolderRegistryValue value)
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, value.View == 64 ? RegistryView.Registry64 : RegistryView.Registry32))
            using (RegistryKey key = root.OpenSubKey(RegistryPath))
                return key == null ? null : key.GetValue(value.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        internal static GlobalFolderBackup Snapshot()
        {
            GlobalFolderBackup backup = new GlobalFolderBackup { Format = "CoolMaconWindows.GlobalFolders.v1", Values = new List<FolderRegistryValue>() };
            foreach (int view in new[] { 64, 32 })
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view == 64 ? RegistryView.Registry64 : RegistryView.Registry32))
                using (RegistryKey key = root.OpenSubKey(RegistryPath))
                    foreach (string name in new[] { "3", "4" })
                    {
                        bool existed = key != null && key.GetValueNames().Contains(name);
                        RegistryValueKind kind = existed ? key.GetValueKind(name) : RegistryValueKind.String;
                        if (existed && kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) throw new InvalidOperationException("An existing folder icon override has an unsupported type. It was left unchanged.");
                        backup.Values.Add(new FolderRegistryValue { View = view, KeyExisted = key != null, Name = name, Existed = existed,
                            Kind = (int)kind, Value = existed ? (string)key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null });
                    }
            return backup;
        }
        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static void ApplyMachine(string theme)
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Windows administrator approval is required to change the default folder icon.");
            FolderArt.Theme(theme);
            GlobalFolderBackup backup = Read() ?? Snapshot();
            Directory.CreateDirectory(MachineFolder);
            string iconPath = Path.Combine(MachineFolder, "folder-" + theme + ".ico"); File.WriteAllBytes(iconPath, FolderArt.IconBytes(theme, "none"));
            backup.Theme = theme; backup.ManagedValue = iconPath + ",0";
            WriteJson(BackupPath, backup); // The original values are persisted before touching either registry view.
            foreach (int view in new[] { 64, 32 })
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view == 64 ? RegistryView.Registry64 : RegistryView.Registry32))
                using (RegistryKey key = root.CreateSubKey(RegistryPath))
                    foreach (string name in new[] { "3", "4" }) key.SetValue(name, backup.ManagedValue, RegistryValueKind.String);
        }
        internal static void RestoreMachine()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Windows administrator approval is required to restore the default folder icons.");
            GlobalFolderBackup backup = Read(); if (backup == null) return;
            foreach (int view in new[] { 64, 32 })
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view == 64 ? RegistryView.Registry64 : RegistryView.Registry32))
                {
                    bool removeEmpty = false;
                    using (RegistryKey key = root.OpenSubKey(RegistryPath, true))
                    {
                        if (key == null) continue;
                        foreach (FolderRegistryValue value in backup.Values.Where(value => value.View == view))
                        {
                            string current = key.GetValue(value.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                            if (!IsManagedValue(current)) continue; // Preserve a newer override written by another program.
                            if (value.Existed) key.SetValue(value.Name, value.Value, (RegistryValueKind)value.Kind); else key.DeleteValue(value.Name, false);
                        }
                        removeEmpty = backup.Values.Where(value => value.View == view).All(value => !value.KeyExisted) && key.ValueCount == 0 && key.SubKeyCount == 0;
                    }
                    if (removeEmpty) root.DeleteSubKey(RegistryPath, false);
                }
            File.Delete(BackupPath);
        }
        private static bool IsManagedValue(string value)
        {
            return FolderArt.Themes.Any(theme => String.Equals(value, Path.Combine(MachineFolder, "folder-" + theme.Id + ".ico") + ",0", StringComparison.OrdinalIgnoreCase));
        }
        internal static FolderThemeResponse RunElevated(string operation, string theme)
        {
            FolderArt.Theme(theme); string request = Guid.NewGuid().ToString("N");
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--global-folder-helper " + operation + " " + theme + " " + request) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (Process helper = Process.Start(start))
                {
                    if (helper == null) throw new InvalidOperationException("The Windows permission request could not start.");
                    helper.WaitForExit();
                    string response = Path.Combine(MachineFolder, "requests", request + ".json");
                    if (!File.Exists(response)) throw new InvalidOperationException("The folder theme helper did not return a result.");
                    return new JavaScriptSerializer().Deserialize<FolderThemeResponse>(File.ReadAllText(response));
                }
            }
            catch (Win32Exception error) { if (error.NativeErrorCode == 1223) return new FolderThemeResponse { Success = false, Message = "Windows permission was declined. The default icons were not changed." }; throw; }
        }
        internal static int Helper(string[] args)
        {
            Guid request; if (args.Length != 4 || !Guid.TryParseExact(args[3], "N", out request)) return 2;
            FolderThemeResponse response;
            try
            {
                if (args[1] == "apply") ApplyMachine(args[2]);
                else if (args[1] == "verify-apply")
                {
                    if (Read() != null) throw new InvalidOperationException("A global theme is already active. Use the existing Apply control.");
                    GlobalFolderBackup before = Snapshot();
                    ApplyMachine(args[2]);
                    if (!Enabled()) throw new InvalidOperationException("Windows did not save both folder icon overrides.");
                    RestoreMachine(); GlobalFolderBackup after = Snapshot();
                    if (new JavaScriptSerializer().Serialize(before) != new JavaScriptSerializer().Serialize(after)) throw new InvalidOperationException("The original registry values did not restore exactly.");
                    ApplyMachine(args[2]);
                }
                else if (args[1] == "restore") RestoreMachine(); else throw new ArgumentException("Unknown folder theme action.");
                response = new FolderThemeResponse { Success = true, Message = args[1] == "restore" ? "Windows folder defaults restored." : "Default folder icons changed; backup and restoration validated." };
            }
            catch (Exception error) { response = new FolderThemeResponse { Success = false, Message = error.Message }; }
            try { WriteJson(Path.Combine(MachineFolder, "requests", request.ToString("N") + ".json"), response); } catch { return 2; }
            return response.Success ? 0 : 1;
        }
        internal static void Refresh()
        {
            SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
            string utility = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ie4uinit.exe");
            if (File.Exists(utility)) using (Process process = Process.Start(new ProcessStartInfo(utility, "-show") { UseShellExecute = false, CreateNoWindow = true })) { if (process != null) process.WaitForExit(3000); }
        }
        internal static bool RestartExplorer()
        {
            bool transfer = false;
            Native.EnumWindows(delegate(IntPtr window, IntPtr unused) { if (Native.IsWindowVisible(window) && Native.ClassName(window) == "OperationStatusWindow") transfer = true; return true; }, IntPtr.Zero);
            if (transfer) return false;
            IntPtr taskbar = Native.FindWindow("Shell_TrayWnd", null); if (taskbar == IntPtr.Zero) return false;
            uint id; Native.GetWindowThreadProcessId(taskbar, out id);
            if (!String.Equals(Path.GetFileName(Native.ProcessPath(id)), "explorer.exe", StringComparison.OrdinalIgnoreCase)) return false;
            List<string> paths = ExplorerFolders();
            using (Process shell = Process.GetProcessById((int)id))
            {
                if (shell.SessionId != Process.GetCurrentProcess().SessionId) return false;
                shell.Kill(); shell.WaitForExit(3000);
            }
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            Stopwatch wait = Stopwatch.StartNew();
            while (Native.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero && wait.ElapsedMilliseconds < 5000) System.Threading.Thread.Sleep(80);
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo(explorer, "\"" + path + "\"") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
            return Native.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;
        }
        private static List<string> ExplorerFolders()
        {
            List<string> result = new List<string>(); object shell = null, windows = null;
            try
            {
                Type type = Type.GetTypeFromProgID("Shell.Application"); shell = Activator.CreateInstance(type);
                windows = type.InvokeMember("Windows", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                int count = Convert.ToInt32(windows.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, windows, null));
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    try
                    {
                        window = windows.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, windows, new object[] { i });
                        string location = window.GetType().InvokeMember("LocationURL", System.Reflection.BindingFlags.GetProperty, null, window, null) as string;
                        Uri uri; if (Uri.TryCreate(location, UriKind.Absolute, out uri) && uri.IsFile && Directory.Exists(uri.LocalPath)) result.Add(uri.LocalPath);
                    }
                    catch { }
                    finally { if (window != null) Marshal.ReleaseComObject(window); }
                }
            }
            catch { }
            finally { if (windows != null) Marshal.ReleaseComObject(windows); if (shell != null) Marshal.ReleaseComObject(shell); }
            return result;
        }
        internal static int ApplyCommonFolders(string theme)
        {
            FolderIcons icons = FolderIcons.Default(); List<CommonFolderTheme> entries = ReadCommon();
            string[] folders = new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") };
            int changed = 0;
            foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                try
                {
                    FolderBackup previous = icons.Read(folder); CommonFolderTheme tracked = entries.FirstOrDefault(entry => String.Equals(entry.Folder, folder, StringComparison.OrdinalIgnoreCase));
                    if (previous != null && (tracked == null || previous.Id != tracked.BackupId)) continue;
                    FolderBackup applied = icons.Apply(folder, theme, "auto");
                    if (tracked == null) entries.Add(new CommonFolderTheme { Folder = folder, BackupId = applied.Id });
                    WriteJson(CommonPath, entries); changed++;
                }
                catch (Exception error) { SettingsStore.Log(error); }
            }
            return changed;
        }
        internal static void RestoreCommonFolders()
        {
            FolderIcons icons = FolderIcons.Default(); List<CommonFolderTheme> remaining = new List<CommonFolderTheme>();
            foreach (CommonFolderTheme entry in ReadCommon())
            {
                try { if (Directory.Exists(entry.Folder)) { FolderBackup record = icons.Read(entry.Folder); if (record != null && record.Id == entry.BackupId) icons.Restore(entry.Folder); } }
                catch (Exception error) { SettingsStore.Log(error); remaining.Add(entry); }
            }
            WriteJson(CommonPath, remaining);
        }
        private static List<CommonFolderTheme> ReadCommon()
        {
            if (!File.Exists(CommonPath)) return new List<CommonFolderTheme>();
            return new JavaScriptSerializer().Deserialize<List<CommonFolderTheme>>(File.ReadAllText(CommonPath)) ?? new List<CommonFolderTheme>();
        }
    }
}
