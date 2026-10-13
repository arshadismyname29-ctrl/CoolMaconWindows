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
    internal sealed class FolderWindow : Window
    {
        internal Action IconsChanged;
        private readonly FolderIcons icons;
        private readonly TextBox folderPath = new TextBox();
        private readonly ComboBox symbol = new ComboBox();
        private readonly Button apply, restore, open;
        private readonly TextBlock status = new TextBlock();
        private readonly ListBox managed = new ListBox();
        private readonly TextBlock globalState = new TextBlock();
        private readonly Button globalApply, globalRestore, refresh;
        private readonly Dictionary<string, Border> cards = new Dictionary<string, Border>();
        private readonly Dictionary<string, Image> previews = new Dictionary<string, Image>();
        private readonly string[] symbols = new[] { "auto", "none", "downloads", "documents", "pictures", "music", "desktop" };
        private string selectedTheme = "blue";
        private bool selecting;
        internal FolderWindow(FolderIcons service)
        {
            icons = service; Title = "CoolMac · Folder icons"; Width = 800; Height = 880; MinWidth = 720; MinHeight = 570;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = new FontFamily("Segoe UI");
            Background = DockWindow.Brush("#151A24"); Foreground = Brushes.White;
            StackPanel body = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            body.Children.Add(SettingsWindow.Text("COOLMAC / FOLDER APPEARANCE", 11, "#8EA6CF", new Thickness(0, 0, 0, 10)));
            body.Children.Add(SettingsWindow.Text("A new look for your folders", 28, "#F1F4FA", new Thickness(0, 0, 0, 6)));
            body.Children.Add(SettingsWindow.Text("Mac-inspired folder icons, right inside Windows Explorer.", 13, "#99A6BA", new Thickness(0, 0, 0, 22)));
            Grid themes = new Grid { Height = 194, Margin = new Thickness(0, 0, 0, 22) };
            for (int i = 0; i < FolderArt.Themes.Length; i++)
            {
                themes.ColumnDefinitions.Add(new ColumnDefinition());
                FolderTheme theme = FolderArt.Themes[i];
                StackPanel contents = new StackPanel { Margin = new Thickness(8, 4, 8, 14) };
                Image image = new Image { Source = FolderArt.Render(theme.Id, "none", 256), Width = 150, Height = 136, Stretch = Stretch.Uniform };
                contents.Children.Add(image); TextBlock name = SettingsWindow.Text(theme.Name, 13, "#E5EDFA", new Thickness(0)); name.HorizontalAlignment = HorizontalAlignment.Center; contents.Children.Add(name);
                Button button = DockWindow.FlatButton(contents); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Stretch;
                Border card = new Border { Child = button, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = new Thickness(i == 0 ? 0 : 6, 0, i == FolderArt.Themes.Length - 1 ? 0 : 6, 0) };
                button.Click += delegate { selectedTheme = theme.Id; UpdatePreview(); }; Grid.SetColumn(card, i); themes.Children.Add(card); cards[theme.Id] = card; previews[theme.Id] = image;
            }
            body.Children.Add(themes);
            Border globalCard = new Border { Background = DockWindow.Brush("#202A3A"), BorderBrush = DockWindow.Brush("#344259"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 22) };
            StackPanel globalContent = new StackPanel(); globalCard.Child = globalContent;
            globalContent.Children.Add(SettingsWindow.Text("ACROSS WINDOWS", 11, "#8EA6CF", new Thickness(0, 0, 0, 7)));
            globalState.FontSize = 13; globalState.Foreground = Brushes.White; globalState.Margin = new Thickness(0, 0, 0, 7); globalContent.Children.Add(globalState);
            globalContent.Children.Add(SettingsWindow.Text("Change default folder icons across this PC. Windows will ask for administrator permission and Explorer will refresh.", 12, "#B3C2D7", new Thickness(0, 0, 0, 12)));
            StackPanel globalButtons = new StackPanel { Orientation = Orientation.Horizontal };
            globalApply = SettingsWindow.ActionButton("Apply across Windows", delegate { ChangeGlobal(true); }, true);
            globalRestore = SettingsWindow.ActionButton("Restore Windows icons", delegate { ChangeGlobal(false); }, false);
            refresh = SettingsWindow.ActionButton("Refresh Explorer", RefreshExplorer, false);
            globalButtons.Children.Add(globalApply); globalButtons.Children.Add(globalRestore); globalButtons.Children.Add(refresh); globalContent.Children.Add(globalButtons);
            globalContent.Children.Add(SettingsWindow.Text("Includes common user folders and folders on your desktop. Elsewhere, custom icons and thumbnail previews may have their own appearance.", 11, "#99A6BA", new Thickness(0, 12, 0, 0)));
            body.Children.Add(globalCard);
            Grid badge = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            badge.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); badge.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock badgeLabel = SettingsWindow.Text("Folder symbol", 13, "#DCE4F2", new Thickness(0, 0, 16, 0)); badgeLabel.VerticalAlignment = VerticalAlignment.Center; badge.Children.Add(badgeLabel);
            foreach (string label in new[] { "Automatic", "None", "Downloads", "Documents", "Pictures", "Music", "Desktop" }) symbol.Items.Add(label);
            symbol.SelectedIndex = 0; symbol.Height = 32; symbol.MaxWidth = 240; symbol.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(symbol, 1); badge.Children.Add(symbol); body.Children.Add(badge);
            symbol.SelectionChanged += delegate { UpdatePreview(); };
            body.Children.Add(SettingsWindow.Text("CHOOSE A FOLDER", 11, "#8093B0", new Thickness(0, 0, 0, 8)));
            Grid picker = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); picker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            folderPath.IsReadOnly = true; folderPath.Height = 36; folderPath.Padding = new Thickness(10, 7, 10, 4); folderPath.FontSize = 12;
            folderPath.Background = DockWindow.Brush("#202A3A"); folderPath.Foreground = Brushes.White; folderPath.BorderBrush = DockWindow.Brush("#33445C"); folderPath.ToolTip = "Drag a folder here, or choose one."; picker.Children.Add(folderPath);
            Button choose = SettingsWindow.ActionButton("Choose folder…", ChooseFolder, false); choose.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(choose, 1); picker.Children.Add(choose); body.Children.Add(picker);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 13) };
            apply = SettingsWindow.ActionButton("Apply icon", Apply, true); restore = SettingsWindow.ActionButton("Restore original", Restore, false);
            open = SettingsWindow.ActionButton("Open in Explorer", delegate { DockWindow.Launch(folderPath.Text); }, false);
            actions.Children.Add(apply); actions.Children.Add(restore); actions.Children.Add(open); body.Children.Add(actions);
            status.Text = "Choose a folder to apply a style. Its original icon is saved for restoration."; status.FontSize = 12; status.Foreground = DockWindow.Brush("#99A6BA"); status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 0, 0, 21); body.Children.Add(status);
            body.Children.Add(SettingsWindow.Text("STYLED FOLDERS", 11, "#8093B0", new Thickness(0, 0, 0, 8)));
            managed.Height = 112; managed.Background = DockWindow.Brush("#1B2230"); managed.Foreground = Brushes.White; managed.BorderThickness = new Thickness(0); managed.FontSize = 12; managed.DisplayMemberPath = "Folder"; body.Children.Add(managed);
            managed.SelectionChanged += delegate { FolderBackup selected = managed.SelectedItem as FolderBackup; if (!selecting && selected != null) SelectFolder(selected.Folder); };
            body.Children.Add(SettingsWindow.Text("Icons stay applied when the dock is off. Restore original reverses the change for the selected folder.", 11, "#8093B0", new Thickness(0, 12, 0, 0)));
            AllowDrop = true;
            DragOver += delegate(object sender, DragEventArgs args) { args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; args.Handled = true; };
            Drop += delegate(object sender, DragEventArgs args)
            {
                if (!args.Data.GetDataPresent(DataFormats.FileDrop)) return;
                string folder = ((string[])args.Data.GetData(DataFormats.FileDrop)).FirstOrDefault(Directory.Exists); if (folder != null) SelectFolder(folder);
            };
            RefreshManaged(); UpdatePreview(); UpdateGlobalState();
        }
        private void ChooseFolder()
        {
            using (Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog { Description = "Choose the Windows folder whose icon you want to style", ShowNewFolderButton = false })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) SelectFolder(dialog.SelectedPath);
        }
        internal void SelectFolder(string folder)
        {
            try
            {
                folderPath.Text = FolderIcons.Normalize(folder); FolderBackup record = icons.Read(folderPath.Text);
                if (record != null) { selectedTheme = record.Theme; symbol.SelectedIndex = Math.Max(0, Array.IndexOf(symbols, record.Symbol)); }
                status.Foreground = DockWindow.Brush("#99A6BA");
                status.Text = record != null ? "This folder is styled. Apply another color, or restore its original icon." : "Ready to style " + Path.GetFileName(folderPath.Text) + ".";
                UpdatePreview();
            }
            catch (Exception error) { ShowError(error); }
        }
        private void UpdatePreview()
        {
            string badge = symbols[Math.Max(0, symbol.SelectedIndex)];
            if (badge == "auto") badge = String.IsNullOrWhiteSpace(folderPath.Text) ? "none" : FolderArt.AutoSymbol(folderPath.Text);
            foreach (FolderTheme theme in FolderArt.Themes)
            {
                bool selected = selectedTheme == theme.Id;
                cards[theme.Id].Background = DockWindow.Brush(selected ? "#243750" : "#1B2433");
                cards[theme.Id].BorderBrush = DockWindow.Brush(selected ? "#78ACFF" : "#344259"); previews[theme.Id].Source = FolderArt.Render(theme.Id, badge, 256);
            }
            bool hasFolder = Directory.Exists(folderPath.Text); apply.IsEnabled = hasFolder; apply.Opacity = hasFolder ? 1 : .4;
            restore.IsEnabled = hasFolder && File.Exists(Path.Combine(folderPath.Text, FolderIcons.Marker)); restore.Opacity = restore.IsEnabled ? 1 : .4;
            open.IsEnabled = hasFolder; open.Opacity = hasFolder ? 1 : .4;
        }
        private void Apply()
        {
            try
            {
                icons.Apply(folderPath.Text, selectedTheme, symbols[Math.Max(0, symbol.SelectedIndex)]);
                if (IconsChanged != null) IconsChanged();
                status.Foreground = DockWindow.Brush("#7BE2B7"); status.Text = FolderArt.Theme(selectedTheme).Name + " applied. Explorer may take a moment to refresh.";
                RefreshManaged(); UpdatePreview();
            }
            catch (Exception error) { ShowError(error); UpdatePreview(); }
        }
        private void Restore()
        {
            try { icons.Restore(folderPath.Text); if (IconsChanged != null) IconsChanged(); status.Foreground = DockWindow.Brush("#7BE2B7"); status.Text = "CoolMac styling removed. Your other folder settings were kept."; RefreshManaged(); UpdatePreview(); }
            catch (Exception error) { ShowError(error); }
        }
        private void RefreshManaged() { selecting = true; try { managed.ItemsSource = icons.Managed(); } finally { selecting = false; } }
        private void UpdateGlobalState()
        {
            try
            {
                GlobalFolderBackup backup = GlobalFolderTheme.Read(); bool enabled = GlobalFolderTheme.Enabled();
                globalState.Text = enabled ? "Windows default folders: " + FolderArt.Theme(backup.Theme).Name : "Windows default folders: original icons";
                globalRestore.IsEnabled = backup != null; globalRestore.Opacity = backup != null ? 1 : .4;
            }
            catch (Exception error) { globalState.Text = error.Message; globalRestore.IsEnabled = false; }
        }
        private void ChangeGlobal(bool enable)
        {
            string theme = selectedTheme; globalApply.IsEnabled = false; globalRestore.IsEnabled = false; refresh.IsEnabled = false;
            globalState.Text = "Waiting for Windows permission…";
            Thread worker = new Thread(delegate()
            {
                FolderThemeResponse response;
                try
                {
                    response = GlobalFolderTheme.RunElevated(enable ? "apply" : "restore", theme);
                    if (response.Success)
                    {
                        int common = 0; if (enable) common = GlobalFolderTheme.ApplyCommonFolders(theme); else GlobalFolderTheme.RestoreCommonFolders();
                        GlobalFolderTheme.Refresh(); bool restarted = GlobalFolderTheme.RestartExplorer();
                        response.Message = (enable ? "Default folder theme applied. " + common + " desktop and common folders styled." : "Windows folder icons restored.") + (restarted ? " Explorer refreshed." : " Finish any file transfers, then refresh Explorer.");
                    }
                }
                catch (Exception error) { response = new FolderThemeResponse { Success = false, Message = error.Message }; }
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(delegate
                {
                    globalApply.IsEnabled = true; refresh.IsEnabled = true; UpdateGlobalState(); RefreshManaged(); UpdatePreview();
                    status.Text = response.Message; status.Foreground = DockWindow.Brush(response.Success ? "#7BE2B7" : "#F4B1A4");
                    if (response.Success && IconsChanged != null) IconsChanged();
                }));
            }); worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA); worker.Start();
        }
        private void RefreshExplorer()
        {
            try
            {
                GlobalFolderBackup theme = GlobalFolderTheme.Read();
                if (theme != null && GlobalFolderTheme.Enabled()) GlobalFolderTheme.ApplyCommonFolders(theme.Theme);
                GlobalFolderTheme.Refresh(); bool refreshed = GlobalFolderTheme.RestartExplorer();
                RefreshManaged(); UpdatePreview();
                status.Text = refreshed ? "Desktop folder icons and Explorer refreshed." : "Folder icons updated. Finish current file transfers before refreshing Explorer.";
                if (IconsChanged != null) IconsChanged();
            }
            catch (Exception error) { ShowError(error); }
        }
        private void ShowError(Exception error) { status.Foreground = DockWindow.Brush("#F4B1A4"); status.Text = error.Message; }
    }
}
