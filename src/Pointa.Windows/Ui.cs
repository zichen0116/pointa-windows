using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using W = System.Windows;
using C = System.Windows.Controls;

namespace Pointa.Windows;

internal static class Ui
{
    public static void UseTheme(string mode)
    {
        var file = mode switch { "System" => "Fluent", "Dark" => "Fluent.Dark", "Light" => "Fluent.Light", _ => throw new ArgumentOutOfRangeException(nameof(mode)) };
        var resources = W.Application.Current.Resources.MergedDictionaries;
        resources.Clear();
        resources.Add(new W.ResourceDictionary { Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/{file}.xaml") });
    }

    public static C.TextBlock Text(string text, double size = 14, bool bold = false, bool muted = false)
    {
        var block = new C.TextBlock { Text = text, FontSize = size, FontWeight = bold ? W.FontWeights.SemiBold : W.FontWeights.Normal,
            TextTrimming = W.TextTrimming.CharacterEllipsis, VerticalAlignment = W.VerticalAlignment.Center };
        block.SetResourceReference(C.TextBlock.ForegroundProperty, muted ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        return block;
    }

    public static C.Button Button(string text, Action action, bool primary = false)
    {
        var button = new C.Button { Content = text, MinHeight = 36, Padding = new W.Thickness(14, 6, 14, 6), HorizontalAlignment = W.HorizontalAlignment.Stretch };
        AutomationProperties.SetName(button, text);
        if (primary) button.SetResourceReference(W.FrameworkElement.StyleProperty, "AccentButtonStyle");
        button.Click += (_, _) => action();
        return button;
    }

    public static C.StackPanel Stack(params W.UIElement[] children)
    {
        var panel = new C.StackPanel();
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static C.Grid Row(W.UIElement left, W.UIElement right, double rightWidth = double.NaN)
    {
        var grid = new C.Grid();
        grid.ColumnDefinitions.Add(new() { Width = new W.GridLength(1, W.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new W.GridLength(16) });
        grid.ColumnDefinitions.Add(new() { Width = double.IsNaN(rightWidth) ? W.GridLength.Auto : new W.GridLength(rightWidth) });
        C.Grid.SetColumn(right, 2);
        grid.Children.Add(left); grid.Children.Add(right);
        return grid;
    }

    public static C.Border Card(W.UIElement content)
    {
        var card = new C.Border { Child = content, Padding = new W.Thickness(16), CornerRadius = new W.CornerRadius(8),
            BorderThickness = new W.Thickness(1), Margin = new W.Thickness(0, 0, 0, 16) };
        card.SetResourceReference(C.Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(C.Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return card;
    }

    public static C.Border Body(W.UIElement content)
    {
        var body = new C.Border { Child = content, Padding = new W.Thickness(24) };
        body.SetResourceReference(C.Border.BackgroundProperty, "ApplicationBackgroundBrush");
        return body;
    }

    public static BitmapImage Logo() => new(new Uri("pack://application:,,,/Pointa;component/Assets/Pointa.png"));
    public static Icon CreateIcon(int size = 32)
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("Pointa.Windows.Assets.Pointa.ico")
            ?? throw new InvalidOperationException("Pointa icon resource is missing.");
        using var icon = new Icon(stream, size, size);
        return (Icon)icon.Clone();
    }

    public static BitmapImage Png(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }

    public static void SavePreview(W.FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }

    public static void Message(W.Window owner, string message) => W.MessageBox.Show(owner, message, "Pointa");
    public static bool Confirm(W.Window owner, string message) => W.MessageBox.Show(owner, message, owner.Title,
        W.MessageBoxButton.YesNo, W.MessageBoxImage.Question, W.MessageBoxResult.No) == W.MessageBoxResult.Yes;
}

internal class DesktopWindow : W.Window, IDisposable
{
    public bool Exiting { get; set; }
    public bool IsClosed { get; private set; }
    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();
    public DesktopWindow(string title, double width)
    {
        SetResourceReference(StyleProperty, typeof(W.Window));
        Title = title; Width = width; SizeToContent = W.SizeToContent.Height;
        ResizeMode = W.ResizeMode.CanMinimize; WindowStartupLocation = W.WindowStartupLocation.CenterScreen;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable, Microsoft YaHei UI"); FontSize = 14;
        Icon = Ui.Logo(); UseLayoutRounding = true; SnapsToDevicePixels = true;
        Closed += (_, _) => IsClosed = true;
    }
    public void Dispose() { Exiting = true; if (!IsClosed) Close(); GC.SuppressFinalize(this); }
}
