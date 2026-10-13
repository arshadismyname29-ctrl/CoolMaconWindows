using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CoolMac
{
    internal sealed class DockWindow : Window
    {
        internal readonly Preferences Preferences;
        internal readonly bool Preview;
        internal Action OpenControl;
        internal Action QuitApplication;
        internal Action OpenFolderIcons;
        private readonly Canvas canvas = new Canvas();
        private readonly Border shelf = new Border();
        private readonly Border tooltip = new Border();
        private readonly TextBlock tooltipText = new TextBlock();
        private readonly List<DockItem> items = new List<DockItem>();
        private readonly Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly TaskbarLease taskbar = new TaskbarLease();
        private readonly DesktopReservation reservation = new DesktopReservation();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly TranslateTransform slide = new TranslateTransform();
        private readonly Stopwatch animationClock = Stopwatch.StartNew();
        private double lastFrame, mouseX = -10000, hideOffset, hideTarget, revealUntil;
        private bool animating, shuttingDown, menuOpen;
        private IntPtr handle;
        private Forms.NotifyIcon tray;
        private SettingsWindow settings;
        private string lastSignature = "";
        private int tick;
        private Border separator;
        private bool hotkeyRegistered;

        internal DockWindow(Preferences preferences, bool preview)
        {
            Preferences = preferences; Preview = preview;
            Title = "CoolMac Dock"; Width = 1000; Height = 164;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            canvas.RenderTransform = slide; Content = canvas;
            shelf.CornerRadius = new CornerRadius(22); shelf.BorderThickness = new Thickness(1);
            shelf.BorderBrush = new SolidColorBrush(Color.FromArgb(125, 255, 255, 255));
            shelf.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 5, Opacity = .27, Color = Colors.Black };
            shelf.MouseEnter += delegate { menuOpen = false; hideTarget = 0; WakeAnimation(); };
            shelf.ContextMenu = BackgroundMenu(); shelf.ContextMenu.Opened += delegate { menuOpen = true; };
            shelf.ContextMenu.Closed += delegate { menuOpen = false; };
            canvas.Children.Add(shelf);
            tooltip.Background = new SolidColorBrush(Color.FromArgb(238, 30, 35, 45));
            tooltip.BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)); tooltip.BorderThickness = new Thickness(1);
            tooltip.CornerRadius = new CornerRadius(8); tooltip.Padding = new Thickness(12, 6, 12, 6);
            tooltipText.Foreground = Brushes.White; tooltipText.FontSize = 12; tooltipText.FontFamily = new FontFamily("Segoe UI");
            tooltip.Child = tooltipText; tooltip.IsHitTestVisible = false; tooltip.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(tooltip, 100); canvas.Children.Add(tooltip);
            AllowDrop = true;
            DragOver += delegate(object sender, DragEventArgs e) { hideTarget = 0; WakeAnimation(); e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
            Drop += OnDrop;
            MouseMove += delegate(object sender, MouseEventArgs e) { mouseX = e.GetPosition(canvas).X; UpdateTargets(); WakeAnimation(); };
            MouseLeave += delegate { mouseX = -10000; tooltip.Visibility = Visibility.Collapsed; UpdateTargets(); WakeAnimation(); };
            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080));
                HwndSource.FromHwnd(handle).AddHook(WindowMessage);
                if (!Preview) hotkeyRegistered = Native.RegisterHotKey(handle, 1, 0x4003, 0x44); // Ctrl+Alt+D
            };
            Loaded += delegate
            {
                ApplyPreferences(); RefreshWindows();
                if (!Preview) { CreateTray(); timer.Interval = TimeSpan.FromMilliseconds(100); timer.Tick += Tick; timer.Start(); }
            };
            Closed += delegate { Cleanup(); };
        }
        private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE, preserve click-to-focus permission.
            if (message == 0x0312 && wParam.ToInt32() == 1) { ShowSettings(); handled = true; }
            if (message == 0x007E || message == 0x02E0 || message == 0x001A)
                Dispatcher.BeginInvoke(new Action(PositionDock), DispatcherPriority.Background);
            return IntPtr.Zero;
        }
        internal void ApplyPreferences()
        {
            Preferences.Normalize();
            if (!Preview)
            {
                if (Preferences.HideTaskbar && !taskbar.Active)
                {
                    try { taskbar.Acquire(reservation.EnsureWindow()); }
                    catch (Exception error) { Preferences.HideTaskbar = false; SettingsStore.Log(error); MessageBox.Show("The recovery helper could not start, so the Windows taskbar is still available.", "CoolMac Dock"); }
                }
                else if (!Preferences.HideTaskbar && taskbar.Active) { reservation.Release(); taskbar.Dispose(); }
            }
            byte opacity = (byte)(Preferences.Opacity * 255);
            shelf.Background = new LinearGradientBrush(Color.FromArgb(opacity, 245, 247, 252), Color.FromArgb((byte)(opacity * .91), 197, 208, 224), 90);
            RefreshWindows(); PositionDock(); LayoutItems();
        }
        internal void Save()
        {
            if (Preview) return;
            try { SettingsStore.Save(Preferences, SettingsStore.FilePath); }
            catch (Exception error) { SettingsStore.Log(error); MessageBox.Show("Your settings could not be saved. " + error.Message, "CoolMac Dock"); }
        }
        private Forms.Screen Screen()
        {
            return Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Preferences.MonitorDevice) ?? Forms.Screen.PrimaryScreen;
        }
        private void PositionDock()
        {
            if (Preview || handle == IntPtr.Zero || shuttingDown) return;
            Forms.Screen screen = Screen();
            System.Drawing.Rectangle bounds = Preferences.HideTaskbar ? screen.Bounds : screen.WorkingArea;
            HwndSource source = HwndSource.FromHwnd(handle); if (source == null || source.CompositionTarget == null) return;
            Matrix fromPixels = source.CompositionTarget.TransformFromDevice;
            Width = Math.Max(200, (bounds.Width - 24) * fromPixels.M11); Height = 164;
            double dpi = 1 / fromPixels.M22;
            if (Preferences.HideTaskbar && !Preferences.AutoHide) reservation.Configure(screen, (int)((Preferences.IconSize + 44) * dpi)); else reservation.Release();
            Native.SetWindowPos(handle, new IntPtr(-1), bounds.Left + 12, bounds.Bottom - (int)(Height * dpi), (int)(Width / fromPixels.M11), (int)(Height * dpi), 0x0010);
            LayoutItems();
        }
        private ImageSource GetIcon(Pin pin)
        {
            if (String.IsNullOrWhiteSpace(pin.IconPath)) return null;
            ImageSource icon; if (iconCache.TryGetValue(pin.IconPath, out icon)) return icon;
            icon = Native.Icon(pin.IconPath); iconCache[pin.IconPath] = icon; return icon;
        }
        internal void RefreshWindows()
        {
            List<RunningWindow> windows = Preview ? new List<RunningWindow>() : Native.Windows();
            string signature = String.Join("|", windows.Select(w => w.Handle + w.Title + w.Path)) + ":" + String.Join("|", Preferences.Pins.Select(p => p.Target));
            if (signature == lastSignature) return; lastSignature = signature;
            foreach (DockItem item in items) canvas.Children.Remove(item.View);
            if (separator != null) canvas.Children.Remove(separator); items.Clear();
            foreach (Pin pin in Preferences.Pins.Where(p => !p.Utility)) AddItem(pin, true, windows.Where(w => DockMath.SameApp(w.Path, pin.MatchPath)).ToList());
            foreach (IGrouping<string, RunningWindow> group in windows.Where(w => !Preferences.Pins.Any(p => DockMath.SameApp(p.MatchPath, w.Path))).GroupBy(w => w.Path, StringComparer.OrdinalIgnoreCase).Take(16))
            {
                Pin pin = Pin.FromPath(group.Key); if (pin != null) { pin.Name = ApplicationName(group.Key); AddItem(pin, false, group.ToList()); }
            }
            if (Preferences.Pins.Any(p => p.Utility))
            {
                separator = new Border { Width = 1, Height = 32, Background = new SolidColorBrush(Color.FromArgb(60, 45, 55, 75)), IsHitTestVisible = false };
                canvas.Children.Add(separator);
            }
            else separator = null;
            foreach (Pin pin in Preferences.Pins.Where(p => p.Utility)) AddItem(pin, true, new List<RunningWindow>());
            AddItem(new Pin { Name = "Dock settings", Target = "coolmac:settings", Glyph = "\uE712", Color = "#31415E", Utility = true }, true, new List<RunningWindow>());
            LayoutItems(); UpdateTargets(); WakeAnimation();
        }
        private static string ApplicationName(string path)
        {
            try { string product = FileVersionInfo.GetVersionInfo(path).ProductName; if (!String.IsNullOrWhiteSpace(product)) return product; } catch { }
            return Path.GetFileNameWithoutExtension(path);
        }
        private void AddItem(Pin pin, bool pinned, List<RunningWindow> windows)
        {
            DockItem item = new DockItem { Pin = pin, Pinned = pinned, Windows = windows, Icon = GetIcon(pin) };
            Grid grid = new Grid();
            FrameworkElement image;
            if (item.Icon != null) image = new Image { Source = item.Icon, Stretch = Stretch.Uniform, Margin = new Thickness(2), IsHitTestVisible = false };
            else
            {
                TextBlock glyph = new TextBlock { Text = pin.Glyph ?? "\uE71D", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 25, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                image = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Background = Brush(pin.Color), Child = glyph, IsHitTestVisible = false };
            }
            item.ScaleTransform = new ScaleTransform(1, 1); item.Lift = new TranslateTransform();
            TransformGroup transform = new TransformGroup(); transform.Children.Add(item.ScaleTransform); transform.Children.Add(item.Lift);
            image.RenderTransformOrigin = new Point(.5, 1); image.RenderTransform = transform;
            item.IconView = image; image.VerticalAlignment = VerticalAlignment.Bottom; image.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(image);
            item.Indicator = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = Brush("#33435B"), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, -9), Visibility = windows.Count > 0 ? Visibility.Visible : Visibility.Collapsed, IsHitTestVisible = false };
            grid.Children.Add(item.Indicator);
            Button button = FlatButton(grid); button.Padding = new Thickness(0); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Stretch;
            AutomationProperties.SetName(button, pin.Name);
            button.Click += delegate { Activate(item); };
            button.ContextMenu = ItemMenu(item);
            button.ContextMenu.Opened += delegate { menuOpen = true; tooltip.Visibility = Visibility.Collapsed; };
            button.ContextMenu.Closed += delegate { menuOpen = false; };
            item.View = new Border { Child = button, Background = Brushes.Transparent }; canvas.Children.Add(item.View); items.Add(item);
        }
        internal static SolidColorBrush Brush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        internal static Button FlatButton(object content)
        {
            Button button = new Button { Content = content, Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty)); border.AppendChild(presenter);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            button.MouseEnter += delegate { button.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)); };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            return button;
        }
        private void UpdateTargets()
        {
            bool motion = !(Preferences.ReduceMotion || !SystemParameters.ClientAreaAnimation);
            double fit = Fit();
            foreach (DockItem item in items)
                item.TargetScale = motion && mouseX > -9999 ? DockMath.Scale(mouseX - item.Center, Preferences.IconSize * fit * 1.15, Preferences.Magnification) : 1;
        }
        private double Fit()
        {
            double full = items.Count * (Preferences.IconSize + 14) + 54;
            // Leave space for magnification even when many apps are open.
            return Math.Min(1, Math.Max(.35, (Width - Preferences.IconSize * 2) / Math.Max(1, full)));
        }
        private void LayoutItems()
        {
            if (items.Count == 0) return;
            double size = Preferences.IconSize * Fit();
            double total = items.Sum(i => size * i.Scale + 14) + 34 + (separator != null ? 18 : 0);
            double x = (Width - total) / 2 + 17;
            double bottom = Height - 30;
            shelf.Width = total; shelf.Height = size + 27; Canvas.SetLeft(shelf, (Width - total) / 2); Canvas.SetTop(shelf, bottom - size - 11);
            bool separated = false;
            foreach (DockItem item in items)
            {
                if (item.Pin.Utility && separator != null && !separated)
                {
                    Canvas.SetLeft(separator, x + 8); Canvas.SetTop(separator, bottom - size / 2 - 16); x += 18; separated = true;
                }
                double slot = size * item.Scale + 14;
                item.Center = x + slot / 2; item.View.Width = slot; item.View.Height = size * Preferences.Magnification + 16;
                item.IconView.Width = size; item.IconView.Height = size;
                Canvas.SetLeft(item.View, x); Canvas.SetTop(item.View, bottom - item.View.Height - 1);
                item.ScaleTransform.ScaleX = item.Scale; item.ScaleTransform.ScaleY = item.Scale;
                Panel.SetZIndex(item.View, 10 + (int)(item.Scale * 10)); x += slot;
            }
            if (mouseX > -9999 && hideOffset < 1 && !menuOpen)
            {
                DockItem nearest = items.OrderBy(i => Math.Abs(mouseX - i.Center)).First();
                if (Math.Abs(mouseX - nearest.Center) < size)
                {
                    tooltipText.Text = nearest.Pin.Name; tooltip.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                    Canvas.SetLeft(tooltip, Math.Max(0, Math.Min(Width - tooltip.DesiredSize.Width, nearest.Center - tooltip.DesiredSize.Width / 2)));
                    Canvas.SetTop(tooltip, Math.Max(2, bottom - size * nearest.Scale - tooltip.DesiredSize.Height - 15)); tooltip.Visibility = Visibility.Visible;
                }
                else tooltip.Visibility = Visibility.Collapsed;
            }
        }
        private void WakeAnimation()
        {
            if (animating || shuttingDown) return;
            lastFrame = animationClock.Elapsed.TotalSeconds; animating = true; CompositionTarget.Rendering += Animate;
        }
        private void Animate(object sender, EventArgs args)
        {
            double now = animationClock.Elapsed.TotalSeconds, delta = Math.Min(.05, Math.Max(.001, now - lastFrame)); lastFrame = now;
            bool settled = true, reduced = Preferences.ReduceMotion || !SystemParameters.ClientAreaAnimation;
            foreach (DockItem item in items)
            {
                item.Scale = reduced ? item.TargetScale : DockMath.Ease(item.Scale, item.TargetScale, delta);
                if (Math.Abs(item.Scale - item.TargetScale) < .002) item.Scale = item.TargetScale; else settled = false;
            }
            hideOffset = reduced ? hideTarget : DockMath.Ease(hideOffset, hideTarget, delta);
            if (Math.Abs(hideOffset - hideTarget) < .2) hideOffset = hideTarget; else settled = false;
            slide.Y = hideOffset; LayoutItems();
            if (settled) { CompositionTarget.Rendering -= Animate; animating = false; }
        }
        private void Tick(object sender, EventArgs args)
        {
            if (shuttingDown) return;
            if (++tick % 10 == 0) { RefreshWindows(); PositionDock(); }
            if (taskbar.Active && tick % 5 == 0) taskbar.Hide();
            Native.Point cursor; Native.GetCursorPos(out cursor);
            Forms.Screen screen = Screen();
            System.Drawing.Rectangle bounds = Preferences.HideTaskbar ? screen.Bounds : screen.WorkingArea;
            bool edge = cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= bounds.Bottom - 5 && cursor.Y < bounds.Bottom;
            if (edge) revealUntil = animationClock.Elapsed.TotalSeconds + 1.2;
            Native.Rect rect; Native.GetWindowRect(handle, out rect);
            double pixelScale = (rect.Bottom - rect.Top) / Height;
            bool hovering = cursor.X >= rect.Left + Canvas.GetLeft(shelf) * pixelScale && cursor.X < rect.Left + (Canvas.GetLeft(shelf) + shelf.Width) * pixelScale && cursor.Y >= rect.Top + (Canvas.GetTop(shelf) + hideOffset - 35) * pixelScale && cursor.Y < rect.Bottom;
            IntPtr monitor = Native.MonitorFromPoint(new Native.Point { X = screen.Bounds.Left + 10, Y = screen.Bounds.Top + 10 }, 2);
            bool fullscreen = Native.Fullscreen(monitor);
            double target = fullscreen || (Preferences.AutoHide && !hovering && !menuOpen && (settings == null || !settings.IsVisible) && animationClock.Elapsed.TotalSeconds > revealUntil) ? 125 : 0;
            if (target != hideTarget) { hideTarget = target; tooltip.Visibility = Visibility.Collapsed; WakeAnimation(); }
        }
        private void Activate(DockItem item)
        {
            if (item.Pin.Target == "coolmac:settings") { ShowSettings(); return; }
            if (item.Windows.Count > 0 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                IntPtr foreground = Native.GetForegroundWindow();
                int current = item.Windows.FindIndex(w => w.Handle == foreground);
                RunningWindow window = item.Windows[(current + 1) % item.Windows.Count];
                if (item.Windows.Count == 1 && current == 0) Native.ShowWindow(window.Handle, 6);
                else { if (Native.IsIconic(window.Handle)) Native.ShowWindow(window.Handle, 9); Native.SetForegroundWindow(window.Handle); }
            }
            else Launch(item.Pin.Target);
            if (!Preferences.ReduceMotion && SystemParameters.ClientAreaAnimation)
            {
                DoubleAnimation bounce = new DoubleAnimation(0, -13, TimeSpan.FromMilliseconds(160)) { AutoReverse = true, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                item.Lift.BeginAnimation(TranslateTransform.YProperty, bounce);
            }
        }
        internal static void Launch(string target)
        {
            try
            {
                if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception error) { SettingsStore.Log(error); MessageBox.Show("Couldn't open this item. " + error.Message, "CoolMac Dock"); }
        }
        private ContextMenu BackgroundMenu()
        {
            ContextMenu menu = new ContextMenu();
            AddMenu(menu, "Dock controls…", delegate { if (OpenControl != null) OpenControl(); else ShowSettings(); });
            if (OpenFolderIcons != null) AddMenu(menu, "Folder icons…", OpenFolderIcons);
            AddMenu(menu, "Dock settings…", ShowSettings);
            AddMenu(menu, "Add apps…", delegate { ShowSettings(); if (settings != null) settings.ShowAppPicker(); });
            AddMenu(menu, "Windows Start menu", delegate { Native.keybd_event(0x5B, 0, 0, UIntPtr.Zero); Native.keybd_event(0x5B, 0, 2, UIntPtr.Zero); });
            menu.Items.Add(new Separator());
            AddMenu(menu, "Show Windows taskbar", delegate { Preferences.HideTaskbar = false; ApplyPreferences(); Save(); });
            menu.Items.Add(new Separator()); AddMenu(menu, "Turn off dock", delegate { Close(); });
            AddMenu(menu, "Exit app", delegate { if (QuitApplication != null) QuitApplication(); else Close(); }); return menu;
        }
        private ContextMenu ItemMenu(DockItem item)
        {
            ContextMenu menu = new ContextMenu();
            if (item.Pin.Target == "coolmac:settings") return BackgroundMenu();
            AddMenu(menu, "Open " + item.Pin.Name, delegate { Launch(item.Pin.Target); });
            foreach (RunningWindow window in item.Windows)
            {
                RunningWindow selected = window;
                AddMenu(menu, window.Title.Length > 70 ? window.Title.Substring(0, 67) + "…" : window.Title, delegate { Native.ShowWindow(selected.Handle, 9); Native.SetForegroundWindow(selected.Handle); });
            }
            menu.Items.Add(new Separator());
            if (item.Pinned)
            {
                AddMenu(menu, "Remove from dock", delegate { Preferences.Pins.Remove(item.Pin); Save(); lastSignature = ""; RefreshWindows(); });
                AddMenu(menu, "Move left", delegate { MovePin(item.Pin, -1); });
                AddMenu(menu, "Move right", delegate { MovePin(item.Pin, 1); });
            }
            else AddMenu(menu, "Keep in dock", delegate { Preferences.Pins.Add(item.Pin); Save(); lastSignature = ""; RefreshWindows(); });
            menu.Items.Add(new Separator()); AddMenu(menu, "Dock settings…", ShowSettings); return menu;
        }
        private void MovePin(Pin pin, int delta)
        {
            List<Pin> group = Preferences.Pins.Where(p => p.Utility == pin.Utility).ToList();
            int groupIndex = group.IndexOf(pin); int next = groupIndex + delta;
            if (next < 0 || next >= group.Count) return;
            int first = Preferences.Pins.IndexOf(pin), second = Preferences.Pins.IndexOf(group[next]);
            Preferences.Pins[first] = group[next]; Preferences.Pins[second] = pin; Save(); lastSignature = ""; RefreshWindows();
        }
        internal static void AddMenu(ContextMenu menu, string title, Action action)
        {
            MenuItem item = new MenuItem { Header = title }; item.Click += delegate { action(); }; menu.Items.Add(item);
        }
        private void OnDrop(object sender, DragEventArgs args)
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop)) return;
            foreach (string path in (string[])args.Data.GetData(DataFormats.FileDrop))
            {
                Pin pin = Pin.FromPath(path);
                if (pin != null && !Preferences.Pins.Any(p => String.Equals(p.Target, pin.Target, StringComparison.OrdinalIgnoreCase)) && Preferences.Pins.Count < 48) Preferences.Pins.Add(pin);
            }
            Save(); lastSignature = ""; RefreshWindows();
        }
        internal void ShowSettings()
        {
            if (Preview) return;
            if (settings == null) { settings = new SettingsWindow(this); settings.Closed += delegate { settings = null; }; }
            settings.Show(); settings.Activate(); hideTarget = 0; WakeAnimation();
        }
        private void CreateTray()
        {
            System.Drawing.Icon dockIcon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName);
            tray = new Forms.NotifyIcon { Icon = dockIcon ?? System.Drawing.SystemIcons.Application, Text = "CoolMac Dock · Ctrl+Alt+D", Visible = true };
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Dock controls", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { if (OpenControl != null) OpenControl(); else ShowSettings(); })); });
            menu.Items.Add("Dock settings", null, delegate { Dispatcher.BeginInvoke(new Action(ShowSettings)); });
            menu.Items.Add("Show Windows taskbar", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { Preferences.HideTaskbar = false; ApplyPreferences(); Save(); })); });
            menu.Items.Add("Turn off dock", null, delegate { Dispatcher.BeginInvoke(new Action(Close)); });
            menu.Items.Add("Exit app", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { if (QuitApplication != null) QuitApplication(); else Close(); })); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(delegate { if (OpenControl != null) OpenControl(); else ShowSettings(); })); };
        }
        internal void Cleanup()
        {
            if (shuttingDown) return; shuttingDown = true;
            timer.Stop(); CompositionTarget.Rendering -= Animate; reservation.Dispose(); taskbar.Dispose();
            if (handle != IntPtr.Zero && hotkeyRegistered) Native.UnregisterHotKey(handle, 1);
            if (tray != null) { tray.Visible = false; System.Drawing.Icon icon = tray.Icon; tray.Dispose(); if (icon != System.Drawing.SystemIcons.Application) icon.Dispose(); }
            if (settings != null) settings.Close();
        }
        internal void PreviewHover(int index)
        {
            mouseX = index >= 0 && index < items.Count ? items[index].Center : -10000;
            UpdateTargets(); foreach (DockItem item in items) item.Scale = item.TargetScale; LayoutItems();
        }
        internal void RefreshFolderIcons()
        {
            foreach (string path in iconCache.Keys.Where(Directory.Exists).ToList()) iconCache.Remove(path);
            lastSignature = ""; RefreshWindows();
        }
        internal int ItemCount { get { return items.Count; } }
    }
}
