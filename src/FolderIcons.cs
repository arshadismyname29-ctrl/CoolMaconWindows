using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace CoolMac
{
    public sealed class FolderBackup
    {
        public string Format { get; set; }
        public string Id { get; set; }
        public string Folder { get; set; }
        public string Theme { get; set; }
        public string Symbol { get; set; }
        public string IconName { get; set; }
        public bool HadIni { get; set; }
        public string OriginalIni { get; set; }
        public int IniAttributes { get; set; }
        public int FolderAttributes { get; set; }
        public long IniWriteTicks { get; set; }
        public List<string> IconHashes { get; set; }
        public List<string> IniHashes { get; set; }
    }
    internal sealed class FolderIcons
    {
        internal const string Marker = ".coolmac-folder.json";
        private const string Format = "CoolMaconWindows.FolderIcon.v1";
        internal readonly string Storage;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(uint change, uint flags, string first, string second);
        internal FolderIcons(string storage) { Storage = storage; }
        internal static FolderIcons Default() { return new FolderIcons(Path.Combine(SettingsStore.Folder, "FolderBackups")); }
        internal static string Normalize(string folder)
        {
            string full = Path.GetFullPath(folder);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Choose an existing folder.");
            string root = Path.GetPathRoot(full);
            if (String.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a folder rather than a drive.");
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Choose the actual folder rather than a linked folder.");
            return full.TrimEnd(Path.DirectorySeparatorChar);
        }
        internal FolderBackup Read(string folder)
        {
            string marker = Path.Combine(folder, Marker); if (!File.Exists(marker)) return null;
            if (new FileInfo(marker).Length > 2000000) throw new InvalidDataException("This folder's icon backup cannot be read.");
            FolderBackup record = new JavaScriptSerializer { MaxJsonLength = 2000000 }.Deserialize<FolderBackup>(File.ReadAllText(marker));
            Guid id;
            if (record == null || record.Format != Format || !Guid.TryParseExact(record.Id, "N", out id) || String.IsNullOrWhiteSpace(record.IconName)
                || Path.GetFileName(record.IconName) != record.IconName || record.IconName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !record.IconName.StartsWith(".coolmac-folder", StringComparison.Ordinal) || !record.IconName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                || record.IconHashes == null || record.IniHashes == null) throw new InvalidDataException("This folder's icon backup is invalid. Its files were left unchanged.");
            record.Folder = folder; return record;
        }
        internal FolderBackup Apply(string folder, string theme, string symbol)
        {
            folder = Normalize(folder); FolderArt.Theme(theme);
            if (symbol == "auto") symbol = FolderArt.AutoSymbol(folder);
            if (!new[] { "none", "downloads", "documents", "pictures", "music", "desktop" }.Contains(symbol)) throw new ArgumentException("Choose a valid folder symbol.");
            byte[] icon = FolderArt.IconBytes(theme, symbol);
            string iniPath = Path.Combine(folder, "desktop.ini"); FolderBackup record = Read(folder);
            if (record == null)
            {
                bool hadIni = File.Exists(iniPath);
                if (hadIni && new FileInfo(iniPath).Length > 1000000) throw new InvalidDataException("This folder has unusually large custom settings. Choose another folder.");
                string iconName = ".coolmac-folder.ico";
                if (File.Exists(Path.Combine(folder, iconName))) iconName = ".coolmac-folder-" + Guid.NewGuid().ToString("N") + ".ico";
                record = new FolderBackup { Format = Format, Id = Guid.NewGuid().ToString("N"), Folder = folder, IconName = iconName, HadIni = hadIni,
                    OriginalIni = hadIni ? Convert.ToBase64String(File.ReadAllBytes(iniPath)) : "", IniAttributes = hadIni ? (int)File.GetAttributes(iniPath) : 0,
                    FolderAttributes = (int)File.GetAttributes(folder), IniWriteTicks = hadIni ? File.GetLastWriteTimeUtc(iniPath).Ticks : 0,
                    IconHashes = new List<string>(), IniHashes = new List<string>() };
            }
            string iconPath = Path.Combine(folder, record.IconName);
            if (File.Exists(iconPath) && !record.IconHashes.Contains(Hash(File.ReadAllBytes(iconPath)))) throw new InvalidOperationException("The icon file was edited outside the app. Restore this folder before applying another style.");
            string existing = File.Exists(iniPath) ? Decode(File.ReadAllBytes(iniPath)) : "";
            byte[] ini = UnicodeBytes(Patch(existing, new[] { "IconResource=" + record.IconName + ",0" }));
            record.Theme = theme; record.Symbol = symbol;
            AddHash(record.IconHashes, Hash(icon)); AddHash(record.IniHashes, Hash(ini));
            Directory.CreateDirectory(Storage); SaveRecord(record); // Durable backup before changing the icon or desktop.ini.
            Write(iconPath, icon); File.SetAttributes(iconPath, FileAttributes.Hidden | FileAttributes.System);
            FileAttributes attributes = File.Exists(iniPath) ? File.GetAttributes(iniPath) : FileAttributes.Archive;
            Write(iniPath, ini); File.SetAttributes(iniPath, attributes | FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            Notify(folder); return record;
        }
        internal void Restore(string folder)
        {
            folder = Normalize(folder); FolderBackup record = Read(folder);
            if (record == null) throw new InvalidOperationException("This folder does not have an icon applied by CoolMac.");
            byte[] original = record.HadIni ? Convert.FromBase64String(record.OriginalIni) : null;
            string iniPath = Path.Combine(folder, "desktop.ini");
            bool exactRestore = false;
            if (File.Exists(iniPath))
            {
                byte[] current = File.ReadAllBytes(iniPath); string text = Decode(current);
                if (record.IniHashes.Contains(Hash(current)))
                {
                    exactRestore = true;
                    if (record.HadIni)
                    {
                        Write(iniPath, original); File.SetAttributes(iniPath, File.GetAttributes(iniPath) & ~FileAttributes.ReadOnly);
                        try { if (record.IniWriteTicks > 0) File.SetLastWriteTimeUtc(iniPath, new DateTime(record.IniWriteTicks, DateTimeKind.Utc)); }
                        finally { File.SetAttributes(iniPath, (FileAttributes)record.IniAttributes); }
                    }
                    else Delete(iniPath);
                }
                else if (OwnIcon(text, record, folder))
                {
                    string patched = Patch(text, record.HadIni ? IconLines(Decode(original)) : new string[0]);
                    if (!record.HadIni && !HasContent(patched)) Delete(iniPath);
                    else { FileAttributes attributes = File.GetAttributes(iniPath); Write(iniPath, UnicodeBytes(patched)); File.SetAttributes(iniPath, record.HadIni ? (FileAttributes)record.IniAttributes : attributes); }
                }
                // A different icon set outside CoolMac is kept, along with any newer folder metadata.
            }
            bool customSettings = File.Exists(iniPath) && HasContent(Decode(File.ReadAllBytes(iniPath)));
            if (((FileAttributes)record.FolderAttributes & FileAttributes.ReadOnly) == 0 && (exactRestore || !customSettings)) File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
            string iconPath = Path.Combine(folder, record.IconName);
            if (File.Exists(iconPath) && record.IconHashes.Contains(Hash(File.ReadAllBytes(iconPath)))) Delete(iconPath);
            Delete(Path.Combine(folder, Marker));
            string journal = Path.Combine(Storage, record.Id + ".json"); if (File.Exists(journal)) Delete(journal);
            Notify(folder);
        }
        internal List<FolderBackup> Managed()
        {
            List<FolderBackup> result = new List<FolderBackup>(); if (!Directory.Exists(Storage)) return result;
            foreach (string file in Directory.GetFiles(Storage, "*.json"))
                try
                {
                    FolderBackup record = new JavaScriptSerializer { MaxJsonLength = 2000000 }.Deserialize<FolderBackup>(File.ReadAllText(file));
                    if (record != null && record.Format == Format) result.Add(record);
                }
                catch { }
            return result.OrderBy(record => record.Folder, StringComparer.OrdinalIgnoreCase).ToList();
        }
        private void SaveRecord(FolderBackup record)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer { MaxJsonLength = 2000000 }.Serialize(record));
            string marker = Path.Combine(record.Folder, Marker); Write(marker, bytes); File.SetAttributes(marker, FileAttributes.Hidden | FileAttributes.System);
            Write(Path.Combine(Storage, record.Id + ".json"), bytes);
        }
        private static void AddHash(List<string> hashes, string hash) { if (!hashes.Contains(hash)) hashes.Add(hash); }
        internal static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        internal static byte[] UnicodeBytes(string value) { return Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(value)).ToArray(); }
        internal static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            try { return new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { return Encoding.Default.GetString(bytes); }
        }
        private static bool IsIconKey(string line) { return Regex.IsMatch(line, @"^\s*(IconResource|IconFile|IconIndex)\s*=", RegexOptions.IgnoreCase); }
        private static bool OwnIcon(string text, FolderBackup record, string folder)
        {
            string resource = IconLines(text).LastOrDefault(line => Regex.IsMatch(line, @"^\s*IconResource\s*=", RegexOptions.IgnoreCase));
            if (resource == null) resource = IconLines(text).LastOrDefault(line => Regex.IsMatch(line, @"^\s*IconFile\s*=", RegexOptions.IgnoreCase));
            if (resource == null) return false;
            string value = resource.Substring(resource.IndexOf('=') + 1).Trim();
            int index = value.LastIndexOf(','); if (index >= 0 && value.Substring(index + 1).Trim() == "0") value = value.Substring(0, index).Trim();
            value = value.Trim('"');
            return String.Equals(value, record.IconName, StringComparison.OrdinalIgnoreCase) || String.Equals(value, ".\\" + record.IconName, StringComparison.OrdinalIgnoreCase) || String.Equals(value, Path.Combine(folder, record.IconName), StringComparison.OrdinalIgnoreCase);
        }
        internal static string[] IconLines(string text)
        {
            List<string> lines = new List<string>(); bool shell = false;
            foreach (string line in Regex.Split(text, "\r\n|\n|\r"))
            {
                if (Regex.IsMatch(line, @"^\s*\[.*\]\s*$")) shell = line.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                else if (shell && IsIconKey(line)) lines.Add(line);
            }
            return lines.ToArray();
        }
        internal static string Patch(string text, IEnumerable<string> iconLines)
        {
            string newline = text.Contains("\r\n") || !text.Contains("\n") ? "\r\n" : "\n";
            List<string> result = new List<string>(); bool shell = false, inserted = false, hadShell = false;
            foreach (string line in Regex.Split(text, "\r\n|\n|\r"))
            {
                if (Regex.IsMatch(line, @"^\s*\[.*\]\s*$"))
                {
                    if (shell && !inserted) { result.AddRange(iconLines); inserted = true; }
                    shell = line.Trim().Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase); hadShell |= shell;
                }
                if (!(shell && IsIconKey(line))) result.Add(line);
            }
            if (shell && !inserted) { result.AddRange(iconLines); inserted = true; }
            if (!hadShell) { result.Add("[.ShellClassInfo]"); result.AddRange(iconLines); }
            return String.Join(newline, result);
        }
        private static bool HasContent(string text) { return Regex.Split(text, "\r\n|\n|\r").Any(line => !String.IsNullOrWhiteSpace(line) && !Regex.IsMatch(line, @"^\s*\[.*\]\s*$")); }
        private static void Write(string path, byte[] bytes)
        {
            FileAttributes attributes = File.Exists(path) ? File.GetAttributes(path) : FileAttributes.Archive;
            if (File.Exists(path)) File.SetAttributes(path, attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.SetAttributes(path, attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));
                        if (File.Exists(temporary)) File.SetAttributes(temporary, FileAttributes.Archive);
                        if (File.Exists(path)) File.Replace(temporary, path, null, true); else File.Move(temporary, path);
                        break;
                    }
                    catch (IOException) { if (attempt >= 11) throw; Thread.Sleep(50); }
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (File.Exists(path)) File.SetAttributes(path, attributes);
            }
        }
        private static void Delete(string path) { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); File.Delete(path); }
        internal static void Notify(string folder)
        {
            SHChangeNotify(0x2000, 0x1005, folder, null); string parent = Path.GetDirectoryName(folder);
            if (parent != null) SHChangeNotify(0x1000, 0x1005, parent, null);
        }
        internal static string CreateDemo(string projectRoot)
        {
            string root = Path.Combine(projectRoot, "Folder Preview"); Directory.CreateDirectory(root);
            FolderIcons icons = Default();
            foreach (string name in new[] { "Projects", "Documents", "Downloads" })
            {
                string folder = Path.Combine(root, name); Directory.CreateDirectory(folder); icons.Apply(folder, "blue", "auto");
            }
            icons.Apply(root, "blue", "none"); return root;
        }
    }
}
