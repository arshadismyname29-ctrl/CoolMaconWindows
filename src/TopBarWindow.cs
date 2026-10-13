using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Calendar = System.Windows.Controls.Calendar;

namespace CoolMac
{
    internal sealed class TopBarWindow : Window
    {
        internal const string WindowTitle = "CoolMac · Top bar";
        internal const double BarHeight = 32;
        private const uint Callback = 0x8003;
        private static readonly uint ExplorerCreated = Native.RegisterWindowMessage("TaskbarCreated");
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        private readonly DockController controller;
        private readonly bool preview;
        private readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background);
        private readonly Dictionary<string, string> appNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Button activeApp, clock, network, sound, battery, controls;
        private readonly List<Popup> popups = new List<Popup>();
        private IntPtr handle, appWindow;
        private bool registered, placing, disposed, fullscreen;
        private Native.Rect last;
        private string currentApp = "Desktop", currentTitle = "Desktop";
        private Popup soundPopup;
        private Slider volumeSlider;
        private Button muteButton;
        private TextBlock volumeLabel;
        private bool updatingVolume;
        internal bool Reserved { get { return registered; } }
        internal IntPtr Handle { get { return handle; } }
        internal string ActiveAppName { get { return currentApp; } }
        internal IntPtr ActiveAppWindow { get { return appWindow; } }

