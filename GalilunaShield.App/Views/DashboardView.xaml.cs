using System.Windows.Controls;
using GalilunaShield.Configuration;

namespace GalilunaShield.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && ModeBox.SelectedItem is MonitorMode m && m != vm.Mode)
        {
            vm.SetModeCommand.Execute(m);
            ModeBox.SelectedItem = vm.Mode; // reverts if the PIN was refused
        }
    }

    private void RecordingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && RecordingBox.SelectedItem is RecordingMode r && r != vm.RecordingMode)
        {
            vm.SetRecordingModeCommand.Execute(r);
            RecordingBox.SelectedItem = vm.RecordingMode;
        }
    }
}
