using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace PictureChecklist
{
    public sealed class PictureViewer : Window
    {
        readonly Shell host;
        readonly string folder;
        readonly Entry entry;
        readonly List<Asset> images;
        readonly TextBlock caption = FloatControls.Text("", 12);
        readonly TextBlock scaleLabel = FloatControls.Text("", 12);
        readonly TextBlock error = FloatControls.Text("", 14);
        readonly ScrollViewer scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false };
        readonly Image picture = new Image { Stretch = Stretch.Fill };
        readonly Button previous, next, fit, actual, smaller, bigger;
        bool fitMode = true;
        bool dragging;
        Point startPoint;
        double startX, startY, zoom = 1;
        int index;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        public PictureViewer(Shell host, Entry entry, int index)
        {
            this.host = host;
            folder = host.Folder;
            this.entry = entry;
            Dictionary<string, Asset> byId = host.Doc.Assets.ToDictionary(a => a.Id);
            images = entry.AssetIds.Select(id => { Asset a; byId.TryGetValue(id, out a); return a; }).ToList();
            this.index = Math.Max(0, Math.Min(index, images.Count - 1));
            Title = "查看原图";
            Owner = host;
            SetBinding(Window.TopmostProperty, new Binding("Topmost") { Source = host, Mode = BindingMode.OneWay });
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            UseLayoutRounding = true;
            Width = 900; Height = 670; MinWidth = 260; MinHeight = 190;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = FloatControls.ColorBrush("#F2F4F1");
            FloatControls.Identify(this, "PictureViewer", "原图查看器");
            FloatControls.Identify(picture, "ViewerImage", "当前原图");
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

            DockPanel layout = new DockPanel();
            Content = layout;
            StackPanel top = new StackPanel { Margin = new Thickness(10, 7, 10, 5) };
            DockPanel.SetDock(top, Dock.Top);
            layout.Children.Add(top);
            WrapPanel toolbar = new WrapPanel();
            top.Children.Add(toolbar);
            previous = Command(toolbar, "上一张", "ViewerPrevious", delegate { Move(-1); });
            next = Command(toolbar, "下一张", "ViewerNext", delegate { Move(1); });
            fit = Command(toolbar, "适合窗口", "ViewerFit", delegate { FitToWindow(); });
            actual = Command(toolbar, "100%", "ViewerActual", delegate { SetZoom(1, null); });
            smaller = Command(toolbar, "−", "ViewerZoomOut", delegate { SetZoom(zoom / 1.25, null); });
            bigger = Command(toolbar, "+", "ViewerZoomIn", delegate { SetZoom(zoom * 1.25, null); });
            smaller.MinWidth = bigger.MinWidth = 30;
            FloatControls.Identify(smaller, "ViewerZoomOut", "缩小原图");
            FloatControls.Identify(bigger, "ViewerZoomIn", "放大原图");
            scaleLabel.Margin = new Thickness(3, 0, 8, 0);
            FloatControls.Identify(scaleLabel, "ViewerZoomLevel", "当前缩放比例");
            toolbar.Children.Add(scaleLabel);
            Command(toolbar, "关闭", "ViewerClose", delegate { Close(); });
            caption.Foreground = FloatControls.Muted;
            caption.Margin = new Thickness(0, 3, 0, 1);
            caption.TextWrapping = TextWrapping.NoWrap;
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            FloatControls.Identify(caption, "ViewerCaption", "当前图片");
            top.Children.Add(caption);
            Grid area = new Grid { Background = FloatControls.ColorBrush("#E2E8E3"), ClipToBounds = true };
            layout.Children.Add(area);
            FloatControls.Identify(scroll, "ViewerScroll", "图片滚动区域");
            scroll.ToolTip = "Ctrl + 滚轮缩放；放大后按住图片拖动。左右方向键切换图片。";
            area.Children.Add(scroll);
            // A centered child keeps small images centered while preserving both scroll extremes at large scales.
            Grid canvas = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            canvas.Children.Add(picture);
            scroll.Content = canvas;
            error.HorizontalAlignment = HorizontalAlignment.Center;
            error.Margin = new Thickness(15);
            error.Foreground = FloatControls.Alert;
            area.Children.Add(error);
            scroll.SizeChanged += delegate { if (fitMode) FitToWindow(); };
            scroll.PreviewMouseWheel += OnWheel;
            picture.MouseLeftButtonDown += OnPanStart;
            picture.MouseMove += OnPan;
            picture.MouseLeftButtonUp += OnPanEnd;
            picture.LostMouseCapture += delegate { dragging = false; };
            PreviewKeyDown += OnKey;
            DpiChanged += delegate
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(delegate { if (fitMode) FitToWindow(); else ApplyScale(zoom); }));
            };
            Loaded += delegate { PlaceOnOwnerMonitor(); ReadCurrent(); };
            Closed += delegate { picture.Source = null; };
        }

        static Button Command(Panel toolbar, string text, string id, Action action)
        {
            Button b = FloatControls.Button(text, id, action);
            b.Margin = new Thickness(0, 0, 4, 2);
            toolbar.Children.Add(b);
            return b;
        }

        void PlaceOnOwnerMonitor()
        {
            IntPtr ownerHandle = new WindowInteropHelper(host).Handle;
            IntPtr window = new WindowInteropHelper(this).Handle;
            if (ownerHandle == IntPtr.Zero || window == IntPtr.Zero) return;
            System.Drawing.Rectangle work = Forms.Screen.FromHandle(ownerHandle).WorkingArea;
            Matrix device = ToDevice(host);
            int w = Math.Min(Math.Max(1, work.Width - 16), (int)Math.Round(1000 * device.M11));
            int h = Math.Min(Math.Max(1, work.Height - 16), (int)Math.Round(760 * device.M22));
            SetWindowPos(window, IntPtr.Zero, work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2,
                w, h, 0x0004 | 0x0010); // Keep z-order and activation established by the user's open action.
        }

        static Matrix ToDevice(Visual visual)
        {
            PresentationSource source = PresentationSource.FromVisual(visual);
            return source == null || source.CompositionTarget == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
        }

        // maxEdge is measured in image pixels. Only read a full bitmap when the original viewer requests it (0).
        internal static BitmapImage ReadImage(string path, int maxEdge)
        {
            int targetWidth = 0, targetHeight = 0;
            if (maxEdge > 0)
            {
                using (FileStream probe = OpenImage(path))
                {
                    BitmapDecoder header = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    BitmapFrame frame = header.Frames[0];
                    if (Math.Max(frame.PixelWidth, frame.PixelHeight) > maxEdge)
                    {
                        if (frame.PixelWidth >= frame.PixelHeight) targetWidth = maxEdge;
                        else targetHeight = maxEdge;
                    }
                }
            }
            using (FileStream stream = OpenImage(path))
            {
                BitmapImage loaded = new BitmapImage();
                loaded.BeginInit();
                loaded.CacheOption = BitmapCacheOption.OnLoad;
                if (targetWidth > 0) loaded.DecodePixelWidth = targetWidth;
                if (targetHeight > 0) loaded.DecodePixelHeight = targetHeight;
                loaded.StreamSource = stream;
                loaded.EndInit();
                loaded.Freeze();
                return loaded;
            }
        }

        static FileStream OpenImage(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        internal static BitmapSource ReadAsset(string folder, Asset asset, int maxEdge)
        {
            return ScreenshotPixels.ReadDisplay(Store.AssetPath(folder, asset), maxEdge, asset.OriginalName);
        }

        void ReadCurrent()
        {
            picture.Source = null;
            picture.Width = double.NaN;
            picture.Height = double.NaN;
            error.Visibility = Visibility.Collapsed;
            Asset asset = images.Count == 0 ? null : images[index];
            string name = asset == null ? "图片记录缺失" : asset.OriginalName;
            string position = images.Count == 0 ? "无图片" : "第 " + (index + 1) + " / " + images.Count + " 张";
            caption.Text = position + "    " + name;
            caption.ToolTip = name;
            Title = "原图 " + (index + 1) + "/" + images.Count + " · " + entry.Title;
            previous.IsEnabled = index > 0;
            next.IsEnabled = index < images.Count - 1;
            try
            {
                if (asset == null) throw new InvalidDataException("清单中缺少这张图片的记录。");
                BitmapSource image = ReadAsset(folder, asset, 0);
                picture.Source = image;
                caption.Text += "    " + image.PixelWidth + " × " + image.PixelHeight;
                fit.IsEnabled = actual.IsEnabled = smaller.IsEnabled = bigger.IsEnabled = true;
                FitToWindow();
            }
            catch (Exception ex)
            {
                error.Text = "无法打开这张图片。" + Environment.NewLine + ex.Message;
                error.Visibility = Visibility.Visible;
                scaleLabel.Text = "";
                fit.IsEnabled = actual.IsEnabled = smaller.IsEnabled = bigger.IsEnabled = false;
            }
            scroll.ScrollToHorizontalOffset(0);
            scroll.ScrollToVerticalOffset(0);
        }

        void Move(int by)
        {
            int newIndex = index + by;
            if (newIndex < 0 || newIndex >= images.Count) return;
            index = newIndex;
            ReadCurrent();
        }

        void ApplyScale(double factor)
        {
            BitmapSource source = picture.Source as BitmapSource;
            if (source == null) return;
            zoom = factor;
            Matrix device = ToDevice(this);
            // 100% maps one image pixel to one physical monitor pixel, including at 125% or 150% desktop scaling.
            picture.Width = source.PixelWidth * zoom / device.M11;
            picture.Height = source.PixelHeight * zoom / device.M22;
            scaleLabel.Text = zoom < 0.01 ? (zoom * 100).ToString("0.##") + "%" : Math.Round(zoom * 100) + "%";
            bigger.IsEnabled = zoom < 8;
            smaller.IsEnabled = zoom > 0.01;
            picture.Cursor = fitMode ? Cursors.Arrow : Cursors.Hand;
        }

        void FitToWindow()
        {
            fitMode = true;
            BitmapSource source = picture.Source as BitmapSource;
            if (source == null || scroll.ActualHeight < 2 || scroll.ActualWidth < 2) return;
            Matrix device = ToDevice(this);
            // The scroll viewer's full area is used; old scrollbars disappear once the image fits.
            double fw = Math.Max(1, scroll.ActualWidth - 3) * device.M11 / source.PixelWidth;
            double fh = Math.Max(1, scroll.ActualHeight - 3) * device.M22 / source.PixelHeight;
            // Small screenshots should be readable on open; 100% remains an explicit toolbar action.
            ApplyScale(Math.Min(8.0, Math.Min(fw, fh)));
        }

        void SetZoom(double factor, Point? point)
        {
            if (picture.Source == null) return;
            double old = zoom;
            double nextZoom = Math.Max(0.01, Math.Min(8, factor));
            Point center = point ?? new Point(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2);
            double sourceX = scroll.HorizontalOffset + center.X - Math.Max(0, (scroll.ViewportWidth - picture.Width) / 2);
            double sourceY = scroll.VerticalOffset + center.Y - Math.Max(0, (scroll.ViewportHeight - picture.Height) / 2);
            fitMode = false;
            ApplyScale(nextZoom);
            scroll.UpdateLayout();
            scroll.ScrollToHorizontalOffset(sourceX * nextZoom / old - center.X + Math.Max(0, (scroll.ViewportWidth - picture.Width) / 2));
            scroll.ScrollToVerticalOffset(sourceY * nextZoom / old - center.Y + Math.Max(0, (scroll.ViewportHeight - picture.Height) / 2));
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            SetZoom(e.Delta > 0 ? zoom * 1.25 : zoom / 1.25, e.GetPosition(scroll));
            e.Handled = true;
        }

        void OnPanStart(object sender, MouseButtonEventArgs e)
        {
            if (scroll.ScrollableWidth <= 0 && scroll.ScrollableHeight <= 0) return;
            startPoint = e.GetPosition(scroll);
            startX = scroll.HorizontalOffset;
            startY = scroll.VerticalOffset;
            dragging = picture.CaptureMouse();
            e.Handled = dragging;
        }

        void OnPan(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point now = e.GetPosition(scroll);
            scroll.ScrollToHorizontalOffset(startX - now.X + startPoint.X);
            scroll.ScrollToVerticalOffset(startY - now.Y + startPoint.Y);
            e.Handled = true;
        }

        void OnPanEnd(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            picture.ReleaseMouseCapture();
            e.Handled = true;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Left: Move(-1); break;
                case Key.Right: Move(1); break;
                case Key.OemPlus: case Key.Add: SetZoom(zoom * 1.25, null); break;
                case Key.OemMinus: case Key.Subtract: SetZoom(zoom / 1.25, null); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