        internal TopBarWindow(DockController owner, bool previewMode)
        {
            controller = owner; preview = previewMode;
            Title = WindowTitle; Width = 1200; Height = BarHeight;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
            ShowActivated = false; ShowInTaskbar = false; Topmost = !preview; Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12; Foreground = DockWindow.Brush("#192534");
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            Border surface = new Border { Background = new LinearGradientBrush(Color.FromArgb(239, 246, 249, 253), Color.FromArgb(229, 224, 232, 241), 90), BorderBrush = DockWindow.Brush("#24000000"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 0, 12, 0) };
            Grid layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 8 }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            surface.Child = layout; Content = surface;
            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal }; layout.Children.Add(left);
            Button brand = BarButton("C", "CoolMac menu", false); brand.FontWeight = FontWeights.Bold; brand.FontSize = 17; brand.Width = 35; left.Children.Add(brand);
            brand.Click += delegate { OpenMenu(brand, BrandMenu()); };
            activeApp = BarButton("Desktop", "Current app and window controls", false); activeApp.FontWeight = FontWeights.SemiBold; activeApp.MaxWidth = 210; left.Children.Add(activeApp);
            activeApp.Click += delegate { OpenMenu(activeApp, AppMenu()); };
            Button go = BarButton("Go", "Open familiar Windows folders", false); left.Children.Add(go); go.Click += delegate { OpenMenu(go, GoMenu()); };
            Button windows = BarButton("Window", "Switch between your open windows", false); left.Children.Add(windows); windows.Click += delegate { OpenMenu(windows, WindowsMenu()); };
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(right, 2); layout.Children.Add(right);
            battery = BarButton("\uE83F", "Battery", true); battery.Visibility = Visibility.Collapsed; right.Children.Add(battery); battery.Click += delegate { Launch("ms-settings:batterysaver"); };
            network = BarButton("\uE701", "Network settings", true); right.Children.Add(network); network.Click += delegate { Launch("ms-settings:network-status"); };
            sound = BarButton("\uE767", "Volume", true); right.Children.Add(sound); sound.Click += delegate { OpenSound(sound); };
            Button search = BarButton("\uE721", "Windows search", true); right.Children.Add(search); search.Click += delegate
            {
                Native.keybd_event(0x5B, 0, 0, UIntPtr.Zero); Native.keybd_event(0x53, 0, 0, UIntPtr.Zero);
                Native.keybd_event(0x53, 0, 2, UIntPtr.Zero); Native.keybd_event(0x5B, 0, 2, UIntPtr.Zero);
            };
            controls = BarButton("\uE713", "Control Center", true); right.Children.Add(controls); controls.Click += delegate { OpenControls(controls); };
            clock = BarButton(ClockText(), "Calendar", false); clock.Margin = new Thickness(5, 0, 0, 0); right.Children.Add(clock); clock.Click += delegate { OpenCalendar(clock); };
            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080));
                HwndSource.FromHwnd(handle).AddHook(Messages);
            };
            Loaded += delegate { if (!preview) { Register(); Tick(); timer.Interval = TimeSpan.FromSeconds(1); timer.Tick += delegate { Tick(); }; timer.Start(); } };
            Closed += delegate { Cleanup(); };
        }
        private static Button BarButton(string text, string tooltip, bool glyph)
        {
            Button button = new Button { Content = text, ToolTip = tooltip, Height = 28, Padding = new Thickness(glyph ? 9 : 10, 0, glyph ? 9 : 10, 0), Margin = new Thickness(0, 1, 2, 1), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = DockWindow.Brush("#192534"), FontFamily = new FontFamily(glyph ? "Segoe MDL2 Assets" : "Segoe UI"), FontSize = glyph ? 14 : 12, VerticalContentAlignment = VerticalAlignment.Center, Focusable = false };
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border)); border.Name = "surface"; border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty)); border.AppendChild(presenter); template.VisualTree = border;
            Trigger hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, DockWindow.Brush("#1C4A6585"), "surface")); template.Triggers.Add(hover);
            Trigger pressed = new Trigger { Property = Button.IsPressedProperty, Value = true }; pressed.Setters.Add(new Setter(Border.BackgroundProperty, DockWindow.Brush("#304A6585"), "surface")); template.Triggers.Add(pressed);
            button.Template = template; return button;
        }
        private Native.AppBarData Data()
        {
            return new Native.AppBarData { Size = (uint)Marshal.SizeOf(typeof(Native.AppBarData)), Window = handle, Callback = Callback, Edge = 1 };
        }
        private IntPtr Messages(IntPtr window, int message, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); }
            if ((uint)message == ExplorerCreated && !preview) { registered = false; last = new Native.Rect(); Dispatcher.BeginInvoke(new Action(Register)); }
            if ((uint)message == Callback && wp.ToInt32() == 1 && !placing) Dispatcher.BeginInvoke(new Action(Place));
            if (message == 0x007E || message == 0x02E0 || message == 0x001A) Dispatcher.BeginInvoke(new Action(Place));
            if (message == 0x0047 && registered && !placing) { Native.AppBarData data = Data(); Native.SHAppBarMessage(9, ref data); }
            return IntPtr.Zero;
        }
        private void Register()
        {
            if (disposed || preview || handle == IntPtr.Zero) return;
            Native.AppBarData data = Data(); registered = Native.SHAppBarMessage(0, ref data).ToUInt64() != 0;
            if (!registered) throw new InvalidOperationException("Windows could not reserve space for the top bar.");
            Place();
        }
        private Forms.Screen Screen()
        {
            return Forms.Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == controller.Preferences.MonitorDevice) ?? Forms.Screen.PrimaryScreen;
        }
        internal void Place()
        {
            if (disposed || preview || !registered || placing) return;
            HwndSource source = HwndSource.FromHwnd(handle); if (source == null || source.CompositionTarget == null) return;
            placing = true;
            try
            {
                System.Drawing.Rectangle bounds = Screen().Bounds;
                int pixels = (int)Math.Round(BarHeight * source.CompositionTarget.TransformToDevice.M22);
                Native.AppBarData data = Data(); data.Rectangle = new Native.Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Top + pixels };
                Native.SHAppBarMessage(2, ref data); data.Rectangle.Bottom = data.Rectangle.Top + pixels;
                Native.Rect next = data.Rectangle;
                if (next.Left == last.Left && next.Top == last.Top && next.Right == last.Right && next.Bottom == last.Bottom) return;
                Native.SHAppBarMessage(3, ref data); last = data.Rectangle;
                Width = (last.Right - last.Left) * source.CompositionTarget.TransformFromDevice.M11; Height = BarHeight;
                Native.SetWindowPos(handle, new IntPtr(-1), last.Left, last.Top, last.Right - last.Left, last.Bottom - last.Top, 0x0010);
            }
            finally { placing = false; }
        }
        internal void Tick()
        {
            if (disposed || preview) return;
            UpdateForeground(); Place();
            clock.Content = ClockText(); clock.ToolTip = DateTime.Now.ToString("F", CultureInfo.CurrentCulture);
            bool online = false; try { online = NetworkInterface.GetIsNetworkAvailable(); } catch { }
            network.Opacity = online ? 1 : .4; network.ToolTip = online ? "Network connected · Open network settings" : "No network connection · Open network settings";
            try
            {
                bool wifi = NetworkInterface.GetAllNetworkInterfaces().Any(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                network.Content = wifi ? "\uE701" : "\uE839";
            }
            catch { }
            Forms.PowerStatus power = Forms.SystemInformation.PowerStatus;
            bool hasBattery = (power.BatteryChargeStatus & Forms.BatteryChargeStatus.NoSystemBattery) == 0 && power.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown;
            battery.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;
            if (hasBattery) { battery.ToolTip = "Battery " + Math.Round(power.BatteryLifePercent * 100) + "%" + (power.PowerLineStatus == Forms.PowerLineStatus.Online ? " · Charging" : ""); }
            UpdateSound();
            IntPtr monitor = Native.MonitorFromPoint(new Native.Point { X = Screen().Bounds.Left + 10, Y = Screen().Bounds.Top + 10 }, 2);
            bool hidden = Native.Fullscreen(monitor) && !popups.Any(popup => popup.IsOpen);
            if (hidden != fullscreen) { fullscreen = hidden; Native.ShowWindow(handle, hidden ? 0 : 8); }
        }
        private void UpdateForeground()
        {
            IntPtr foreground = Native.GetForegroundWindow(); if (foreground == IntPtr.Zero || foreground == handle) return;
            string cls = Native.ClassName(foreground);
            if (cls.Contains("TrayWnd") || cls == "#32768" || cls == "tooltips_class32") return;
            uint id; Native.GetWindowThreadProcessId(foreground, out id);
            if (id == Process.GetCurrentProcess().Id)
            {
                StringBuilder ownTitle = new StringBuilder(256); Native.GetWindowText(foreground, ownTitle, ownTitle.Capacity);
                if (ownTitle.Length == 0) return;
                currentApp = "CoolMac"; currentTitle = ownTitle.ToString(); appWindow = IntPtr.Zero;
            }
            else if (cls == "Progman" || cls == "WorkerW") { currentApp = "Desktop"; currentTitle = "Desktop"; appWindow = IntPtr.Zero; }
            else
            {
                string path = Native.ProcessPath(id); StringBuilder title = new StringBuilder(1024); Native.GetWindowText(foreground, title, title.Capacity);
                if (path.Length == 0 || title.Length == 0) return;
                string name;
                if (!appNames.TryGetValue(path, out name))
                {
                    name = Path.GetFileNameWithoutExtension(path);
                    try { string product = FileVersionInfo.GetVersionInfo(path).ProductName; if (!String.IsNullOrWhiteSpace(product)) name = product; } catch { }
                    if (String.Equals(Path.GetFileName(path), "explorer.exe", StringComparison.OrdinalIgnoreCase)) name = "File Explorer";
                    appNames[path] = name;
                }
                currentApp = name; currentTitle = title.ToString(); appWindow = foreground;
            }
            activeApp.Content = new TextBlock { Text = currentApp, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis }; activeApp.ToolTip = currentTitle;
        }
        private static string ClockText() { DateTime now = DateTime.Now; return now.ToString("ddd d MMM", CultureInfo.CurrentCulture) + "  " + now.ToString("t", CultureInfo.CurrentCulture); }
        private static ContextMenu Menu()
        {
            return new ContextMenu { Background = DockWindow.Brush("#F5F7FB"), Foreground = DockWindow.Brush("#192534"), FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Padding = new Thickness(5), MinWidth = 220, BorderBrush = DockWindow.Brush("#CFD7E3") };
        }
        private void OpenMenu(Button button, ContextMenu menu)
        {
            ClosePopups(); menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.VerticalOffset = 5; menu.IsOpen = true;
        }
        private ContextMenu BrandMenu()
        {
            ContextMenu menu = Menu(); DockWindow.AddMenu(menu, "CoolMac controls…", controller.ShowControl);
            DockWindow.AddMenu(menu, "Folder icons…", controller.ShowFolders);
            menu.Items.Add(new Separator()); DockWindow.AddMenu(menu, "Windows Settings…", delegate { Launch("ms-settings:"); });
            DockWindow.AddMenu(menu, "Task Manager", delegate { Launch("taskmgr.exe"); });
            menu.Items.Add(new Separator()); DockWindow.AddMenu(menu, controller.IsRunning ? "Turn off dock" : "Turn on dock", delegate { if (controller.IsRunning) controller.StopDock(); else controller.StartDock(); });
            DockWindow.AddMenu(menu, "Turn off top bar", controller.StopTopBar);
            DockWindow.AddMenu(menu, "Exit CoolMac", controller.Exit); return menu;
        }
        private ContextMenu AppMenu()
        {
            IntPtr target = appWindow; ContextMenu menu = Menu();
            menu.Items.Add(new MenuItem { Header = currentTitle.Length > 75 ? currentTitle.Substring(0, 72) + "…" : currentTitle, IsEnabled = false });
            if (target != IntPtr.Zero && Native.IsWindow(target))
            {
                menu.Items.Add(new Separator()); DockWindow.AddMenu(menu, "Bring to front", delegate { FocusWindow(target); });
                DockWindow.AddMenu(menu, "Minimize", delegate { Native.ShowWindow(target, 6); });
                DockWindow.AddMenu(menu, IsZoomed(target) ? "Restore window" : "Maximize", delegate { Native.ShowWindow(target, IsZoomed(target) ? 9 : 3); });
                menu.Items.Add(new Separator()); DockWindow.AddMenu(menu, "Close window", delegate { Native.PostMessage(target, 0x0112, new IntPtr(0xF060), IntPtr.Zero); });
            }
            else DockWindow.AddMenu(menu, "Open File Explorer", delegate { Launch("explorer.exe"); });
            return menu;
        }
        private ContextMenu GoMenu()
        {
            ContextMenu menu = Menu();
            DockWindow.AddMenu(menu, "This PC", delegate { Launch("shell:MyComputerFolder"); });
            foreach (Environment.SpecialFolder special in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.UserProfile })
            {
                string path = Environment.GetFolderPath(special); string name = special == Environment.SpecialFolder.DesktopDirectory ? "Desktop" : special == Environment.SpecialFolder.UserProfile ? "Home" : special == Environment.SpecialFolder.MyDocuments ? "Documents" : "Pictures";
                if (Directory.Exists(path)) DockWindow.AddMenu(menu, name, delegate { Launch(path); });
            }
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); if (Directory.Exists(downloads)) DockWindow.AddMenu(menu, "Downloads", delegate { Launch(downloads); });
            return menu;
        }
        private ContextMenu WindowsMenu()
        {
            ContextMenu menu = Menu(); List<RunningWindow> windows = Native.Windows();
            if (windows.Count == 0) menu.Items.Add(new MenuItem { Header = "No open app windows", IsEnabled = false });
            foreach (RunningWindow window in windows.OrderBy(window => window.Handle == appWindow ? 0 : 1).Take(40))
            {
                RunningWindow item = window; MenuItem entry = new MenuItem { Header = item.Title.Length > 65 ? item.Title.Substring(0, 62) + "…" : item.Title, IsChecked = item.Handle == appWindow };
                entry.Click += delegate { FocusWindow(item.Handle); }; menu.Items.Add(entry);
            }
            return menu;
        }
        private static void FocusWindow(IntPtr target) { if (!Native.IsWindow(target)) return; if (Native.IsIconic(target)) Native.ShowWindow(target, 9); Native.SetForegroundWindow(target); }
        private void Launch(string target) { DockWindow.Launch(target); }
        private Popup Card(Button anchor, StackPanel content, double width)
        {
            ClosePopups();
            Border card = new Border { Background = DockWindow.Brush("#F8FAFD"), BorderBrush = DockWindow.Brush("#CCD5E2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(20), Width = width, Child = content, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = .2 } };
            Popup popup = new Popup { Child = new Border { Padding = new Thickness(18), Child = card }, PlacementTarget = anchor, Placement = PlacementMode.Bottom, HorizontalOffset = -width + anchor.ActualWidth + 18, VerticalOffset = -10, AllowsTransparency = true, StaysOpen = false };
            popup.Closed += delegate { popups.Remove(popup); if (soundPopup == popup) soundPopup = null; };
            popups.Add(popup); popup.IsOpen = true; return popup;
        }
        private static TextBlock Label(string text, double size, bool bold)
        {
            return new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = DockWindow.Brush("#24334A"), Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        }
        private Button CardButton(string title, Action action)
        {
            Button button = BarButton(title, title, false); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Height = 34; button.Background = DockWindow.Brush("#EAF0F8"); button.Margin = new Thickness(0, 3, 0, 3); button.Click += delegate { ClosePopups(); action(); }; return button;
        }
        private void OpenControls(Button anchor)
        {
            if (popups.Any(popup => popup.PlacementTarget == anchor && popup.IsOpen)) { ClosePopups(); return; }
            StackPanel panel = new StackPanel(); panel.Children.Add(Label("Control Center", 19, true));
            panel.Children.Add(Label("Your Windows desktop", 11, false));
            panel.Children.Add(CardButton("Network & Internet", delegate { Launch("ms-settings:network-status"); }));
            panel.Children.Add(CardButton("Bluetooth & devices", delegate { Launch("ms-settings:bluetooth"); }));
            panel.Children.Add(CardButton("Display & brightness", delegate { Launch("ms-settings:display"); }));
            panel.Children.Add(CardButton("Sound settings", delegate { Launch("ms-settings:sound"); }));
            panel.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });
            panel.Children.Add(CardButton(controller.IsRunning ? "Dock is on · Turn off" : "Dock is off · Turn on", delegate { if (controller.IsRunning) controller.StopDock(); else controller.StartDock(); }));
            panel.Children.Add(CardButton("CoolMac controls", controller.ShowControl)); Card(anchor, panel, 290);
        }
        private void OpenCalendar(Button anchor)
        {
            if (popups.Any(popup => popup.PlacementTarget == anchor && popup.IsOpen)) { ClosePopups(); return; }
            StackPanel panel = new StackPanel(); panel.Children.Add(Label(DateTime.Today.ToString("dddd", CultureInfo.CurrentCulture), 12, false)); panel.Children.Add(Label(DateTime.Today.ToString("D", CultureInfo.CurrentCulture), 18, true));
            Calendar calendar = new Calendar { DisplayDate = DateTime.Today, SelectedDate = DateTime.Today, BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center }; panel.Children.Add(calendar);
            panel.Children.Add(CardButton("Date & time settings", delegate { Launch("ms-settings:dateandtime"); })); Card(anchor, panel, 320);
        }
        private void OpenSound(Button anchor)
        {
            if (soundPopup != null && soundPopup.IsOpen) { ClosePopups(); return; }
            StackPanel panel = new StackPanel(); panel.Children.Add(Label("Sound", 19, true));
            volumeLabel = Label("Output volume", 12, false); panel.Children.Add(volumeLabel);
            double level = .5; bool muted = false; bool available = true;
            try { using (AudioEndpoint audio = new AudioEndpoint()) { level = audio.Level; muted = audio.Muted; } } catch { available = false; volumeLabel.Text = "No playback device available"; }
            volumeSlider = new Slider { Minimum = 0, Maximum = 100, Value = level * 100, IsEnabled = available, Margin = new Thickness(0, 5, 0, 14), Height = 24, TickFrequency = 1, IsSnapToTickEnabled = true };
            volumeSlider.Template = VolumeTemplate();
            System.Windows.Automation.AutomationProperties.SetName(volumeSlider, "Windows output volume");
            volumeSlider.ValueChanged += delegate
            {
                if (updatingVolume || soundPopup == null || !soundPopup.IsOpen) return;
                try { using (AudioEndpoint audio = new AudioEndpoint()) { audio.Level = volumeSlider.Value / 100; if (volumeSlider.Value > 0) audio.Muted = false; } UpdateSound(); }
                catch (Exception error) { volumeLabel.Text = "Volume unavailable"; SettingsStore.Log(error); }
            }; panel.Children.Add(volumeSlider);
            // Keep this popup open when muting; the slider can still be used.
            muteButton = BarButton(muted ? "Unmute" : "Mute", "Toggle mute", false); muteButton.Background = DockWindow.Brush("#EAF0F8"); muteButton.IsEnabled = available; muteButton.Margin = new Thickness(0, 3, 0, 10);
            muteButton.Click += delegate { try { using (AudioEndpoint audio = new AudioEndpoint()) audio.Muted = !audio.Muted; UpdateSound(); } catch (Exception error) { SettingsStore.Log(error); volumeLabel.Text = "Volume unavailable"; } }; panel.Children.Add(muteButton);
            panel.Children.Add(CardButton("Sound settings", delegate { Launch("ms-settings:sound"); })); soundPopup = Card(anchor, panel, 280); UpdateSound();
        }
        private static ControlTemplate VolumeTemplate()
        {
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>
  <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}'>
    <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='#347DE7'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
    <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='#D7DFEB'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
    <Track.Thumb><Thumb Width='15' Height='15'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border CornerRadius='8' Background='#347DE7' BorderBrush='White' BorderThickness='1.5'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
  </Track>
