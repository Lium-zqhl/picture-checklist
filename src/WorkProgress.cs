using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PictureChecklist
{
    public sealed partial class Shell
    {
        internal T RunWork<T>(string caption,Func<T> action)
        {
            T result=default(T);Exception failure=null;bool finished=false;
            var window=new Window {
                Owner=this,Title="图文修改清单",WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Width=390,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,
                ShowInTaskbar=false,FontFamily=FontFamily,FontSize=14,Background=Design.Paper
            };
            var panel=new StackPanel{Margin=new Thickness(24)};
            panel.Children.Add(Design.Text(caption,15,true));
            panel.Children.Add(new ProgressBar{IsIndeterminate=true,Height=5,Margin=new Thickness(0,20,0,10),Foreground=Design.Accent});
            window.Content=panel;
            window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!finished)e.Cancel=true;};
            Task.Factory.StartNew(action).ContinueWith(task=>{
                if(task.IsFaulted)failure=task.Exception.GetBaseException();
                else if(task.IsCanceled)failure=new OperationCanceledException("操作已取消。");
                else result=task.Result;
                finished=true;window.Close();
            },TaskScheduler.FromCurrentSynchronizationContext());
            window.ShowDialog();
            if(failure!=null)throw failure;
            return result;
        }
    }
}
