using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GalilunaShield.App;

public partial class MainWindow : Window
{
    public static readonly IValueConverter NonZeroToVisibility = new NonZeroConverter();

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Renders each navigation page of the window to a PNG in <paramref name="folder"/>.</summary>
    public async Task RenderAllPagesAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var pages = new (RadioButton Nav, string Name)[]
        {
            (NavDashboard, "dashboard"), (NavAlerts, "alerts"), (NavTranscript, "transcript"), (NavReports, "reports"),
            (NavWords, "words"), (NavBuckets, "buckets"), (NavSettings, "settings"), (NavAbout, "about"),
        };
        foreach (var (nav, name) in pages)
        {
            nav.IsChecked = true;
            await RenderPageAsync(folder, name);
        }
        NavDashboard.IsChecked = true;
    }

    /// <summary>Renders the window as it currently looks to <c>folder\name.png</c>.</summary>
    public async Task RenderPageAsync(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        await Dispatcher.Yield(DispatcherPriority.Render);
        await Task.Delay(600); // let images decode and layout settle
        UpdateLayout();
        // Render the client area (the content), not the Window: the Window's ActualWidth/Height include the chrome.
        var content = (FrameworkElement)Content;
        var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        await using var file = File.Create(Path.Combine(folder, $"{name}.png"));
        encoder.Save(file);
    }

    private sealed class NonZeroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int i && i != 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
