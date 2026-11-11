using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PictureChecklist
{
    internal static class FloatControls
    {
        public static readonly Brush Ink = ColorBrush("#263934");
        public static readonly Brush Muted = ColorBrush("#7D8983");
        public static readonly Brush Line = ColorBrush("#E0E6DF");
        public static readonly Brush Green = ColorBrush("#25735E");
        public static readonly Brush Alert = ColorBrush("#94491F");
        public static readonly Brush Completed = ColorBrush("#EDF5EF");
        public static readonly Brush CompletedLine = ColorBrush("#D3E6D8");

        public static Brush ColorBrush(string color)
        {
            SolidColorBrush brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            return brush;
        }

        public static void Identify(DependencyObject element, string id, string name)
        {
            AutomationProperties.SetAutomationId(element, id);
            AutomationProperties.SetName(element, name);
        }

        public static TextBlock Text(string value, double size)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        }

        public static Button Button(string label, string id, Action action)
        {
            Button button = new Button { Content = label, MinHeight = 26, Padding = new Thickness(6, 2, 6, 2),
                Background = Brushes.White, BorderBrush = Line, Foreground = Ink, Cursor = Cursors.Hand };
            Identify(button, id, label);
            button.Click += delegate { action(); };
            return button;
        }

        public static DockPanel Row()
        {
            return new DockPanel { LastChildFill = true };
        }
    }

    // Recycled list containers create cards only for the visible part of a long list.
    public sealed class FloatCardPresenter : ContentControl
    {
        public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
            "Owner", typeof(FloatView), typeof(FloatCardPresenter),
            new PropertyMetadata(null, delegate(DependencyObject sender, DependencyPropertyChangedEventArgs args)
            {
                ((FloatCardPresenter)sender).RefreshCard();
            }));

        public FloatView Owner
        {
            get { return (FloatView)GetValue(OwnerProperty); }
            set { SetValue(OwnerProperty, value); }
        }

        public FloatCardPresenter()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            DataContextChanged += delegate { RefreshCard(); };
        }

        void RefreshCard()
        {
            Entry entry = DataContext as Entry;
            Content = Owner != null && entry != null ? Owner.BuildCard(entry) : null;
        }
    }

    public sealed class FloatView : UserControl
    {
        static readonly string[] FilterCodes = { "all", "open", "todo", "doing", "done", "unclear" };
        static readonly string[] FilterNames = { "全部", "未完成", "未开始", "进行中", "已完成", "待澄清" };
        readonly Shell host;
        readonly List<Entry> all;
        readonly Dictionary<string, int> numbers = new Dictionary<string, int>();
        readonly Dictionary<string, Asset> assets;
        readonly Dictionary<string, Button> numberButtons = new Dictionary<string, Button>();
        readonly TextBlock outstanding = FloatControls.Text("", 13);
        readonly TextBlock totals = FloatControls.Text("", 13);
        readonly TextBlock unclearCount = FloatControls.Text("", 13);
        readonly ProgressBar progress = new ProgressBar { Height = 3, BorderThickness = new Thickness(0),
            Background = FloatControls.Line, Foreground = FloatControls.Green, Margin = new Thickness(0, 9, 0, 10) };
        readonly TextBox query = new TextBox { Padding = new Thickness(6, 3, 6, 3), MinHeight = 26 };
        readonly ComboBox filter = new ComboBox { Width = 89, VerticalContentAlignment = VerticalAlignment.Center };
        readonly ComboBox jump = new ComboBox { VerticalContentAlignment = VerticalAlignment.Center,
            MaxDropDownHeight = 370, IsTextSearchEnabled = true };
        readonly ListBox cards = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        readonly ContentControl reading = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        readonly Button previous;
        readonly Button next;
        readonly List<Button> visibleFilters = new List<Button>();
        readonly FloatComposer composer;
        List<Entry> matches = new List<Entry>();
        bool filling;

        public FloatView(Shell host)
        {
            this.host = host;
            all = host.Doc.Items.Where(e => !e.Deleted).ToList();
            for (int i = 0; i < all.Count; i++) numbers[all[i].Id] = i + 1;
            assets = host.Doc.Assets.ToDictionary(a => a.Id);
            FloatControls.Identify(this, "FloatView", "浮窗执行");
            DockPanel main = new DockPanel { Background = Brushes.White };
            Content = main;
            StackPanel fixedPart = new StackPanel { Margin = new Thickness(18, 4, 18, 0) };
            DockPanel.SetDock(fixedPart, Dock.Top);
            main.Children.Add(fixedPart);

            DockPanel summary = FloatControls.Row();
            totals.FontSize = 13; outstanding.FontSize = 12;
            DockPanel.SetDock(outstanding, Dock.Right);
            summary.Children.Add(outstanding);
            summary.Children.Add(totals);
            unclearCount.Visibility = Visibility.Collapsed;
            fixedPart.Children.Add(unclearCount);
            FloatControls.Identify(outstanding, "FloatOpenCount", "剩余数");
            FloatControls.Identify(totals, "FloatProgressText", "完成数");
            FloatControls.Identify(unclearCount, "FloatUnclearCount", "待澄清数");
            FloatControls.Identify(progress, "FloatProgressBar", "完成进度");
            fixedPart.Children.Add(summary);
            fixedPart.Children.Add(progress);

            WrapPanel numberGrid = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            foreach (Entry entry in all)
            {
                Entry target = entry;
                Button button = FloatControls.Button("", "FloatNumber_" + entry.Id, delegate { JumpTo(target); });
                button.MinWidth = 32; button.FontSize = 11;
                button.Margin = new Thickness(0, 0, 5, 5);
                button.Padding = new Thickness(4, 2, 4, 2);
                numberButtons.Add(entry.Id, button);
                numberGrid.Children.Add(button);
            }
            ScrollViewer numberScroll = new ScrollViewer { Content = numberGrid, MaxHeight = 92,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            FloatControls.Identify(numberScroll, "FloatNumberGrid", "意见编号与完成状态");

            DockPanel tools = FloatControls.Row();
            tools.Margin = new Thickness(-3, 0, -3, 8);
            filter.ItemsSource = FilterNames;
            filter.SelectedIndex = Math.Max(0, Array.IndexOf(FilterCodes, host.Filter ?? "all"));
            filter.Visibility = Visibility.Collapsed;
            FloatControls.Identify(filter, "FloatFilter", "状态筛选");
            fixedPart.Children.Add(filter);
            Button searchToggle = FloatControls.Button("搜索", "FloatSearchToggle", delegate { });
            searchToggle.BorderThickness = new Thickness(0); searchToggle.Background = Brushes.Transparent;
            searchToggle.Foreground = FloatControls.Muted; searchToggle.FontSize = 12;
            DockPanel.SetDock(searchToggle, Dock.Right); tools.Children.Add(searchToggle);
            StackPanel tabs = new StackPanel { Orientation = Orientation.Horizontal };
            string[] tabNames = { "全部", "未完成", "已完成" };
            string[] tabIds = { "FloatFilterAll", "FloatFilterOpen", "FloatFilterDone" };
            int[] tabIndices = { 0, 1, 4 };
            for (int n = 0; n < tabNames.Length; n++) {
                int index = tabIndices[n];
                Button tab = FloatControls.Button(tabNames[n], tabIds[n], delegate { filter.SelectedIndex = index; });
                tab.FontSize = 12; tab.Padding = new Thickness(8, 5, 8, 5); tab.Margin = new Thickness(0, 0, 3, 0);
                tab.BorderThickness = new Thickness(0); tab.Tag = index;
                tabs.Children.Add(tab); visibleFilters.Add(tab);
            }
            tools.Children.Add(tabs); fixedPart.Children.Add(tools);
            Grid searchBox = new Grid();
            searchBox.Margin = new Thickness(0, 0, 0, 9);
            query.Text = host.Query ?? "";
            query.ToolTip = "搜索编号、标题、意见或备注";
            FloatControls.Identify(query, "FloatSearch", "搜索编号、意见或备注");
            searchBox.Children.Add(query);
            TextBlock placeholder = FloatControls.Text("搜索编号、意见或备注", 12);
            placeholder.Foreground = FloatControls.Muted;
            placeholder.IsHitTestVisible = false;
            placeholder.Margin = new Thickness(8, 0, 3, 0);
            placeholder.Visibility = query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            searchBox.Children.Add(placeholder);
            searchBox.Visibility = query.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            searchToggle.Click += delegate {
                searchBox.Visibility = searchBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (searchBox.Visibility == Visibility.Visible) query.Focus();
            };
            fixedPart.Children.Add(searchBox);
            fixedPart.Children.Add(numberScroll);

            DockPanel navigation = FloatControls.Row();
            navigation.Margin = new Thickness(0, 0, 0, 6);
            navigation.Visibility = all.Count > 100 ? Visibility.Visible : Visibility.Collapsed;
            previous = FloatControls.Button("上一条", "FloatPrevious", delegate { Step(-1); });
            next = FloatControls.Button("下一条", "FloatNext", delegate { Step(1); });
            DockPanel.SetDock(previous, Dock.Left);
            DockPanel.SetDock(next, Dock.Right);
            navigation.Children.Add(previous);
            navigation.Children.Add(next);
            jump.Margin = new Thickness(5, 0, 5, 0);
            jump.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingStackPanel.SetIsVirtualizing(jump, true);
            VirtualizingStackPanel.SetVirtualizationMode(jump, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(jump, true);
            FloatControls.Identify(jump, "FloatJump", "编号跳转");
            navigation.Children.Add(jump);
            fixedPart.Children.Add(navigation);

            ConfigureCards();
            composer = new FloatComposer(host, this);
            DockPanel.SetDock(composer, Dock.Bottom); main.Children.Add(composer);
            FloatControls.Identify(reading, "FloatReading", "意见卡片列表");
            reading.Margin = new Thickness(12, 8, 12, 0);
            reading.Background = FloatControls.ColorBrush("#F7F9F6");
            main.Children.Add(reading);
            query.TextChanged += delegate
            {
                placeholder.Visibility = query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                host.Query = query.Text;
                if (!filling) Refilter();
            };
            filter.SelectionChanged += delegate
            {
                if (filter.SelectedIndex < 0) return;
                host.Filter = FilterCodes[filter.SelectedIndex];
                if (!filling) Refilter();
            };
            jump.SelectionChanged += delegate
            {
                if (!filling && jump.SelectedIndex >= 0) Select(matches[jump.SelectedIndex], true);
            };
            Recount();
            RefreshNumbers();
            Refilter();
        }

        internal bool PasteData(IDataObject data) { return composer.PasteData(data); }

        void ConfigureCards()
        {
            FloatControls.Identify(cards, "FloatCards", "图文意见");
            cards.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            VirtualizingPanel.SetIsVirtualizing(cards, true);
            VirtualizingPanel.SetVirtualizationMode(cards, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(cards, ScrollUnit.Pixel);
            ScrollViewer.SetCanContentScroll(cards, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(cards, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(cards, ScrollBarVisibility.Auto);
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, new Binding("ActualWidth") {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ScrollContentPresenter), 1) }));
            var itemContent = new FrameworkElementFactory(typeof(ContentPresenter));
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty,
                new ControlTemplate(typeof(ListBoxItem)) { VisualTree = itemContent }));
            cards.ItemContainerStyle = itemStyle;
            var presenter = new FrameworkElementFactory(typeof(FloatCardPresenter));
            presenter.SetBinding(FloatCardPresenter.OwnerProperty, new Binding { Source = this });
            cards.ItemTemplate = new DataTemplate(typeof(Entry)) { VisualTree = presenter };
            cards.SelectionChanged += delegate
            {
                Entry entry = cards.SelectedItem as Entry;
                if (!filling && entry != null) Select(entry, false);
            };
        }

        static bool Unclear(Entry entry) { return entry.NeedsConfirmation && !entry.Clarified; }

        void Recount()
        {
            int done = all.Count(e => e.Status == "done");
            int remaining = all.Count - done;
            outstanding.Text = "剩余 " + remaining + " 条";
            outstanding.Foreground = remaining == 0 ? FloatControls.Green : FloatControls.Alert;
            totals.Text = done + " / " + all.Count + " 已完成";
            totals.Foreground = FloatControls.Green;
            unclearCount.Text = "待澄清 " + all.Count(Unclear);
            progress.Maximum = Math.Max(1, all.Count);
            progress.Value = done;
        }

        void RefreshNumbers()
        {
            foreach (Entry entry in all)
            {
                Button button = numberButtons[entry.Id];
                bool done = entry.Status == "done";
                string label = (done ? "✓ " : "") + numbers[entry.Id].ToString("00");
                button.Content = label;
                button.Background = done ? FloatControls.Completed : Brushes.White;
                button.BorderBrush = done ? FloatControls.CompletedLine : FloatControls.Line;
                button.Foreground = done ? FloatControls.Green : FloatControls.Ink;
                button.ToolTip = entry.Title + (done ? " · 已完成" : " · 未完成") + (Unclear(entry) ? " · 待澄清" : "");
                FloatControls.Identify(button, "FloatNumber_" + entry.Id, label + " " + entry.Title);
            }
        }

        bool Matches(Entry entry)
        {
            string mode = FilterCodes[Math.Max(0, filter.SelectedIndex)];
            if (mode == "unclear" && !Unclear(entry)) return false;
            if (mode == "open" && entry.Status == "done") return false;
            if ((mode == "todo" || mode == "doing" || mode == "done") && entry.Status != mode) return false;
            string search = query.Text.Trim();
            if (search.Length == 0) return true;
            string[] fields = { numbers[entry.Id].ToString("00"), entry.Title, entry.Body, entry.Caution, entry.Note };
            if (fields.Any(t => Has(t, search))) return true;
            return entry.LegacyActions != null && entry.LegacyActions.Any(t => Has(t, search));
        }

        static bool Has(string text, string search)
        {
            return text != null && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Refilter()
        {
            foreach (Button tab in visibleFilters) {
                bool active = (int)tab.Tag == filter.SelectedIndex;
                tab.Background = active ? FloatControls.Completed : Brushes.Transparent;
                tab.Foreground = active ? FloatControls.Green : FloatControls.Muted;
                tab.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
            matches = all.Where(Matches).ToList();
            Entry selected = matches.FirstOrDefault(e => e.Id == host.SelectedId);
            if (selected == null && host.SelectedId != null && numbers.ContainsKey(host.SelectedId))
                selected = matches.FirstOrDefault(e => numbers[e.Id] > numbers[host.SelectedId]);
            if (selected == null) selected = matches.FirstOrDefault();
            filling = true;
            cards.ItemsSource = matches;
            jump.ItemsSource = matches.Select(e => numbers[e.Id].ToString("00") + "  " + e.Title).ToList();
            filling = false;
            if (selected == null) { ShowEmpty(); UpdateNavigation(-1); return; }
            reading.Content = cards;
            Select(selected, true);
        }

        void UpdateNavigation(int index)
        {
            filling = true;
            jump.SelectedIndex = index;
            filling = false;
            previous.IsEnabled = index > 0;
            next.IsEnabled = index >= 0 && index + 1 < matches.Count;
            jump.IsEnabled = matches.Count > 0;
        }

        void Select(Entry entry, bool scroll)
        {
            host.SelectedId = entry.Id;
            filling = true;
            cards.SelectedItem = entry;
            filling = false;
            UpdateNavigation(matches.IndexOf(entry));
            if (scroll)
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!cards.Items.Contains(entry)) return;
                    cards.ScrollIntoView(entry);
                    cards.UpdateLayout();
                    FrameworkElement container = cards.ItemContainerGenerator.ContainerFromItem(entry) as FrameworkElement;
                    if (container != null && cards.ActualHeight > 2)
                        container.BringIntoView(new Rect(0, 0, container.ActualWidth,
                            Math.Min(container.ActualHeight, cards.ActualHeight - 2)));
                }), DispatcherPriority.Loaded);
        }

        void Step(int direction)
        {
            int index = matches.FindIndex(e => e.Id == host.SelectedId) + direction;
            if (index >= 0 && index < matches.Count) Select(matches[index], true);
        }

        void JumpTo(Entry entry)
        {
            host.SelectedId = entry.Id;
            if (!Matches(entry))
            {
                filling = true;
                filter.SelectedIndex = 0;
                query.Clear();
                host.Filter = "all";
                host.Query = "";
                filling = false;
                Refilter();
            }
            Select(entry, true);
        }

        void ShowEmpty()
        {
            StackPanel body = new StackPanel { Margin = new Thickness(7, 20, 7, 10) };
            body.Children.Add(FloatControls.Text(all.Count == 0 ? "从一张截图开始" : "没有符合筛选条件的意见。", 16));
            Button action = all.Count == 0
                ? FloatControls.Button("在下方粘贴截图或填写意见", "FloatEmptyOrganize", delegate { composer.FocusInput(); })
                : FloatControls.Button("清除筛选", "FloatClearFilters", delegate { filter.SelectedIndex = 0; query.Clear(); });
            action.HorizontalAlignment = HorizontalAlignment.Left;
            action.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(action);
            reading.Content = body;
        }

        internal FrameworkElement BuildCard(Entry entry)
        {
            string suffix = "_" + entry.Id;
            Border card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(host.Compact ? 10 : 13), Margin = new Thickness(0, 0, 3, 11) };
            FloatControls.Identify(card, "FloatCard" + suffix, "意见 " + numbers[entry.Id].ToString("00"));
            StackPanel content = new StackPanel();
            card.Child = content;

            DockPanel titleRow = FloatControls.Row();
            TextBlock state = FloatControls.Text("", 11);
            state.Margin = new Thickness(8, 0, 0, 0);
            FloatControls.Identify(state, "FloatStatusLabel" + suffix, "完成状态");
            Button edit = FloatControls.Button("编辑", "FloatEdit" + suffix, delegate {
                host.SelectedId = entry.Id; new EntryEditor(host, entry).ShowDialog();
            });
            edit.Background = Brushes.Transparent; edit.BorderThickness = new Thickness(0);
            edit.FontSize = 11; edit.Foreground = FloatControls.Muted; edit.MinHeight = 22; edit.Padding = new Thickness(3, 0, 0, 0);
            DockPanel.SetDock(edit, Dock.Right); titleRow.Children.Add(edit);
            TextBlock title = FloatControls.Text(numbers[entry.Id].ToString("00") + "  " + entry.Title, 14);
            title.FontWeight = FontWeights.SemiBold;
            FloatControls.Identify(title, "FloatTitle" + suffix, title.Text);
            CheckBox completed = new CheckBox { Content = title, IsChecked = entry.Status == "done",
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(3, 0, 0, 0), Cursor = Cursors.Hand };
            FloatControls.Identify(completed, "FloatDone" + suffix, "完成 " + title.Text);
            titleRow.Children.Add(completed);
            content.Children.Add(titleRow);

            Action refreshState = delegate
            {
                bool done = entry.Status == "done";
                completed.IsChecked = done;
                card.Background = FloatControls.ColorBrush(done ? "#F2F7F1" : "#FFFFFF");
                card.BorderBrush = done ? FloatControls.ColorBrush("#D7E4D8") : FloatControls.Line;
                state.Text = done ? "✓ 已完成" : "未完成";
                state.Foreground = done ? FloatControls.Green : FloatControls.Muted;
                completed.ToolTip = done ? "点击取消完成，重新打开" : "点击标记为已完成";
            };
            Action changed = delegate
            {
                Recount();
                RefreshNumbers();
                if (!Matches(entry)) Refilter();
                else refreshState();
            };
            completed.Click += delegate
            {
                bool done = completed.IsChecked == true;
                host.SelectedId = entry.Id;
                host.Change(done ? "完成意见" : "重新打开意见", delegate
                {
                    entry.Status = done ? "done" : "doing";
                    Touch(entry);
                }, false);
                changed();
            };
            refreshState();
            content.Children.Add(PhotoPanel(entry));

            string bodyText = entry.Body;
            if (string.IsNullOrWhiteSpace(bodyText) && entry.LegacyActions != null)
                bodyText = string.Join(Environment.NewLine, entry.LegacyActions);
            StackPanel description = new StackPanel();
            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                TextBox body = new TextBox { Text = bodyText, FontSize = 13, Foreground = FloatControls.Ink,
                    IsReadOnly = true, IsUndoEnabled = false, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                    Margin = new Thickness(0, 6, 0, 2), Cursor = Cursors.IBeam,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    IsInactiveSelectionHighlightEnabled = true,
                    ToolTip = "拖选文字后按 Ctrl+C 复制；右键可复制或全选" };
                FloatControls.Identify(body, "FloatBody" + suffix, "意见正文");
                ContextMenu textMenu = new ContextMenu();
                textMenu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy,
                    CommandTarget = body, InputGestureText = "Ctrl+C" });
                textMenu.Items.Add(new MenuItem { Header = "全选", Command = ApplicationCommands.SelectAll,
                    CommandTarget = body, InputGestureText = "Ctrl+A" });
                body.ContextMenu = textMenu;
                description.Children.Add(body);
            }
            if (description.Children.Count > 0)
            {
                if (host.Compact)
                    content.Children.Add(new Expander { Header = "意见说明", Content = description, Margin = new Thickness(0, 4, 0, 0) });
                else content.Children.Add(description);
            }

            StackPanel details = new StackPanel();
            if (!string.IsNullOrWhiteSpace(entry.Caution)) {
                TextBlock warning = FloatControls.Text(entry.Caution, 12); warning.Foreground = FloatControls.Alert;
                details.Children.Add(new Border { Background = FloatControls.ColorBrush("#FFF7E9"), Padding = new Thickness(7),
                    CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 3, 0, 5), Child = warning });
            }
            if (entry.AssetIds == null || entry.AssetIds.Count == 0)
            {
                Button addImage = FloatControls.Button("为本条添加图片…", "FloatAddImages" + suffix,
                    delegate { host.SelectedId = entry.Id; ImageImport.Append(host, entry); });
                addImage.HorizontalAlignment = HorizontalAlignment.Left;
                details.Children.Add(addImage);
            }
            TextBox note = new TextBox { Text = entry.Note ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 56, MaxHeight = 168, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 3, 0, 6),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            FloatControls.Identify(note, "FloatNote" + suffix, "意见备注");
            details.Children.Add(note);
            WrapPanel states = new WrapPanel();
            foreach (string value in new[] { "todo", "doing" })
            {
                string code = value;
                string label = code == "todo" ? "未开始" : "进行中";
                Button button = FloatControls.Button(label, (code == "todo" ? "FloatStatusTodo" : "FloatStatusDoing") + suffix, delegate
                {
                    if (entry.Status == code) return;
                    host.SelectedId = entry.Id;
                    host.Change("状态改为" + label, delegate { entry.Status = code; Touch(entry); }, false);
                    changed();
                });
                button.Margin = new Thickness(0, 0, 6, 5);
                states.Children.Add(button);
            }
            details.Children.Add(states);
            CheckBox clarify = new CheckBox { Content = "要求待澄清", IsChecked = Unclear(entry),
                Margin = new Thickness(1, 3, 0, 3), Foreground = FloatControls.Alert };
            FloatControls.Identify(clarify, "FloatUnclear" + suffix, "要求待澄清");
            clarify.Click += delegate
            {
                bool active = clarify.IsChecked == true;
                host.SelectedId = entry.Id;
                host.Change(active ? "标记待澄清" : "意见已澄清", delegate
                {
                    entry.NeedsConfirmation = true; entry.Clarified = !active; Touch(entry);
                }, false);
                changed();
            };
            details.Children.Add(clarify);
            Expander notes = new Expander { Content = details,
                Header = string.IsNullOrEmpty(entry.Note) ? "添加备注" : "查看备注",
                IsExpanded = false, FontSize = 11, Foreground = FloatControls.Muted, Margin = new Thickness(0, 7, 0, 1) };
            FloatControls.Identify(notes, "FloatNotesToggle" + suffix, "展开备注和进度");
            note.TextChanged += delegate
            {
                string value = note.Text;
                if (value == entry.Note) return;
                host.SelectedId = entry.Id;
                host.Change("编辑备注 " + numbers[entry.Id].ToString("00"), delegate { entry.Note = value; Touch(entry); }, false, true);
                notes.Header = string.IsNullOrEmpty(value) ? "添加备注" : "查看备注";
            };
            DockPanel notesRow = FloatControls.Row();
            state.FontSize = 10; state.VerticalAlignment = VerticalAlignment.Top; state.Margin = new Thickness(8, 10, 0, 0);
            DockPanel.SetDock(state, Dock.Right); notesRow.Children.Add(state); notesRow.Children.Add(notes);
            content.Children.Add(notesRow);
            return card;
        }

        static void Touch(Entry entry) { entry.Updated = DateTimeOffset.Now.ToString("o"); }

        FrameworkElement PhotoPanel(Entry entry)
        {
            string suffix = "_" + entry.Id;
            int count = entry.AssetIds == null ? 0 : entry.AssetIds.Count;
            if (count == 0) return new Border { Height = 2 };
            int imageIndex = 0;
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 7, 0, 1) };
            Button preview = FloatControls.Button("", "FloatPhoto" + suffix, delegate { host.ShowImage(entry, imageIndex); });
            FloatControls.Identify(preview, "FloatPhoto" + suffix, "查看原图");
            preview.Padding = new Thickness(2);
            preview.BorderThickness = new Thickness(0);
            preview.Background = Brushes.Transparent;
            BitmapSource currentImage = null;
            Action resizePreview = delegate {
                if (currentImage == null) return;
                double width = preview.ActualWidth > 4 ? preview.ActualWidth - 4 : Math.Max(1, host.ActualWidth - 74);
                preview.Height = Math.Max(40, Math.Min(host.Compact ? 100 : 250, width * currentImage.PixelHeight / currentImage.PixelWidth));
            };
            preview.SizeChanged += delegate { resizePreview(); };
            preview.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            preview.VerticalContentAlignment = VerticalAlignment.Stretch;
            panel.Children.Add(preview);

            DockPanel controls = FloatControls.Row();
            controls.Margin = new Thickness(0, 3, 0, 0);
            TextBlock counter = FloatControls.Text("", 12);
            counter.Foreground = FloatControls.Muted;
            counter.TextWrapping = TextWrapping.NoWrap;
            counter.TextTrimming = TextTrimming.CharacterEllipsis;
            counter.Margin = new Thickness(5, 0, 4, 0);
            FloatControls.Identify(counter, "FloatPhotoCount" + suffix, "当前图片");
            Button prevImage = null, nextImage = null;
            Action render = delegate
            {
                Asset asset;
                assets.TryGetValue(entry.AssetIds[imageIndex], out asset);
                string name = asset == null ? "图片记录缺失" : asset.OriginalName;
                counter.Text = (imageIndex + 1) + " / " + count;
                counter.ToolTip = name;
                if (prevImage != null) prevImage.IsEnabled = imageIndex > 0;
                if (nextImage != null) nextImage.IsEnabled = imageIndex < count - 1;
                try
                {
                    if (asset == null) throw new InvalidOperationException("图片记录缺失");
                    currentImage = PictureViewer.ReadAsset(host.Folder, asset, 600);
                    Image picture = new Image { Source = currentImage,
                        Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
                    RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
                    preview.ToolTip = "点击放大 " + name;
                    preview.Content = picture;
                    resizePreview();
                }
                catch (Exception ex)
                {
                    preview.Content = FloatControls.Text("图片无法预览", 13);
                    preview.Height = 65;
                    preview.ToolTip = ex.Message;
                }
            };
            if (count > 1)
            {
                prevImage = FloatControls.Button("‹", "FloatPreviousPhoto" + suffix, delegate { if (imageIndex > 0) { imageIndex--; render(); } });
                nextImage = FloatControls.Button("›", "FloatNextPhoto" + suffix, delegate { if (imageIndex < count - 1) { imageIndex++; render(); } });
                prevImage.ToolTip = "上一张图片";
                nextImage.ToolTip = "下一张图片";
                prevImage.MinWidth = nextImage.MinWidth = 28;
                DockPanel.SetDock(prevImage, Dock.Left);
                DockPanel.SetDock(nextImage, Dock.Left);
                controls.Children.Add(prevImage);
                controls.Children.Add(nextImage);
            }
            controls.Children.Add(counter);
            if (count > 1) panel.Children.Add(controls);
            render();
            return panel;
        }
    }
}

