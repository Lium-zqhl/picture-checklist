using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace PictureChecklist
{
    static class Dialogs
    {
        static Window Dialog(Window owner,string title,double width)
        {
            return new Window { Owner=owner,Title=title,WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Background=Design.Paper,FontFamily=owner.FontFamily,FontSize=13,Width=width,SizeToContent=SizeToContent.Height,
                ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false };
        }
        static StackPanel Body(Window w)
        {
            var panel=new StackPanel{Margin=new Thickness(20)};w.Content=panel;return panel;
        }
        static TextBox Field(StackPanel panel,string caption,string initial,string id)
        {
            var label=Design.Text(caption,13,true);label.Margin=new Thickness(0,1,0,5);panel.Children.Add(label);
            var box=new TextBox{Text=initial,Margin=new Thickness(0,0,0,12),MinWidth=100};
            AutomationProperties.SetAutomationId(box,id);panel.Children.Add(box);return box;
        }
        static StackPanel Actions(StackPanel panel)
        {
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
            panel.Children.Add(actions);return actions;
        }
        public static string Name(Window owner,string old)
        {
            var w=Dialog(owner,"重命名清单",365);var panel=Body(w);
            var box=Field(panel,"清单名称",old,"RenameName");
            var buttons=Actions(panel);
            var confirm=Design.Button("保存","ConfirmRename",delegate{if(box.Text.Trim().Length>0)w.DialogResult=true;},true);
            confirm.IsDefault=true;buttons.Children.Add(confirm);
            var cancel=Design.Button("取消","CancelRename",delegate{w.DialogResult=false;});cancel.IsCancel=true;buttons.Children.Add(cancel);
            w.Loaded+=delegate{box.SelectAll();box.Focus();};
            return w.ShowDialog()==true?box.Text.Trim():null;
        }
        public static bool ConfirmLibraryRemoval(Window owner,string name,string folder)
        {
            var w=Dialog(owner,"从清单库删除",440);var panel=Body(w);
            panel.Children.Add(Design.Text("从清单库删除“"+name+"”？",16,true));
            var description=Design.Text("仅移除本机列表入口，不会删除意见、图片或完成记录。可以撤销删除，或通过“打开清单”重新加入。",13);
            description.Margin=new Thickness(0,12,0,10);panel.Children.Add(description);
            var location=Design.Text(folder,11);location.Foreground=Design.Muted;panel.Children.Add(location);
            var buttons=Actions(panel);
            var cancel=Design.Button("取消","CancelDeleteLibrary",delegate{w.DialogResult=false;});
            cancel.IsCancel=true;cancel.IsDefault=true;buttons.Children.Add(cancel);
            var confirm=Design.Button("从列表删除","DeleteLibraryConfirm",delegate{w.DialogResult=true;});
            confirm.Foreground=Design.Brush("#A04A3B");buttons.Children.Add(confirm);
            return w.ShowDialog()==true;
        }
        public static string PickDirectory(Window owner,string description,string initial)
        {
            using(var picker=new Forms.FolderBrowserDialog {Description=description,SelectedPath=Directory.Exists(initial)?initial:"",ShowNewFolderButton=true})
                return picker.ShowDialog(new NativeOwner(owner))==Forms.DialogResult.OK?picker.SelectedPath:null;
        }
        class NativeOwner : Forms.IWin32Window
        {
            public IntPtr Handle {get;private set;}
            public NativeOwner(Window window){Handle=new WindowInteropHelper(window).Handle;}
        }
        public static string Target(Window owner,string title,string suggestion,bool newList,out string name)
        {
            var w=Dialog(owner,title,490);var panel=Body(w);
            var box=Field(panel,newList?"清单名称":"副本文件夹名",suggestion,"TargetName");
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"图文清单");
            var parent=Field(panel,"保存位置",root,"TargetParent");
            var browse=Design.Button("选择位置…","PickTargetParent",delegate{
                string pick=PickDirectory(w,"选择保存清单的上级文件夹",parent.Text);if(pick!=null)parent.Text=pick;
            });
            browse.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(browse);
            var hint=Design.Text("",12);hint.Foreground=Design.Muted;hint.Margin=new Thickness(0,9,0,0);panel.Children.Add(hint);
            Func<string> target=delegate{
                string requested=box.Text.Trim(),baseDir=parent.Text.Trim();
                if(String.IsNullOrWhiteSpace(requested) || requested=="." || requested==".." ||
                   requested.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || requested.EndsWith(".") ||
                   System.Text.RegularExpressions.Regex.IsMatch(requested,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new ArgumentException("请输入可用作文件夹名的名称。");
                if(String.IsNullOrWhiteSpace(baseDir) || !(baseDir.StartsWith(@"\\") || (baseDir.Length>2 && baseDir[1]==':' && (baseDir[2]=='\\' || baseDir[2]=='/'))))
                    throw new ArgumentException("请选择完整的保存路径。");
                string rootPath=Path.GetFullPath(baseDir); string path=Path.Combine(rootPath,requested);
                for(int i=2;Directory.Exists(path)||File.Exists(path);i++)path=Path.Combine(rootPath,requested+" ("+i+")");
                return path;
            };
            Action preview=delegate{try{hint.Text="新建于 "+target();}catch(Exception ex){hint.Text=ex.Message;}};
            parent.TextChanged+=delegate{preview();};box.TextChanged+=delegate{preview();};preview();
            string chosen=null;var buttons=Actions(panel);
            var confirm=Design.Button(newList?"创建":"生成副本","ConfirmTarget",delegate{
                try{ chosen=target();Directory.CreateDirectory(Path.GetDirectoryName(chosen));w.DialogResult=true; }
                catch(Exception ex){hint.Text=ex.Message;}
            },true);confirm.IsDefault=true;buttons.Children.Add(confirm);
            var cancel=Design.Button("取消","CancelTarget",delegate{w.DialogResult=false;});cancel.IsCancel=true;buttons.Children.Add(cancel);
            w.Loaded+=delegate{box.SelectAll();box.Focus();};
            bool ok=w.ShowDialog()==true;name=ok?box.Text.Trim():null;return ok?chosen:null;
        }
        public static int Unsaved(Window owner)
        {
            var w=Dialog(owner,"尚有修改未保存",460);var panel=Body(w);
            panel.Children.Add(Design.Text("最近的修改仍在窗口中。可以继续编辑并重试，也可以导出包含当前修改和原图的 ZIP 副本。",14));
            int result=0;var buttons=Actions(panel);
            buttons.Children.Add(Design.Button("继续编辑","KeepEditing",delegate{w.DialogResult=false;},true));
            buttons.Children.Add(Design.Button("导出副本并离开","SaveAndLeave",delegate{result=1;w.DialogResult=true;}));
            buttons.Children.Add(Design.Button("放弃未保存修改","Discard",delegate{result=2;w.DialogResult=true;}));
            w.ShowDialog();return result;
        }
    }
}
