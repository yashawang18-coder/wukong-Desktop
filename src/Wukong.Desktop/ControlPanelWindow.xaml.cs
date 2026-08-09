using System.Windows;

namespace Wukong.Desktop;

public partial class ControlPanelWindow : Window
{
    private readonly DesktopRuntimeHost _runtime;

    public ControlPanelWindow(DesktopRuntimeHost runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        TraceList.ItemsSource = _runtime.TraceLines;
    }

    private async void FakeModelButton_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SubmitFakeModelMessageAsync(ModelInput.Text);

    private void DeveloperToggle_Changed(object sender, RoutedEventArgs e) =>
        TraceList.Visibility = DeveloperToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
}
