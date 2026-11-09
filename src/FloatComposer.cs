using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PictureChecklist
{
    // Drafts belong to a document, not a particular rendering of the floating view.
    internal sealed class FloatComposer : Border
    {
        sealed class Draft { internal string DocumentId, Text = ""; internal bool Pasting; internal TextBox Input; }
        static readonly ConditionalWeakTable<Shell, Draft> Drafts = new ConditionalWeakTable<Shell, Draft>();
        readonly Shell host;
        readonly Draft draft;
        readonly TextBox input;
        readonly TextBlock placeholder;

        static Draft State(Shell host)
        {
            Draft result = Drafts.GetValue(host, delegate { return new Draft(); });
            if (result.DocumentId != host.Doc.Id) { result.DocumentId = host.Doc.Id; result.Text = ""; result.Pasting = false; result.Input = null; }
            return result;
        }
        internal static bool HasDraft(Shell host)
        {
            Draft value;
            return host.Doc != null && Drafts.TryGetValue(host, out value) && value.DocumentId == host.Doc.Id && !String.IsNullOrWhiteSpace(value.Text);
        }
        internal static void CommitDraft(Shell host)
        {
            if (!HasDraft(host)) return;
            Draft value = State(host);
            string text = value.Text.Trim();
            string first = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "新意见";
            Entry entry = new Entry { Title = first.Length > 28 ? first.Substring(0, 28) : first, Body = text };
            host.Change("新增文字意见", delegate {
                host.Doc.Items.Add(entry);
                host.SelectedId = entry.Id;
                host.Query = ""; host.Filter = "all";
            }, false);
            if (host.Doc.Items.Any(e => e.Id == entry.Id)) {
                value.Text = "";
                if (value.Input != null) value.Input.Clear();
            }
        }

        internal void FocusInput() { input.Focus(); }

        internal FloatComposer(Shell host, UIElement pasteSurface)
        {
            this.host = host;
            draft = State(host);
            Margin = new Thickness(16, 9, 16, 3);
            Padding = new Thickness(11, 9, 11, 7);
            Background = Brushes.White;
            BorderBrush = FloatControls.ColorBrush("#DCE4DD"); BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(9);
            FloatControls.Identify(this, "FloatComposer", "粘贴截图，新建意见");
            StackPanel layout = new StackPanel(); Child = layout;
            TextBlock label = FloatControls.Text("粘贴截图，新建意见", 12);
            label.Foreground = FloatControls.Green; label.FontWeight = FontWeights.SemiBold;
            label.Margin = new Thickness(0, 0, 0, 4); layout.Children.Add(label);
            Grid textArea = new Grid(); layout.Children.Add(textArea);
            input = new TextBox { Text = draft.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
                MinHeight = 42, MaxHeight = 92, FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            FloatControls.Identify(input, "FloatComposerInput", "粘贴截图或填写新意见");
            draft.Input = input;
            input.ToolTip = "Ctrl+V 粘贴；每张图片新建一条意见。文字可点击添加，关闭或切换清单时会自动保留为意见。";
            textArea.Children.Add(input);
            placeholder = FloatControls.Text("点击这里 Ctrl+V，每张截图一条意见\n也可以直接写下修改意见…", 11);
            placeholder.Foreground = FloatControls.ColorBrush("#9AA69D");
            placeholder.VerticalAlignment = VerticalAlignment.Top; placeholder.IsHitTestVisible = false;
            placeholder.LineHeight = 19; textArea.Children.Add(placeholder);
            placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            input.TextChanged += delegate { draft.Text = input.Text; placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };

            DockPanel actions = FloatControls.Row(); actions.Margin = new Thickness(0, 5, 0, 0); layout.Children.Add(actions);
            Button add = FloatControls.Button("添加意见", "FloatAddText", delegate {
                if (!HasDraft(host)) { input.Focus(); return; }
                CommitDraft(host); host.RefreshView();
            });
            add.Background = FloatControls.Green; add.Foreground = Brushes.White; add.BorderBrush = FloatControls.Green;
            add.Padding = new Thickness(11, 4, 11, 4); add.FontSize = 12;
            DockPanel.SetDock(add, Dock.Right); actions.Children.Add(add);
            Button files = FloatControls.Button("选图片", "FloatImportImages", PickFiles);
            files.BorderThickness = new Thickness(0); files.Background = Brushes.Transparent; files.Foreground = FloatControls.Muted;
            files.FontSize = 11; files.Margin = new Thickness(0, 0, 7, 0);
            DockPanel.SetDock(files, Dock.Right); actions.Children.Add(files);
            TextBlock hint = FloatControls.Text("也可拖入图片", 10); hint.Foreground = FloatControls.ColorBrush("#98A59B"); actions.Children.Add(hint);

            var context = new ContextMenu();
            var paste = new MenuItem { Header = "粘贴", Command = ApplicationCommands.Paste, CommandTarget = input };
            FloatControls.Identify(paste, "FloatComposerPaste", "粘贴文字或截图"); context.Items.Add(paste); input.ContextMenu = context;
            CommandManager.AddPreviewCanExecuteHandler(pasteSurface, CanPaste);
            CommandManager.AddPreviewExecutedHandler(pasteSurface, PasteCommand);
            pasteSurface.PreviewKeyDown += PasteKey;
            DataObject.AddPastingHandler(pasteSurface, OnPasting);
            pasteSurface.AllowDrop = true;
            pasteSurface.PreviewDragOver += delegate(object sender, DragEventArgs e) {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                e.Effects = DragDropEffects.Copy; e.Handled = true;
            };
            pasteSurface.PreviewDrop += delegate(object sender, DragEventArgs e) {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                e.Handled = true; PasteData(e.Data);
            };
        }

        internal bool PasteData(IDataObject data)
        {
            if (draft.Pasting) return false;
            try {
                if (!DirectPaste.HasImages(data)) return false;
                draft.Pasting = true;
                if (!DirectPaste.Apply(host, data, draft.Text.Trim())) return false;
                draft.Text = "";
                host.Query = ""; host.Filter = "all";
                host.RefreshView();
                return true;
            } catch (Exception ex) { host.Error(ex.Message); return false; }
            finally { draft.Pasting = false; }
        }
        void PickFiles()
        {
            var picker = new OpenFileDialog { Title = "每张图片新建一条意见", Multiselect = true, CheckFileExists = true,
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*" };
            if (picker.ShowDialog(host) != true) return;
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, picker.FileNames); PasteData(data);
        }
        void CanPaste(object sender, CanExecuteRoutedEventArgs e)
        {
            if (e.Command != ApplicationCommands.Paste) return;
            try { if (DirectPaste.HasImages(DirectPaste.ReadClipboard())) { e.CanExecute = true; e.Handled = true; } }
            catch (Exception) { }
        }
        void PasteCommand(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command != ApplicationCommands.Paste) return;
            try { var data = DirectPaste.ReadClipboard(); if (!DirectPaste.HasImages(data)) return; e.Handled = true; PasteData(data); }
            catch (Exception ex) { e.Handled = true; host.Error(ex.Message); }
        }
        void PasteKey(object sender, KeyEventArgs e)
        {
            if (!((e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift))) return;
            try { var data = DirectPaste.ReadClipboard(); if (!DirectPaste.HasImages(data)) return; e.Handled = true; PasteData(data); }
            catch (Exception ex) { e.Handled = true; host.Error(ex.Message); }
        }
        void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            try {
                if (!DirectPaste.HasImages(e.DataObject)) return;
                e.CancelCommand(); PasteData(e.DataObject);
            } catch (Exception ex) { e.CancelCommand(); host.Error(ex.Message); }
        }
    }
}
