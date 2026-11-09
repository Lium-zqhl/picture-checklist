using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureChecklist
{
    // Original geometric drawings only: no screenshots, downloaded assets or external fixture files.
    internal static class SyntheticData
    {
        internal static BitmapSource Pattern(int width, int height, int seed, byte alpha)
        {
            byte[] pixels = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                int p = (y * width + x) * 4;
                pixels[p] = (byte)((x * 3 + seed) % 256);
                pixels[p + 1] = (byte)((y * 5 + seed) % 256);
                pixels[p + 2] = (byte)((x + y + seed * 2) % 256);
                pixels[p + 3] = alpha;
            }
            BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            image.Freeze(); return image;
        }

        internal static void WritePng(string path, BitmapSource image)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (FileStream output = File.Create(path)) encoder.Save(output);
        }

        static Brush Color(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        static void Text(DrawingContext draw, string value, double size, string color, double x, double y)
        {
            draw.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), size, Color(color), 1.0), new Point(x, y));
        }

        static BitmapSource Illustration(bool compact)
        {
            int width = compact ? 430 : 720, height = compact ? 170 : 420;
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext draw = visual.RenderOpen()) {
                draw.DrawRectangle(Color("#F2F5F3"), null, new Rect(0, 0, width, height));
                if (compact) {
                    draw.DrawRoundedRectangle(Brushes.White, new Pen(Color("#D8E3DD"), 1), new Rect(12, 12, 406, 146), 12, 12);
                    draw.DrawEllipse(Color("#D8EAE1"), null, new Point(54, 68), 22, 22);
                    Text(draw, "+", 30, "#2E6B55", 43, 46);
                    Text(draw, "Nothing here yet", 20, "#244A3C", 91, 41);
                    Text(draw, "Add your first note to get started.", 14, "#5F766D", 91, 79);
                    draw.DrawLine(new Pen(Color("#DB6B4D"), 3), new Point(89, 102), new Point(366, 102));
                } else {
                    draw.DrawRoundedRectangle(Brushes.White, new Pen(Color("#D8E3DD"), 1), new Rect(26, 26, 668, 368), 14, 14);
                    Text(draw, "A simple settings panel", 25, "#244A3C", 55, 51);
                    Text(draw, "Workspace name", 16, "#5F766D", 55, 111);
                    draw.DrawRoundedRectangle(Color("#F6F8F7"), new Pen(Color("#CAD8D0"), 1), new Rect(55, 142, 609, 48), 6, 6);
                    Text(draw, "My notes", 17, "#244A3C", 70, 154);
                    Text(draw, "Keep actions aligned with the form edge.", 16, "#5F766D", 55, 218);
                    draw.DrawRoundedRectangle(Color("#2E6B55"), null, new Rect(430, 291, 191, 49), 7, 7);
                    Text(draw, "Save changes", 19, "#FFFFFF", 467, 302);
                    Pen arrow = new Pen(Color("#DB6B4D"), 3);
                    draw.DrawLine(arrow, new Point(634, 316), new Point(665, 316));
                    draw.DrawLine(arrow, new Point(656, 307), new Point(665, 316));
                    draw.DrawLine(arrow, new Point(656, 325), new Point(665, 316));
                    draw.DrawLine(new Pen(Color("#DB6B4D"), 1), new Point(665, 270), new Point(665, 353));
                }
            }
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }

        internal static Document CreateDemo(string folder, string inputs)
        {
            const string stamp = "2026-01-01T00:00:00+00:00";
            string first = Path.Combine(inputs, "button-alignment.png"), second = Path.Combine(inputs, "empty-message.png");
            WritePng(first, Illustration(false)); WritePng(second, Illustration(true));
            Document doc = new Document { Id = "10000000000000000000000000000001", Name = "入门演示 · 图文修改清单", Updated = stamp };
            Asset a = Store.AddFile(folder, doc, first), b = Store.AddFile(folder, doc, second);
            a.Id = "20000000000000000000000000000001"; b.Id = "20000000000000000000000000000002";
            Entry one = new Entry { Id = "30000000000000000000000000000001", Title = "调整按钮对齐", Updated = stamp,
                Body = "将保存按钮向右移动，与输入框的右边缘对齐。点击图片可以放大核对。", Note = "这是程序绘制的演示画面，不对应真实项目。" };
            one.AssetIds.Add(a.Id);
            Entry two = new Entry { Id = "30000000000000000000000000000002", Title = "让空白状态提示更清楚", Updated = stamp,
                Body = "将提示文字改为明确的下一步操作，例如“添加第一条意见”。这是一张小尺寸演示图片，可测试自动放大。" };
            two.AssetIds.Add(b.Id);
            Entry three = new Entry { Id = "30000000000000000000000000000003", Title = "核对操作说明", Updated = stamp, Status = "done",
                Body = "确认说明包含：每张截图建立一条意见、在输入框直接粘贴、点击复选框完成。纯文字意见也可以独立保存。", Note = "演示已完成状态；再次点击复选框可以重新打开。" };
            doc.Items.Add(one); doc.Items.Add(two); doc.Items.Add(three);
            Store.Save(folder, doc); return doc;
        }
    }
}
