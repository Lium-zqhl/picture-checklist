using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PictureChecklist
{
    // Everyday editing stays beside the floating checklist; the organizer remains available for larger jobs.
    internal sealed class EntryEditor : Window
    {
        readonly Shell host;
        readonly string entryId;
        readonly TextBox title, body, note, caution;
        readonly CheckBox unclear;
        Entry Current { get { return host.Doc.Items.FirstOrDefault(e => e.Id == entryId && !e.Deleted); } }
        internal EntryEditor(Shell host, Entry entry)
        {
            this.host = host; entryId = entry.Id;
            Title = "编辑意见"; Owner = host; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = host.FontFamily; FontSize = 13; Background = Brushes.White; Foreground = FloatControls.Ink;
            Width = Math.Min(470, SystemParameters.WorkArea.Width); Height = Math.Min(650, SystemParameters.WorkArea.Height);
            MinWidth = 320; MinHeight = 380; UseLayoutRounding = true;
            FloatControls.Identify(this, "EntryEditor", "编辑意见");
            DockPanel layout = new DockPanel { Margin = new Thickness(18) }; Content = layout;
            DockPanel footer = FloatControls.Row(); footer.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
            Button save = FloatControls.Button("保存修改", "EditorSave", Save);
            save.Background = FloatControls.Green; save.Foreground = Brushes.White; save.BorderBrush = FloatControls.Green;
            save.Padding = new Thickness(18, 7, 18, 7); DockPanel.SetDock(save, Dock.Right); footer.Children.Add(save);
            Button cancel = FloatControls.Button("取消", "EditorCancel", Close); cancel.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel);
            footer.Children.Add(FloatControls.Text("删除和排序均可撤销", 11));
            StackPanel fields = new StackPanel();
            layout.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            title = Field(fields, "标题", "EditorTitle", entry.Title, 34, false);
            body = Field(fields, "修改意见", "EditorBody", entry.Body, 112, true);
            note = Field(fields, "备注 / 部分完成情况", "EditorNote", entry.Note, 68, true);
            caution = Field(fields, "原始补充说明", "EditorCaution", entry.Caution, 60, true);
            unclear = new CheckBox { Content = "这条意见需要澄清", IsChecked = entry.NeedsConfirmation && !entry.Clarified, Margin = new Thickness(0, 3, 0, 16) };
            FloatControls.Identify(unclear, "EditorUnclear", "这条意见需要澄清"); fields.Children.Add(unclear);
            WrapPanel tools = new WrapPanel(); fields.Children.Add(tools);
            Button up = FloatControls.Button("上移", "EditorMoveUp", delegate { Move(-1); }); up.Margin = new Thickness(0, 0, 7, 7); tools.Children.Add(up);
            Button down = FloatControls.Button("下移", "EditorMoveDown", delegate { Move(1); }); down.Margin = new Thickness(0, 0, 7, 7); tools.Children.Add(down);
            Button remove = FloatControls.Button("移入回收站", "EditorDelete", delegate {
                Entry current = Current; if (current == null) return;
                host.Change("移入回收站", delegate { ApplyFields(current); current.Deleted = true; Touch(current); }); Close();
            });
            remove.Foreground = FloatControls.Alert; remove.Margin = new Thickness(0, 0, 7, 7); tools.Children.Add(remove);
            if (entry.AssetIds.Count > 1) {
                Button split = FloatControls.Button("拆为单图意见", "EditorSplit", Split);
                split.ToolTip = "第一张保留原条目，后续图片各成为一条；原文、备注和完成状态保留。整次拆分可撤销。";
                split.Margin = new Thickness(0, 0, 7, 7); tools.Children.Add(split);
            }
            Button append = FloatControls.Button("为本条补图…", "EditorAppendImages", delegate {
                Entry current = Current; if (current != null) ImageImport.Append(host, current);
            });
            append.Margin = new Thickness(0, 0, 7, 7); tools.Children.Add(append);
        }
        static TextBox Field(Panel parent, string label, string id, string value, double height, bool multiline)
        {
            TextBlock caption = FloatControls.Text(label, 12); caption.Foreground = FloatControls.Muted;
            caption.Margin = new Thickness(0, 0, 0, 5); parent.Children.Add(caption);
            TextBox text = new TextBox { Text = value ?? "", MinHeight = height, MaxHeight = multiline ? height + 60 : height,
                AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 13) };
            FloatControls.Identify(text, id, label); parent.Children.Add(text); return text;
        }
        void Save()
        {
            Entry current = Current; if (current == null) { Close(); return; }
            host.Change("编辑意见", delegate {
                ApplyFields(current); host.SelectedId = current.Id;
            });
            Close();
        }
        void ApplyFields(Entry current)
        {
            current.Title = String.IsNullOrWhiteSpace(title.Text) ? "新意见" : title.Text.Trim();
            current.Body = body.Text; current.Note = note.Text; current.Caution = caution.Text;
            if (unclear.IsChecked == true) { current.NeedsConfirmation = true; current.Clarified = false; }
            else if (current.NeedsConfirmation) current.Clarified = true;
            Touch(current);
        }
        void Move(int by)
        {
            Entry current = Current; if (current == null) return;
            var active = host.Doc.Items.Where(e => !e.Deleted).ToList();
            int n = active.IndexOf(current), other = n + by;
            if (other < 0 || other >= active.Count) return;
            int first = host.Doc.Items.IndexOf(current), second = host.Doc.Items.IndexOf(active[other]);
            host.Change(by < 0 ? "上移意见" : "下移意见", delegate {
                host.Doc.Items[first] = active[other]; host.Doc.Items[second] = current; host.SelectedId = current.Id;
            });
        }
        void Split()
        {
            Entry current = Current; if (current == null || current.AssetIds.Count < 2) return;
            host.Change("拆为单图意见", delegate {
                ApplyFields(current);
                string[] images = current.AssetIds.ToArray();
                int index = host.Doc.Items.IndexOf(current);
                string source = ModelJson.Write(current);
                current.AssetIds = new List<string> { images[0] }; Touch(current);
                for (int i = 1; i < images.Length; i++) {
                    Entry copy = ModelJson.Read<Entry>(source); copy.Id = Guid.NewGuid().ToString("N");
                    copy.AssetIds = new List<string> { images[i] }; copy.LegacyNumber = 0; Touch(copy);
                    host.Doc.Items.Insert(index + i, copy);
                }
                host.SelectedId = current.Id;
            });
            Close();
        }
        static void Touch(Entry entry) { entry.Updated = DateTimeOffset.Now.ToString("o"); }
    }
}
