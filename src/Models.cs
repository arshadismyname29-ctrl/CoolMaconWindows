using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Media;

namespace CoolMac
{
    public sealed class Pin
    {
        public string Name { get; set; }
        public string Target { get; set; }
        public string MatchPath { get; set; }
        public string IconPath { get; set; }
        public string Glyph { get; set; }
        public string Color { get; set; }
        public bool Utility { get; set; }
        public Pin() { Color = "#5489EE"; }
        internal static Pin FromPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path))) return null;
            Pin pin = new Pin { Name = Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar)), Target = path, IconPath = path, MatchPath = path };
            if (Directory.Exists(path)) { pin.Name = new DirectoryInfo(path).Name; pin.MatchPath = ""; pin.Utility = true; }
            if (String.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                object shell = null, shortcut = null;
                try
                {
                    Type type = Type.GetTypeFromProgID("WScript.Shell"); shell = Activator.CreateInstance(type);
                    shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                    pin.MatchPath = (string)shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                }
                catch { pin.MatchPath = ""; }
                finally { if (shortcut != null) Marshal.ReleaseComObject(shortcut); if (shell != null) Marshal.ReleaseComObject(shell); }
            }
            return pin;
        }
    }
    public sealed class Preferences
    {
        public int IconSize { get; set; }
        public double Magnification { get; set; }
        public double Opacity { get; set; }
        public bool HideTaskbar { get; set; }
        public bool AutoHide { get; set; }
        public bool ReduceMotion { get; set; }
        public bool DockEnabled { get; set; }
        public bool TopBarEnabled { get; set; }
        public string MonitorDevice { get; set; }
        public List<Pin> Pins { get; set; }
        public Preferences() { IconSize = 48; Magnification = 1.55; Opacity = 0.80; HideTaskbar = true; DockEnabled = true; Pins = new List<Pin>(); }
        internal void Normalize()
        {
            IconSize = Math.Max(32, Math.Min(64, IconSize));
            Magnification = Double.IsNaN(Magnification) || Double.IsInfinity(Magnification) ? 1.55 : Math.Max(1, Math.Min(1.9, Magnification));
            Opacity = Double.IsNaN(Opacity) || Double.IsInfinity(Opacity) ? .8 : Math.Max(.35, Math.Min(.98, Opacity));
            Pins = (Pins ?? new List<Pin>()).Where(p => p != null && !String.IsNullOrWhiteSpace(p.Name) && !String.IsNullOrWhiteSpace(p.Target)).Take(48).ToList();
        }
        internal static Preferences Defaults()
        {
            Preferences p = new Preferences();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            p.Pins.Add(new Pin { Name = "File Explorer", Target = Path.Combine(windows, "explorer.exe"), MatchPath = Path.Combine(windows, "explorer.exe"), IconPath = Path.Combine(windows, "explorer.exe") });
            string edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft\\Edge\\Application\\msedge.exe");
            if (File.Exists(edge)) { Pin browser = Pin.FromPath(edge); browser.Name = "Microsoft Edge"; p.Pins.Add(browser); }
            string code = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\Microsoft VS Code\\Code.exe");
            if (File.Exists(code)) { Pin editor = Pin.FromPath(code); editor.Name = "Visual Studio Code"; p.Pins.Add(editor); }
            string terminal = Path.Combine(windows, "System32\\WindowsPowerShell\\v1.0\\powershell.exe");
            if (File.Exists(terminal)) { Pin term = Pin.FromPath(terminal); term.Name = "PowerShell"; p.Pins.Add(term); }
            p.Pins.Add(new Pin { Name = "Settings", Target = "ms-settings:", MatchPath = Path.Combine(windows, "ImmersiveControlPanel\\SystemSettings.exe"), Glyph = "\uE713", Color = "#727D94" });
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads)) { Pin folder = Pin.FromPath(downloads); folder.Name = "Downloads"; p.Pins.Add(folder); }
            p.Pins.Add(new Pin { Name = "Recycle Bin", Target = "shell:RecycleBinFolder", Glyph = "\uE74D", Color = "#64748B", Utility = true });
            return p;
        }
    }
    internal static class SettingsStore
    {
        internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoolMaconWindows");
        internal static string FilePath { get { return Path.Combine(Folder, "settings.json"); } }
        internal static Preferences Load(string path)
        {
            try { if (File.Exists(path)) { Preferences p = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(path)); if (p != null) { p.Normalize(); return p; } } }
            catch (Exception error) { if (String.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) Log(error); }
            return Preferences.Defaults();
        }
        internal static void Save(Preferences preferences, string path)
        {
            preferences.Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(preferences));
                // Explorer and antivirus can briefly hold a settings file during an atomic replacement.
                for (int attempt = 0; ; attempt++)
                {
                    try { if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); break; }
                    catch (IOException) { if (attempt >= 11) throw; System.Threading.Thread.Sleep(50); }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static void Log(Exception error)
        {
            try { Directory.CreateDirectory(Folder); File.AppendAllText(Path.Combine(Folder, "dock.log"), DateTime.UtcNow.ToString("o") + " " + error.ToString() + Environment.NewLine); } catch { }
        }
    }
    internal sealed class RunningWindow { internal IntPtr Handle; internal string Title; internal string Path; }
    internal sealed class DockItem
    {
        internal Pin Pin; internal List<RunningWindow> Windows = new List<RunningWindow>();
        internal bool Pinned; internal ImageSource Icon;
        internal double Scale = 1, TargetScale = 1, Center;
        internal System.Windows.Controls.Border View;
        internal System.Windows.Controls.Border Indicator;
        internal System.Windows.FrameworkElement IconView;
        internal System.Windows.Media.ScaleTransform ScaleTransform;
        internal System.Windows.Media.TranslateTransform Lift;
    }
    internal static class DockMath
    {
        internal static double Scale(double distance, double radius, double maximum) { return 1 + (maximum - 1) * Math.Exp(-distance * distance / (2 * radius * radius)); }
        internal static double Ease(double current, double target, double seconds) { return current + (target - current) * (1 - Math.Exp(-seconds * 18)); }
        internal static bool SameApp(string first, string second) { return !String.IsNullOrWhiteSpace(first) && !String.IsNullOrWhiteSpace(second) && String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }
    }
}
