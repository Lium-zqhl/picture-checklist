using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureChecklist
{
    // Legacy clipboard DIB bitmaps sometimes expose an unused fourth byte as alpha.
    // Explicit encoded PNG data is never sent through the clipboard repair path.
    internal static class ScreenshotPixels
    {
        internal static bool IsLegacyScreenshot(string originalName)
        {
            return originalName != null && Regex.IsMatch(originalName,
                @"\A截图-[0-9]{8}-[0-9]{6}(?:-[0-9]{3})?\.png\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static BitmapSource FromClipboardBitmap(BitmapSource source)
        {
            return RecoverUnusedAlpha(source, false);
        }

        internal static BitmapSource RecoverUnusedAlpha(BitmapSource source, bool requireColor)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32) return source;
            int width = source.PixelWidth, height = source.PixelHeight;
            int stride = checked(width * 4);
            long length = (long)stride * height;
            if (stride <= 0 || height <= 0 || length > 1024L * 1024 * 1024)
                throw new InvalidDataException("图片像素尺寸超限。");
            // Scan before conversion: premultiplying zero alpha would discard the RGB evidence.
            byte[] row = new byte[stride];
            bool hasColor = false;
            for (int y = 0; y < height; y++) {
                source.CopyPixels(new Int32Rect(0, y, width, 1), row, stride, 0);
                for (int p = 0; p < stride; p += 4) {
                    if (row[p + 3] != 0) return source; // Genuine transparency is retained byte-for-byte.
                    if (row[p] != 0 || row[p + 1] != 0 || row[p + 2] != 0) hasColor = true;
                }
            }
            if (requireColor && !hasColor) return source;
            byte[] pixels = new byte[(int)length];
            source.CopyPixels(pixels, stride, 0);
            for (int p = 3; p < pixels.Length; p += 4) pixels[p] = 255;
            BitmapSource result = BitmapSource.Create(width, height, source.DpiX, source.DpiY,
                PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        internal static BitmapSource ReadDisplay(string path, int maxEdge, string originalName)
        {
            if (!IsLegacyScreenshot(originalName)) return PictureViewer.ReadImage(path, maxEdge);
            BitmapSource full = PictureViewer.ReadImage(path, 0);
            BitmapSource recovered = RecoverUnusedAlpha(full, true);
            if (maxEdge <= 0 || Math.Max(full.PixelWidth, full.PixelHeight) <= maxEdge) return recovered;
            if (Object.ReferenceEquals(full, recovered)) return PictureViewer.ReadImage(path, maxEdge);
            double scale = (double)maxEdge / Math.Max(recovered.PixelWidth, recovered.PixelHeight);
            var scaled = new TransformedBitmap(recovered, new ScaleTransform(scale, scale));
            // Copy the small result so a thumbnail does not retain the full bitmap as its source.
            var result = new WriteableBitmap(scaled);
            result.Freeze();
            return result;
        }
    }
}
