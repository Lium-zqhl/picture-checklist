using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PictureChecklist
{
    internal static class DirectPaste
    {
        private sealed class ClipboardImage
        {
            internal string[] Files;
            internal byte[] Encoded;
            internal BitmapSource Bitmap;
        }

        public static IDataObject ReadClipboard()
        {
            return Clipboard.GetDataObject();
        }

        public static bool HasImages(IDataObject data)
        {
            return data != null && (data.GetDataPresent(DataFormats.FileDrop) ||
                data.GetDataPresent("PNG", false) || data.GetDataPresent(DataFormats.Bitmap, true));
        }

        private static ClipboardImage Capture(IDataObject data)
        {
            if (data.GetDataPresent(DataFormats.FileDrop)) {
                var files = data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) throw new InvalidOperationException("剪贴板中的图片文件列表为空。");
                return new ClipboardImage { Files = files.ToArray() };
            }

            if (data.GetDataPresent("PNG", false)) {
                object encoded = data.GetData("PNG", false);
                byte[] bytes = encoded as byte[];
                if (bytes != null) return new ClipboardImage { Encoded = (byte[])bytes.Clone() };
                var memory = encoded as MemoryStream;
                if (memory != null) return new ClipboardImage { Encoded = memory.ToArray() };
                var stream = encoded as Stream;
                if (stream != null) {
                    long position = stream.CanSeek ? stream.Position : 0;
                    try {
                        if (stream.CanSeek) stream.Position = 0;
                        using (var copy = new MemoryStream()) {
                            stream.CopyTo(copy);
                            return new ClipboardImage { Encoded = copy.ToArray() };
                        }
                    } finally { if (stream.CanSeek) stream.Position = position; }
                }
            }

            object value = data.GetData(DataFormats.Bitmap, true);
            var bitmap = value as BitmapSource;
            if (bitmap != null) {
                BitmapSource copy = bitmap.Clone();
                copy.Freeze();
                return new ClipboardImage { Bitmap = ScreenshotPixels.FromClipboardBitmap(copy) };
            }
            var drawing = value as System.Drawing.Image;
            if (drawing != null) {
                using (var encoded = new MemoryStream()) {
                    drawing.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
                    encoded.Position = 0;
                    var decoded = BitmapDecoder.Create(encoded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource copy = ScreenshotPixels.FromClipboardBitmap(decoded.Frames[0]);
                    if (!copy.IsFrozen) copy.Freeze();
                    return new ClipboardImage { Bitmap = copy };
                }
            }
            throw new InvalidOperationException("无法读取剪贴板图像。请重新截图后粘贴。");
        }

        // The same entry point accepts native clipboard data and isolated test DataObjects.
        // It reads the supplied data once and never changes the system clipboard.
        public static bool Apply(Shell host, IDataObject data, string initialBody = null)
        {
            if (host == null || host.Doc == null || !HasImages(data)) return false;
            string folder = host.Folder;
            Document document = host.Doc;
            ClipboardImage captured = Capture(data);
            string stage = Path.Combine(folder, ".picture-paste-" + Guid.NewGuid().ToString("N"));
            var stagedDocument = new Document();
            var stagedImages = new List<Asset>();
            var published = new List<Asset>();
            bool applied = false;
            try {
                host.RunWork("正在添加图片…", delegate {
                    Directory.CreateDirectory(stage);
                    string[] files = captured.Files;
                    if (files == null) {
                        string screenshot = Path.Combine(stage, "粘贴截图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                        if (captured.Encoded != null) File.WriteAllBytes(screenshot, captured.Encoded);
                        else {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(captured.Bitmap));
                            using (var output = new FileStream(screenshot, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                encoder.Save(output);
                        }
                        files = new[] { screenshot };
                    }
                    foreach (string file in files) stagedImages.Add(Store.AddFile(stage, stagedDocument, file));
                    return true;
                });

                if (host.Doc != document || !String.Equals(host.Folder, folder, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("当前清单已切换，请在目标清单中重新粘贴。");

                host.Change("按截图新建意见", delegate {
                    var adopted = new Dictionary<string, Asset>();
                    foreach (Asset asset in stagedDocument.Assets) {
                        bool existed = File.Exists(Store.AssetPath(folder, asset));
                        Asset copied = Store.AdoptStaged(stage, folder, document, asset);
                        if (!existed && copied.Id == asset.Id) published.Add(asset);
                        adopted.Add(asset.Id, copied);
                    }
                    Entry lastEntry = null;
                    foreach (Asset image in stagedImages) {
                        var entry = new Entry();
                        if (lastEntry == null) entry.Body = initialBody ?? "";
                        entry.AssetIds.Add(adopted[image.Id].Id);
                        document.Items.Add(entry);
                        lastEntry = entry;
                    }
                    OrganizerView.RevealImport(host, lastEntry.Id);
                    applied = true;
                }, false);
                return applied;
            }
            finally {
                if (!applied) {
                    foreach (Asset asset in published) {
                        try {
                            string path = Store.AssetPath(folder, asset);
                            if (!host.Doc.Assets.Any(a => a.RelativePath == asset.RelativePath) &&
                                File.Exists(path) && Store.HashFile(path) == asset.Sha256)
                                File.Delete(path);
                        } catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

