using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CoolMac
{
    internal static class TopBarVerification
    {
        private static void Check(bool condition, string text, StreamWriter report)
        {
            if (!condition) throw new InvalidOperationException(text); report.WriteLine("PASS " + text); report.Flush();
        }
        internal static void Preview(string directory, StreamWriter report)
        {
            DockController controller = new DockController(Preferences.Defaults(), true, false);
            controller.View.WindowStartupLocation = WindowStartupLocation.Manual; controller.View.Left = -20000; controller.View.Top = -20000;
            try
            {
                controller.View.Show(); controller.View.TopBarOnButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(controller.IsTopBarRunning && !controller.IsRunning && !controller.View.TopBarOnButton.IsEnabled && controller.View.TopBarOffButton.IsEnabled, "Top bar starts with the dock off and updates its independent controls", report);
                TopBarWindow bar = controller.TopBar; bar.UpdateLayout();
                Check(!bar.Reserved && bar.ActualHeight == TopBarWindow.BarHeight, "Preview top bar renders without reserving the real desktop", report);
                Verification.Snapshot(bar, Path.Combine(directory, "top-bar.png"));
                double? originalVolume = null; bool originalMute = false;
                try { using (AudioEndpoint audio = new AudioEndpoint()) { originalVolume = audio.Level; originalMute = audio.Muted; } } catch (System.Runtime.InteropServices.COMException) { }
                foreach (string card in new[] { "controls", "calendar", "sound" }) Verification.SnapshotElement(bar.PreviewCard(card), Path.Combine(directory, "top-bar-" + card + ".png"));
                if (originalVolume.HasValue) using (AudioEndpoint audio = new AudioEndpoint()) Check(Math.Abs(audio.Level - originalVolume.Value) < .0001 && audio.Muted == originalMute, "Opening and rendering the volume popup leaves Windows volume and mute unchanged", report);
                controller.View.UpdateLayout(); Verification.Snapshot(controller.View, Path.Combine(directory, "desktop-controls.png"));
                controller.View.Close(); Check(controller.IsTopBarRunning && !controller.View.IsVisible, "Closing controls keeps a standalone top bar running", report);
                controller.ShowControl(); controller.StartDock(); controller.StopDock();
                Check(controller.IsTopBarRunning && !controller.IsRunning, "Turning the dock off leaves the independent top bar running", report);
                controller.View.TopBarOffButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(!controller.IsTopBarRunning && controller.View.TopBarOnButton.IsEnabled, "Turn off top bar closes it and restores its controls", report);
            }
            finally { controller.StopTopBar(); controller.StopDock(); controller.Cleanup(); controller.View.Close(); }
        }
        private static bool Wait(Func<bool> condition, int milliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < milliseconds)
            {
                DispatcherFrame frame = new DispatcherFrame(); DispatcherTimer pulse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                pulse.Tick += delegate { pulse.Stop(); frame.Continue = false; }; pulse.Start(); Dispatcher.PushFrame(frame);
            }
            return condition();
        }
        internal static int Live(string directory)
        {
            Directory.CreateDirectory(directory);
            using (StreamWriter report = new StreamWriter(Path.Combine(directory, "top-bar-verification.txt")))
            {
                DockController controller = new DockController(Preferences.Defaults(), false, false);
                IntPtr monitor = Native.MonitorFromPoint(new Native.Point { X = Forms.Screen.PrimaryScreen.Bounds.Left + 10, Y = Forms.Screen.PrimaryScreen.Bounds.Top + 10 }, 2);
                Native.Rect before = Native.Monitor(monitor).Work; uint taskbarState = Native.TaskbarState(); IntPtr foreground = Native.GetForegroundWindow();
                try
                {
                    for (int cycle = 0; cycle < 3; cycle++)
                    {
                        controller.StartTopBar();
                        Check(controller.TopBar.Reserved && Wait(delegate { return Native.Monitor(monitor).Work.Top >= before.Top + 30; }, 3000), "Top bar reserves space above maximized windows (cycle " + (cycle + 1) + ")", report);
                        Check(Native.GetForegroundWindow() == foreground, "Showing the top bar preserves foreground app focus", report);
                        Native.Rect bar; Native.GetWindowRect(controller.TopBar.Handle, out bar);
                        Check(bar.Left == before.Left && bar.Right == before.Right && bar.Top == before.Top, "Top bar spans the selected monitor at the correct screen edge", report);
                        controller.TopBar.Tick();
                        Check(Native.TaskbarState() == taskbarState, "Independent top bar does not change Windows taskbar settings", report);
                        controller.StopTopBar();
                        Check(Wait(delegate { Native.Rect after = Native.Monitor(monitor).Work; return after.Top == before.Top && after.Bottom == before.Bottom; }, 3000), "Turning off top bar restores the original desktop work area", report);
                    }
                    try { using (AudioEndpoint audio = new AudioEndpoint()) Check(audio.Level >= 0 && audio.Level <= 1, "Volume control reads the current Windows playback device", report); }
                    catch (System.Runtime.InteropServices.COMException error) { report.WriteLine("INFO No playback device: " + error.Message); }
                    controller.StartTopBar(); controller.StartDock();
                    Check(Wait(delegate { Native.Rect work = Native.Monitor(monitor).Work; return work.Top >= before.Top + 30 && work.Bottom < before.Bottom; }, 3000), "Top bar and dock reserve both edges together", report);
                    controller.StopDock();
                    Check(controller.IsTopBarRunning && Wait(delegate { Native.Rect work = Native.Monitor(monitor).Work; return work.Top >= before.Top + 30 && work.Bottom == before.Bottom && Native.TaskbarState() == taskbarState; }, 4000), "Turning dock off restores the taskbar while keeping the top bar reservation", report);
                    controller.StopTopBar();
                    Check(Wait(delegate { return Native.Monitor(monitor).Work.Top == before.Top; }, 3000), "Stopping both bars restores the full original desktop work area", report);
                    string probePath = Path.Combine(directory, "top-bar-crash-probe.txt"); if (File.Exists(probePath)) File.Delete(probePath);
                    using (Process probe = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--topbar-crash-probe \"" + probePath + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                        Check(Wait(delegate { return probe.HasExited; }, 6000) && probe.ExitCode == 0 && File.Exists(probePath) && File.ReadAllText(probePath) == "reserved", "Crash probe registers the top bar before an abrupt process exit", report);
                    Check(Wait(delegate { return Native.Monitor(monitor).Work.Top == before.Top; }, 4000), "Windows releases the top bar reservation after a process crash", report);
                    report.WriteLine("All top bar integration checks passed."); return 0;
                }
                catch (Exception error) { report.WriteLine("FAIL " + error); return 1; }
                finally { controller.StopDock(); controller.StopTopBar(); controller.Cleanup(); controller.View.Close(); }
            }
        }
    }
}
