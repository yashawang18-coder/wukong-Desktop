using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Wukong.Desktop;

public partial class DesktopSpeechBubbleWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new();

    public DesktopSpeechBubbleWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    public void ShowMessage(string text, Rect workArea, Rect petBounds)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        BubbleText.Text = text.Trim();
        if (!IsVisible)
            Show();
        UpdateLayout();
        Reposition(workArea, petBounds);
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(4 + BubbleText.Text.Length / 12.0, 5, 12));
        _hideTimer.Start();
    }

    public void Reposition(Rect workArea, Rect petBounds)
    {
        if (!IsVisible)
            return;
        var position = DesktopChatPlacement.PlaceSpeechAbove(
            workArea,
            petBounds,
            new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : 100));
        Left = position.X;
        Top = position.Y;
    }

    private void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _hideTimer.Stop();
        Hide();
        e.Handled = true;
    }
}
