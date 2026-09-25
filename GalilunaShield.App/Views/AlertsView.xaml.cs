using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace GalilunaShield.App.Views;

public partial class AlertsView : UserControl
{
    /// <summary>Dim a dismissed alert card.</summary>
    public static readonly IValueConverter DimIfDismissed = new DimConverter();

    public AlertsView()
    {
        InitializeComponent();
    }

    private sealed class DimConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 0.55 : 1.0;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
