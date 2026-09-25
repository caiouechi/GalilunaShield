using System.Windows;
using System.Windows.Input;

namespace GalilunaShield.App;

public partial class PinDialog : Window
{
    private readonly string _expected;
    private int _attempts;

    public PinDialog(string expected, string prompt)
    {
        InitializeComponent();
        _expected = expected;
        Prompt.Text = prompt;
        Loaded += (_, _) => Pin.Focus();
    }

    /// <summary>Returns true when no PIN is configured or the user typed the right one.</summary>
    public static bool Authorize(string configuredPin, string prompt)
    {
        if (string.IsNullOrEmpty(configuredPin)) return true;
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        var dlg = new PinDialog(configuredPin, prompt);
        if (owner is not null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Check();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Pin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Check();
    }

    private void Check()
    {
        if (Pin.Password == _expected)
        {
            DialogResult = true;
            return;
        }
        _attempts++;
        Error.Visibility = Visibility.Visible;
        Pin.Clear();
        Pin.Focus();
        if (_attempts >= 5)
        {
            DialogResult = false; // don't let a child brute-force it by mashing keys
        }
    }
}
