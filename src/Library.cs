using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;

namespace PictureChecklist
{
    public sealed partial class Shell
    {
        sealed class LibraryEntry
        {
            public string Folder,Name,Summary;
            public bool Archived,Broken;
        }
        readonly Stack<LibraryEntry> removedLibraryEntries=new Stack<LibraryEntry>();
        void ForgetLibraryRemoval(string folder)
        {
            string key=LibraryVisibility.Key(folder);
            var keep=removedLibraryEntries.Where(i=>LibraryVisibility.Key(i.Folder)!=key).Reverse().ToList();
            removedLibraryEntries.Clear();foreach(var entry in keep)removedLibraryEntries.Push(entry);
        }
        static string SampleRoot {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"示例清单");}}
        List<LibraryEntry> LibraryItems()
        {
            var known=new List<string>(prefs.Recent);
            if(Directory.Exists(SampleRoot))known.AddRange(Directory.GetDirectories(SampleRoot));
            var result=new List<LibraryEntry>();
            var hidden=LibraryVisibility.HiddenFolders();
            foreach(var path in known.Distinct(StringComparer.OrdinalIgnoreCase)) {
                try {
                    if(hidden.Contains(LibraryVisibility.Key(path)))continue;
                    if(!File.Exists(Path.Combine(path,"checklist.json")) && !File.Exists(Path.Combine(path,"backups","previous.json")))continue;
                    var d=ModelJson.Read<Document>(File.ReadAllText(Path.Combine(path,"checklist.json"),System.Text.Encoding.UTF8));
                    if(d.Version!=1 || d.Items==null || String.IsNullOrWhiteSpace(d.Name))throw new IOException("清单格式无法识别。");var entries=d.Items.Where(x=>!x.Deleted).ToList();
                    result.Add(new LibraryEntry {Folder=path,Name=d.Name,Archived=d.Archived,
                        Summary="已完成 "+entries.Count(x=>x.Status=="done")+" / "+entries.Count+"     要求待确认 "+entries.Count(x=>x.NeedsConfirmation&&!x.Clarified)});
                }catch(Exception ex){result.Add(new LibraryEntry{Folder=path,Name=Path.GetFileName(path),Summary=ex.Message,Broken=true});}
            }
            return result;
        }
        void RenderHome()
        {
            status=null;view=null;Title="清单库 · 图文修改清单";Topmost=false;mode="library";
            var layout=new DockPanel{Margin=new Thickness(24,18,24,16)};
            var header=new StackPanel();
            var title=Design.Text("图文修改清单",24,true);title.Margin=new Thickness(0,0,0,13);header.Children.Add(title);
            var commands=new WrapPanel();
            commands.Children.Add(Design.Button("新建清单","NewList",delegate{Guard(NewList);},true));
            commands.Children.Add(Design.Button("打开清单…","OpenList",delegate{Guard(BrowseOpen);}));
            Button more=null;more=Design.Button("导入与恢复 ▾","ImportMenu",delegate{
                var m=new ContextMenu{PlacementTarget=more,Placement=PlacementMode.Bottom};
                AddMenu(m,"导入完整 ZIP…","HomeZip",ImportZip);
                AddMenu(m,"迁移旧版清单…","HomeLegacy",ImportLegacy);
                AddMenu(m,"恢复上一保存点…","HomePrevious",delegate{RestorePrevious(null);});m.IsOpen=true;
            });commands.Children.Add(more);header.Children.Add(commands);
            var strip=new DockPanel{Margin=new Thickness(0,22,0,8)};
            strip.Children.Add(Design.Text("我的清单",16,true));
            var category=new ComboBox{ItemsSource=new[]{"常用清单","已归档","全部"},SelectedIndex=0,Width=105,HorizontalAlignment=HorizontalAlignment.Right};
            AutomationProperties.SetAutomationId(category,"LibraryFilter");strip.Children.Add(category);header.Children.Add(strip);
            DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
            var removalBar=new DockPanel{Margin=new Thickness(0,10,0,0),LastChildFill=true};
            DockPanel.SetDock(removalBar,Dock.Bottom);layout.Children.Add(removalBar);
            var restore=Design.Button("撤销删除","UndoLibraryDelete",delegate{});
            DockPanel.SetDock(restore,Dock.Right);removalBar.Children.Add(restore);
            var removalNotice=Design.Text("",12);removalNotice.Foreground=Design.Muted;
            AutomationProperties.SetAutomationId(removalNotice,"LibraryNotice");removalBar.Children.Add(removalNotice);
            var list=new StackPanel();var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            layout.Children.Add(scroll);var data=LibraryItems();
            Action draw=null;draw=delegate {
                list.Children.Clear();
                removalBar.Visibility=removedLibraryEntries.Count==0?Visibility.Collapsed:Visibility.Visible;
                if(removedLibraryEntries.Count>0)removalNotice.Text="已移除“"+removedLibraryEntries.Peek().Name+"” · 原文件保留";
                foreach(var entry in data.Where(i=>category.SelectedIndex==2 || i.Archived==(category.SelectedIndex==1))){
                    var row=new DockPanel{Margin=new Thickness(12,10,10,10)};
                    var open=Design.Button(entry.Broken?"恢复副本":"打开","OpenRecent",delegate{
                        Guard(delegate{if(entry.Broken)RestorePrevious(entry.Folder);else OpenFolder(entry.Folder,null);});
                    });DockPanel.SetDock(open,Dock.Right);open.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(open);
                    var remove=Design.Button("删除","DeleteRecent",delegate{
                        if(!Dialogs.ConfirmLibraryRemoval(this,entry.Name,entry.Folder))return;
                        Guard(delegate{
                            LibraryVisibility.SetHidden(entry.Folder,true);
                            removedLibraryEntries.Push(entry);data=LibraryItems();draw();
                        });
                    });remove.ToolTip="从清单库移除，保留资料文件";
                    remove.Foreground=Design.Brush("#A04A3B");remove.Background=Brushes.Transparent;
                    remove.BorderThickness=new Thickness(0);remove.VerticalAlignment=VerticalAlignment.Center;
                    DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
                    var labels=new StackPanel{Margin=new Thickness(0,0,12,0)};
                    labels.Children.Add(Design.Text(entry.Name+(entry.Archived?"  ·  已归档":""),15,true));
                    var summary=Design.Text(entry.Summary,12);summary.Foreground=Design.Muted;labels.Children.Add(summary);
                    var path=Design.Text(entry.Folder,11);path.Foreground=Design.Muted;path.TextWrapping=TextWrapping.NoWrap;path.TextTrimming=TextTrimming.CharacterEllipsis;path.ToolTip=entry.Folder;labels.Children.Add(path);
                    row.Children.Add(labels);list.Children.Add(new Border {Child=row,Margin=new Thickness(0,0,0,7),
                        BorderThickness=new Thickness(1),BorderBrush=Design.Line,Background=Brushes.White,CornerRadius=new CornerRadius(5)});
                }
                if(list.Children.Count==0) {
                    var empty=Design.Text(category.SelectedIndex==1?"暂无已归档清单。":"创建一份清单，或打开已有资料夹中的 checklist.json。",14);
                    empty.Foreground=Design.Muted;empty.Margin=new Thickness(3,24,8,8);list.Children.Add(empty);
                }
            };
            restore.Click+=delegate {Guard(delegate{
                if(removedLibraryEntries.Count==0)return;
                var entry=removedLibraryEntries.Peek();
                if(!File.Exists(Path.Combine(entry.Folder,"checklist.json")) && !File.Exists(Path.Combine(entry.Folder,"backups","previous.json")))
                    throw new IOException("原资料已不在该位置，请通过“打开清单”选择资料的新位置。");
                LibraryVisibility.SetHidden(entry.Folder,false);removedLibraryEntries.Pop();
                data=LibraryItems();category.SelectedIndex=entry.Archived?1:0;draw();
            });};
            category.SelectionChanged+=delegate{draw();};draw();Content=layout;
        }
        void NewList()
        {
            string name;
            string target=Dialogs.Target(this,"新建清单","新清单",true,out name);if(target==null)return;
            if(Doc!=null && !CanLeave())return;
            var doc=new Document{Name=name};
            Directory.CreateDirectory(target); Store.Save(target,doc); OpenFolder(target,"float",true);
        }
        void BrowseOpen()
        {
            var open=new OpenFileDialog {Title="打开清单文件",Filter="清单文件 (checklist.json)|checklist.json",CheckFileExists=true};
            if(open.ShowDialog(this)==true)OpenFolder(open.FileName,null);
        }
        bool ExportPackage(bool rescueMode)
        {
            if(Doc==null)return false;
            if(!rescueMode && !Flush())return false;
            string safe=String.Concat(Doc.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
            var save=new SaveFileDialog{Title=rescueMode?"另存当前内容和原图":"导出完整清单",Filter="完整清单 (*.zip)|*.zip",
                FileName=safe+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip",DefaultExt=".zip",AddExtension=true};
            if(save.ShowDialog(this)!=true)return false;
            try {
                RunWork("正在打包清单与原图…",delegate{Package.Export(Folder,Doc,save.FileName);return true;});
                if(pending) {SetStatus("副本已导出 · 原清单保存失败",true);}
                else SetStatus("已导出完整备份",false);
                return true;
            }catch(Exception ex){Error(ex.Message);return false;}
        }
        void ImportZip()
        {
            var pick=new OpenFileDialog{Title="选择清单备份",Filter="清单 ZIP (*.zip)|*.zip"};
            if(pick.ShowDialog(this)!=true)return;
            string name,target=Dialogs.Target(this,"从 ZIP 导入清单",Path.GetFileNameWithoutExtension(pick.FileName),false,out name);
            if(target==null || (Doc!=null&&!CanLeave()))return;
            RunWork("正在导入并核对清单…",delegate{return Package.Import(pick.FileName,target);});OpenFolder(target,null,true);
        }
        void ImportLegacy()
        {
            var pick=new OpenFileDialog{Title="选择旧清单的 tasks.json",Filter="旧清单 (tasks.json)|tasks.json"};
            if(pick.ShowDialog(this)!=true)return;
            string src=Path.GetDirectoryName(pick.FileName);
            var text=RunWork("正在检查旧清单…",delegate{return Legacy.Preview(src);});
            if(MessageBox.Show(this,text+"\n\n继续选择新副本的保存位置？","迁移预览",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
            string name,target=Dialogs.Target(this,"迁移到新清单","旧清单（迁移副本）",false,out name);
            if(target==null || (Doc!=null&&!CanLeave()))return;
            RunWork("正在复制旧清单与原图…",delegate{return Legacy.Import(src,target);});OpenFolder(target,null,true);
        }
        void RestorePrevious(string source)
        {
            if(source==null){
                var pick=new OpenFileDialog{Title="选择清单或 backups 内的 previous.json",Filter="清单与保存点|checklist.json;previous.json"};
                if(pick.ShowDialog(this)!=true)return;
                source=Path.GetDirectoryName(pick.FileName);
                if(Path.GetFileName(pick.FileName).Equals("previous.json",StringComparison.OrdinalIgnoreCase))source=Path.GetDirectoryName(source);
            }
            string name,target=Dialogs.Target(this,"恢复上一保存点",Path.GetFileName(source)+"（恢复）",false,out name);
            if(target==null || (Doc!=null&&!CanLeave()))return;
            RunWork("正在恢复保存点…",delegate{return Store.RestorePrevious(source,target);});OpenFolder(target,null,true);
        }
    }
}

