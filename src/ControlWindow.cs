using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CoolMac
{
    internal sealed class DockController
    {
        internal readonly Preferences Preferences;
        internal readonly ControlWindow View;
        private readonly bool preview, rememberSettings;
        private DockWindow dock;
        private FolderWindow folders;
        private bool exiting;
        internal bool IsRunning { get { return dock != null; } }
        internal bool Exiting { get { return exiting; } }
        internal bool HasOpenTools { get { return folders != null && folders.IsVisible; } }
        internal bool ExitWhenWindowCloses { get { return rememberSettings && !preview; } }
        internal DockController(Preferences preferences, bool previewMode, bool persist)
        {
            Preferences = preferences; preview = previewMode; rememberSettings = persist;
            View = new ControlWindow(this);
        }
        internal void StartDock()
        {
            if (dock != null || exiting) return;
            DockWindow next = new DockWindow(Preferences, preview);
            if (preview) { next.Left = -20000; next.Top = -20000; }
            next.OpenControl = ShowControl; next.QuitApplication = Exit; next.OpenFolderIcons = ShowFolders;
            dock = next;
            next.Closed += delegate
            {
                if (dock != next) return;
                dock = null; Preferences.DockEnabled = false; Save(); View.RefreshState();
                if (!exiting && !preview && !View.IsVisible) ShowControl();
            };
            try { next.Show(); Preferences.DockEnabled = true; Save(); View.RefreshState(); }
            catch { dock = null; next.Cleanup(); next.Close(); View.RefreshState(); throw; }
        }
        internal void StopDock()
        {
            DockWindow current = dock;
            if (current != null) current.Close();
            Preferences.DockEnabled = false; Save(); View.RefreshState();
        }
        internal void ShowControl()
        {
            if (exiting) return;
            View.RefreshState(); View.Show();
            if (View.WindowState == WindowState.Minimized) View.WindowState = WindowState.Normal;
            View.Activate();
        }
        internal void ShowSettings() { if (dock != null) dock.ShowSettings(); }
        internal void ShowFolders()
        {
            if (exiting) return;
            if (folders == null)
            {
                folders = new FolderWindow(FolderIcons.Default());
                folders.IconsChanged = delegate { if (dock != null) dock.RefreshFolderIcons(); };
                string example = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName, "Folder Preview");
                if (Directory.Exists(example)) folders.SelectFolder(example);
                folders.Closed += delegate { folders = null; if (!exiting && !View.IsVisible) ShowControl(); };
            }
            folders.Show(); if (folders.WindowState == WindowState.Minimized) folders.WindowState = WindowState.Normal; folders.Activate();
        }
        internal void Exit()
        {
            if (exiting) return; exiting = true; StopDock(); Application.Current.Shutdown();
        }
        internal void Cleanup() { exiting = true; if (dock != null) dock.Cleanup(); }
        private void Save()
        {
            if (!rememberSettings) return;
            try { SettingsStore.Save(Preferences, SettingsStore.FilePath); }
            catch (Exception error) { SettingsStore.Log(error); View.ShowSaveError(); }
        }
    }
    internal sealed class ControlWindow : Window
    {
        internal const string WindowTitle = "CoolMac Dock · Control";
        internal static readonly uint ShowMessage = Native.RegisterWindowMessage("CoolMaconWindows.Control.Show");
        internal static readonly uint OffMessage = Native.RegisterWindowMessage("CoolMaconWindows.Control.Off");
        internal static readonly uint ExitMessage = Native.RegisterWindowMessage("CoolMaconWindows.Control.Exit");
        internal static readonly uint FolderMessage = Native.RegisterWindowMessage("CoolMaconWindows.Control.Folders");
        private readonly DockController controller;
        private readonly TextBlock state = new TextBlock();
        private readonly TextBlock detail = new TextBlock();
        private readonly Border dot = new Border();
        private readonly TextBlock closing = new TextBlock();
        private readonly TextBlock error = new TextBlock();
        internal readonly Button OnButton, OffButton;
        private readonly Button settings;
        internal ControlWindow(DockController owner)
        {
            controller = owner; Title = WindowTitle; Width = 530; Height = 560;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = DockWindow.Brush("#151A24"); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");
            ImageSource icon = Native.Icon(Process.GetCurrentProcess().MainModule.FileName); if (icon != null) Icon = icon;
            StackPanel body = new StackPanel { Margin = new Thickness(30, 26, 30, 24) };
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            body.Children.Add(SettingsWindow.Text("COOLMAC / WINDOWS DOCK", 11, "#8EA6CF", new Thickness(0, 0, 0, 10)));
            body.Children.Add(SettingsWindow.Text("Dock controls", 30, "#F1F4FA", new Thickness(0, 0, 0, 5)));
            body.Children.Add(SettingsWindow.Text("Control your dock and return to the Windows taskbar.", 13, "#99A6BA", new Thickness(0, 0, 0, 24)));
            Border card = new Border { CornerRadius = new CornerRadius(16), Background = DockWindow.Brush("#202A3A"), BorderBrush = DockWindow.Brush("#33445C"), BorderThickness = new Thickness(1), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 18) };
            StackPanel cardBody = new StackPanel(); card.Child = cardBody;
            StackPanel heading = new StackPanel { Orientation = Orientation.Horizontal };
            dot.Width = 9; dot.Height = 9; dot.CornerRadius = new CornerRadius(5); dot.Margin = new Thickness(0, 0, 9, 0); dot.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(dot); state.FontSize = 17; state.FontWeight = FontWeights.SemiBold; state.Foreground = Brushes.White; heading.Children.Add(state); cardBody.Children.Add(heading);
            detail.FontSize = 12; detail.Foreground = DockWindow.Brush("#B3C2D7"); detail.Margin = new Thickness(0, 8, 0, 16); detail.TextWrapping = TextWrapping.Wrap; cardBody.Children.Add(detail);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal };
            OnButton = SettingsWindow.ActionButton("Turn on dock", controller.StartDock, true); OnButton.Width = 185; OnButton.Height = 42;
            OffButton = SettingsWindow.ActionButton("Turn off dock", controller.StopDock, false); OffButton.Width = 185; OffButton.Height = 42;
            buttons.Children.Add(OnButton); buttons.Children.Add(OffButton); cardBody.Children.Add(buttons); body.Children.Add(card);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            settings = SettingsWindow.ActionButton("Dock settings", controller.ShowSettings, false); actions.Children.Add(settings);
            actions.Children.Add(SettingsWindow.ActionButton("Folder icons", controller.ShowFolders, false));
            actions.Children.Add(SettingsWindow.ActionButton("Open EXE folder", delegate { DockWindow.Launch(AppDomain.CurrentDomain.BaseDirectory); }, false)); body.Children.Add(actions);
            closing.FontSize = 12; closing.Foreground = DockWindow.Brush("#99A6BA"); closing.TextWrapping = TextWrapping.Wrap; closing.Margin = new Thickness(0, 19, 0, 13); body.Children.Add(closing);
            error.FontSize = 11; error.Foreground = DockWindow.Brush("#F4B1A4"); error.Visibility = Visibility.Collapsed; error.Margin = new Thickness(0, 0, 0, 8); error.TextWrapping = TextWrapping.Wrap; body.Children.Add(error);
            Button exit = SettingsWindow.ActionButton("Exit app", controller.Exit, false); exit.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(exit);
            SourceInitialized += delegate
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr window, int message, IntPtr wp, IntPtr lp, ref bool handled)
                {
                    if ((uint)message == ShowMessage) { controller.ShowControl(); handled = true; }
                    else if ((uint)message == OffMessage) { controller.StopDock(); controller.ShowControl(); handled = true; }
                    else if ((uint)message == ExitMessage) { controller.Exit(); handled = true; }
                    else if ((uint)message == FolderMessage) { controller.ShowFolders(); handled = true; }
                    return IntPtr.Zero;
                });
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs args)
            {
                if (controller.Exiting) return;
                if (controller.IsRunning || controller.HasOpenTools) { args.Cancel = true; Hide(); }
            };
            Closed += delegate { if (controller.ExitWhenWindowCloses && !controller.Exiting) controller.Exit(); };
            RefreshState();
        }
        internal void RefreshState()
        {
            bool running = controller.IsRunning;
            state.Text = running ? "Dock is on" : "Dock is off";
            dot.Background = DockWindow.Brush(running ? "#62D8A7" : "#8293AA");
            detail.Text = running ? "Your dock is running. Turn it off to restore the Windows taskbar." : "The Windows taskbar is available. Turn on the dock whenever you want.";
            OnButton.IsEnabled = !running; OnButton.Opacity = running ? .35 : 1;
            OffButton.IsEnabled = running; OffButton.Opacity = running ? 1 : .35;
            settings.IsEnabled = running; settings.Opacity = running ? 1 : .4;
            closing.Text = running ? "Closing this window keeps the dock running. Open the EXE again to return here." : "Your off setting is remembered. Close this window to exit the app.";
        }
        internal void ShowSaveError() { error.Text = "The dock changed state, but the preference could not be saved."; error.Visibility = Visibility.Visible; }
        internal static bool Signal(uint message)
        {
            IntPtr handle = IntPtr.Zero;
            for (int attempt = 0; attempt < 25 && handle == IntPtr.Zero; attempt++)
            {
                handle = Native.FindWindow(null, WindowTitle); if (handle == IntPtr.Zero) Thread.Sleep(80);
            }
            if (handle == IntPtr.Zero) return false;
            uint process; Native.GetWindowThreadProcessId(handle, out process); Native.AllowSetForegroundWindow(process);
            return Native.PostMessage(handle, message, IntPtr.Zero, IntPtr.Zero);
        }
        internal static void StopLegacyDock()
        {
            IntPtr handle = Native.FindWindow(null, "CoolMac Dock"); if (handle == IntPtr.Zero) return;
            uint process; Native.GetWindowThreadProcessId(handle, out process); string path = Native.ProcessPath(process);
            if (String.Equals(Path.GetDirectoryName(path), AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && String.Equals(Path.GetFileName(path), "CoolMacDock.exe", StringComparison.OrdinalIgnoreCase))
                Native.PostMessage(handle, 0x10, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
