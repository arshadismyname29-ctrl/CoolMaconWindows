using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoolMac
{
    internal static class Native
    {
        internal delegate bool EnumProc(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
        {
            public int Size; public Rect Monitor; public Rect Work; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct FileInfo
        {
            public IntPtr Icon; public int IconIndex; public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct AppBarData
        {
            public uint Size; public IntPtr Window; public uint Callback; public uint Edge; public Rect Rectangle; public IntPtr Parameter;
        }
        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr image, IntPtr mask, out int index);
            [PreserveSig] int ReplaceIcon(int index, IntPtr icon, out int result);
            [PreserveSig] int SetOverlayImage(int image, int overlay);
            [PreserveSig] int Replace(int index, IntPtr image, IntPtr mask);
            [PreserveSig] int AddMasked(IntPtr image, int color, out int index);
            [PreserveSig] int Draw(IntPtr parameters);
            [PreserveSig] int Remove(int index);
            [PreserveSig] int GetIcon(int index, uint flags, out IntPtr icon);
        }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out FileInfo info, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "#727")] private static extern int SHGetImageList(int list, ref Guid iid, out IImageList images);
        [DllImport("shell32.dll")] internal static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(uint process);

        internal static MonitorInfo Monitor(IntPtr monitor)
        {
            MonitorInfo info = new MonitorInfo(); info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            GetMonitorInfo(monitor, ref info); return info;
        }
        internal static string ClassName(IntPtr window)
        {
            StringBuilder value = new StringBuilder(256); GetClassName(window, value, value.Capacity); return value.ToString();
        }
        internal static string ProcessPath(uint id)
        {
            IntPtr handle = OpenProcess(0x1000, false, id); if (handle == IntPtr.Zero) return "";
            try { uint length = 32768; StringBuilder name = new StringBuilder((int)length); return QueryFullProcessImageName(handle, 0, name, ref length) ? name.ToString() : ""; }
            finally { CloseHandle(handle); }
        }
        internal static List<RunningWindow> Windows()
        {
            List<RunningWindow> windows = new List<RunningWindow>();
            int ownId = Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (!IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
                long style = GetWindowLongPtr(window, -20).ToInt64(); if ((style & 0x80) != 0) return true;
                string cls = ClassName(window);
                if (cls == "Progman" || cls == "WorkerW" || cls.Contains("TrayWnd") || cls == "Windows.UI.Core.CoreWindow") return true;
                StringBuilder title = new StringBuilder(1024); GetWindowText(window, title, title.Capacity); if (title.Length == 0) return true;
                uint id; GetWindowThreadProcessId(window, out id); if (id == ownId) return true;
                string path = ProcessPath(id); if (path.Length == 0) return true;
                windows.Add(new RunningWindow { Handle = window, Title = title.ToString(), Path = path }); return true;
            }, IntPtr.Zero);
            return windows;
        }
        internal static BitmapSource Icon(string path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            IntPtr icon = IntPtr.Zero; IImageList list = null;
            try
            {
                FileInfo info;
                if (SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf(typeof(FileInfo)), 0x4000) != IntPtr.Zero)
                {
                    Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                    if (SHGetImageList(4, ref iid, out list) == 0) list.GetIcon(info.IconIndex, 1, out icon);
                }
                if (icon == IntPtr.Zero)
                {
                    SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf(typeof(FileInfo)), 0x100); icon = info.Icon;
                }
                if (icon == IntPtr.Zero) return null;
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                // Older shortcuts can have a 32px image in the corner of the jumbo canvas.
                FormatConvertedBitmap pixels = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int width = pixels.PixelWidth, height = pixels.PixelHeight;
                byte[] bytes = new byte[width * height * 4]; pixels.CopyPixels(bytes, width * 4, 0);
                int left = width, top = height, right = -1, bottom = -1;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    if (bytes[(y * width + x) * 4 + 3] > 10) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                if (right < left) return null;
                int pad = Math.Max(2, Math.Max(right - left, bottom - top) / 25);
                left = Math.Max(0, left - pad); top = Math.Max(0, top - pad); right = Math.Min(width - 1, right + pad); bottom = Math.Min(height - 1, bottom + pad);
                BitmapSource cropped = new CroppedBitmap(source, new Int32Rect(left, top, right - left + 1, bottom - top + 1)); cropped.Freeze(); return cropped;
            }
            catch { return null; }
            finally { if (icon != IntPtr.Zero) DestroyIcon(icon); if (list != null) Marshal.ReleaseComObject(list); }
        }
        internal static List<IntPtr> Taskbars()
        {
            List<IntPtr> result = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr unused) { string cls = ClassName(window); if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") result.Add(window); return true; }, IntPtr.Zero);
            return result;
        }
        internal static uint TaskbarState()
        {
            AppBarData data = new AppBarData(); data.Size = (uint)Marshal.SizeOf(typeof(AppBarData)); return (uint)SHAppBarMessage(4, ref data).ToUInt64();
        }
        internal static void SetTaskbarState(uint state)
        {
            IntPtr primary = FindWindow("Shell_TrayWnd", null); if (primary == IntPtr.Zero) return;
            AppBarData data = new AppBarData { Size = (uint)Marshal.SizeOf(typeof(AppBarData)), Window = primary, Parameter = new IntPtr(state) };
            SHAppBarMessage(10, ref data);
        }
        internal static void RestoreTaskbars(uint state)
        {
            SetTaskbarState(state); foreach (IntPtr bar in Taskbars()) ShowWindow(bar, 8);
        }
        internal static string TaskbarVisibility()
        {
            List<IntPtr> bars = Taskbars();
            return String.Join(",", bars.ConvertAll(bar => bar.ToInt64() + ":" + (IsWindowVisible(bar) ? "1" : "0")));
        }
        internal static void RestoreTaskbars(uint state, string visibility)
        {
            SetTaskbarState(state);
            // A stale SW_HIDE snapshot from a prior failed run must not hide the taskbar again.
            // Windows controls preexisting auto-hide through its state; taskbar windows are always shown.
            foreach (IntPtr bar in Taskbars()) ShowWindow(bar, 8);
        }
        internal static bool TaskbarsOnScreen()
        {
            List<IntPtr> bars = Taskbars(); if (bars.Count == 0) return false;
            foreach (IntPtr bar in bars)
            {
                Rect rectangle; if (!IsWindowVisible(bar) || !GetWindowRect(bar, out rectangle)) return false;
                Rect screen = Monitor(MonitorFromWindow(bar, 2)).Monitor;
                int width = rectangle.Right - rectangle.Left, height = rectangle.Bottom - rectangle.Top;
                int visibleWidth = Math.Max(0, Math.Min(rectangle.Right, screen.Right) - Math.Max(rectangle.Left, screen.Left));
                int visibleHeight = Math.Max(0, Math.Min(rectangle.Bottom, screen.Bottom) - Math.Max(rectangle.Top, screen.Top));
                if (width <= 0 || height <= 0 || (long)visibleWidth * visibleHeight < (long)width * height * .8) return false;
            }
            return true;
        }
        internal static void RemoveAppBar(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            AppBarData data = new AppBarData { Size = (uint)Marshal.SizeOf(typeof(AppBarData)), Window = handle }; SHAppBarMessage(1, ref data);
        }
        internal static bool Fullscreen(IntPtr monitor)
        {
            IntPtr foreground = GetForegroundWindow(); if (foreground == IntPtr.Zero || MonitorFromWindow(foreground, 2) != monitor) return false;
            string cls = ClassName(foreground); if (cls == "Progman" || cls == "WorkerW" || cls.Contains("TrayWnd")) return false;
            Rect rectangle; if (!GetWindowRect(foreground, out rectangle)) return false;
            Rect screen = Monitor(monitor).Monitor;
            return rectangle.Left <= screen.Left && rectangle.Top <= screen.Top && rectangle.Right >= screen.Right && rectangle.Bottom >= screen.Bottom;
        }
    }

    internal sealed class TaskbarLease : IDisposable
    {
        private uint originalState; private string originalVisibility; private EventWaitHandle stop; private Process watcher; private bool active;
        internal bool Active { get { return active; } }
        internal void Acquire(IntPtr reservationWindow)
        {
            if (active) return;
            originalState = Native.TaskbarState();
            originalVisibility = Native.TaskbarVisibility();
            string eventName = "Local\\CoolMacDock.Recovery." + Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N");
            stop = new EventWaitHandle(false, EventResetMode.ManualReset, eventName); stop.Reset();
            try
            {
                using (EventWaitHandle ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".Ready"))
                {
                    ready.Reset();
                    watcher = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                        "--watchdog " + Process.GetCurrentProcess().Id + " " + originalState + " " + eventName + " " + reservationWindow.ToInt64() + " \"" + originalVisibility + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    if (watcher == null || !ready.WaitOne(4000) || watcher.HasExited) throw new InvalidOperationException("The taskbar recovery helper could not start.");
                }
                active = true; Native.SetTaskbarState(originalState | 1); Hide();
            }
            catch { if (stop != null) { stop.Set(); stop.Dispose(); stop = null; } throw; }
        }
        internal void Hide() { if (active) foreach (IntPtr bar in Native.Taskbars()) Native.ShowWindow(bar, 0); }
        public void Dispose()
        {
            if (!active) return;
            Native.RestoreTaskbars(originalState, originalVisibility); active = false;
            if (stop != null) { stop.Set(); stop.Dispose(); stop = null; }
            if (watcher != null) { watcher.Dispose(); watcher = null; }
        }
        internal static void Watch(string[] arguments)
        {
            uint state = UInt32.Parse(arguments[2]);
            using (EventWaitHandle signal = EventWaitHandle.OpenExisting(arguments[3]))
            {
                using (EventWaitHandle ready = EventWaitHandle.OpenExisting(arguments[3] + ".Ready")) ready.Set();
                try
                {
                    using (Process parent = Process.GetProcessById(Int32.Parse(arguments[1])))
                        while (!parent.WaitForExit(250)) if (signal.WaitOne(0)) return;
                }
                catch (ArgumentException) { }
                if (!signal.WaitOne(0))
                {
                    if (arguments.Length > 4) Native.RemoveAppBar(new IntPtr(Int64.Parse(arguments[4])));
                    if (arguments.Length > 5) Native.RestoreTaskbars(state, arguments[5]); else Native.RestoreTaskbars(state);
                }
            }
        }
    }
    internal sealed class DesktopReservation : IDisposable
    {
        private Window window; private IntPtr handle; private bool registered, updating;
        private System.Windows.Forms.Screen screen; private int height; private Native.Rect last;
        internal IntPtr EnsureWindow()
        {
            if (window != null) return handle;
            window = new Window { Width = 1, Height = 1, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false, Opacity = 0, Left = -10000, Top = -10000 };
            window.Show(); handle = new WindowInteropHelper(window).Handle;
            Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080));
            HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
            {
                if (message == 0x8002 && wp.ToInt32() == 1 && registered && !updating) Place(); return IntPtr.Zero;
            });
            return handle;
        }
        internal void Configure(System.Windows.Forms.Screen target, int pixels)
        {
            screen = target; height = pixels; EnsureWindow();
            if (!registered)
            {
                Native.AppBarData data = new Native.AppBarData { Size = (uint)Marshal.SizeOf(typeof(Native.AppBarData)), Window = handle, Callback = 0x8002 };
                registered = Native.SHAppBarMessage(0, ref data).ToUInt64() != 0;
                if (!registered) return;
            }
            Place();
        }
        private void Place()
        {
            if (!registered || updating || screen == null) return; updating = true;
            try
            {
                System.Drawing.Rectangle bounds = screen.Bounds;
                Native.AppBarData data = new Native.AppBarData { Size = (uint)Marshal.SizeOf(typeof(Native.AppBarData)), Window = handle, Edge = 3,
                    Rectangle = new Native.Rect { Left = bounds.Left, Top = bounds.Bottom - height, Right = bounds.Right, Bottom = bounds.Bottom } };
                Native.SHAppBarMessage(2, ref data); data.Rectangle.Top = data.Rectangle.Bottom - height;
                Native.Rect next = data.Rectangle;
                if (next.Left == last.Left && next.Top == last.Top && next.Right == last.Right && next.Bottom == last.Bottom) return;
                last = next; Native.SHAppBarMessage(3, ref data);
                Native.SetWindowPos(handle, IntPtr.Zero, data.Rectangle.Left, data.Rectangle.Top, data.Rectangle.Right - data.Rectangle.Left, data.Rectangle.Bottom - data.Rectangle.Top, 0x0014);
            }
            finally { updating = false; }
        }
        internal void Release() { if (registered) { registered = false; Native.RemoveAppBar(handle); last = new Native.Rect(); } }
        public void Dispose() { Release(); if (window != null) { window.Close(); window = null; handle = IntPtr.Zero; } }
    }
}
