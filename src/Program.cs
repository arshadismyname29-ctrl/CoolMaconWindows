using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CoolMac
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--global-folder-helper") return GlobalFolderTheme.Helper(args);
            if (args.Length > 0 && args[0] == "--apply-global-folders")
            {
                FolderThemeResponse response = GlobalFolderTheme.RunElevated(GlobalFolderTheme.Read() == null ? "verify-apply" : "apply", "blue");
                if (!response.Success) return 1;
                GlobalFolderTheme.ApplyCommonFolders("blue"); GlobalFolderTheme.Refresh(); GlobalFolderTheme.RestartExplorer(); return 0;
            }
            if (args.Length > 0 && args[0] == "--folder-icon-probe")
            {
                BitmapSource image = Native.Icon(args[1]);
                if (image == null) return 2;
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (FileStream file = File.Create(args[2])) encoder.Save(file); return 0;
            }
            if (args.Length > 0 && args[0] == "--watchdog")
            {
                try { TaskbarLease.Watch(args); return 0; } catch (Exception error) { SettingsStore.Log(error); return 2; }
            }
            if (args.Length > 0 && args[0] == "--recovery-probe")
            {
                // Deliberately exit without Dispose: exercise the independent recovery process.
                TaskbarLease lease = new TaskbarLease(); lease.Acquire(IntPtr.Zero);
                File.WriteAllText(args[1], "hidden=" + Native.Taskbars().All(bar => !Native.IsWindowVisible(bar)));
                Environment.Exit(0); return 0;
            }
            if (args.Length > 0 && args[0] == "--verify") return Verification.Run(args[1], args.Contains("--live"));
            if (args.Length > 0 && args[0] == "--make-folder-demo")
            {
                FolderIcons.CreateDemo(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName); return 0;
            }
            if (args.Length > 0 && (args[0] == "--off" || args[0] == "--quit"))
            {
                if (!ControlWindow.Signal(args[0] == "--off" ? ControlWindow.OffMessage : ControlWindow.ExitMessage)) ControlWindow.StopLegacyDock();
                return 0;
            }
            bool created;
            using (Mutex singleton = new Mutex(true, "Local\\CoolMaconWindows.Dock", out created))
            {
                if (!created) { if (!ControlWindow.Signal(args.Contains("--folders") ? ControlWindow.FolderMessage : args.Contains("--paused") ? ControlWindow.OffMessage : ControlWindow.ShowMessage)) MessageBox.Show("The dock is starting. Open the app again in a moment.", "CoolMac Dock"); return 0; }
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                DockController controller = new DockController(SettingsStore.Load(SettingsStore.FilePath), false, true);
                if (args.Contains("--paused")) controller.Preferences.DockEnabled = false;
                if (args.Contains("--resume")) controller.Preferences.DockEnabled = true;
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs error)
                {
                    SettingsStore.Log(error.Exception); controller.Cleanup();
                    MessageBox.Show("The dock stopped and restored your taskbar. Details are in the dock log.", "CoolMac Dock"); error.Handled = true; app.Shutdown(1);
                };
                app.SessionEnding += delegate { controller.Cleanup(); };
                app.Exit += delegate { controller.Cleanup(); };
                try
                {
                    controller.ShowControl();
                    if (controller.Preferences.DockEnabled) controller.StartDock(); else controller.StopDock();
                    if (args.Contains("--folders")) controller.ShowFolders();
                    app.Run();
                }
                finally { controller.Cleanup(); singleton.ReleaseMutex(); }
            }
            return 0;
        }
    }
    internal static class Verification
    {
        private static void Check(bool condition, string description, StreamWriter report)
        {
            if (!condition) throw new InvalidOperationException(description); report.WriteLine("PASS " + description); report.Flush();
        }
        internal static int Run(string directory, bool live)
        {
            Directory.CreateDirectory(directory);
            using (StreamWriter report = new StreamWriter(Path.Combine(directory, "verification.txt")))
            {
                try
                {
                    Check(Math.Abs(DockMath.Scale(0, 55, 1.6) - 1.6) < .0001 && DockMath.Scale(1000, 55, 1.6) < 1.001, "Magnification peaks at the cursor and settles at distant icons", report);
                    Check(DockMath.Ease(1, 2, .016) > 1 && DockMath.Ease(1, 2, .016) < 2, "Animation moves toward its target without overshooting", report);
                    Check(!DockMath.SameApp("", "") && DockMath.SameApp("C:\\Windows\\explorer.exe", "c:\\windows\\EXPLORER.EXE"), "Running apps match paths case-insensitively without grouping empty targets", report);
                    Preferences saved = Preferences.Defaults(); saved.IconSize = 999; saved.Magnification = 99;
                    string settingsFile = Path.Combine(directory, "test-settings.json");
                    SettingsStore.Save(saved, settingsFile); Preferences loaded = SettingsStore.Load(settingsFile);
                    Check(loaded.IconSize == 64 && loaded.Magnification == 1.9 && loaded.Pins.Count == saved.Pins.Count, "Settings round-trip and invalid dimensions are clamped", report);
                    loaded.IconSize = 40; SettingsStore.Save(loaded, settingsFile);
                    Check(SettingsStore.Load(settingsFile).IconSize == 40, "Existing settings are replaced atomically", report);
                    loaded.DockEnabled = false; SettingsStore.Save(loaded, settingsFile);
                    Check(!SettingsStore.Load(settingsFile).DockEnabled, "Turned-off preference survives reopening the app", report);
                    File.WriteAllText(settingsFile, "invalid JSON"); Check(SettingsStore.Load(settingsFile).Pins.Count >= 4, "Corrupt settings recover to usable default pins", report); File.Delete(settingsFile);
                    string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    BitmapSource icon = Native.Icon(explorer);
                    Check(icon != null && icon.PixelWidth >= 32, "Windows shell returns a usable native Explorer icon", report);
                    Check(Pin.FromPath("Z:\\coolmac-missing-file.exe") == null, "Missing dropped paths are rejected", report);
                    Check(Pin.FromPath(directory).Utility, "Folder pins are separated from running applications", report);
                    report.WriteLine("INFO Visible app windows: " + Native.Windows().Count);
                    if (live) VerifyRecovery(directory, report);
                    Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    Preferences preview = Preferences.Defaults(); preview.HideTaskbar = false;
                    DockWindow dock = new DockWindow(preview, true) { Left = -20000, Top = -20000, Width = 1050 };
                    dock.Show(); dock.UpdateLayout();
                    Check(dock.ItemCount >= 5, "Native WPF dock initializes with app icons and settings access", report);
                    Snapshot(dock, Path.Combine(directory, "dock.png"));
                    dock.PreviewHover(1); dock.UpdateLayout(); Snapshot(dock, Path.Combine(directory, "dock-hover.png"));
                    SettingsWindow settings = new SettingsWindow(dock) { Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
                    settings.Show(); settings.UpdateLayout(); Snapshot(settings, Path.Combine(directory, "settings.png"));
                    settings.Close(); dock.Close();
                    VerifyControls(directory, report);
                    FolderVerification.Run(directory, report);
                    if (live) VerifyDesktop(directory, report);
                    app.Shutdown();
                    report.WriteLine("PASS Dock, magnification, and settings render to PNG previews");
                    report.WriteLine("All verification checks passed."); return 0;
                }
                catch (Exception error) { report.WriteLine("FAIL " + error.ToString()); return 1; }
            }
        }
        private static string VisibilitySignature() { return String.Join(",", Native.TaskbarVisibility().Split(',').OrderBy(entry => entry)); }
        private static bool WaitFor(Func<bool> condition, int milliseconds)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (!condition() && wait.ElapsedMilliseconds < milliseconds)
            {
                if (Application.Current == null) { Thread.Sleep(40); continue; }
                DispatcherFrame frame = new DispatcherFrame();
                DispatcherTimer pulse = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
                pulse.Tick += delegate { pulse.Stop(); frame.Continue = false; }; pulse.Start(); Dispatcher.PushFrame(frame);
            }
            return condition();
        }
        private static bool TaskbarRestored(uint state, string visibility)
        {
            return Native.TaskbarState() == state && VisibilitySignature() == visibility && ((state & 1) != 0 || Native.TaskbarsOnScreen());
        }
        private static void VerifyControls(string directory, StreamWriter report)
        {
            DockController controller = new DockController(Preferences.Defaults(), true, false);
            controller.View.WindowStartupLocation = WindowStartupLocation.Manual; controller.View.Left = -20000; controller.View.Top = -20000; controller.View.Show(); controller.View.UpdateLayout();
            Check(!controller.IsRunning && controller.View.OnButton.IsEnabled && !controller.View.OffButton.IsEnabled, "Control window shows an off dock with only Turn on enabled", report);
            Snapshot(controller.View, Path.Combine(directory, "control-off.png"));
            controller.View.OnButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); controller.View.UpdateLayout();
            Check(controller.IsRunning && !controller.View.OnButton.IsEnabled && controller.View.OffButton.IsEnabled, "Turn on button starts a dock and updates the status", report);
            Snapshot(controller.View, Path.Combine(directory, "control-on.png"));
            controller.View.Close();
            Check(controller.IsRunning && !controller.View.IsVisible, "Closing controls while the dock is on hides the window and keeps the dock running", report);
            controller.ShowControl();
            Check(controller.View.IsVisible && controller.IsRunning, "Hidden controls can be reopened without starting another dock", report);
            controller.View.OffButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(!controller.IsRunning && controller.View.IsVisible, "Turn off button stops the dock while keeping its control window available", report);
            controller.StartDock(); controller.StopDock();
            Check(!controller.IsRunning && controller.View.OnButton.IsEnabled, "Dock can be turned on and off repeatedly in the same process", report);
            controller.View.Close();
        }
        private static void VerifyRecovery(string directory, StreamWriter report)
        {
            Check(Native.Taskbars().Count > 0, "Interactive Windows taskbar is available for integration checks", report);
            uint state = Native.TaskbarState(); string visibility = Native.TaskbarVisibility(), signature = VisibilitySignature();
            string probePath = Path.Combine(directory, "recovery-probe.txt");
            try
            {
                using (Process probe = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--recovery-probe \"" + probePath + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                {
                    Check(probe.WaitForExit(7000) && probe.ExitCode == 0, "Taskbar recovery helper starts before the parent can hide the taskbar", report);
                }
                Check(File.Exists(probePath) && File.ReadAllText(probePath) == "hidden=True", "Probe actually hides the native taskbar before exiting abruptly", report);
                Check(WaitFor(delegate { return TaskbarRestored(state, signature); }, 4000), "Independent helper restores taskbar state, visibility, and on-screen position after an abrupt parent exit", report);
            }
            finally { Native.RestoreTaskbars(state, visibility); }
        }
        private static void VerifyDesktop(string directory, StreamWriter report)
        {
            uint state = Native.TaskbarState(); string visibility = Native.TaskbarVisibility(), signature = VisibilitySignature();
            IntPtr monitor = Native.MonitorFromPoint(new Native.Point { X = 10, Y = 10 }, 2);
            Native.MonitorInfo before = Native.Monitor(monitor);
            DockController controller = new DockController(Preferences.Defaults(), false, false);
            controller.View.WindowStartupLocation = WindowStartupLocation.Manual; controller.View.Left = -20000; controller.View.Top = -20000; controller.View.Show();
            try
            {
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    controller.View.OnButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Check(controller.IsRunning && Native.Taskbars().All(bar => !Native.IsWindowVisible(bar)), "Turn on hides the real Windows taskbar (cycle " + (cycle + 1) + ")", report);
                    Check((Native.TaskbarState() & 1) != 0, "Dock enables Windows auto-hide using the actual taskbar window handle", report);
                    Native.MonitorInfo during = Native.Monitor(monitor);
                    Check(during.Monitor.Bottom - during.Work.Bottom >= 48, "Running dock reserves space for maximized windows", report);
                    controller.View.OffButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Check(!controller.IsRunning && controller.View.IsVisible && WaitFor(delegate { return TaskbarRestored(state, signature); }, 4000), "Turn off restores the taskbar's state and physical screen position (cycle " + (cycle + 1) + ")", report);
                    Stopwatch settled = Stopwatch.StartNew(); bool stayedRestored = true;
                    WaitFor(delegate { if (!TaskbarRestored(state, signature)) stayedRestored = false; return settled.ElapsedMilliseconds >= 1000; }, 1500);
                    Check(stayedRestored, "Taskbar stays restored after delayed Explorer layout updates", report);
                    Native.MonitorInfo after = Native.Monitor(monitor);
                    Check(after.Work.Bottom == before.Work.Bottom, "Turn off releases the dock's reserved desktop space", report);
                }
                if ((state & 1) == 0)
                {
                    foreach (IntPtr bar in Native.Taskbars()) Native.ShowWindow(bar, 0);
                    using (TaskbarLease stale = new TaskbarLease()) stale.Acquire(IntPtr.Zero);
                    Check(WaitFor(delegate { return Native.TaskbarsOnScreen() && Native.TaskbarState() == state; }, 4000), "Turning off repairs a stale hidden-window snapshot instead of hiding the taskbar again", report);
                }
            }
            finally { controller.StopDock(); controller.Cleanup(); controller.View.Close(); Native.RestoreTaskbars(state, visibility); }
        }
        internal static void Snapshot(Window window, string path)
        {
            FrameworkElement content = window.Content as FrameworkElement;
            int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
            RenderTargetBitmap render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            RenderTargetBitmap contentImage = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); contentImage.Render(content);
            DrawingVisual frame = new DrawingVisual();
            using (DrawingContext drawing = frame.RenderOpen())
            {
                drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(31, 45, 70), Color.FromRgb(14, 21, 35), 30), null, new Rect(0, 0, width, height));
                drawing.DrawImage(contentImage, new Rect(0, 0, width, height));
            }
            render.Render(frame); PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(render));
            using (FileStream output = File.Create(path)) png.Save(output);
        }
    }
}
