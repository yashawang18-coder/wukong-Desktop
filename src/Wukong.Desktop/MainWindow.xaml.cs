using System.Windows;
using System.Windows.Threading;
using Wukong.Domain;
using Wukong.Infrastructure;

namespace Wukong.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopRuntimeHost _runtime = new();
    private readonly DispatcherTimer _autonomousTimer;
    private ControlPanelWindow? _controlPanel;

    public MainWindow()
    {
        InitializeComponent();
        _autonomousTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autonomousTimer.Tick += async (_, _) => await _runtime.SubmitAutonomousTickAsync();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height - 24;
        _autonomousTimer.Start();
    }

    private async void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await _runtime.RecordInputAsync(DesktopInputEventAdapter.PointerDown(e.GetPosition(this)));
    }

    private async void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        await _runtime.RecordInputAsync(DesktopInputEventAdapter.PointerMove(e.GetPosition(this)));
    }

    private async void Window_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await _runtime.SubmitOwnerInputAsync(DesktopInputEventAdapter.PointerUp(e.GetPosition(this)));
    }

    private async void TouchMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SubmitContextMenuIntentAsync(new SemanticIntent(SemanticIntentKind.Touch, "wk.interaction.prone_touch"));

    private async void QuietMenuItem_Click(object sender, RoutedEventArgs e) =>
        await _runtime.SubmitContextMenuIntentAsync(new SemanticIntent(SemanticIntentKind.Quiet, "wk.core.prone_idle"));

    private void OpenPanelMenuItem_Click(object sender, RoutedEventArgs e) => OpenControlPanel();

    private void OpenControlPanel()
    {
        if (_controlPanel is { IsLoaded: true })
        {
            _controlPanel.Show();
            _controlPanel.Activate();
            return;
        }

        _controlPanel = new ControlPanelWindow(_runtime);
        _controlPanel.Closed += (_, _) => _controlPanel = null;
        _controlPanel.Show();
    }
}
