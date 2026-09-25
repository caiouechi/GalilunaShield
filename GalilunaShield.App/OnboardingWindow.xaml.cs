using System.Windows;
using System.Windows.Controls;
using GalilunaShield.Configuration;

namespace GalilunaShield.App;

/// <summary>First-run wizard: pick the child's age (applies a sensible protection profile) and set a PIN.</summary>
public partial class OnboardingWindow : Window
{
    private readonly AppConfig _config;
    private readonly IReadOnlyList<string> _bucketIds;
    private AgeProfile.Band? _band;

    private static readonly (AgeProfile.Band Band, string Title)[] Ages =
    {
        (AgeProfile.Band.Under5, "Under 5"),
        (AgeProfile.Band.Age5to8, "5 to 8"),
        (AgeProfile.Band.Age9to12, "9 to 12"),
        (AgeProfile.Band.Age13to15, "13 to 15"),
        (AgeProfile.Band.Age16to17, "16 to 17"),
    };

    public OnboardingWindow(AppConfig config, IReadOnlyList<string> bucketIds)
    {
        InitializeComponent();
        _config = config;
        _bucketIds = bucketIds;
        BuildAgeButtons();
    }

    private void BuildAgeButtons()
    {
        foreach (var (band, title) in Ages)
        {
            var rb = new RadioButton
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15 },
                        new TextBlock { Text = AgeProfile.Describe(band), Foreground = (System.Windows.Media.Brush)FindResource("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
                    },
                },
                GroupName = "age",
                Margin = new Thickness(0, 3, 0, 3),
                Padding = new Thickness(12, 8, 12, 8),
                Tag = band,
            };
            rb.Checked += (_, _) =>
            {
                _band = band;
                ProfileSummary.Text = $"{title}: {AgeProfile.Describe(band)}\nWe'll switch on the matching topic packs and set microphone + system-audio listening with Smart proof recording.";
                FinishButton.IsEnabled = true;
            };
            AgeList.Items.Add(rb);
        }
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_band is null) return;
        var pin = PinBox.Text.Trim();
        if (pin.Length > 0 && (pin.Length is < 4 or > 8 || !pin.All(char.IsDigit)))
        {
            MessageBox.Show("The PIN must be 4 to 8 digits, or empty.", AppPaths.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AgeProfile.Apply(_config, _band.Value, _bucketIds);
        if (pin.Length > 0) _config.ParentPin = pin;
        _config.Notifications.Enabled = false; // parent sets this up in Settings
        Save();
        DialogResult = true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Save(); // still marks onboarding complete so we don't nag
        DialogResult = false;
    }

    private void Save()
    {
        try
        {
            _config.Save(AppPaths.ConfigFile);
            File.WriteAllText(Path.Combine(AppPaths.ConfigDirectory, "onboarded.marker"), DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save setup: {ex.Message}", AppPaths.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
