using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PictureChecklist
{
    public static class ImageImport
    {
        private const string ImageFilter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*";

        public static void Show(Shell host, string[] files)
        {
            Open(host, files, null);
        }

        internal static void Append(Shell host, Entry target)
        {
            Open(host, null, target);
        }

        private static void Open(Shell host, string[] files, Entry target)
        {
            if (host == null || host.Doc == null) return;
            try {
                if (files == null || files.Length == 0) {
                    var picker = new OpenFileDialog {
                        Title = "选择要导入的图片", Filter = ImageFilter, Multiselect = true, CheckFileExists = true
                    };
                    if (picker.ShowDialog(host) != true) return;
                    files = picker.FileNames;
                }
                new ImportDialog(host, files, target).ShowDialog();
            } catch (Exception ex) { host.Error("无法打开图片导入。\n" + ex.Message); }
        }

        public static void Paste(Shell host)
        {
            if (host == null || host.Doc == null) return;
            try {
                IDataObject data = DirectPaste.ReadClipboard();
                if (!DirectPaste.HasImages(data)) {
                    host.Error("剪贴板里没有图片。请先截图或复制图片文件，再点击“粘贴截图”。");
                    return;
                }
                if (DirectPaste.Apply(host, data)) host.RefreshView();
            } catch (Exception ex) { host.Error("无法读取剪贴板图片。\n" + ex.Message); }
        }

        private static void DeleteTemp(string path)
        {
            if (path == null) return;
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class ImportRow
        {
            public string Path { get; set; }
            public string Title { get { return System.IO.Path.GetFileName(Path); } }
            public string Summary { get { return System.IO.Path.GetDirectoryName(Path); } }
            public string EntryTitle { get; set; }
            public BitmapSource Thumbnail { get { return OrganizerUi.Thumbnail(Path); } }
            public override string ToString() { return Title; }
        }

        private sealed class ImportDialog : Window
        {
            private readonly Shell host;
            private readonly ObservableCollection<ImportRow> files = new ObservableCollection<ImportRow>();
            private readonly Grid options;
            private readonly ListBox list;
            private readonly ComboBox mode, target;
            private readonly TextBox rowTitle, groupTitle;
            private readonly StackPanel rowArea, groupArea, targetArea;
            private readonly TextBlock summary, result;
            private readonly Button confirm, cancel;
            private CancellationTokenSource work;
            private bool editing;
            private enum Destination { Separate, Group, Append, Library }
            private Destination Choice { get { return (Destination)mode.SelectedIndex; } }

            public ImportDialog(Shell host, IEnumerable<string> paths, Entry selectedTarget)
            {
                this.host = host;
                Owner = host;
                Title = "导入图片 · 预览与分配";
                FontFamily = host.FontFamily;
                FontSize = 13;
                Background = Brushes.White;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                Width = Math.Min(690, SystemParameters.WorkArea.Width);
                Height = Math.Min(615, SystemParameters.WorkArea.Height);
                MinWidth = Math.Min(485, Width);
                MinHeight = Math.Min(425, Height);
                ShowInTaskbar = false;
                UseLayoutRounding = true;
                OrganizerUi.Id(this, "ImportDialog", Title);
                foreach (string path in paths.Where(p => !String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                    files.Add(new ImportRow { Path = path, EntryTitle = Path.GetFileNameWithoutExtension(path) });

                var layout = new Grid { Margin = new Thickness(18, 12, 18, 12) };
                OrganizerUi.AddRow(layout);
                OrganizerUi.AddRow(layout, 0);
                OrganizerUi.AddRow(layout);
                OrganizerUi.AddRow(layout);
                Content = layout;
                OrganizerUi.Cell(layout, OrganizerUi.Text("导入图片", 22, true), 0);
                options = new Grid();
                OrganizerUi.AddRow(options);
                OrganizerUi.AddRow(options);
                OrganizerUi.AddRow(options, 0);
                OrganizerUi.AddRow(options);
                OrganizerUi.AddRow(options);
                OrganizerUi.Cell(layout, options, 1);
                var destinations = new StackPanel();
                mode = OrganizerUi.Id(new ComboBox { MinHeight = 31 }, "ImportMode", "图片分配方式");
                mode.Items.Add("每张图片新建一条");
                mode.Items.Add("多张图片合为一条");
                mode.Items.Add("追加到已有条目");
                mode.Items.Add("仅放入图片资料库");
                mode.SelectedIndex = selectedTarget == null ? 0 : 2;
                OrganizerUi.Field(destinations, "分配方式", mode);
                groupArea = new StackPanel();
                groupTitle = OrganizerUi.Box(files.Count == 0 ? "" : files[0].EntryTitle, "ImportGroupTitle", "合并条目标题", 1);
                OrganizerUi.Field(groupArea, "合并条目标题", groupTitle);
                destinations.Children.Add(groupArea);
                targetArea = new StackPanel();
                target = OrganizerUi.Id(new ComboBox { MinHeight = 30, MaxDropDownHeight = 300,
                    IsTextSearchEnabled = true, DisplayMemberPath = "Title" }, "ImportTarget", "选择已有条目");
                var targets = host.Doc.Items.Where(e => !e.Deleted).Select((e, i) => new OrganizerEntryRow(e, (i + 1).ToString("00") + "  ")).ToList();
                target.ItemsSource = targets;
                string targetId = selectedTarget == null ? host.SelectedId : selectedTarget.Id;
                target.SelectedItem = targets.FirstOrDefault(r => r.Item.Id == targetId) ?? targets.FirstOrDefault();
                OrganizerUi.Field(targetArea, "目标条目", target);
                destinations.Children.Add(targetArea);
                OrganizerUi.Cell(options, destinations, 0);
                summary = OrganizerUi.Text("", 13, true);
                summary.Margin = new Thickness(0, 10, 0, 5);
                OrganizerUi.Cell(options, summary, 1);
                list = OrganizerUi.List("ImportFileList", "导入图片预览");
                list.ItemTemplate = OrganizerUi.RowTemplate(true);
                list.ItemsSource = files;
                OrganizerUi.Cell(options, list, 2);
                var arrange = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
                arrange.Children.Add(OrganizerUi.Button("上移", "ImportMoveUp", delegate { Move(-1); }));
                arrange.Children.Add(OrganizerUi.Button("下移", "ImportMoveDown", delegate { Move(1); }));
                arrange.Children.Add(OrganizerUi.Button("放大预览", "ImportPreview", Preview));
                arrange.Children.Add(OrganizerUi.Button("移出本次导入", "ImportRemove", Remove));
                OrganizerUi.Cell(options, arrange, 3);
                rowArea = new StackPanel();
                rowTitle = OrganizerUi.Box("", "ImportRowTitle", "选中图片的新条目标题", 1);
                OrganizerUi.Field(rowArea, "选中图片的新条目标题", rowTitle);
                OrganizerUi.Cell(options, rowArea, 4);
                result = OrganizerUi.Id(OrganizerUi.Text("", 13), "ImportResult", "导入状态");
                result.Margin = new Thickness(0, 8, 0, 8);
                OrganizerUi.Cell(layout, result, 2);
                var footer = new DockPanel { LastChildFill = false };
                confirm = OrganizerUi.Button("确认导入", "ImportConfirm", Apply, true);
                cancel = OrganizerUi.Button("取消", "ImportCancel", Cancel);
                DockPanel.SetDock(confirm, Dock.Right);
                DockPanel.SetDock(cancel, Dock.Right);
                footer.Children.Add(confirm);
                footer.Children.Add(cancel);
                OrganizerUi.Cell(layout, footer, 3);

                mode.SelectionChanged += delegate { RefreshChoices(); };
                target.SelectionChanged += delegate { RefreshChoices(); };
                list.SelectionChanged += delegate { SelectRow(); };
                list.MouseDoubleClick += delegate { Preview(); };
                rowTitle.TextChanged += delegate {
                    var row = list.SelectedItem as ImportRow;
                    if (!editing && row != null) row.EntryTitle = rowTitle.Text;
                };
                Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                    if (work != null) { work.Cancel(); e.Cancel = true; result.Text = "正在取消…"; }
                };
                PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                    if (e.Key == Key.Escape) { e.Handled = true; Cancel(); }
                };
                list.SelectedIndex = files.Count > 0 ? 0 : -1;
                RefreshChoices();
            }

            private void RefreshChoices()
            {
                groupArea.Visibility = Choice == Destination.Group ? Visibility.Visible : Visibility.Collapsed;
                targetArea.Visibility = Choice == Destination.Append ? Visibility.Visible : Visibility.Collapsed;
                rowArea.Visibility = Choice == Destination.Separate ? Visibility.Visible : Visibility.Collapsed;
                summary.Text = "图片预览  ·  " + files.Count + " 张";
                bool missingTarget = Choice == Destination.Append && target.SelectedItem == null;
                confirm.IsEnabled = files.Count > 0 && !missingTarget && work == null;
                result.Foreground = OrganizerUi.Muted;
                if (missingTarget) result.Text = "当前清单没有可追加的条目。请选择新建条目或仅放入资料库。";
                else if (files.Count == 0) result.Text = "本次导入没有图片。可取消并重新选择。";
                else if (Choice == Destination.Library) result.Text = "图片将保存在此清单的资料库，可随后关联到一条或多条修改意见。";
                else if (Choice == Destination.Append) result.Text = "这些图片将按预览顺序追加到所选条目。";
                else if (Choice == Destination.Group) result.Text = "将新建 1 条修改意见，并按预览顺序关联这些图片。";
                else result.Text = "将新建 " + files.Count + " 条修改意见。选择图片可逐项改标题或调整顺序。";
            }

            private void SelectRow()
            {
                var row = list.SelectedItem as ImportRow;
                editing = true;
                rowTitle.Text = row == null ? "" : row.EntryTitle;
                rowTitle.IsEnabled = row != null;
                editing = false;
            }

            private void Preview()
            {
                var row = list.SelectedItem as ImportRow;
                if (row == null) return;
                try {
                    BitmapImage bitmap;
                    using (var stream = File.OpenRead(row.Path)) {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 1600;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                    var preview = new Window { Title = "导入预览 · " + row.Title, Owner = this,
                        Width = Math.Min(900, SystemParameters.WorkArea.Width),
                        Height = Math.Min(670, SystemParameters.WorkArea.Height),
                        WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
                        Background = new SolidColorBrush(Color.FromRgb(231, 237, 235)), FontFamily = FontFamily };
                    var layout = new DockPanel { Margin = new Thickness(10) };
                    var close = OrganizerUi.Button("关闭预览", "ImportPreviewClose", delegate { preview.Close(); });
                    DockPanel.SetDock(close, Dock.Bottom);
                    layout.Children.Add(close);
                    layout.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
                    preview.Content = layout;
                    preview.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                        if (e.Key == Key.Escape) preview.Close();
                    };
                    preview.ShowDialog();
                } catch (Exception ex) {
                    result.Text = "无法预览“" + row.Title + "”。" + ex.Message;
                    result.Foreground = Brushes.Firebrick;
                }
            }

            private void Move(int delta)
            {
                int current = list.SelectedIndex, next = current + delta;
                if (current < 0 || next < 0 || next >= files.Count) return;
                var moved = files[current];
                files.Move(current, next);
                list.SelectedItem = moved;
                list.ScrollIntoView(moved);
            }

            private void Remove()
            {
                int index = list.SelectedIndex;
                if (index < 0) return;
                files.RemoveAt(index);
                list.SelectedIndex = Math.Min(index, files.Count - 1);
                RefreshChoices();
            }

            private void Cancel()
            {
                if (work == null) { DialogResult = false; return; }
                work.Cancel();
                result.Text = "正在取消…";
            }

            private static string ValidTitle(string title, string fallback)
            {
                return String.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
            }

            private async void Apply()
            {
                if (work != null || files.Count == 0) return;
                Destination destination = Choice;
                var entryRow = target.SelectedItem as OrganizerEntryRow;
                if (destination == Destination.Append && entryRow == null) return;
                var requested = files.ToArray();
                string groupedTitle = ValidTitle(groupTitle.Text, "新意见");
                string stage = Path.Combine(host.Folder, ".picture-import-" + Guid.NewGuid().ToString("N"));
                string currentFolder = host.Folder;
                var stagedDoc = new Document();
                var staged = new List<Asset>();
                var newlyPublished = new List<Asset>();
                work = new CancellationTokenSource();
                CancellationToken token = work.Token;
                options.IsEnabled = confirm.IsEnabled = false;
                cancel.Content = "取消导入";
                result.Foreground = OrganizerUi.Muted;
                result.Text = "正在复制并校验图片…";
                bool close = false, applied = false;
                try {
                    Directory.CreateDirectory(stage);
                    IProgress<string> progress = new Progress<string>(message => {
                        if (work != null && !token.IsCancellationRequested) result.Text = message;
                    });
                    await Task.Run(delegate {
                        for (int i = 0; i < requested.Length; i++) {
                            token.ThrowIfCancellationRequested();
                            var row = requested[i];
                            progress.Report("正在校验 " + (i + 1) + " / " + requested.Length + "  ·  " + row.Title);
                            try { staged.Add(Store.AddFile(stage, stagedDoc, row.Path)); }
                            catch (Exception ex) { throw new IOException("“" + row.Title + "”无法导入。\n" + ex.Message, ex); }
                        }
                        token.ThrowIfCancellationRequested();
                    }, token);
                    token.ThrowIfCancellationRequested();
                    host.Change("导入图片", delegate {
                        var adopted = new Dictionary<string, Asset>();
                        foreach (var asset in stagedDoc.Assets) {
                            bool existed = File.Exists(Store.AssetPath(currentFolder, asset));
                            var copied = Store.AdoptStaged(stage, currentFolder, host.Doc, asset);
                            if (!existed && copied.Id == asset.Id) newlyPublished.Add(asset);
                            adopted.Add(asset.Id, copied);
                        }
                        var images = staged.Select(a => adopted[a.Id]).ToList();
                        string selectedId = null;
                        if (destination == Destination.Separate) {
                            for (int i = 0; i < requested.Length; i++) {
                                var entry = new Entry { Title = ValidTitle(requested[i].EntryTitle, "新意见") };
                                entry.AssetIds.Add(images[i].Id);
                                host.Doc.Items.Add(entry);
                                if (selectedId == null) selectedId = entry.Id;
                            }
                        } else if (destination == Destination.Group || destination == Destination.Append) {
                            var entry = destination == Destination.Group ? new Entry { Title = groupedTitle } :
                                host.Doc.Items.First(e => e.Id == entryRow.Item.Id && !e.Deleted);
                            if (destination == Destination.Group) host.Doc.Items.Add(entry);
                            var assigned = new HashSet<string>(entry.AssetIds);
                            foreach (var image in images) if (assigned.Add(image.Id)) entry.AssetIds.Add(image.Id);
                            OrganizerView.Touch(entry);
                            selectedId = entry.Id;
                        }
                        OrganizerView.RevealImport(host, selectedId);
                        applied = true;
                    });
                    close = applied;
                    if (!applied) result.Text = "导入未完成，可以调整后再试。";
                } catch (OperationCanceledException) { close = true; }
                catch (Exception ex) {
                    result.Text = ex.Message;
                    result.Foreground = Brushes.Firebrick;
                } finally {
                    if (!applied) {
                        foreach (var asset in newlyPublished) {
                            // Only this transaction's new, unreferenced files are
                            // removed. Existing and shared originals are preserved.
                            try {
                                string path = Store.AssetPath(currentFolder, asset);
                                if (!host.Doc.Assets.Any(a => a.RelativePath == asset.RelativePath)
                                    && File.Exists(path) && Store.HashFile(path) == asset.Sha256) File.Delete(path);
                            } catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        }
                    }
                    DeleteTemp(stage);
                    work.Dispose();
                    work = null;
                    confirm.IsEnabled = options.IsEnabled = true;
                    cancel.Content = "取消";
                }
                if (close) DialogResult = applied;
            }
        }
    }
}