</ControlTemplate>");
        }
        private void UpdateSound()
        {
            try
            {
                using (AudioEndpoint audio = new AudioEndpoint())
                {
                    double level = audio.Level; bool muted = audio.Muted;
                    sound.Content = muted || level == 0 ? "\uE74F" : "\uE767"; sound.ToolTip = (muted ? "Muted" : "Volume " + Math.Round(level * 100) + "%") + " · Adjust sound";
                    if (soundPopup != null && soundPopup.IsOpen)
                    {
                        updatingVolume = true;
                        try { if (!volumeSlider.IsMouseCaptureWithin) volumeSlider.Value = level * 100; volumeLabel.Text = "Output volume · " + Math.Round(level * 100) + "%"; muteButton.Content = muted ? "Unmute" : "Mute"; }
                        finally { updatingVolume = false; }
                    }
                }
            }
            catch { sound.Content = "\uE74F"; sound.ToolTip = "Sound settings · No playback device available"; }
        }
        private void ClosePopups() { foreach (Popup popup in popups.ToArray()) popup.IsOpen = false; }
        internal FrameworkElement PreviewCard(string type)
        {
            if (type == "calendar") OpenCalendar(clock); else if (type == "sound") OpenSound(sound); else OpenControls(controls);
            FrameworkElement card = popups.Last().Child as FrameworkElement; card.UpdateLayout(); return card;
        }
        internal void Cleanup()
        {
            if (disposed) return; disposed = true; timer.Stop(); ClosePopups();
            if (registered) { registered = false; Native.RemoveAppBar(handle); }
        }
    }
}
