using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace CoolMac
{
    internal sealed class SettingsWindow : Window
    {
        private readonly DockWindow dock;
        private readonly StackPanel pins = new StackPanel();
        private CheckBox replacement;
        internal SettingsWindow(DockWindow owner)
        {
            dock = owner; Title = "CoolMac · Dock settings"; Width = 520; Height = 760; MinWidth = 480; MinHeight = 500;
            Background = DockWindow.Brush("#151A24"); Foreground = DockWindow.Brush("#F1F4FA"); FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            StackPanel content = new StackPanel { Margin = new Thickness(30, 28, 30, 24) }; scroll.Content = content; Content = scroll;
            content.Children.Add(Text("COOLMAC / WINDOWS DOCK", 11, "#8EA6CF", new Thickness(0, 0, 0, 12)));
            content.Children.Add(Text("Dock settings", 30, "#F1F4FA", new Thickness(0, 0, 0, 8)));
            content.Children.Add(Text("Your Windows apps, a little closer.", 13, "#99A6BA", new Thickness(0, 0, 0, 26)));
            content.Children.Add(Section("Appearance"));
            AddSlider(content, "Icon size", 32, 64, dock.Preferences.IconSize, 1, delegate(double value) { dock.Preferences.IconSize = (int)value; }, delegate(double value) { return ((int)value).ToString() + " px"; });
            AddSlider(content, "Hover magnification", 1, 1.9, dock.Preferences.Magnification, .05, delegate(double value) { dock.Preferences.Magnification = value; }, delegate(double value) { return value.ToString("0.00") + "×"; });
            AddSlider(content, "Glass opacity", .35, .98, dock.Preferences.Opacity, .01, delegate(double value) { dock.Preferences.Opacity = value; }, delegate(double value) { return ((int)(value * 100)).ToString() + "%"; });
            content.Children.Add(Section("Behavior"));
            replacement = Toggle("Replace Windows taskbar", "Restore the original taskbar when the dock closes.", dock.Preferences.HideTaskbar,
                delegate(bool value) { dock.Preferences.HideTaskbar = value; }); content.Children.Add(replacement);
            content.Children.Add(Toggle("Automatically hide the dock", "Move to the bottom edge to bring it back.", dock.Preferences.AutoHide, delegate(bool value) { dock.Preferences.AutoHide = value; }));
            content.Children.Add(Toggle("Reduce motion", "Keep the dock still, without zoom or bounce.", dock.Preferences.ReduceMotion, delegate(bool value) { dock.Preferences.ReduceMotion = value; }));
            content.Children.Add(Text("Display", 13, "#DCE4F2", new Thickness(0, 14, 0, 7)));
            ComboBox screens = new ComboBox { Height = 32, Margin = new Thickness(0, 0, 0, 22) };
            Forms.Screen[] all = Forms.Screen.AllScreens;
            for (int i = 0; i < all.Length; i++) screens.Items.Add("Display " + (i + 1) + " · " + all[i].Bounds.Width + " × " + all[i].Bounds.Height + (all[i].Primary ? " · Primary" : ""));
            int selected = Array.FindIndex(all, s => s.DeviceName == dock.Preferences.MonitorDevice);
            if (selected < 0) selected = Array.FindIndex(all, s => s.Primary); screens.SelectedIndex = selected;
            screens.SelectionChanged += delegate { if (screens.SelectedIndex >= 0) { dock.Preferences.MonitorDevice = all[screens.SelectedIndex].DeviceName; Changed(); } };
            content.Children.Add(screens);
            content.Children.Add(Section("Pinned apps & folders"));
            content.Children.Add(Text("Drag a shortcut, app, file, or folder onto the dock to pin it.", 12, "#99A6BA", new Thickness(0, 0, 0, 12)));
            content.Children.Add(pins); UpdatePins();
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 22) };
            actions.Children.Add(ActionButton("+ Add apps", ShowAppPicker, true));
            actions.Children.Add(ActionButton("Add folder", AddFolder, false)); content.Children.Add(actions);
            Border tips = new Border { Background = DockWindow.Brush("#202838"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 4, 0, 20) };
            tips.Child = Text("Click to open or switch. Click an active app to minimize.\nShift-click opens another instance. Right-click for options.\nCtrl + Alt + D opens these settings.", 12, "#BBC9DF", new Thickness(0)); content.Children.Add(tips);
            StackPanel footer = new StackPanel { Orientation = Orientation.Horizontal };
            footer.Children.Add(ActionButton("Restore taskbar", delegate { dock.Preferences.HideTaskbar = false; replacement.IsChecked = false; Changed(); }, false));
            footer.Children.Add(ActionButton("Quit dock", delegate { dock.Close(); }, false)); content.Children.Add(footer);
        }
        private void Changed() { dock.ApplyPreferences(); dock.Save(); }
        private void AddSlider(StackPanel content, string label, double min, double max, double value, double step, Action<double> update, Func<double, string> format)
        {
            Grid row = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            row.Children.Add(Text(label, 13, "#DCE4F2", new Thickness(0)));
            TextBlock amount = Text(format(value), 12, "#99A6BA", new Thickness(0)); amount.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(amount); content.Children.Add(row);
            Slider slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 0, 20), Height = 22, Foreground = DockWindow.Brush("#79A8FF") };
            slider.ValueChanged += delegate { update(slider.Value); amount.Text = format(slider.Value); Changed(); };
            content.Children.Add(slider);
        }
        private CheckBox Toggle(string title, string description, bool initial, Action<bool> update)
        {
            StackPanel label = new StackPanel(); label.Children.Add(Text(title, 13, "#DCE4F2", new Thickness(0)));
            label.Children.Add(Text(description, 11, "#99A6BA", new Thickness(0, 3, 0, 0)));
            CheckBox box = new CheckBox { Content = label, IsChecked = initial, Margin = new Thickness(0, 5, 0, 15), VerticalContentAlignment = VerticalAlignment.Center };
            box.Checked += delegate { update(true); Changed(); }; box.Unchecked += delegate { update(false); Changed(); }; return box;
        }
        internal void UpdatePins()
        {
            pins.Children.Clear();
            foreach (Pin pin in dock.Preferences.Pins)
            {
                Pin selected = pin;
                Grid row = new Grid { Margin = new Thickness(0, 0, 0, 5), Height = 37 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                ImageSource icon = Native.Icon(pin.IconPath);
                if (icon != null) row.Children.Add(new Image { Source = icon, Width = 25, Height = 25, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
                else row.Children.Add(new TextBlock { Text = pin.Glyph ?? "\uE71D", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, Foreground = DockWindow.Brush("#A6BCDF"), VerticalAlignment = VerticalAlignment.Center });
                TextBlock name = Text(pin.Name, 13, "#DCE4F2", new Thickness(0)); name.VerticalAlignment = VerticalAlignment.Center; name.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(name, 1); row.Children.Add(name);
                Button remove = DockWindow.FlatButton("×"); remove.Foreground = DockWindow.Brush("#9CAAC0"); remove.FontSize = 18; remove.ToolTip = "Remove " + pin.Name; Grid.SetColumn(remove, 2);
                remove.Click += delegate { dock.Preferences.Pins.Remove(selected); Changed(); UpdatePins(); }; row.Children.Add(remove); pins.Children.Add(row);
            }
        }
        internal void ShowAppPicker()
        {
            AppPicker picker = new AppPicker(delegate(Pin pin)
            {
                if (dock.Preferences.Pins.Count >= 48) { MessageBox.Show("The dock can hold up to 48 pinned items.", "CoolMac Dock"); return; }
                if (!dock.Preferences.Pins.Any(p => String.Equals(p.Target, pin.Target, StringComparison.OrdinalIgnoreCase))) dock.Preferences.Pins.Add(pin);
                Changed(); UpdatePins();
            }); picker.Owner = this; picker.Show();
        }
        private void AddFolder()
        {
            using (Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog { Description = "Choose a folder to pin to your dock", ShowNewFolderButton = false })
                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    Pin pin = Pin.FromPath(dialog.SelectedPath);
                    if (pin != null && dock.Preferences.Pins.Count < 48 && !dock.Preferences.Pins.Any(p => DockMath.SameApp(p.Target, pin.Target))) { dock.Preferences.Pins.Add(pin); Changed(); UpdatePins(); }
                }
        }
        internal static TextBlock Text(string value, double size, string color, Thickness margin)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = DockWindow.Brush(color), Margin = margin, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5 };
        }
        private static TextBlock Section(string name) { return Text(name.ToUpperInvariant(), 11, "#8093B0", new Thickness(0, 2, 0, 13)); }
        internal static Button ActionButton(string title, Action action, bool primary)
        {
            Button button = DockWindow.FlatButton(title); button.Background = DockWindow.Brush(primary ? "#387BE9" : "#293244");
            button.Foreground = Brushes.White; button.Padding = new Thickness(14, 9, 14, 9); button.Margin = new Thickness(0, 0, 8, 0); button.FontSize = 12;
            button.Click += delegate { action(); };
            // Preserve the action surface after the dock's flat-button hover events.
            button.MouseLeave += delegate { button.Background = DockWindow.Brush(primary ? "#387BE9" : "#293244"); }; return button;
        }
    }
    internal sealed class AppPicker : Window
    {
        private readonly TextBox search = new TextBox();
        private readonly ListBox list = new ListBox();
        private readonly TextBlock status;
        private readonly Action<Pin> pin;
        private List<string> shortcuts = new List<string>();
        internal AppPicker(Action<Pin> onPin)
        {
            pin = onPin; Title = "Add apps to your dock"; Width = 470; Height = 550; MinWidth = 400; MinHeight = 420;
            Background = DockWindow.Brush("#151A24"); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Grid grid = new Grid { Margin = new Thickness(24) }; Content = grid;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock title = SettingsWindow.Text("Add your Windows apps", 22, "#F1F4FA", new Thickness(0, 0, 0, 16)); grid.Children.Add(title);
            search.Height = 34; search.FontSize = 14; search.Padding = new Thickness(8, 4, 8, 4); search.Background = DockWindow.Brush("#252E40"); search.Foreground = Brushes.White; search.BorderBrush = DockWindow.Brush("#3E4E68");
            search.ToolTip = "Search installed Start menu shortcuts"; Grid.SetRow(search, 1); grid.Children.Add(search); search.TextChanged += delegate { Filter(); };
            status = SettingsWindow.Text("Loading Start menu apps…", 11, "#99A6BA", new Thickness(0, 9, 0, 9)); Grid.SetRow(status, 2); grid.Children.Add(status);
            list.Background = DockWindow.Brush("#1B2230"); list.Foreground = Brushes.White; list.BorderThickness = new Thickness(0); list.FontSize = 14;
            list.DisplayMemberPath = "Name"; Grid.SetRow(list, 3); grid.Children.Add(list);
            list.MouseDoubleClick += delegate { PinSelected(); };
            StackPanel footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) }; Grid.SetRow(footer, 4); grid.Children.Add(footer);
            footer.Children.Add(SettingsWindow.ActionButton("Pin selected app", PinSelected, true)); footer.Children.Add(SettingsWindow.ActionButton("Browse…", Browse, false));
            Loaded += delegate
            {
                search.Focus();
                Thread thread = new Thread(delegate()
                {
                    List<string> paths = new List<string>();
                    foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
                        try { paths.AddRange(Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories)); } catch (Exception error) { SettingsStore.Log(error); }
                    List<string> result = paths.GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(p => Path.GetFileNameWithoutExtension(p)).ToList();
                    if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(delegate { shortcuts = result; Filter(); }));
                }); thread.IsBackground = true; thread.Start();
            };
        }
        private sealed class AppChoice { public string Name { get; set; } public string Path { get; set; } }
        private void Filter()
        {
            string query = search.Text.Trim();
            List<AppChoice> matches = shortcuts.Where(p => Path.GetFileNameWithoutExtension(p).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).Select(p => new AppChoice { Name = Path.GetFileNameWithoutExtension(p), Path = p }).ToList();
            list.ItemsSource = matches; if (matches.Count > 0) list.SelectedIndex = 0;
            status.Text = matches.Count + " apps · select one to keep in your dock";
        }
        private void PinSelected()
        {
            AppChoice selected = list.SelectedItem as AppChoice; if (selected == null) return;
            Pin chosen = Pin.FromPath(selected.Path); if (chosen != null) { pin(chosen); status.Text = chosen.Name + " added to your dock"; }
        }
        private void Browse()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose an app or shortcut", Filter = "Apps and shortcuts|*.exe;*.lnk|All files|*.*", Multiselect = false };
            if (dialog.ShowDialog(this) == true) { Pin chosen = Pin.FromPath(dialog.FileName); if (chosen != null) pin(chosen); }
        }
    }
}
