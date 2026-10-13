using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoolMac
{
    internal static class FolderVerification
    {
        private static void Check(bool condition, string description, StreamWriter report)
        {
            if (!condition) throw new InvalidOperationException(description); report.WriteLine("PASS " + description); report.Flush();
        }
        internal static void Run(string directory, StreamWriter report)
        {
            string root = Path.Combine(directory, "folder-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
            FolderIcons icons = new FolderIcons(Path.Combine(root, "backups"));
            string plain = Path.Combine(root, "Plain folder"); Directory.CreateDirectory(plain);
            string sentinel = Path.Combine(plain, "keep.txt"); File.WriteAllText(sentinel, "User content stays intact.");
            FileAttributes initialAttributes = File.GetAttributes(plain);
            FolderBackup applied = icons.Apply(plain, "blue", "none");
            string ini = Path.Combine(plain, "desktop.ini");
            Check(File.ReadAllText(ini).Contains("IconResource=" + applied.IconName + ",0") && (File.GetAttributes(plain) & FileAttributes.ReadOnly) != 0, "Folder styling writes a relative icon resource and enables Windows folder customization", report);
            byte[] ico = File.ReadAllBytes(Path.Combine(plain, applied.IconName));
            Check(ico[2] == 1 && ico[4] == 10, "Folder ICO contains ten sizes from 16px to 256px", report);
            BitmapSource shellIcon = Native.Icon(plain);
            Check(shellIcon != null && BlueIcon(shellIcon), "Windows shell resolves the new blue folder icon", report);
            Save(shellIcon, Path.Combine(directory, "folder-explorer.png"));
            icons.Restore(plain);
            Check(!File.Exists(ini) && !File.Exists(Path.Combine(plain, FolderIcons.Marker)) && File.GetAttributes(plain) == initialAttributes && File.ReadAllText(sentinel) == "User content stays intact.", "Restore removes generated metadata and preserves folder attributes and user contents", report);

            string customized = Path.Combine(root, "Unicode – Documents"); Directory.CreateDirectory(customized);
            string customIni = Path.Combine(customized, "desktop.ini");
            byte[] original = FolderIcons.UnicodeBytes("; Original settings\r\n[.ShellClassInfo]\r\nLocalizedResourceName=My café folder\r\nIconFile=original.ico\r\nIconIndex=2\r\nInfoTip=Keep this text\r\n[ViewState]\r\nMode=4\r\n");
            File.WriteAllBytes(customIni, original); File.SetAttributes(customIni, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
            File.SetAttributes(customized, File.GetAttributes(customized) | FileAttributes.ReadOnly);
            FileAttributes originalFlags = File.GetAttributes(customIni);
            icons.Apply(customized, "teal", "documents"); icons.Apply(customized, "lavender", "documents");
            Check(File.ReadAllText(customIni).Contains("LocalizedResourceName=My café folder") && File.ReadAllText(customIni).Contains("Mode=4"), "Applying and recoloring preserve unrelated localized folder settings", report);
            icons.Restore(customized);
            Check(File.ReadAllBytes(customIni).SequenceEqual(original) && File.GetAttributes(customIni) == originalFlags, "Restore recovers original desktop.ini bytes and attributes after repeated style changes", report);

            string edited = Path.Combine(root, "Later metadata"); Directory.CreateDirectory(edited); icons.Apply(edited, "blue", "none");
            string editedIni = Path.Combine(edited, "desktop.ini");
            File.AppendAllText(editedIni, "\r\nInfoTip=A note added later\r\n", Encoding.Unicode); icons.Restore(edited);
            Check(File.ReadAllText(editedIni).Contains("InfoTip=A note added later") && !File.ReadAllText(editedIni).Contains("IconResource="), "Restore keeps metadata added after styling and removes only the managed icon", report);

            string external = Path.Combine(root, "Another app icon"); Directory.CreateDirectory(external); icons.Apply(external, "blue", "none");
            string externalIni = Path.Combine(external, "desktop.ini");
            using (FileStream file = new FileStream(externalIni, FileMode.Open, FileAccess.Write)) { file.SetLength(0); byte[] changed = FolderIcons.UnicodeBytes("[.ShellClassInfo]\r\nIconResource=other-app.ico,0\r\n"); file.Write(changed, 0, changed.Length); }
            icons.Restore(external);
            Check(File.ReadAllText(externalIni).Contains("IconResource=other-app.ico,0"), "Restore does not overwrite a newer icon set by another application", report);

            string moved = Path.Combine(root, "Before move"); Directory.CreateDirectory(moved); icons.Apply(moved, "blue", "none");
            string destination = Path.Combine(root, "After move");
            Check(Path.GetFullPath(moved).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(destination).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Rename fixture paths stay inside the verification workspace", report);
            Directory.Move(moved, destination); icons.Restore(destination);
            Check(!File.Exists(Path.Combine(destination, "desktop.ini")) && !File.Exists(Path.Combine(destination, FolderIcons.Marker)), "Folder icon and backup travel with a renamed folder and can still be restored", report);
            Directory.Move(destination, moved);

            string collision = Path.Combine(root, "Existing icon file"); Directory.CreateDirectory(collision);
            string unrelated = Path.Combine(collision, ".coolmac-folder.ico"); File.WriteAllText(unrelated, "Keep this unrelated file.");
            FolderBackup collisionRecord = icons.Apply(collision, "blue", "none"); icons.Restore(collision);
            Check(collisionRecord.IconName != ".coolmac-folder.ico" && File.ReadAllText(unrelated) == "Keep this unrelated file.", "Styling does not overwrite a preexisting icon file with the same name", report);
            Check(icons.Managed().Count == 0, "Restored folders are removed from the managed-folder journal", report);

            string invalid = Path.Combine(root, "Invalid backup"); Directory.CreateDirectory(invalid); string invalidMarker = Path.Combine(invalid, FolderIcons.Marker); File.WriteAllText(invalidMarker, "This file is not a CoolMac backup.");
            bool rejected = false; try { icons.Apply(invalid, "blue", "none"); } catch (ArgumentException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(invalidMarker) == "This file is not a CoolMac backup." && !File.Exists(Path.Combine(invalid, "desktop.ini")), "Invalid or foreign backup metadata is left untouched", report);

            foreach (FolderTheme theme in FolderArt.Themes) foreach (string symbol in new[] { "none", "downloads", "documents", "pictures", "music", "desktop" })
                Save(FolderArt.Render(theme.Id, symbol, 256), Path.Combine(directory, "folder-" + theme.Id + "-" + symbol + ".png"));
            FolderWindow window = new FolderWindow(icons) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            window.SelectFolder(plain); window.Show(); window.UpdateLayout(); Verification.Snapshot(window, Path.Combine(directory, "folder-icons-window.png")); window.Close();
            report.WriteLine("PASS All folder colors, embossed symbols, and the folder icon manager render successfully");
        }
        private static bool BlueIcon(BitmapSource image)
        {
            FormatConvertedBitmap converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            byte[] bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
            long red = 0, blue = 0, count = 0;
            for (int i = 0; i < bytes.Length; i += 4) if (bytes[i + 3] > 150) { blue += bytes[i]; red += bytes[i + 2]; count++; }
            return count > 0 && blue > red + count * 35;
        }
        private static void Save(BitmapSource bitmap, string path)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (FileStream file = File.Create(path)) encoder.Save(file);
        }
    }
}
