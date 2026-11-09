using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Data;

[assembly: AssemblyTitle("图文修改清单")]
[assembly: AssemblyProduct("图文修改清单")]
[assembly: AssemblyDescription("个人使用的本地图文意见清单")]
[assembly: AssemblyVersion("1.0.5.0")]
[assembly: AssemblyFileVersion("1.0.5.0")]

namespace PictureChecklist
{
    static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                Design.Install(app);
                var shell = new Shell(args.Length == 0 ? null : args[0]);
                app.Run(shell);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "清单无法启动", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }

    static class Design
    {
        public static SolidColorBrush Ink = Brush("#263934"), Muted = Brush("#7D8983"),
            Accent = Brush("#25735E"), Paper = Brush("#FFFFFF"), Line = Brush("#E0E6DF");
        public static SolidColorBrush Brush(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); brush.Freeze(); return brush;
        }
        public static void Install(Application app)
        {
            var button = new Style(typeof(Button));
            button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 4, 9, 4)));
            button.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            button.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
            button.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
            button.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 28.0));
            button.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            button.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            var chrome = new FrameworkElementFactory(typeof(Border));
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            foreach (var pair in new[] { new { Target=Border.BackgroundProperty, Source="Background" },
                new { Target=Border.BorderBrushProperty, Source="BorderBrush" },
                new { Target=Border.BorderThicknessProperty, Source="BorderThickness" },
                new { Target=Border.PaddingProperty, Source="Padding" } })
                chrome.SetBinding(pair.Target, new Binding(pair.Source) { RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent) });
            content.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent) });
            content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, new Binding("HorizontalContentAlignment") { RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent) });
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(content);
            button.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree=chrome }));
            var hover = new Trigger { Property=UIElement.IsMouseOverProperty, Value=true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, .85)); button.Triggers.Add(hover);
            var disabled = new Trigger { Property=UIElement.IsEnabledProperty, Value=false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4)); button.Triggers.Add(disabled);
            app.Resources.Add(typeof(Button), button);
            var text = new Style(typeof(TextBox));
            text.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 4, 5, 4)));
            text.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
            app.Resources.Add(typeof(TextBox), text);
            var tooltips = new Style(typeof(ToolTip));
            tooltips.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            app.Resources.Add(typeof(ToolTip), tooltips);
        }
        public static Button Button(string caption, string id, Action action, bool primary = false)
        {
            var b = new Button { Content = caption, Margin = new Thickness(0,0,5,0), Cursor = System.Windows.Input.Cursors.Hand };
            AutomationProperties.SetAutomationId(b, id);
            if(primary) { b.Background = Accent; b.Foreground = Brushes.White; b.BorderBrush = Accent; }
            b.Click += delegate { action(); }; return b;
        }
        public static Button Quiet(Button button)
        {
            button.BorderThickness=new Thickness(0);button.Background=Brushes.Transparent;
            button.Foreground=Muted;button.Padding=new Thickness(5,3,5,3);button.FontSize=12;
            return button;
        }
        public static TextBlock Text(string value, double size, bool strong = false)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = Ink, TextWrapping = TextWrapping.Wrap,
                FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment=VerticalAlignment.Center };
        }
    }
}
