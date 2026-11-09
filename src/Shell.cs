using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PictureChecklist
{
    public sealed partial class Shell : Window
    {
        readonly Preferences prefs = Preferences.Read();
        readonly DispatcherTimer delay = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) };
        readonly List<UndoPoint> history=new List<UndoPoint>();
        readonly string launchPath;
        FileStream lease;
        string mode="library", typingGroup, savedMessage="";
        bool pending, started, activatedElsewhere;
        TextBlock status;
        Button retry, rescue, undo;
        ContentControl view;
        PictureViewer picture;
        public Document Doc {get; private set;}
        public string Folder {get; private set;}
        public string SelectedId {get; set;}
        public string Query {get; set;}
        public string Filter {get; set;}
        public bool Compact {get {return prefs.Compact;}}
        public Entry Current {get {return Doc==null?null:Doc.Items.FirstOrDefault(e=>e.Id==SelectedId && !e.Deleted);}}

        class UndoPoint
        {
            public string Label,Json,Selection;
        }
        public Shell(string path)
        {
            launchPath=path; Title="图文修改清单"; Width=800; Height=585;
            FontFamily=new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI"); FontSize=13;
            Background=Design.Paper; Foreground=Design.Ink;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            UseLayoutRounding=true; SnapsToDevicePixels=true;
            delay.Tick+=delegate { delay.Stop(); Flush(); };
            Loaded+=delegate { if(started)return; started=true; Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.Loaded); };
            Closing+=OnClosing;
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.Key==Key.Z && Keyboard.Modifiers==ModifierKeys.Control &&
                    !(Keyboard.FocusedElement is TextBoxBase) && Doc!=null) { Undo(); e.Handled=true; }
            };
        }
        void Start()
        {
            string target=launchPath;
            if(target=="--home")target=null;
            else if(String.IsNullOrWhiteSpace(target) && !String.IsNullOrEmpty(prefs.LastFolder) &&
                    File.Exists(Path.Combine(prefs.LastFolder,"checklist.json")) && !LibraryVisibility.IsHidden(prefs.LastFolder)) target=prefs.LastFolder;
            if(!String.IsNullOrWhiteSpace(target)) {
                bool explicitTarget=!String.IsNullOrWhiteSpace(launchPath) && launchPath!="--home";
                if(OpenFolder(target,null,false,explicitTarget))return;
                if(activatedElsewhere){Close();return;}
            }
            RenderHome();
            ApplyPlacement();
        }
        public void Error(string message)
        {
            MessageBox.Show(this,message,"图文修改清单",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        void Guard(Action action)
        {
            try { action(); } catch(Exception ex) { Error(ex.Message); }
        }
        void SavePrefs()
        {
            try { prefs.Write(); } catch(Exception ex) {
                if(status!=null) { status.ToolTip="本机阅读设置未保存："+ex.Message; }
            }
        }
        void SavePlace()
        {
            if(!started)return;
            var placement=ScreenPlacement.Capture(this);
            if(placement!=null)prefs.Windows[mode]=placement;
            if(Doc!=null) prefs.Places[Folder.ToUpperInvariant()]=new ReadingPlace{SelectedId=SelectedId,Query=Query,Filter=Filter,Organizer=mode=="organize",Layout="A"};
        }
        void ApplyPlacement()
        {
            Placement last;
            prefs.Windows.TryGetValue(mode,out last);
            ScreenPlacement.Apply(this,last,mode);
        }
        void EndSession()
        {
            delay.Stop(); ClosePicture(); SavePlace();
            if(lease!=null){lease.Dispose();lease=null;}
            history.Clear(); pending=false; typingGroup=null; Doc=null; Folder=null;
        }
        void OnClosing(object sender,CancelEventArgs e)
        {
            if(Doc!=null && !CanLeave()){e.Cancel=true;return;}
            if(!activatedElsewhere){SavePlace(); SavePrefs();}
            EndSession();
        }
        void ClosePicture()
        {
            if(picture!=null){var old=picture;picture=null;old.Close();}
        }
        public void ShowImage(Entry entry,int index)
        {
            Guard(delegate {
                if(entry==null || entry.AssetIds.Count==0)return;
                ClosePicture();
                var window=new PictureViewer(this,entry,index);
                if(window.Owner==null)window.Owner=this;
                picture=window;
                window.Closed+=delegate {if(picture==window)picture=null;};
                window.Show();
            });
        }
        public void Change(string label,Action action,bool redraw=true,bool debounce=false)
        {
            if(Doc==null)return;
            bool capture=!debounce || !pending || typingGroup!=label;
            string before=capture?ModelJson.Write(Doc):null, beforeId=SelectedId;
            try {action();}
            catch(Exception ex) {
                if(before!=null)ReplaceDocument(ModelJson.Read<Document>(before));
                SelectedId=beforeId;RefreshView();Error(ex.Message);return;
            }
            if(capture) {
                history.Add(new UndoPoint{Json=before,Label=label,Selection=beforeId});
                if(history.Count>24)history.RemoveAt(0);
            }
            typingGroup=debounce?label:null; pending=true;
            Doc.Updated=DateTimeOffset.Now.ToString("o");
            SetStatus("有修改，正在保存…",false);
            if(debounce){delay.Stop();delay.Start();} else Flush();
            if(redraw)RefreshView();
        }
        void ReplaceDocument(Document replacement)
        {
            Store.InheritRevision(Doc,replacement); Doc=replacement; ClosePicture();
        }
        void Undo()
        {
            if(Doc==null || history.Count==0)return;
            var point=history[history.Count-1];history.RemoveAt(history.Count-1);
            ReplaceDocument(ModelJson.Read<Document>(point.Json));
            SelectedId=point.Selection; pending=true;typingGroup=null;
            Doc.Updated=DateTimeOffset.Now.ToString("o");
            if(Flush())SetStatus("已撤销“"+point.Label+"” · 已保存",false);
            RefreshView();
        }
        bool Flush()
        {
            delay.Stop(); typingGroup=null;
            if(!pending || Doc==null)return true;
            try {
                Store.Save(Folder,Doc); pending=false;
                SetStatus("已保存 "+DateTime.Now.ToString("HH:mm:ss"),false);return true;
            } catch(Exception ex) {
                SetStatus("保存失败 · 更改仍在窗口中",true);
                if(status!=null)status.ToolTip=ex.Message;
                return false;
            }
        }
        bool CanLeave()
        {
            bool hadDraft=FloatComposer.HasDraft(this);
            FloatComposer.CommitDraft(this);
            if(hadDraft)RefreshView();
            if(Flush())return true;
            int result=Dialogs.Unsaved(this);
            if(result==1)return ExportPackage(true);
            return result==2;
        }
        void SetStatus(string message,bool failed)
        {
            savedMessage=message;
            if(status==null)return;
            status.Text=message;status.Foreground=failed?Brushes.Firebrick:Design.Muted;
            status.ToolTip=message;
            retry.Visibility=rescue.Visibility=failed?Visibility.Visible:Visibility.Collapsed;
            undo.IsEnabled=history.Count>0;
            undo.ToolTip=undo.IsEnabled?"撤销"+history[history.Count-1].Label:"暂无可撤销的操作";
        }
        public void RefreshView()
        {
            if(Doc==null || view==null)return;
            Title=Doc.Name+" · 图文修改清单";
            view.Content=mode=="organize"?(object)new OrganizerView(this):new FloatView(this);
            if(undo!=null)undo.IsEnabled=history.Count>0;
        }
        public void Organize(){SetMode("organize");}
        public void Float(){SetMode("float");}
        void SetMode(string newMode)
        {
            if(Doc==null || newMode==mode)return;
            Flush(); SavePlace(); mode=newMode; BuildWorkingWindow(); ApplyPlacement();
        }
        void Library()
        {
            if(!CanLeave())return;
            EndSession(); mode="library";prefs.LastFolder=null;SavePrefs();RenderHome();ApplyPlacement();
        }
        void BuildWorkingWindow()
        {
            Topmost=prefs.Pinned;
            var layout=new DockPanel();
            var header=new Grid{Margin=new Thickness(17,12,12,8)};
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var headBox=new Border{Background=Brushes.White,Child=header};
            DockPanel.SetDock(headBox,Dock.Top);layout.Children.Add(headBox);
            Button menu=null; menu=Design.Quiet(Design.Button("","ListMenu",delegate{ OpenMenu(menu);}));
            menu.HorizontalContentAlignment=HorizontalAlignment.Stretch;menu.FontSize=17;
            menu.Foreground=Design.Ink;menu.FontWeight=FontWeights.SemiBold;menu.Padding=new Thickness(0,4,4,4);
            menu.ToolTip="新建、打开、切换或重命名清单";
            var nameLayout=new DockPanel();
            var arrow=Design.Text("⌄",13);arrow.Foreground=Design.Muted;arrow.Margin=new Thickness(6,0,0,0);
            DockPanel.SetDock(arrow,Dock.Right);nameLayout.Children.Add(arrow);
            var name=Design.Text(Doc.Name,17,true);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;
            name.ToolTip=Folder;nameLayout.Children.Add(name);menu.Content=nameLayout;header.Children.Add(menu);
            var headActions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(headActions,1);header.Children.Add(headActions);
            var pinned=new CheckBox{Content="置顶",IsChecked=prefs.Pinned,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5,0,5,0)};
            pinned.FontSize=12;pinned.Foreground=Design.Accent;
            AutomationProperties.SetAutomationId(pinned,"Pin");
            pinned.Click+=delegate {Topmost=prefs.Pinned=pinned.IsChecked==true;SavePrefs();};
            headActions.Children.Add(pinned);
            bool organizing=mode=="organize";
            if(organizing)headActions.Children.Add(Design.Button("返回浮窗","ToggleMode",Float,true));
            Button more=null;more=Design.Quiet(Design.Button("更多","MoreMenu",delegate{OpenMore(more);}));headActions.Children.Add(more);
            var footer=new DockPanel {LastChildFill=true,Margin=new Thickness(18,3,13,7)};
            var footBox=new Border{Background=Brushes.White,Child=footer};
            DockPanel.SetDock(footBox,Dock.Bottom);layout.Children.Add(footBox);
            if(!organizing) {
                var compact=new CheckBox{Content="紧凑",IsChecked=Compact,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,4,0),FontSize=11,Foreground=Design.Muted};
                AutomationProperties.SetAutomationId(compact,"Compact");
                compact.Click+=delegate {prefs.Compact=compact.IsChecked==true;SavePrefs();RefreshView();};
                DockPanel.SetDock(compact,Dock.Right);footer.Children.Add(compact);
            }
            undo=Design.Quiet(Design.Button("撤销","Undo",Undo));retry=Design.Button("重试","RetrySave",delegate{Flush();});
            rescue=Design.Button("另存","Rescue",delegate{ExportPackage(true);});
            foreach(var b in new[]{undo,rescue,retry}){DockPanel.SetDock(b,Dock.Right);footer.Children.Add(b);}
            status=Design.Text(savedMessage,11);status.TextWrapping=TextWrapping.NoWrap;status.TextTrimming=TextTrimming.CharacterEllipsis;
            AutomationProperties.SetAutomationId(status,"SaveState");footer.Children.Add(status);
            view=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
            layout.Children.Add(view);Content=layout; RefreshView();SetStatus(savedMessage,pending && savedMessage.StartsWith("保存失败",StringComparison.Ordinal));
        }
        void OpenMenu(Button owner)
        {
            var menu=new ContextMenu {PlacementTarget=owner,Placement=PlacementMode.Bottom};
            AddMenu(menu,"清单库与切换","MenuLibrary",Library);
            AddMenu(menu,"新建清单…","MenuNew",NewList);
            AddMenu(menu,"打开清单…","MenuOpen",BrowseOpen);
            AddMenu(menu,"重命名…","MenuRename",delegate{
                string name=Dialogs.Name(this,Doc.Name);
                if(name==null || name==Doc.Name)return;
                Change("重命名清单",delegate{Doc.Name=name;},false,false);
                BuildWorkingWindow();
            });
            AddMenu(menu,Doc.Archived?"恢复归档清单":"归档清单","MenuArchive",delegate{
                bool archive=!Doc.Archived;
                Change(archive?"归档清单":"恢复归档",delegate{Doc.Archived=archive;},false,false);
                if(!pending)Library();
            });
            menu.IsOpen=true;
        }
        void OpenMore(Button owner)
        {
            var menu=new ContextMenu {PlacementTarget=owner,Placement=PlacementMode.Bottom};
            if(mode=="float")AddMenu(menu,"整理与图片资料库…","ToggleMode",Organize);
            AddMenu(menu,"回收站…","MenuTrash",delegate{Organize();var organizer=view.Content as OrganizerView;if(organizer!=null)organizer.ShowTrash();});
            menu.Items.Add(new Separator());
            AddMenu(menu,"导出完整备份 ZIP…","MenuExport",delegate{ExportPackage(false);});
            AddMenu(menu,"从 ZIP 恢复到新目录…","MenuImport",ImportZip);
            AddMenu(menu,"从上一保存点恢复副本…","MenuPrevious",delegate{RestorePrevious(Folder);});
            AddMenu(menu,"迁移旧版清单…","MenuMigrate",ImportLegacy);
            menu.IsOpen=true;
        }
        void AddMenu(ContextMenu menu,string label,string id,Action action)
        {
            var item=new MenuItem{Header=label};AutomationProperties.SetAutomationId(item,id);
            item.Click+=delegate{Guard(action);};menu.Items.Add(item);
        }
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h,int cmd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        FileStream Acquire(string folder)
        {
            string path=Path.Combine(folder,".picturechecklist.lock");
            FileStream stream;
            try { stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read); }
            catch(IOException) {
                try {
                    using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                    using(var reader=new StreamReader(file)) {
                        string[] info=reader.ReadToEnd().Split('|');uint id;long raw;
                        if(info.Length==2 && uint.TryParse(info[0],out id) && long.TryParse(info[1],out raw)) {
                            var h=new IntPtr(raw);uint actual;
                            GetWindowThreadProcessId(h,out actual);
                            if(actual==id && Process.GetProcessById((int)id).ProcessName==Process.GetCurrentProcess().ProcessName) {
                                if(IsIconic(h))ShowWindowAsync(h,9);
                                SetForegroundWindow(h);activatedElsewhere=true;return null;
                            }
                        }
                    }
                }catch{}
                throw new IOException("此清单正被使用，或其文件夹暂时无法写入。");
            }
            try {
                var data=Encoding.ASCII.GetBytes(Process.GetCurrentProcess().Id+"|"+new WindowInteropHelper(this).Handle.ToInt64());
                stream.SetLength(0);stream.Write(data,0,data.Length);stream.Flush();return stream;
            }catch{stream.Dispose();throw;}
        }
        bool OpenFolder(string input,string preferredMode,bool leaveChecked=false,bool restoreLibraryEntry=true)
        {
            FileStream incoming=null;activatedElsewhere=false;
            try {
                string folder=Path.GetFullPath(File.Exists(input)?Path.GetDirectoryName(input):input).TrimEnd(Path.DirectorySeparatorChar);
                if(!restoreLibraryEntry && LibraryVisibility.IsHidden(folder))return false;
                if(!File.Exists(Path.Combine(folder,"checklist.json")))throw new IOException("所选文件夹中找不到 checklist.json。");
                if(String.Equals(folder,Folder,StringComparison.OrdinalIgnoreCase)) {
                    if(restoreLibraryEntry){LibraryVisibility.SetHidden(folder,false);ForgetLibraryRemoval(folder);}return true;
                }
                if(Doc!=null && !leaveChecked && !CanLeave())return false;
                incoming=Acquire(folder);if(incoming==null) {
                    if(activatedElsewhere && restoreLibraryEntry){LibraryVisibility.SetHidden(folder,false);ForgetLibraryRemoval(folder);}
                    return false;
                }
                var document=Store.Load(folder);
                if(!restoreLibraryEntry && LibraryVisibility.IsHidden(folder))return false;
                if(restoreLibraryEntry){LibraryVisibility.SetHidden(folder,false);ForgetLibraryRemoval(folder);}
                EndSession();
                Folder=folder; Doc=document; lease=incoming;incoming=null;
                ReadingPlace place;
                prefs.Places.TryGetValue(Folder.ToUpperInvariant(),out place);
                Query=place==null?"":place.Query??"";Filter=place==null?"all":place.Filter??"all";
                SelectedId=place==null?null:place.SelectedId;
                if(Current==null){var first=Doc.Items.FirstOrDefault(t=>!t.Deleted);SelectedId=first==null?null:first.Id;}
                mode=preferredMode??(place!=null && place.Layout=="A" && place.Organizer?"organize":"float");
                prefs.LastFolder=folder;prefs.Remember(folder);SavePrefs();
                savedMessage="已载入 · "+DateTime.Now.ToString("HH:mm");BuildWorkingWindow();ApplyPlacement();return true;
            }catch(Exception ex){Error(ex.Message);return false;}
            finally{if(incoming!=null)incoming.Dispose();}
        }
    }
}


