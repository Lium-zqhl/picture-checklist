using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PictureChecklist
{
    // This executable compiles with the product source. It never accesses the system clipboard.
    public static class PublicChecks
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly List<string> report = new List<string>();
        static string run, folder, demo;
        static Shell shell;
        static int assertions, groups, exit = 1;
        static Exception unexpectedDialog;
        static FileStream saveLock;
        static HookProc hookProc;
        static IntPtr hook;
        delegate IntPtr HookProc(int code, IntPtr window, IntPtr detail);
        delegate bool ChildProc(IntPtr window, IntPtr unused);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int kind, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr handle);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr window, IntPtr detail);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr window, ChildProc proc, IntPtr unused);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);

        static void Log(string message)
        {
            report.Add(message); Console.WriteLine(message);
            File.WriteAllLines(Path.Combine(run, "report.txt"), report, Utf8);
        }
        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Pass(string message) { groups++; Log("PASS " + groups + " - " + message); }
        static void Rejected(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (IOException) { rejected = true; }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        static IntPtr WindowHook(int code, IntPtr window, IntPtr detail)
        {
            if (code == 5) {
                StringBuilder type = new StringBuilder(100); GetClassName(window, type, type.Capacity);
                if (type.ToString() == "#32770") {
                    List<string> messages = new List<string>();
                    EnumChildWindows(window, delegate(IntPtr child, IntPtr unused) {
                        StringBuilder value = new StringBuilder(4096); GetWindowText(child, value, value.Capacity);
                        if (value.Length > 0) messages.Add(value.ToString()); return true;
                    }, IntPtr.Zero);
                    unexpectedDialog = new InvalidOperationException("Unexpected native dialog in the test UI thread: " + String.Join(" | ", messages));
                    ShowWindow(window, 0); PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero);
                }
                return new IntPtr(1); // CBT hook is scoped to this thread, never another process.
            }
            return CallNextHookEx(hook, code, window, detail);
        }
        static IEnumerable<T> Elements<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            T value = root as T; if (value != null) yield return value;
            for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
                foreach (T item in Elements<T>(VisualTreeHelper.GetChild(root, n))) yield return item;
        }
        static T Find<T>(DependencyObject root, string id) where T : DependencyObject
        {
            return Elements<T>(root).FirstOrDefault(x => AutomationProperties.GetAutomationId(x) == id);
        }
        static T Need<T>(DependencyObject root, string id) where T : DependencyObject
        {
            T found = Find<T>(root, id); Check(found != null, "Missing control: " + id); return found;
        }
        static T Need<T>(string id) where T : DependencyObject { shell.UpdateLayout(); return Need<T>((DependencyObject)shell.Content, id); }
        static void Click(DependencyObject root, string id)
        {
            Button button = Need<Button>(root, id); Check(button.IsEnabled, "Disabled control: " + id);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); shell.UpdateLayout();
        }
        static void Click(string id) { Click((DependencyObject)shell.Content, id); }
        static async Task Wait(string reason, Func<bool> predicate)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!predicate()) {
                if (unexpectedDialog != null) throw unexpectedDialog;
                if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException(reason);
                await Task.Delay(30); shell.UpdateLayout();
            }
            if (unexpectedDialog != null) throw unexpectedDialog;
            await Task.Delay(50); shell.UpdateLayout();
        }
        static async Task Open(string path)
        {
            shell = new Shell(path) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            shell.Show();
            await Wait("Shell did not load", delegate { return shell.Doc != null && shell.Content != null; });
            shell.Float();
            await Wait("Floating composer did not load", delegate { return Find<TextBox>((DependencyObject)shell.Content, "FloatComposerInput") != null; });
        }
        static byte[] Pixels(BitmapSource image)
        {
            BitmapSource bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            byte[] bytes = new byte[checked(bgra.PixelWidth * bgra.PixelHeight * 4)];
            bgra.CopyPixels(bytes, bgra.PixelWidth * 4, 0); return bytes;
        }
        static DataObject BitmapData(int seed, byte alpha)
        {
            DataObject data = new DataObject(); data.SetData(DataFormats.Bitmap, SyntheticData.Pattern(128, 80, seed, alpha), true); return data;
        }
        static void PasteEvent(IDataObject data, string format)
        {
            DataObjectPastingEventArgs e = new DataObjectPastingEventArgs(data, false, format) { RoutedEvent = DataObject.PastingEvent };
            Need<TextBox>("FloatComposerInput").RaiseEvent(e); shell.UpdateLayout();
            if (unexpectedDialog != null) throw unexpectedDialog;
            Check(e.CommandCancelled, "Image paste must be consumed by the composer");
        }
        static async Task Reveal(string id)
        {
            // Use the real navigation command so pending layout work also targets this entry.
            Click("FloatNumber_" + id);
            await Wait("Card was not realized", delegate { return Find<CheckBox>((DependencyObject)shell.Content, "FloatDone_" + id) != null; });
        }
        static void Complete(string id, bool done)
        {
            CheckBox box = Need<CheckBox>("FloatDone_" + id);
            Check(box.IsChecked != done, "Completion must change its previous value");
            box.IsChecked = done; box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); shell.UpdateLayout();
        }
        static void Render(string filename)
        {
            shell.UpdateLayout(); FrameworkElement content = (FrameworkElement)shell.Content;
            // WPF keeps a root content margin in the rendered visual offset. Include that area in the capture.
            int width = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
            int height = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
            Check(width > 100 && height > 100, "WPF content has a renderable size");
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual background = new DrawingVisual();
            using (DrawingContext draw = background.RenderOpen()) {
                Rect area = new Rect(0, 0, width, height);
                draw.DrawRectangle(shell.Background, null, area);
                VisualBrush brush = new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = area, Stretch = Stretch.Fill };
                draw.DrawRectangle(brush, null, area);
            }
            bitmap.Render(background); SyntheticData.WritePng(Path.Combine(run, filename), bitmap);
            Log("RENDER " + filename + " " + width + "x" + height + " (WPF content render, not a desktop screenshot)");
        }

        static void StorageCases()
        {
            string storage = Path.Combine(run, "storage"), inputs = Path.Combine(run, "storage-inputs");
            string one = Path.Combine(inputs, "one.png"), two = Path.Combine(inputs, "two.png");
            SyntheticData.WritePng(one, SyntheticData.Pattern(96, 60, 7, 255));
            SyntheticData.WritePng(two, SyntheticData.Pattern(96, 60, 11, 255));
            Document doc = new Document { Name = "Synthetic storage checks" };
            Asset a = Store.AddFile(storage, doc, one), b = Store.AddFile(storage, doc, two);
            Entry first = new Entry { Title = "First", Body = "Original first body", Note = "First note", Status = "done" };
            Entry second = new Entry { Title = "Second", Note = "Second note" };
            first.AssetIds.Add(a.Id); second.AssetIds.Add(b.Id); doc.Items.Add(first); doc.Items.Add(second);
            Store.Save(storage, doc);
            string original = ModelJson.Write(doc), firstId = first.Id, secondId = second.Id;
            doc.Items.Reverse(); doc.Items.Insert(1, new Entry { Title = "Inserted text" });
            first.Deleted = true; second.Status = "done"; Store.Save(storage, doc);
            Document saved = Store.Load(storage);
            Check(saved.Items[0].Id == secondId && saved.Items[2].Id == firstId, "Reordering preserves stable IDs");
            Check(saved.Items[2].Deleted && saved.Items[2].Note == "First note" && saved.Items[2].Status == "done", "Deletion and sorting keep note/status attached");
            Check(saved.Items[0].AssetIds.SequenceEqual(new[] { b.Id }) && saved.Items[2].AssetIds.SequenceEqual(new[] { a.Id }), "Sorting keeps image associations");
            Check(saved.Items.Select(x => x.Id).Distinct().Count() == 3, "Inserted entry receives a distinct stable ID");
            File.Delete(one); File.Delete(two);
            Check(Store.HashFile(Store.AssetPath(storage, a)) == a.Sha256 && Store.HashFile(Store.AssetPath(storage, b)) == b.Sha256, "Imported images survive removal of original temporary files");
            string previous = Path.Combine(run, "restored-previous");
            Document restored = Store.RestorePrevious(storage, previous);
            Check(ModelJson.Write(restored) == original, "Previous save restores the exact original document");
            Check(restored.Assets.All(x => Store.HashFile(Store.AssetPath(previous, x)) == x.Sha256), "Previous save restoration includes verified images");
            Check(Store.Load(storage).Items.Count == 3, "Restoring previous save does not overwrite source");
            Pass("Stable IDs, insertion/reorder/deletion, local image ownership and previous-save recovery");

            string zip = Path.Combine(run, "portable-backup.zip"), imported = Path.Combine(run, "portable-restored");
            Package.Export(storage, saved, zip); Package.Import(zip, imported);
            Document portable = Store.Load(imported);
            Check(ModelJson.Write(saved) == ModelJson.Write(portable), "Package restores text, IDs, notes, completion and order");
            Check(portable.Assets.All(x => Store.HashFile(Store.AssetPath(imported, x)) == x.Sha256), "Package restores exact original image bytes");
            portable.Name = "Independent restored copy"; Store.Save(imported, portable);
            Check(Store.Load(storage).Name == "Synthetic storage checks", "Restored copy is independent of original");
            string tampered = Path.Combine(run, "tampered.zip"); File.Copy(zip, tampered);
            using (ZipArchive archive = ZipFile.Open(tampered, ZipArchiveMode.Update)) {
                archive.GetEntry(Store.ProjectFile).Delete();
                using (StreamWriter output = new StreamWriter(archive.CreateEntry(Store.ProjectFile).Open())) output.Write("{}");
            }
            string badTarget = Path.Combine(run, "rejected-package");
            Rejected(delegate { Package.Import(tampered, badTarget); }, "Tampered backup must fail integrity validation");
            Check(!Directory.Exists(badTarget), "Failed import does not publish a partial checklist");
            Pass("Portable backup restoration, independence and rejection of tampered contents");

            Document stale = Store.Load(storage), fresh = Store.Load(storage);
            fresh.Name = "Latest saved version"; Store.Save(storage, fresh);
            string latestHash = Store.HashFile(Path.Combine(storage, Store.ProjectFile));
            stale.Name = "Stale write";
            Rejected(delegate { Store.Save(storage, stale); }, "Stale document must not overwrite newer save");
            Check(Store.HashFile(Path.Combine(storage, Store.ProjectFile)) == latestHash, "Conflict leaves persisted document unchanged");
            using (FileStream locked = new FileStream(Path.Combine(storage, ".save.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                Rejected(delegate { Store.Save(storage, fresh); }, "Exclusive save lock is reported as a failure");
            Check(Store.HashFile(Path.Combine(storage, Store.ProjectFile)) == latestHash, "Failed save leaves last valid document unchanged");
            Pass("Stale-write and locked-save failures preserve the last saved document");
        }

        static void ImageCases()
        {
            BitmapSource raw = SyntheticData.Pattern(128, 80, 23, 0);
            BitmapSource fixedImage = ScreenshotPixels.FromClipboardBitmap(raw);
            byte[] before = Pixels(raw), after = Pixels(fixedImage);
            Check(Enumerable.Range(0, after.Length / 4).All(n => after[n * 4 + 3] == 255), "Zero-alpha bitmap becomes opaque");
            Check(Enumerable.Range(0, after.Length).Where(n => n % 4 != 3).All(n => before[n] == after[n]), "Alpha repair retains RGB evidence");
            BitmapSource partial = SyntheticData.Pattern(18, 12, 9, 128);
            Check(Object.ReferenceEquals(partial, ScreenshotPixels.FromClipboardBitmap(partial)), "Intentional partial alpha is unchanged");
            byte[] blackPixels = new byte[4 * 4 * 4];
            BitmapSource black = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, blackPixels, 16);
            Check(Pixels(ScreenshotPixels.FromClipboardBitmap(black))[3] == 255, "All-black clipboard bitmap also recovers unused alpha");
            string dir = Path.Combine(run, "legacy-alpha"), rawPath = Path.Combine(run, "image-inputs", "截图-20260101-120000-001.png");
            SyntheticData.WritePng(rawPath, raw);
            Document doc = new Document(); Asset asset = Store.AddFile(dir, doc, rawPath); Store.Save(dir, doc);
            string hash = Store.HashFile(Store.AssetPath(dir, asset));
            BitmapSource displayed = PictureViewer.ReadAsset(dir, asset, 0), thumb = PictureViewer.ReadAsset(dir, asset, 64);
            Check(Pixels(displayed)[3] == 255 && displayed.PixelWidth == 128, "Legacy-named screenshot recovers for full display");
            Check(thumb.PixelWidth <= 64 && Pixels(thumb)[3] > 0, "Legacy screenshot recovers before thumbnail reduction");
            Check(Store.HashFile(Store.AssetPath(dir, asset)) == hash, "Compatibility display never rewrites the saved original");
            Asset explicitPng = new Asset { Id = asset.Id, OriginalName = "intentional-transparent.png", RelativePath = asset.RelativePath, Sha256 = asset.Sha256 };
            Check(Pixels(PictureViewer.ReadAsset(dir, explicitPng, 0))[3] == 0, "Explicit transparent PNG is not treated as legacy screenshot");
            Pass("Synthetic zero-alpha repair, real transparency preservation and non-destructive legacy display");
        }

        static async Task UiCases()
        {
            await Open(folder); Render("empty-window.png");
            Need<TextBox>("FloatComposerInput").Text = "Body attached only to the first screenshot.";
            PasteEvent(BitmapData(41, 0), DataFormats.Bitmap);
            Check(shell.Doc.Items.Count == 1 && shell.Doc.Items[0].AssetIds.Count == 1, "First paste creates one image entry");
            string first = shell.Doc.Items[0].Id, firstAsset = shell.Doc.Items[0].AssetIds[0];
            Check(shell.Doc.Items[0].Body.Contains("first screenshot") && Need<TextBox>("FloatComposerInput").Text == "", "Image paste consumes and clears its text draft");
            Check(Pixels(PictureViewer.ReadAsset(folder, shell.Doc.Assets[0], 0))[3] == 255, "Actual paste path repairs zero-alpha bitmap before save");
            PasteEvent(BitmapData(41, 0), DataFormats.Bitmap);
            Check(shell.Doc.Items.Count == 2 && shell.Doc.Items[1].AssetIds.Count == 1, "Second paste creates a second entry");
            Check(shell.Doc.Items[1].Id != first && shell.Doc.Items[1].Body == "", "Repeated image is an independent opinion without duplicated draft");
            Check(shell.Doc.Assets.Count == 1 && shell.Doc.Items[1].AssetIds[0] == firstAsset, "Identical image bytes share an asset, never an opinion ID");
            string imageOne = Path.Combine(run, "drop-inputs", "one.png"), imageTwo = Path.Combine(run, "drop-inputs", "two.png");
            SyntheticData.WritePng(imageOne, SyntheticData.Pattern(120, 70, 1, 255));
            SyntheticData.WritePng(imageTwo, SyntheticData.Pattern(120, 70, 2, 255));
            DataObject files = new DataObject(); files.SetData(DataFormats.FileDrop, new[] { imageOne, imageTwo });
            PasteEvent(files, DataFormats.FileDrop);
            Check(shell.Doc.Items.Count == 4 && shell.Doc.Items.All(x => x.AssetIds.Count == 1), "Multi-file paste creates exactly one entry per image");
            Check(shell.Doc.Items.Select(x => x.Id).Distinct().Count() == 4, "Every image entry receives a unique stable ID");
            Check(Store.Load(folder).Items.Count == 4, "All pasted image entries are persisted");
            File.Delete(imageOne); File.Delete(imageTwo);
            Check(shell.Doc.Assets.All(x => File.Exists(Store.AssetPath(folder, x))), "Pasted image files are owned by the checklist");
            Pass("Composer PastingEvent: one image per opinion, repeated image independence, multiple files and zero-alpha screenshot");

            DataObject text = new DataObject(); text.SetData(DataFormats.UnicodeText, "A text note");
            DataObjectPastingEventArgs textEvent = new DataObjectPastingEventArgs(text, false, DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
            Need<TextBox>("FloatComposerInput").RaiseEvent(textEvent);
            Check(!textEvent.CommandCancelled, "Plain text paste remains available to the TextBox");
            const string body = "Check the help text.\r\nKeep the next action clear.";
            Need<TextBox>("FloatComposerInput").Text = body; shell.RefreshView();
            Check(Need<TextBox>("FloatComposerInput").Text == body, "Draft survives view refresh");
            Click("FloatAddText");
            Check(shell.Doc.Items.Count == 5 && shell.Doc.Items.Last().Body == body && shell.Doc.Items.Last().AssetIds.Count == 0, "Multiline text creates a separate image-free opinion");
            Check(Need<TextBox>("FloatComposerInput").Text == "", "Adding text clears the visible composer");
            await Reveal(first); Complete(first, true);
            Check(Store.Load(folder).Items.Single(x => x.Id == first).Status == "done", "Clicking the checkbox persists completion");
            Click("Undo"); await Reveal(first);
            Check(Store.Load(folder).Items.Single(x => x.Id == first).Status == "todo", "Undo restores the pre-completion state");
            Complete(first, true); await Reveal(first); Complete(first, false);
            Check(Store.Load(folder).Items.Single(x => x.Id == first).Status != "done", "Clicking again reopens a completed opinion");
            await Reveal(first); Complete(first, true);
            shell.Close(); await Open(folder);
            Check(shell.Doc.Items.Count == 5 && shell.Doc.Items.Single(x => x.Id == first).Status == "done", "Close and reopen retains opinion count and completion");
            Check(shell.Doc.Items.Last().Body == body, "Close and reopen retains the exact multiline text");
            Click("FloatFilterDone"); Check(Need<ListBox>("FloatCards").Items.Count == 1, "Completed filter contains just the checked opinion");
            Click("FloatFilterOpen"); Check(Need<ListBox>("FloatCards").Items.Count == 4, "Open filter excludes the checked opinion");
            Click("FloatFilterAll"); Click("FloatSearchToggle"); Need<TextBox>("FloatSearch").Text = "next action";
            await Wait("Search did not filter", delegate { return Need<ListBox>("FloatCards").Items.Count == 1; });
            Check(Need<ListBox>("FloatCards").Items.Cast<Entry>().Single().Body == body, "Search finds the intended body without changing IDs");
            Need<TextBox>("FloatSearch").Text = "";
            await Wait("Search did not clear", delegate { return Need<ListBox>("FloatCards").Items.Count == 5; });
            Pass("Text draft/add, checkbox completion/reopen, undo, persistence, filters and search");

            Entry splitSource = shell.Doc.Items.Single(x => x.Id == first);
            string otherAsset = shell.Doc.Assets.First(x => x.Id != firstAsset).Id;
            shell.Change("Prepare synthetic multi-image entry", delegate {
                splitSource.AssetIds.Add(otherAsset); splitSource.Note = "Shared split note"; splitSource.Status = "done";
            });
            EntryEditor editor = new EntryEditor(shell, splitSource) { Opacity = 0, ShowActivated = false };
            editor.Show(); editor.UpdateLayout(); Click((DependencyObject)editor.Content, "EditorSplit");
            Check(shell.Doc.Items.Count == 6 && shell.Doc.Items[0].Id == first, "Split retains the first opinion ID and adds one new entry");
            Check(shell.Doc.Items[0].AssetIds.SequenceEqual(new[] { firstAsset }) && shell.Doc.Items[1].AssetIds.SequenceEqual(new[] { otherAsset }), "Split preserves image order with one image per opinion");
            Check(shell.Doc.Items[1].Id != first && shell.Doc.Items.Take(2).All(x => x.Note == "Shared split note" && x.Status == "done"), "Split copies progress/notes into distinct IDs");
            Check(Store.Load(folder).Items.Count == 6, "Split is persisted");
            Click("Undo");
            Check(shell.Doc.Items.Count == 5 && shell.Doc.Items[0].AssetIds.Count == 2, "One undo restores the complete pre-split entry");
            Check(Store.Load(folder).Items.Count == 5, "Split undo is persisted");
            editor = new EntryEditor(shell, shell.Doc.Items[0]) { Opacity = 0, ShowActivated = false };
            editor.Show(); editor.UpdateLayout(); Click((DependencyObject)editor.Content, "EditorSplit");
            string splitCopy = shell.Doc.Items[1].Id;
            shell.Close(); await Open(folder);
            Check(shell.Doc.Items.Single(x => x.Id == splitCopy).AssetIds.SequenceEqual(new[] { otherAsset }), "Split associations survive close/reopen");
            Pass("Actual editor split command, copied progress, stable original ID, atomic undo and reopen");

            string previousDisk = Store.HashFile(Path.Combine(folder, Store.ProjectFile));
            int count = shell.Doc.Items.Count;
            saveLock = new FileStream(Path.Combine(folder, ".save.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            Need<TextBox>("FloatComposerInput").Text = "Recover this note after the save lock is released."; Click("FloatAddText");
            Check(shell.Doc.Items.Count == count + 1, "Save failure retains the new opinion in memory");
            Check(Store.HashFile(Path.Combine(folder, Store.ProjectFile)) == previousDisk, "Save failure preserves the valid on-disk version");
            Check(Need<TextBlock>("SaveState").Text.Contains("保存失败"), "Save failure is visibly reported instead of saved");
            Check(Need<TextBox>("FloatComposerInput").Text == "", "Committed draft is cleared even while disk save fails");
            saveLock.Dispose(); saveLock = null; Click("RetrySave");
            Check(Store.Load(folder).Items.Count == count + 1, "Retry persists the retained opinion after lock release");
            Check(!Need<TextBlock>("SaveState").Text.Contains("保存失败"), "Successful retry clears save-failure status");
            Pass("Save failure status, retained in-memory opinion and successful retry");

            Entry small = shell.Doc.Items.First(x => x.AssetIds.Contains(firstAsset));
            PictureViewer viewer = new PictureViewer(shell, small, 0) { Opacity = 0, ShowActivated = false };
            viewer.Show(); viewer.UpdateLayout();
            await Wait("Viewer did not load", delegate { return Find<Image>((DependencyObject)viewer.Content, "ViewerImage").Source != null; });
            TextBlock zoom = Need<TextBlock>((DependencyObject)viewer.Content, "ViewerZoomLevel");
            double fit = Double.Parse(zoom.Text.TrimEnd('%'), System.Globalization.CultureInfo.CurrentCulture);
            Check(fit > 100 && fit <= 800, "Small screenshot fits above 100%, within the 800% limit");
            Image shown = Need<Image>((DependencyObject)viewer.Content, "ViewerImage");
            Check(Pixels((BitmapSource)shown.Source)[3] == 255 && shown.ActualWidth > 128, "Viewer contains visible, enlarged screenshot pixels");
            Click((DependencyObject)viewer.Content, "ViewerActual"); Check(zoom.Text == "100%", "100% view remains available");
            Click((DependencyObject)viewer.Content, "ViewerZoomIn"); Check(zoom.Text == "125%", "Manual zoom increases to 125%");
            Click((DependencyObject)viewer.Content, "ViewerFit"); Check(Double.Parse(zoom.Text.TrimEnd('%')) > 100, "Fit command restores readable small-image enlargement");
            viewer.Close();
            Pass("Real picture-viewer controls: small-image fit, visible pixels, 100% and manual zoom");

            shell.Close(); await Open(demo);
            Check(shell.Doc.Items.Count == 3 && shell.Doc.Assets.Count == 2, "Generated demo has three opinions and two synthetic images");
            Check(shell.Doc.Items.Count(x => x.Status == "done") == 1 && shell.Doc.Items.Count(x => x.AssetIds.Count == 0) == 1, "Demo includes completed and text-only opinions");
            Render("demo-window.png"); shell.Width = 340; shell.Height = 620; shell.UpdateLayout(); Render("demo-window-narrow.png");
            Check(Need<TextBox>("FloatComposerInput").ActualWidth > 100, "Narrow floating window retains usable composer width");
            Check(Need<ListBox>("FloatCards").ActualHeight > 100, "Narrow floating window retains a scrollable opinion area");
            shell.Close(); shell = null;
            Pass("Self-contained synthetic demo, normal/narrow WPF render and usable layout");
        }

        static Dictionary<string, string> LibraryDataHashes(string path)
        {
            string prefix = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Where(file => file == Path.Combine(path, Store.ProjectFile) ||
                    file.StartsWith(Path.Combine(path, "assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    file.StartsWith(Path.Combine(path, "backups") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(file => file.Substring(prefix.Length), file => Store.HashFile(file), StringComparer.OrdinalIgnoreCase);
        }
        static void SameLibraryData(string path, Dictionary<string, string> original, string reason)
        {
            Dictionary<string, string> current = LibraryDataHashes(path);
            Check(current.Count == original.Count && original.All(pair => current.ContainsKey(pair.Key) && current[pair.Key] == pair.Value), reason);
        }
        static int LibraryRows()
        {
            shell.UpdateLayout();
            return Elements<Button>((DependencyObject)shell.Content).Count(button => AutomationProperties.GetAutomationId(button) == "OpenRecent");
        }
        static async Task OpenLibrary(string launch)
        {
            shell = new Shell(launch) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            shell.Show();
            await Wait("Library did not load", delegate {
                return Find<ComboBox>((DependencyObject)shell.Content, "LibraryFilter") != null || shell.Doc != null;
            });
            Check(shell.Doc == null && Find<ComboBox>((DependencyObject)shell.Content, "LibraryFilter") != null,
                "Startup stays in the library rather than reopening a removed entry");
        }
        static void DeleteFromLibrary(bool confirm)
        {
            Exception failure = null; bool answered = false;
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += delegate {
                Window question = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Owner == shell && w.Title == "从清单库删除");
                if (question == null) return;
                timer.Stop();
                try {
                    question.UpdateLayout();
                    Button cancel = Need<Button>((DependencyObject)question.Content, "CancelDeleteLibrary");
                    Button remove = Need<Button>((DependencyObject)question.Content, "DeleteLibraryConfirm");
                    Check(cancel.IsDefault && cancel.IsCancel && !remove.IsDefault, "Removal confirmation defaults to cancellation");
                    (confirm ? remove : cancel).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); answered = true;
                } catch (Exception ex) { failure = ex; question.Close(); }
            };
            timer.Start();
            try { Click("DeleteRecent"); }
            finally { timer.Stop(); }
            if (failure != null) throw failure;
            Check(answered, "The owned library-removal confirmation was answered");
        }
        static async Task LibraryCases()
        {
            // A fresh profile gives the library exactly one synthetic entry, independent of all earlier checks.
            string originalProfile = Environment.GetEnvironmentVariable("PICLIST_PROFILE_DIR");
            string profile = Path.Combine(run, "library-profile"), path = Path.Combine(run, "library-data");
            Shell olderWindow = null;
            try {
                Environment.SetEnvironmentVariable("PICLIST_PROFILE_DIR", profile);
                Document document = SyntheticData.CreateDemo(path, Path.Combine(run, "library-inputs"));
                document.Items[0].Note = "Preserve this note and the previous save during library removal.";
                Store.Save(path, document);
                Dictionary<string, string> original = LibraryDataHashes(path);
                Check(original.Count == 4 && original.ContainsKey(Store.ProjectFile) &&
                    original.ContainsKey(Path.Combine("backups", "previous.json")), "Library fixture includes checklist, two images and a previous-save backup");
                Preferences local = Preferences.Read(); local.Pinned = false; local.LastFolder = path; local.Remember(path); local.Write();
                string visibilityFile = Path.Combine(profile, "library-visibility.json");
                Check(!LibraryVisibility.IsHidden(path), "New synthetic library entry starts visible");
                await OpenLibrary("--home"); Check(LibraryRows() == 1, "Library contains exactly one synthetic entry");
                Button delete = Need<Button>("DeleteRecent"), open = Need<Button>("OpenRecent");
                Check(delete.TranslatePoint(new Point(), shell).X + delete.ActualWidth <= open.TranslatePoint(new Point(), shell).X + 1,
                    "Delete button is positioned to the left of Open in the same row");
                Render("library-with-delete.png");
                DeleteFromLibrary(false);
                Check(!LibraryVisibility.IsHidden(path) && LibraryRows() == 1, "Cancelling removal preserves the visible entry");
                Check(!File.Exists(visibilityFile), "Cancelling removal does not create a visibility record");
                SameLibraryData(path, original, "Cancelling removal preserves checklist, images and backup byte-for-byte");
                DeleteFromLibrary(true);
                Check(LibraryVisibility.IsHidden(path) && LibraryRows() == 0, "Confirmed removal hides the library entry");
                Check(Need<TextBlock>("LibraryNotice").IsVisible && Need<TextBlock>("LibraryNotice").Text.Contains("原文件保留"),
                    "Successful removal explicitly reports preserved original files");
                SameLibraryData(path, original, "Confirmed removal preserves checklist, images and backup byte-for-byte");
                Render("library-removed.png");
                Click("UndoLibraryDelete");
                Check(!LibraryVisibility.IsHidden(path) && LibraryRows() == 1, "Undo immediately restores the library entry");
                SameLibraryData(path, original, "Undo restores only the entry and does not rewrite its data");
                Pass("Library Delete is left of Open; cancel, confirm, success notice and undo preserve all checklist data");

                Preferences stalePreferences = Preferences.Read();
                olderWindow = new Shell("--home") { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
                olderWindow.Show();
                await Wait("Older library window did not load", delegate { return Find<Button>((DependencyObject)olderWindow.Content, "OpenRecent") != null; });
                DeleteFromLibrary(true);
                Check(LibraryVisibility.IsHidden(path), "Removal persists before a stale window saves preferences");
                stalePreferences.Pinned = !stalePreferences.Pinned; stalePreferences.Remember(path); stalePreferences.Write();
                olderWindow.Close(); olderWindow = null;
                Check(LibraryVisibility.IsHidden(path), "Stale Preferences.Write and an older Shell cannot revive the removed entry");
                shell.Close(); await OpenLibrary(null);
                Check(LibraryRows() == 0 && LibraryVisibility.IsHidden(path), "Default close/reopen retains hiding even when LastFolder points to the removed entry");
                var automaticOpen = typeof(Shell).GetMethod("OpenFolder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Check(automaticOpen != null, "Automatic folder-open path is available for a deterministic stale-startup check");
                bool reopened = (bool)automaticOpen.Invoke(shell, new object[] { path, null, false, false });
                Check(!reopened && shell.Doc == null && LibraryVisibility.IsHidden(path),
                    "Automatic stale startup refuses a now-hidden folder without opening it or clearing removal state");
                SameLibraryData(path, original, "Stale windows and library restart preserve data bytes");
                shell.Close(); await Open(path);
                Check(!LibraryVisibility.IsHidden(path) && shell.Doc.Id == document.Id, "Explicit reopening of the original folder restores its library entry");
                shell.Close(); await OpenLibrary("--home");
                Check(LibraryRows() == 1, "Explicitly reopened checklist is listed again");
                SameLibraryData(path, original, "Explicit reopening restores the entry without rewriting checklist data");
                LibraryVisibility.SetHidden(path + Path.DirectorySeparatorChar, true);
                Check(LibraryVisibility.IsHidden(path.ToUpperInvariant()) && LibraryVisibility.HiddenFolders().Count == 1,
                    "Visibility keys normalize case and trailing directory separators");
                LibraryVisibility.SetHidden(path, false);
                Check(LibraryVisibility.HiddenFolders().Count == 0, "Removing normalized hide state does not leave a duplicate record");
                Pass("Library hiding survives close/reopen and stale multi-window preferences; explicit open restores entry and normalized keys stay unique");

                string visibilityBefore = Store.HashFile(visibilityFile);
                using (FileStream locked = new FileStream(visibilityFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                    Rejected(delegate { LibraryVisibility.SetHidden(path, true); }, "Locked visibility file rejects persistence instead of reporting success");
                    DeleteFromLibrary(true);
                    Check(unexpectedDialog != null && unexpectedDialog.Message.Contains("清单库记录"), "Failed removal reports the visibility-file error to the user");
                    Log("EXPECTED ERROR " + unexpectedDialog.Message); unexpectedDialog = null;
                    Check(LibraryRows() == 1, "Failed removal keeps the visible entry");
                    Check(!Need<TextBlock>("LibraryNotice").IsVisible, "Failed removal does not show a successful-removal notice or undo action");
                }
                Check(Store.HashFile(visibilityFile) == visibilityBefore && !LibraryVisibility.IsHidden(path),
                    "Visibility write failure leaves the previous record unchanged");
                SameLibraryData(path, original, "Visibility write failure preserves checklist, images and backup byte-for-byte");
                DeleteFromLibrary(true); Check(LibraryRows() == 0 && LibraryVisibility.IsHidden(path), "Removal succeeds after the file lock is released");
                Click("UndoLibraryDelete"); Check(LibraryRows() == 1 && !LibraryVisibility.IsHidden(path), "Successful retry remains undoable");
                SameLibraryData(path, original, "Retry and undo still preserve every original data file");
                shell.Close(); shell = null;
                Pass("Visibility-file lock causes an explicit failure with no false success; release, retry and undo preserve the original data");
            } finally {
                if (olderWindow != null) olderWindow.Close();
                if (shell != null) { shell.Close(); shell = null; }
                Environment.SetEnvironmentVariable("PICLIST_PROFILE_DIR", originalProfile);
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool safe = false;
            try {
                Console.OutputEncoding = Utf8;
                if (args.Length != 1) throw new ArgumentException("Pass the isolated artifacts/tests run directory.");
                run = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
                DirectoryInfo info = new DirectoryInfo(run);
                if (info.Parent == null || info.Parent.Name != "tests" || info.Parent.Parent == null || info.Parent.Parent.Name != "artifacts" ||
                    !String.Equals(run, Path.GetDirectoryName(typeof(PublicChecks).Assembly.Location), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only this executable's artifacts/tests subdirectory may be written.");
                safe = true;
                string temp = Path.Combine(run, "temp"); Directory.CreateDirectory(temp);
                Environment.SetEnvironmentVariable("PICLIST_PROFILE_DIR", Path.Combine(run, "profile"));
                Environment.SetEnvironmentVariable("TEMP", temp); Environment.SetEnvironmentVariable("TMP", temp);
                Log("METHOD Full product source, isolated files/profile, synthetic PNGs, WPF control events and process-local DataObject inputs.");
                Log("LIMIT No system clipboard reads/writes or physical input; no user window access. PNGs are WPF content renders, not desktop acceptance.");
                StorageCases(); ImageCases();
                demo = Path.Combine(run, "generated-demo"); SyntheticData.CreateDemo(demo, Path.Combine(run, "demo-inputs"));
                folder = Path.Combine(run, "ui-checklist"); Store.Save(folder, new Document { Name = "Synthetic integration checks" });
                Preferences prefs = Preferences.Read(); prefs.Pinned = false; prefs.Compact = false; prefs.Write();
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Design.Install(app);
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e) {
                    Window window = (Window)sender; window.Opacity = 0; window.ShowInTaskbar = false; window.ShowActivated = false;
                    if (AutomationProperties.GetAutomationId(window) == "ImportDialog") {
                        unexpectedDialog = new InvalidOperationException("Direct paste unexpectedly opened an image assignment dialog."); window.DialogResult = false;
                    }
                }));
                hookProc = WindowHook; hook = SetWindowsHookEx(5, hookProc, IntPtr.Zero, GetCurrentThreadId());
                Check(hook != IntPtr.Zero, "Test-thread activation suppression is installed");
                app.Dispatcher.BeginInvoke(new Action(async delegate {
                    try { await UiCases(); await LibraryCases(); exit = 0; Log("RESULT PASS " + groups + " groups / " + assertions + " assertions"); }
                    catch (Exception ex) { Log("RESULT FAIL " + ex); }
                    finally { if (saveLock != null) { saveLock.Dispose(); saveLock = null; } app.Shutdown(exit); }
                }), DispatcherPriority.Background);
                app.Run();
                return exit;
            } catch (Exception ex) { Console.Error.WriteLine(ex); if (safe) Log("RESULT FAIL " + ex); return 1; }
            finally {
                if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                if (safe) File.WriteAllText(Path.Combine(run, "results.json"), ModelJson.Write(new {
                    Passed = exit == 0, Groups = groups, Assertions = assertions,
                    Method = "In-process WPF events and isolated DataObjects; synthetic PNG fixtures only", DesktopAcceptance = false
                }), Utf8);
            }
        }
    }
}
