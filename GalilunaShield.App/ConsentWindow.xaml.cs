using System.Windows;
using System.Windows.Controls;

namespace GalilunaShield.App;

/// <summary>First-run (and on-policy-change) consent + age acknowledgement. Blocks the app until accepted.</summary>
public partial class ConsentWindow : Window
{
    private readonly ConsentManager _consent;

    public ConsentWindow(ConsentManager consent)
    {
        InitializeComponent();
        _consent = consent;
    }

    private IEnumerable<CheckBox> Boxes => Checks.Children.OfType<CheckBox>();

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (AgreeButton is null) return; // during InitializeComponent
        var allChecked = Boxes.All(c => c.IsChecked == true);
        var ageChosen = AgeBox.SelectedItem is not null;
        AgreeButton.IsEnabled = allChecked && ageChosen;
        Hint.Text = AgreeButton.IsEnabled ? "Thank you. You can continue." : "Tick every box and choose an age to continue.";
    }

    private void Agree_Click(object sender, RoutedEventArgs e)
    {
        var attestations = new Dictionary<string, bool>();
        foreach (var box in Boxes)
        {
            var label = box.Content is string s ? s : (box.Content as TextBlock)?.Text ?? box.Content?.ToString() ?? "";
            attestations[label] = box.IsChecked == true;
        }
        var age = (AgeBox.SelectedItem as ComboBoxItem)?.Content as string;
        var version = typeof(ConsentWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        try
        {
            _consent.Save(attestations, age, version);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save your consent record:\n\n{ex.Message}", AppPaths.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Decline_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OpenEula(object sender, RoutedEventArgs e) => OpenLegal("EULA.md");
    private void OpenPrivacy(object sender, RoutedEventArgs e) => OpenLegal("PRIVACY-POLICY.md");
    private void OpenAup(object sender, RoutedEventArgs e) => OpenLegal("ACCEPTABLE-USE.md");

    private static void OpenLegal(string fileName)
    {
        var path = Path.Combine(AppPaths.LegalDirectory, fileName);
        if (File.Exists(path)) Shell.Open(path);
        else MessageBox.Show($"The document {fileName} was not found next to the program.", AppPaths.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
