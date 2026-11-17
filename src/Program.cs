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
[assembly: AssemblyVersion("1.0.7.0")]
[assembly: AssemblyFileVersion("1.0.7.0")]

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
        public const string Ink = "ChecklistInk", Muted = "ChecklistMuted",
            Accent = "ChecklistAccent", Line = "ChecklistLine", Completed = "ChecklistCompleted",
            CompletedLine = "ChecklistCompletedLine", CompletedCard = "ChecklistCompletedCard",
            Surface = "ChecklistSurface", ViewerCanvas = "ChecklistViewerCanvas";
        public static readonly SolidColorBrush Paper = Brushes.White;
        public static string Theme { get; private set; }

        public static void ApplyTheme(string theme)
        {
            bool green = theme == "green";
            Theme = green ? "green" : "blue";
            var resources = Application.Current.Resources;
            resources[Ink] = Brush(green ? "#263934" : "#203752");
            resources[Muted] = Brush(green ? "#596C63" : "#526983");
            resources[Accent] = Brush(green ? "#25735E" : "#1B5DBF");
            resources[Line] = Brush(green ? "#E0E6DF" : "#D8E4F2");
            resources[Completed] = Brush(green ? "#EDF5EF" : "#E8F1FF");
            resources[CompletedLine] = Brush(green ? "#D3E6D8" : "#CADFF8");
            resources[CompletedCard] = Brush(green ? "#F2F7F1" : "#F0F6FF");
            resources[Surface] = Brush(green ? "#F7F9F6" : "#F5F9FF");
            resources[ViewerCanvas] = Brush(green ? "#E2E8E3" : "#E4EDF9");
        }

        public static SolidColorBrush Brush(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); brush.Freeze(); return brush;
        }
        public static void Install(Application app)
        {
            ApplyTheme("blue");
            var button = new Style(typeof(Button));
            button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 4, 9, 4)));
            button.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            button.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(Line)));
            button.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(Ink)));
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
            text.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(Line)));
            app.Resources.Add(typeof(TextBox), text);
            var tooltips = new Style(typeof(ToolTip));
            tooltips.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            app.Resources.Add(typeof(ToolTip), tooltips);
        }
        public static Button Button(string caption, string id, Action action, bool primary = false)
        {
            var b = new Button { Content = caption, Margin = new Thickness(0,0,5,0), Cursor = System.Windows.Input.Cursors.Hand };
            AutomationProperties.SetAutomationId(b, id);
            if(primary) Primary(b);
            b.Click += delegate { action(); }; return b;
        }
        public static void Primary(Button button)
        {
            button.SetResourceReference(Control.BackgroundProperty, Accent);
            button.SetResourceReference(Control.BorderBrushProperty, Accent);
            button.Foreground = Brushes.White;
        }
        public static Button Quiet(Button button)
        {
            button.BorderThickness=new Thickness(0);button.Background=Brushes.Transparent;
            button.SetResourceReference(Control.ForegroundProperty, Muted);
            button.Padding=new Thickness(5,3,5,3);button.FontSize=12;
            return button;
        }
        public static TextBlock Text(string value, double size, bool strong = false)
        {
            var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
                FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment=VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, Ink);
            return text;
        }
    }
}
