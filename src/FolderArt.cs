using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoolMac
{
    internal sealed class FolderTheme
    {
        internal string Id, Name, BackTop, BackBottom, FrontTop, FrontBottom, Edge, Highlight;
    }
    internal static class FolderArt
    {
        internal static readonly FolderTheme[] Themes = new[]
        {
            new FolderTheme { Id = "blue", Name = "Classic blue", BackTop = "#67C4F2", BackBottom = "#2692D8", FrontTop = "#87DAFA", FrontBottom = "#38A9E9", Edge = "#359EDC", Highlight = "#DDF7FF" },
            new FolderTheme { Id = "teal", Name = "Soft teal", BackTop = "#5DCBBC", BackBottom = "#269E98", FrontTop = "#89E1D3", FrontBottom = "#37B5AB", Edge = "#299C94", Highlight = "#DBFFF4" },
            new FolderTheme { Id = "lavender", Name = "Lavender", BackTop = "#A79CE9", BackBottom = "#7967CD", FrontTop = "#C5BBFA", FrontBottom = "#9783E2", Edge = "#8470D2", Highlight = "#F2EEFF" }
        };
        internal static FolderTheme Theme(string id)
        {
            foreach (FolderTheme theme in Themes) if (theme.Id == id) return theme;
            throw new ArgumentException("Choose a valid folder color.");
        }
        internal static string AutoSymbol(string folder)
        {
            string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)).ToLowerInvariant();
            foreach (string symbol in new[] { "downloads", "documents", "pictures", "music", "desktop" }) if (name == symbol) return symbol;
            return "none";
        }
        internal static BitmapSource Render(string themeId, string symbol, int size)
        {
            FolderTheme theme = Theme(themeId); DrawingVisual visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
                for (int i = 5; i >= 1; i--) drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(5, 15, 38, 65)), null, new Rect(29 - i, 205 - i, 198 + i * 2, 18 + i * 2), 12, 12);
                Geometry back = Geometry.Parse("M24,74 Q24,57 44,57 L95,57 Q106,57 113,65 L128,82 L213,82 Q232,82 232,102 L232,200 Q232,222 210,222 L46,222 Q24,222 24,200 Z");
                Geometry front = Geometry.Parse("M24,96 Q24,82 38,82 L218,82 Q232,82 232,96 L232,202 Q232,222 210,222 L46,222 Q24,222 24,202 Z");
                drawing.DrawGeometry(new LinearGradientBrush(ColorOf(theme.BackTop), ColorOf(theme.BackBottom), 90), null, back);
                drawing.DrawGeometry(new LinearGradientBrush(ColorOf(theme.FrontTop), ColorOf(theme.FrontBottom), 90), new Pen(DockWindow.Brush(theme.Edge), 1.4), front);
                drawing.DrawLine(new Pen(DockWindow.Brush(theme.Highlight), 1.2), new Point(39, 84), new Point(217, 84));
                DrawSymbol(drawing, symbol, theme); drawing.Pop();
            }
            RenderTargetBitmap bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }
        private static Color ColorOf(string value) { return (Color)ColorConverter.ConvertFromString(value); }
        private static void DrawSymbol(DrawingContext drawing, string symbol, FolderTheme theme)
        {
            string path = null;
            if (symbol == "downloads") path = "M128,119 L128,171 M107,151 L128,172 L149,151 M100,185 L156,185";
            else if (symbol == "documents") path = "M107,116 L139,116 L154,132 L154,183 L103,183 L103,120 M138,117 L138,133 L153,133 M114,147 L143,147 M114,158 L143,158 M114,169 L134,169";
            else if (symbol == "pictures") path = "M98,122 L159,122 L159,181 L98,181 Z M100,173 L119,147 L136,167 L146,155 L158,173 M141,138 A4,4 0 1 1 140.9,138";
            else if (symbol == "music") path = "M117,174 L117,127 L155,118 L155,164 M118,138 L155,129 M117,174 C117,186 98,188 98,177 C98,168 117,164 117,174 M155,165 C155,177 136,179 136,168 C136,159 155,155 155,165";
            else if (symbol == "desktop") path = "M96,121 L160,121 L160,166 L96,166 Z M118,167 L118,183 M138,167 L138,183 M108,184 L148,184";
            if (path == null) return;
            Geometry geometry = Geometry.Parse(path);
            drawing.PushTransform(new TranslateTransform(0, 1.5)); drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(100, 244, 253, 255)), 5), geometry); drawing.Pop();
            SolidColorBrush ink = DockWindow.Brush(theme.Edge); ink.Opacity = .72;
            Pen pen = new Pen(ink, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            drawing.DrawGeometry(null, pen, geometry);
        }
        internal static byte[] IconBytes(string theme, string symbol)
        {
            int[] sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 }; List<byte[]> images = new List<byte[]>();
            foreach (int size in sizes)
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(theme, symbol, size)));
                using (MemoryStream stream = new MemoryStream()) { encoder.Save(stream); images.Add(stream.ToArray()); }
            }
            using (MemoryStream stream = new MemoryStream()) using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
                }
                foreach (byte[] bytes in images) writer.Write(bytes); return stream.ToArray();
            }
        }
    }
}
