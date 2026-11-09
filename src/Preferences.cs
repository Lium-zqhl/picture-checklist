using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace PictureChecklist
{
    public class ReadingPlace
    {
        public string SelectedId { get; set; }
        public string Query { get; set; }
        public string Filter { get; set; }
        public bool Organizer { get; set; }
        public string Layout { get; set; }
    }
    public class Placement
    {
        public string Monitor { get; set; }
        public double Across { get; set; }
        public double Down { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
    public class Preferences
    {
        private Dictionary<string,string> originalPlaces=new Dictionary<string,string>();
        private Dictionary<string,string> originalWindows=new Dictionary<string,string>();
        private string originalLastFolder;
        private bool originalPinned=true,originalCompact,recentChanged;
        public List<string> Recent { get; set; }
        public Dictionary<string,ReadingPlace> Places { get; set; }
        public Dictionary<string,Placement> Windows { get; set; }
        public string LastFolder { get; set; }
        public bool Pinned { get; set; }
        public bool Compact { get; set; }
        public Preferences()
        {
            Pinned = true; Recent = new List<string>(); Places = new Dictionary<string,ReadingPlace>(); Windows = new Dictionary<string,Placement>();
        }
        internal static string Profile
        {
            get {
                string test = Environment.GetEnvironmentVariable("PICLIST_PROFILE_DIR");
                return String.IsNullOrWhiteSpace(test) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PictureChecklist") : Path.GetFullPath(test);
            }
        }
        static string FileName { get { return Path.Combine(Profile, "preferences.json"); } }
        public static Preferences Read()
        {
            try {
                if(File.Exists(FileName)) {
                    var p = ModelJson.Read<Preferences>(File.ReadAllText(FileName, Encoding.UTF8));
                    if(p != null && p.Recent != null && p.Places != null && p.Windows != null) {p.Baseline(); return p;}
                }
            } catch (IOException) { } catch (ArgumentException) { } catch (InvalidOperationException) { } catch (UnauthorizedAccessException) { }
            return new Preferences();
        }
        public void Remember(string folder)
        {
            recentChanged=true;
            Recent.RemoveAll(p => String.Equals(p,folder,StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0,folder);
        }
        void Baseline()
        {
            originalPlaces=Places.ToDictionary(p=>p.Key,p=>ModelJson.Write(p.Value));
            originalWindows=Windows.ToDictionary(p=>p.Key,p=>ModelJson.Write(p.Value));
            originalLastFolder=LastFolder;originalPinned=Pinned;originalCompact=Compact;recentChanged=false;
        }
        static void MergeUnchanged<T>(Dictionary<string,T> local,Dictionary<string,T> disk,Dictionary<string,string> baseline)
        {
            foreach(var pair in disk) {
                string original;
                if(!local.ContainsKey(pair.Key) || (baseline.TryGetValue(pair.Key,out original) && original==ModelJson.Write(local[pair.Key])))
                    local[pair.Key]=pair.Value;
            }
        }
        public void Write()
        {
            Directory.CreateDirectory(Profile);
            using(var mutex = new System.Threading.Mutex(false, @"Local\PictureChecklistPreferencesV1")) {
                bool held=false;
                try {
                    try { held=mutex.WaitOne(1500); } catch(System.Threading.AbandonedMutexException) { held=true; }
                    if(!held) throw new IOException("本机设置正在由另一个窗口保存。");
                    var other=Read();
                    if(!recentChanged)Recent=other.Recent;
                    else foreach(var path in other.Recent) if(!Recent.Contains(path,StringComparer.OrdinalIgnoreCase)) Recent.Add(path);
                    MergeUnchanged(Places,other.Places,originalPlaces);
                    MergeUnchanged(Windows,other.Windows,originalWindows);
                    if(LastFolder==originalLastFolder)LastFolder=other.LastFolder;
                    if(Pinned==originalPinned)Pinned=other.Pinned;
                    if(Compact==originalCompact)Compact=other.Compact;
                    
                    string tmp=FileName+".tmp";
                    File.WriteAllText(tmp,ModelJson.Write(this),new UTF8Encoding(false));
                    if(File.Exists(FileName)) File.Replace(tmp,FileName,FileName+".bak",true); else File.Move(tmp,FileName);
                    Baseline();
                } finally { if(held)mutex.ReleaseMutex(); }
            }
        }
    }

    static class ScreenPlacement
    {
        [StructLayout(LayoutKind.Sequential)] struct Bounds { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Bounds r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr order,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p,uint flags);
        [DllImport("Shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon,int kind,out uint x,out uint y);
        static double Scale(Forms.Screen monitor)
        {
            try {
                var rect=monitor.Bounds;
                uint dx,dy;
                if(GetDpiForMonitor(MonitorFromPoint(new Point {X=rect.Left+rect.Width/2,Y=rect.Top+rect.Height/2},2),0,out dx,out dy)==0 && dx>0) return dx/96.0;
            } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
            return 1.0;
        }
        static double Limit(double value,double low,double high)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? low : Math.Max(low,Math.Min(high,value));
        }
        public static Placement Capture(Window w)
        {
            IntPtr handle=new WindowInteropHelper(w).Handle;
            Bounds r; if(handle==IntPtr.Zero || w.WindowState!=WindowState.Normal || !GetWindowRect(handle,out r))return null;
            var screen=Forms.Screen.FromHandle(handle); var area=screen.WorkingArea; double scale=Scale(screen);
            int width=r.Right-r.Left,height=r.Bottom-r.Top;
            return new Placement { Monitor=screen.DeviceName, Width=width/scale, Height=height/scale,
                Across=Limit((double)(r.Left-area.Left)/Math.Max(1,area.Width-width),0,1),
                Down=Limit((double)(r.Top-area.Top)/Math.Max(1,area.Height-height),0,1) };
        }
        public static void Apply(Window w, Placement p, string mode)
        {
            var handle=new WindowInteropHelper(w).Handle; if(handle==IntPtr.Zero)return;
            var screen=p==null ? Forms.Screen.FromHandle(handle) : Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==p.Monitor) ?? Forms.Screen.PrimaryScreen;
            var rect=screen.WorkingArea; double scale=Scale(screen); bool small=mode=="float";
            w.MinWidth=Math.Min(small?340:mode=="organize"?760:560,rect.Width/scale);
            w.MinHeight=Math.Min(small?520:440,rect.Height/scale);
            var defaultWidth=small?430:mode=="organize"?1080:800;
            var defaultHeight=small?760:mode=="organize"?740:585;
            int width=(int)Math.Round(Limit(p==null?defaultWidth:p.Width,w.MinWidth,rect.Width/scale)*scale);
            int height=(int)Math.Round(Limit(p==null?defaultHeight:p.Height,w.MinHeight,rect.Height/scale)*scale);
            int x=rect.Left+(int)Math.Round(Limit(p==null?(small?.97:.5):p.Across,0,1)*(rect.Width-width));
            int y=rect.Top+(int)Math.Round(Limit(p==null?.35:p.Down,0,1)*(rect.Height-height));
            if(w.WindowState!=WindowState.Normal)w.WindowState=WindowState.Normal;
            SetWindowPos(handle,IntPtr.Zero,x,y,width,height,0x14);
        }
    }
}


