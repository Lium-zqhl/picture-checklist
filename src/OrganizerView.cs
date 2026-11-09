using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PictureChecklist
{
    internal static class OrganizerUi
    {
        internal static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(31, 49, 55));
        internal static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(78, 99, 105));
        internal static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(23, 102, 92));
        internal static readonly Brush Line = new SolidColorBrush(Color.FromRgb(209, 220, 218));

        internal static T Id<T>(T element, string id, string name) where T : FrameworkElement
        {
            AutomationProperties.SetAutomationId(element, id);
            AutomationProperties.SetName(element, name);
            return element;
        }

        internal static TextBlock Text(string value, double size, bool bold = false)
        {
            return new TextBlock {
                Text = value, FontSize = size, Foreground = bold ? Ink : Muted,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
        }

        internal static Button Button(string title, string id, Action action, bool primary = false)
        {
            var button = Id(new Button {
                Content = title, Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 5, 3), MinHeight = 29,
                VerticalContentAlignment = VerticalAlignment.Center
            }, id, title);
            if (primary) {
                button.Background = Accent;
                button.BorderBrush = Accent;
                button.Foreground = Brushes.White;
            }
            button.Click += delegate { action(); };
            return button;
        }

        internal static TextBox Box(string value, string id, string name, int lines)
        {
            return Id(new TextBox {
                Text = value ?? "", Padding = new Thickness(6, 4, 6, 4),
                MinHeight = lines == 1 ? 30 : lines * 19 + 10,
                MinLines = lines, MaxLines = lines == 1 ? 1 : lines + 4,
                AcceptsReturn = lines > 1,
                TextWrapping = lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = lines > 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Center
            }, id, name);
        }

        internal static ListBox List(string id, string name)
        {
            var list = Id(new ListBox { BorderBrush = Line, BorderThickness = new Thickness(1) }, id, name);
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(list, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            return list;
        }

        internal static void AddRow(Grid grid, double height = -1)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = height < 0 ? GridLength.Auto
                : height == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(height) });
        }

        internal static void Cell(Grid grid, UIElement element, int row, int col = 0)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, col);
            grid.Children.Add(element);
        }

        internal static void Field(Panel panel, string label, UIElement control)
        {
            var caption = Text(label, 13, true);
            caption.Margin = new Thickness(0, 10, 0, 4);
            panel.Children.Add(caption);
            panel.Children.Add(control);
        }

        internal static FrameworkElementFactory BoundText(string property, double size, bool bold)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(property));
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(TextBlock.FontSizeProperty, size);
            text.SetValue(TextBlock.FontWeightProperty, bold ? FontWeights.SemiBold : FontWeights.Normal);
            text.SetValue(TextBlock.ForegroundProperty, bold ? Ink : Muted);
            text.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
            return text;
        }

        internal static DataTemplate RowTemplate(bool thumbnail)
        {
            var row = new FrameworkElementFactory(typeof(DockPanel));
            row.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 3, 4, 3));
            if (thumbnail) {
                var image = new FrameworkElementFactory(typeof(Image));
                image.SetValue(FrameworkElement.WidthProperty, 55.0);
                image.SetValue(FrameworkElement.HeightProperty, 41.0);
                image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 9, 0));
                image.SetValue(Image.StretchProperty, Stretch.Uniform);
                image.SetBinding(Image.SourceProperty, new Binding("Thumbnail"));
                row.AppendChild(image);
            }
            var labels = new FrameworkElementFactory(typeof(StackPanel));
            labels.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            labels.AppendChild(BoundText("Title", 14, true));
            labels.AppendChild(BoundText("Summary", 12, false));
            row.AppendChild(labels);
            return new DataTemplate { VisualTree = row };
        }

        // Virtualized rows request the thumbnail when realized. The bounded cache
        // contains only small decoded bitmaps and never keeps image files locked.
        private static readonly Dictionary<string, BitmapSource> Thumbs =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<string> ThumbOrder = new Queue<string>();

        internal static BitmapSource Thumbnail(string path, string originalName = null)
        {
            try {
                var info = new FileInfo(path);
                if (!info.Exists) return null;
                string key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + originalName;
                BitmapSource result;
                if (Thumbs.TryGetValue(key, out result)) return result;
                result = ScreenshotPixels.ReadDisplay(path, 104, originalName ?? Path.GetFileName(path));
                if (ThumbOrder.Count >= 80) Thumbs.Remove(ThumbOrder.Dequeue());
                ThumbOrder.Enqueue(key);
                Thumbs.Add(key, result);
                return result;
            } catch (Exception) { return null; }
        }
    }

    internal sealed class OrganizerEntryRow : INotifyPropertyChanged
    {
        public Entry Item { get; private set; }
        public string Prefix { get; private set; }
        public string Title { get { return Prefix + (String.IsNullOrWhiteSpace(Item.Title) ? "未命名条目" : Item.Title); } }
        public string Summary {
            get {
                string status = Item.Status == "done" ? "已完成" : Item.Status == "doing" ? "进行中" : "未开始";
                string flag = Item.NeedsConfirmation ? (Item.Clarified ? "  ·  已澄清" : "  ·  待澄清") : "";
                return status + flag + (Item.AssetIds.Count > 0 ? "  ·  " + Item.AssetIds.Count + " 张图" : "");
            }
        }
        public OrganizerEntryRow(Entry item, string prefix) { Item = item; Prefix = prefix; }
        public event PropertyChangedEventHandler PropertyChanged;
        public void TitleChanged() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Title")); }
        public override string ToString() { return Title; }
    }

    internal sealed class OrganizerAssetRow
    {
        public Asset Asset { get; set; }
        public string Folder { get; set; }
        public string Title { get { return Asset.OriginalName; } }
        public string Summary { get; set; }
        public BitmapSource Thumbnail {
            get {
                try { return OrganizerUi.Thumbnail(Store.AssetPath(Folder, Asset), Asset.OriginalName); }
                catch (Exception) { return null; }
            }
        }
        public override string ToString() { return Title; }
    }

    public class OrganizerView : UserControl
    {
        private sealed class Preferences
        {
            public string DocumentId, Folder;
            public string Search = "", AssetSearch = "", TrashId, BodyFocusId;
            public int BodySelectionStart, BodySelectionLength;
            public bool Trash, Library, AllAssets;
        }
        private static readonly ConditionalWeakTable<Shell, Preferences> Sessions = new ConditionalWeakTable<Shell, Preferences>();
        private readonly Shell host;
        private readonly Preferences state;
        private readonly ListBox entries;
        private readonly TabControl tabs;
        private readonly TabItem detailTab, libraryTab;
        private readonly TextBlock count;
        private readonly ComboBox scope;
        private TextBox body;
        private bool filling, pasting;

        internal static void RevealImport(Shell host, string entryId)
        {
            var preferences = Sessions.GetOrCreateValue(host);
            preferences.DocumentId = host.Doc.Id;
            preferences.Folder = host.Folder;
            preferences.Trash = false;
            preferences.Search = preferences.AssetSearch = "";
            preferences.Library = entryId == null;
            if (entryId != null) host.SelectedId = entryId;
            else preferences.AllAssets = true;
        }

        public OrganizerView(Shell host)
        {
            this.host = host;
            state = Sessions.GetOrCreateValue(host);
            if (state.DocumentId != host.Doc.Id || !String.Equals(state.Folder, host.Folder, StringComparison.OrdinalIgnoreCase)) {
                Sessions.Remove(host);
                state = Sessions.GetOrCreateValue(host);
                state.DocumentId = host.Doc.Id;
                state.Folder = host.Folder;
            }
            OrganizerUi.Id(this, "OrganizerView", "整理模式");
            var main = new Grid { Margin = new Thickness(12, 8, 12, 8) };
            OrganizerUi.AddRow(main);
            OrganizerUi.AddRow(main, 0);
            Content = main;
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
            toolbar.Children.Add(OrganizerUi.Button("＋ 新建条目", "AddEntry", AddEntry, true));
            toolbar.Children.Add(OrganizerUi.Button("导入图片…", "ImportImages", delegate { ImageImport.Show(host, null); }));
            toolbar.Children.Add(OrganizerUi.Button("粘贴截图", "PasteImage", PasteCurrent));
            var hint = OrganizerUi.Text("也可将图片拖入窗口", 12);
            hint.Margin = new Thickness(6, 0, 0, 3);
            toolbar.Children.Add(hint);
            OrganizerUi.Cell(main, toolbar, 0);

            var split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.32, GridUnitType.Star), MinWidth = 205, MaxWidth = 350 });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.68, GridUnitType.Star) });
            OrganizerUi.Cell(main, split, 1);
            var sidebar = new Grid();
            OrganizerUi.AddRow(sidebar);
            OrganizerUi.AddRow(sidebar);
            OrganizerUi.AddRow(sidebar);
            OrganizerUi.AddRow(sidebar, 0);
            OrganizerUi.AddRow(sidebar);
            OrganizerUi.Cell(split, sidebar, 0);
            scope = OrganizerUi.Id(new ComboBox { MinHeight = 29 }, "EntryScope", "条目范围");
            scope.Items.Add("当前条目");
            scope.Items.Add("回收站（已删除）");
            scope.SelectedIndex = state.Trash ? 1 : 0;
            OrganizerUi.Cell(sidebar, scope, 0);
            var search = OrganizerUi.Box(state.Search, "EntrySearch", "搜索条目", 1);
            search.ToolTip = "按标题、修改要求、提醒和备注搜索";
            var searchArea = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var searchCaption = OrganizerUi.Text("搜索 ", 12);
            searchArea.Children.Add(searchCaption);
            searchArea.Children.Add(search);
            OrganizerUi.Cell(sidebar, searchArea, 1);
            count = OrganizerUi.Text("", 12);
            count.Margin = new Thickness(2, 6, 0, 5);
            OrganizerUi.Cell(sidebar, count, 2);
            entries = OrganizerUi.List("EntryList", "条目列表");
            entries.ItemTemplate = OrganizerUi.RowTemplate(false);
            OrganizerUi.Cell(sidebar, entries, 3);
            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (state.Trash) actions.Children.Add(OrganizerUi.Button("恢复条目", "RestoreEntry", Restore));
            else {
                actions.Children.Add(OrganizerUi.Button("上移", "MoveEntryUp", delegate { Move(-1); }));
                actions.Children.Add(OrganizerUi.Button("下移", "MoveEntryDown", delegate { Move(1); }));
                actions.Children.Add(OrganizerUi.Button("移入回收站", "DeleteEntry", Delete));
            }
            OrganizerUi.Cell(sidebar, actions, 4);
            tabs = OrganizerUi.Id(new TabControl(), "OrganizerTabs", "条目与图片资料");
            detailTab = OrganizerUi.Id(new TabItem { Header = "条目详情" }, "DetailsTab", "条目详情");
            libraryTab = OrganizerUi.Id(new TabItem { Header = "图片资料库" }, "LibraryTab", "图片资料库");
            tabs.Items.Add(detailTab);
            tabs.Items.Add(libraryTab);
            tabs.SelectedIndex = state.Library ? 1 : 0;
            CommandManager.AddPreviewCanExecuteHandler(tabs, PasteCanExecute);
            CommandManager.AddPreviewExecutedHandler(tabs, PasteExecuted);
            tabs.PreviewKeyDown += PasteKeyDown;
            DataObject.AddPastingHandler(tabs, Pasting);
            detailTab.MouseDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.ChangedButton == MouseButton.Left && body != null) body.Focus();
            };
            OrganizerUi.Cell(split, tabs, 0, 2);

            search.TextChanged += delegate { state.Search = search.Text; FillList(); };
            scope.SelectionChanged += delegate {
                state.Trash = scope.SelectedIndex == 1;
                state.Search = "";
                state.Library = false;
                host.RefreshView();
            };
            entries.SelectionChanged += delegate {
                if (filling) return;
                var selected = Selected;
                if (selected != null) {
                    if (state.Trash) state.TrashId = selected.Id;
                    else host.SelectedId = selected.Id;
                }
                FillTab();
            };
            tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e) {
                if (e.OriginalSource != tabs) return;
                state.Library = tabs.SelectedIndex == 1;
                FillTab();
            };
            AllowDrop = true;
            PreviewDragOver += delegate(object sender, DragEventArgs e) {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
            PreviewDrop += delegate(object sender, DragEventArgs e) {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                e.Handled = true;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) ImageImport.Show(host, files);
            };
            FillList();
        }

        public void ShowTrash()
        {
            state.Trash = true; state.Search = ""; state.Library = false;
            host.RefreshView();
        }

        private Entry Selected {
            get { var row = entries.SelectedItem as OrganizerEntryRow; return row == null ? null : row.Item; }
        }

        private void PasteCurrent()
        {
            try {
                var data = DirectPaste.ReadClipboard();
                if (!DirectPaste.HasImages(data)) {
                    host.Error("剪贴板里没有可添加的图片。请先截图或复制图片文件。");
                    return;
                }
                PasteData(data);
            } catch (Exception ex) { host.Error(ex.Message); }
        }

        internal bool PasteData(IDataObject data)
        {
            if (pasting) return false;
            try {
                if (!DirectPaste.HasImages(data)) return false;
                pasting = true;
                if (!DirectPaste.Apply(host, data)) return false;
                state.BodyFocusId = host.SelectedId;
                state.BodySelectionStart = 0;
                state.BodySelectionLength = 0;
                host.RefreshView();
                return true;
            } catch (Exception ex) {
                host.Error(ex.Message);
                return false;
            } finally { pasting = false; }
        }

        private void PasteCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            if (e.Command != ApplicationCommands.Paste) return;
            try {
                if (!DirectPaste.HasImages(DirectPaste.ReadClipboard())) return;
                e.CanExecute = true;
                e.Handled = true;
            } catch (Exception) {
                // Command availability is queried while opening a context menu.
                // The executed paste reports any clipboard read error.
            }
        }

        private void PasteExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command != ApplicationCommands.Paste) return;
            try {
                var data = DirectPaste.ReadClipboard();
                if (!DirectPaste.HasImages(data)) return;
                e.Handled = true;
                PasteData(data);
            } catch (Exception ex) { e.Handled = true; host.Error(ex.Message); }
        }

        private void PasteKeyDown(object sender, KeyEventArgs e)
        {
            bool shortcut = (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) ||
                (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift);
            if (!shortcut) return;
            try {
                var data = DirectPaste.ReadClipboard();
                if (!DirectPaste.HasImages(data)) return;
                e.Handled = true;
                PasteData(data);
            } catch (Exception ex) { e.Handled = true; host.Error(ex.Message); }
        }

        private void Pasting(object sender, DataObjectPastingEventArgs e)
        {
            try {
                if (!DirectPaste.HasImages(e.DataObject)) return;
                e.CancelCommand();
                PasteData(e.DataObject);
            } catch (Exception ex) { e.CancelCommand(); host.Error(ex.Message); }
        }

        private static bool Contains(string value, string query)
        {
            return String.IsNullOrEmpty(query) || (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void FillList(bool rebuildDetail = true)
        {
            var rows = new List<OrganizerEntryRow>();
            string query = (state.Search ?? "").Trim();
            int total = 0;
            foreach (var item in host.Doc.Items) {
                if (item.Deleted != state.Trash) continue;
                total++;
                if (!Contains(item.Title, query) && !Contains(item.Body, query) && !Contains(item.Note, query)
                    && !Contains(item.Caution, query)) continue;
                rows.Add(new OrganizerEntryRow(item, state.Trash ? "" : total.ToString("00") + "  "));
            }
            count.Text = rows.Count == total ? "共 " + total + " 条" : "找到 " + rows.Count + " / " + total + " 条";
            string id = state.Trash ? state.TrashId : host.SelectedId;
            var chosen = rows.FirstOrDefault(r => r.Item.Id == id) ?? rows.FirstOrDefault();
            filling = true;
            entries.ItemsSource = rows;
            entries.SelectedItem = chosen;
            if (chosen != null) {
                if (state.Trash) state.TrashId = chosen.Item.Id;
                else host.SelectedId = chosen.Item.Id;
                entries.ScrollIntoView(chosen);
            }
            filling = false;
            if (rebuildDetail) FillTab();
        }

        private void FillTab()
        {
            if (state.Library) BuildLibrary();
            else BuildDetail();
        }

        private void AddEntry()
        {
            var item = new Entry();
            host.Change("新建条目", delegate {
                host.Doc.Items.Add(item);
                host.SelectedId = item.Id;
                state.Trash = false;
                state.Library = false;
                state.Search = "";
                state.BodyFocusId = item.Id;
                state.BodySelectionStart = state.BodySelectionLength = 0;
            });
        }

        private void Move(int amount)
        {
            var item = Selected;
            if (item == null || item.Deleted) return;
            var items = host.Doc.Items;
            int from = items.IndexOf(item), to = from + amount;
            while (to >= 0 && to < items.Count && items[to].Deleted) to += amount;
            if (to < 0 || to >= items.Count) return;
            host.Change(amount < 0 ? "上移条目" : "下移条目", delegate {
                var adjacent = items[to];
                items[to] = item;
                items[from] = adjacent;
            });
        }

        private void Delete()
        {
            var item = Selected;
            if (item == null || item.Deleted) return;
            host.Change("移入回收站", delegate {
                item.Deleted = true;
                Touch(item);
                state.TrashId = item.Id;
            });
        }

        private void Restore()
        {
            var item = Selected;
            if (item == null || !item.Deleted) return;
            host.Change("恢复条目", delegate {
                item.Deleted = false;
                Touch(item);
                state.Trash = false;
                state.Search = "";
                host.SelectedId = item.Id;
            });
        }

        internal static void Touch(Entry item) { item.Updated = DateTimeOffset.Now.ToString("o"); }

        private void BuildDetail()
        {
            var item = Selected;
            body = null;
            var form = new StackPanel { Margin = new Thickness(13, 5, 14, 12) };
            if (item == null && (state.Trash || !String.IsNullOrWhiteSpace(state.Search))) {
                var empty = OrganizerUi.Text(state.Trash ? "回收站中没有符合条件的条目" :
                    "没有找到匹配的条目", 18, true);
                empty.Margin = new Thickness(0, 18, 0, 8);
                form.Children.Add(empty);
                form.Children.Add(OrganizerUi.Text(state.Trash ? "移入回收站的条目可在这里恢复。" :
                    "试试其他关键词，或清空搜索框。", 14));
                detailTab.Content = form;
                return;
            }
            if (item != null && item.Deleted) {
                form.Children.Add(OrganizerUi.Text(item.Title, 19, true));
                form.Children.Add(OrganizerUi.Text("此条目在回收站。恢复后可继续编辑，原图与进度会一同恢复。", 13));
                form.Children.Add(OrganizerUi.Text(item.Body, 14));
                detailTab.Content = new ScrollViewer { Content = form };
                return;
            }
            var heading = new StackPanel();
            var pictures = new StackPanel();
            var extras = new StackPanel();
            form.Children.Add(heading);
            var composer = CreateComposer(item, heading, pictures, extras);
            OrganizerUi.Field(form, "修改要求", composer);
            form.Children.Add(pictures);
            form.Children.Add(extras);
            if (item == null) {
                var guide = OrganizerUi.Text("每张截图自动新建一条意见；可连续粘贴。文字写入当前意见。", 12);
                guide.Margin = new Thickness(0, 10, 0, 0);
                pictures.Children.Add(guide);
                FocusBody(body, 0, 0);
            } else {
                EntryHeading(heading, item);
                Attachments(pictures, item);
                ExtraFields(extras, item);
                if (state.BodyFocusId == item.Id) {
                    state.BodyFocusId = null;
                    FocusBody(body, state.BodySelectionStart, state.BodySelectionLength);
                }
            }
            var scroll = new ScrollViewer { Content = form, Background = Brushes.Transparent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroll.ContextMenu = ComposerMenu(body, false);
            detailTab.Content = scroll;
        }

        private Grid CreateComposer(Entry item, Panel heading, Panel pictures, Panel extras)
        {
            var editor = OrganizerUi.Box(item == null ? "" : item.Body, "EntryBody", "修改要求，可直接粘贴截图", 6);
            body = editor;
            editor.MinHeight = 180;
            editor.MaxLines = 18;
            editor.FontSize = 15;
            editor.Padding = new Thickness(12, 10, 12, 10);
            editor.VerticalContentAlignment = VerticalAlignment.Top;
            editor.BorderBrush = OrganizerUi.Accent;
            editor.Background = new SolidColorBrush(Color.FromRgb(250, 253, 251));
            editor.ContextMenu = ComposerMenu(editor, true);
            var area = new Grid();
            area.Children.Add(editor);
            var hint = OrganizerUi.Id(OrganizerUi.Text("在这里输入修改意见；Ctrl+V 粘贴截图，每张新建一条", 14),
                "ComposerHint", "输入文字或直接粘贴截图");
            hint.Margin = new Thickness(14, 13, 14, 0);
            hint.VerticalAlignment = VerticalAlignment.Top;
            hint.IsHitTestVisible = false;
            hint.Visibility = editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            area.Children.Add(hint);
            editor.TextChanged += delegate {
                string next = editor.Text;
                hint.Visibility = next.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (item == null) {
                    if (next.Length == 0) return;
                    var created = new Entry { Body = next };
                    host.Change("编辑修改要求", delegate {
                        host.Doc.Items.Add(created);
                        host.SelectedId = created.Id;
                        state.Trash = state.Library = false;
                        state.Search = "";
                    }, false, true);
                    if (!host.Doc.Items.Contains(created)) return;
                    item = created;
                    FillList(false);
                    // Keep this TextBox attached while updating surrounding fields.
                    // This preserves the caret, selection and active IME composition.
                    heading.Children.Clear();
                    pictures.Children.Clear();
                    extras.Children.Clear();
                    EntryHeading(heading, item);
                    Attachments(pictures, item);
                    ExtraFields(extras, item);
                } else {
                    var current = host.Doc.Items.FirstOrDefault(e => e.Id == item.Id && !e.Deleted);
                    if (current == null) return;
                    host.Change("编辑修改要求", delegate { current.Body = next; Touch(current); }, false, true);
                }
            };
            return area;
        }

        private static ContextMenu ComposerMenu(TextBox target, bool editing)
        {
            var menu = new ContextMenu();
            if (editing) {
                menu.Items.Add(new MenuItem { Header = "撤销", Command = ApplicationCommands.Undo, CommandTarget = target });
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = "剪切", Command = ApplicationCommands.Cut, CommandTarget = target });
                menu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy, CommandTarget = target });
            }
            menu.Items.Add(OrganizerUi.Id(new MenuItem { Header = "粘贴", Command = ApplicationCommands.Paste,
                CommandTarget = target }, "ComposerPaste", "粘贴文字或截图"));
            if (editing)
                menu.Items.Add(new MenuItem { Header = "全选", Command = ApplicationCommands.SelectAll, CommandTarget = target });
            return menu;
        }

        private static void FocusBody(TextBox editor, int start, int length)
        {
            editor.Loaded += delegate {
                editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate {
                    if (!editor.IsLoaded || !editor.IsVisible) return;
                    int selectionStart = Math.Max(0, Math.Min(start, editor.Text.Length));
                    editor.Focus();
                    editor.Select(selectionStart, Math.Max(0, Math.Min(length, editor.Text.Length - selectionStart)));
                }));
            };
        }

        private void ExtraFields(Panel panel, Entry item)
        {
            Edit(panel, item, "提醒", "EntryCaution", item.Caution, 2, delegate(string s) { item.Caution = s; });
            Edit(panel, item, "备注", "EntryNote", item.Note, 2, delegate(string s) { item.Note = s; });
        }

        private void EntryHeading(Panel form, Entry item)
        {
            Edit(form, item, "条目标题", "EntryTitle", item.Title, 1, delegate(string s) { item.Title = s; });
            var progress = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
            progress.Children.Add(OrganizerUi.Text("进度  ", 13, true));
            var status = OrganizerUi.Id(new ComboBox { MinWidth = 90, Margin = new Thickness(0, 0, 10, 3) }, "EntryStatus", "条目进度");
            string[] codes = { "todo", "doing", "done" };
            foreach (string name in new string[] { "未开始", "进行中", "已完成" }) status.Items.Add(name);
            status.SelectedIndex = Array.IndexOf(codes, item.Status);
            status.SelectionChanged += delegate {
                if (status.SelectedIndex < 0) return;
                string next = codes[status.SelectedIndex];
                if (next != item.Status) host.Change("更新进度", delegate { item.Status = next; Touch(item); });
            };
            progress.Children.Add(status);
            if (item.Status == "done") progress.Children.Add(OrganizerUi.Button("重新打开", "ReopenEntry", delegate {
                host.Change("重新打开条目", delegate { item.Status = "doing"; Touch(item); });
            }));
            var flag = OrganizerUi.Id(new CheckBox { Content = "要求需核对", IsChecked = item.NeedsConfirmation,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 3) }, "NeedsConfirmation", "要求待澄清");
            flag.ToolTip = "标记要求需要核对；核对明确后勾选“已澄清”。进度可独立更新。";
            flag.Click += delegate {
                host.Change("调整澄清要求", delegate {
                    item.NeedsConfirmation = flag.IsChecked == true;
                    if (!item.NeedsConfirmation) item.Clarified = false;
                    Touch(item);
                });
            };
            progress.Children.Add(flag);
            if (item.NeedsConfirmation) {
                var clarified = OrganizerUi.Id(new CheckBox { Content = "已澄清", IsChecked = item.Clarified,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) }, "Clarified", "要求已澄清");
                clarified.Click += delegate { host.Change("更新澄清结果", delegate { item.Clarified = clarified.IsChecked == true; Touch(item); }); };
                progress.Children.Add(clarified);
            }
            form.Children.Add(progress);
        }

        private TextBox Edit(Panel panel, Entry item, string label, string id, string value, int lines, Action<string> set)
        {
            var box = OrganizerUi.Box(value, id, label, lines);
            OrganizerUi.Field(panel, label, box);
            box.TextChanged += delegate {
                string next = box.Text;
                host.Change("编辑" + label, delegate { set(next); Touch(item); }, false, true);
                var row = entries.SelectedItem as OrganizerEntryRow;
                if (id == "EntryTitle" && row != null) row.TitleChanged();
            };
            return box;
        }

        private void Attachments(Panel panel, Entry item)
        {
            var head = new WrapPanel { Margin = new Thickness(0, 12, 0, 3) };
            var caption = OrganizerUi.Text("关联图片（" + item.AssetIds.Count + "）", 14, true);
            caption.Margin = new Thickness(0, 0, 6, 3);
            head.Children.Add(caption);
            head.Children.Add(OrganizerUi.Button("从资料库关联", "ChooseAsset", delegate { tabs.SelectedIndex = 1; }));
            head.Children.Add(OrganizerUi.Button("添加本地图片…", "AppendFiles", delegate { ImageImport.Append(host, item); }));
            panel.Children.Add(head);
            if (item.AssetIds.Count == 0) {
                panel.Children.Add(OrganizerUi.Text("Ctrl+V 粘贴截图会新建意见。为本条配图，请选“添加本地图片”或“从资料库关联”。", 12));
                return;
            }
            var assets = host.Doc.Assets.ToDictionary(a => a.Id);
            var rows = new List<OrganizerAssetRow>();
            for (int i = 0; i < item.AssetIds.Count; i++) {
                Asset asset;
                if (assets.TryGetValue(item.AssetIds[i], out asset)) rows.Add(new OrganizerAssetRow {
                    Asset = asset, Folder = host.Folder, Summary = "第 " + (i + 1) + " 张"
                });
            }
            var list = OrganizerUi.List("LinkedImages", "此条目关联的图片");
            list.ItemTemplate = OrganizerUi.RowTemplate(true);
            list.Height = Math.Min(155, rows.Count * 57 + 6);
            list.ItemsSource = rows;
            list.SelectedIndex = 0;
            panel.Children.Add(list);
            Action open = delegate {
                var row = list.SelectedItem as OrganizerAssetRow;
                if (row != null) host.ShowImage(item, item.AssetIds.IndexOf(row.Asset.Id));
            };
            list.MouseDoubleClick += delegate { open(); };
            var buttons = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            buttons.Children.Add(OrganizerUi.Button("查看原图", "ViewLinkedImage", open));
            buttons.Children.Add(OrganizerUi.Button("解除关联", "UnlinkImage", delegate {
                var row = list.SelectedItem as OrganizerAssetRow;
                if (row != null) host.Change("解除图片关联", delegate { item.AssetIds.Remove(row.Asset.Id); Touch(item); });
            }));
            if (item.AssetIds.Count > 1) {
                var split = OrganizerUi.Button("拆为单图意见", "SplitImages", delegate { SplitImages(item); });
                split.ToolTip = "每张图片独立成条，保留原文、备注和进度；可用底栏“撤销”恢复。";
                buttons.Children.Add(split);
            }
            panel.Children.Add(buttons);
        }

        private void SplitImages(Entry item)
        {
            if (item == null || item.Deleted || item.AssetIds.Count < 2) return;
            host.Change("拆为单图意见", delegate {
                int position = host.Doc.Items.IndexOf(item);
                if (position < 0) throw new InvalidOperationException("当前意见已不可用。");
                string[] images = item.AssetIds.ToArray();
                item.AssetIds = new List<string> { images[0] };
                Touch(item);
                for (int i = 1; i < images.Length; i++) {
                    var created = new Entry {
                        Title = item.Title, Body = item.Body, Caution = item.Caution, Note = item.Note,
                        Status = item.Status, NeedsConfirmation = item.NeedsConfirmation, Clarified = item.Clarified,
                        LegacyActions = item.LegacyActions == null ? new List<string>() : new List<string>(item.LegacyActions)
                    };
                    created.AssetIds.Add(images[i]);
                    host.Doc.Items.Insert(position + i, created);
                }
                RevealImport(host, item.Id);
            });
        }

        private void BuildLibrary()
        {
            var item = Selected;
            if (item != null && item.Deleted) item = null;
            var grid = new Grid { Margin = new Thickness(12, 9, 12, 7) };
            OrganizerUi.AddRow(grid);
            OrganizerUi.AddRow(grid);
            OrganizerUi.AddRow(grid);
            OrganizerUi.AddRow(grid, 0);
            OrganizerUi.AddRow(grid);
            var filterBar = new DockPanel();
            var filter = OrganizerUi.Id(new ComboBox { MinWidth = 139, MinHeight = 30,
                Margin = new Thickness(0, 0, 7, 0) }, "AssetScope", "图片范围");
            filter.Items.Add("待整理（未关联）");
            filter.Items.Add("全部图片");
            filter.SelectedIndex = state.AllAssets ? 1 : 0;
            filterBar.Children.Add(filter);
            filterBar.Children.Add(OrganizerUi.Text("搜索 ", 12));
            var find = OrganizerUi.Box(state.AssetSearch, "AssetSearch", "搜索图片名称或关联条目", 1);
            filterBar.Children.Add(find);
            OrganizerUi.Cell(grid, filterBar, 0);
            var target = OrganizerUi.Text(item == null ? "在左侧选择条目后，可将图片关联到该条目。" :
                "关联到当前条目  ·  " + item.Title, 13, true);
            target.Margin = new Thickness(0, 9, 0, 3);
            OrganizerUi.Cell(grid, target, 1);
            var info = OrganizerUi.Text("", 12);
            info.Margin = new Thickness(0, 2, 0, 6);
            OrganizerUi.Cell(grid, info, 2);
            var list = OrganizerUi.List("AssetList", "图片资料列表");
            list.ItemTemplate = OrganizerUi.RowTemplate(true);
            list.SelectionMode = SelectionMode.Extended;
            OrganizerUi.Cell(grid, list, 3);
            var owners = new Dictionary<string, List<string>>();
            foreach (var entry in host.Doc.Items.Where(e => !e.Deleted)) {
                foreach (var id in entry.AssetIds) {
                    List<string> links;
                    if (!owners.TryGetValue(id, out links)) owners[id] = links = new List<string>();
                    links.Add(entry.Title);
                }
            }
            Action fill = delegate {
                string query = state.AssetSearch.Trim();
                var rows = new List<OrganizerAssetRow>();
                foreach (var asset in host.Doc.Assets) {
                    List<string> links;
                    bool linked = owners.TryGetValue(asset.Id, out links);
                    if (linked && !state.AllAssets) continue;
                    string summary = linked ? "关联 " + links.Count + " 条  ·  " + String.Join("、", links) : "待整理  ·  尚未关联条目";
                    if (Contains(asset.OriginalName, query) || Contains(summary, query))
                        rows.Add(new OrganizerAssetRow { Asset = asset, Folder = host.Folder, Summary = summary });
                }
                info.Text = rows.Count > 0 ? rows.Count + " 张图片  ·  按住 Ctrl 可多选" :
                    state.AllAssets ? "没有符合条件的图片。可从上方导入本地图片或粘贴截图。" :
                    "没有符合条件的待整理图片。切换到“全部图片”可选择已用于其他条目的原图。";
                list.ItemsSource = rows;
                if (rows.Count > 0) list.SelectedIndex = 0;
            };
            filter.SelectionChanged += delegate { state.AllAssets = filter.SelectedIndex == 1; fill(); };
            find.TextChanged += delegate { state.AssetSearch = find.Text; fill(); };
            var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            var link = OrganizerUi.Button("关联所选图片", "LinkSelectedImages", delegate {
                var selected = list.SelectedItems.Cast<OrganizerAssetRow>().ToList();
                if (item == null || selected.Count == 0) return;
                var added = selected.Select(r => r.Asset.Id).Where(id => !item.AssetIds.Contains(id)).ToList();
                if (added.Count == 0) { info.Text = "所选图片已关联到当前条目。"; return; }
                host.Change("关联资料库图片", delegate { item.AssetIds.AddRange(added); Touch(item); });
            }, true);
            link.IsEnabled = item != null;
            actions.Children.Add(link);
            Action open = delegate {
                var selected = list.SelectedItem as OrganizerAssetRow;
                if (selected == null) return;
                var preview = new Entry { Title = selected.Asset.OriginalName };
                preview.AssetIds.Add(selected.Asset.Id);
                host.ShowImage(preview, 0);
            };
            list.MouseDoubleClick += delegate { open(); };
            actions.Children.Add(OrganizerUi.Button("查看原图", "ViewLibraryImage", open));
            OrganizerUi.Cell(grid, actions, 4);
            fill();
            libraryTab.Content = grid;
        }
    }
}
