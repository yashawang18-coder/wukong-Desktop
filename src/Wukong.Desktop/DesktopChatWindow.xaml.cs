using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wukong.Application;

namespace Wukong.Desktop;

public partial class DesktopChatWindow : Window
{
    private readonly DesktopAgentRuntime _agent;
    private readonly DispatcherTimer _autoCollapseTimer;
    private CancellationTokenSource? _requestCancellation;

    public DesktopChatWindow(DesktopAgentRuntime agent)
    {
        _agent = agent;
        InitializeComponent();
        _autoCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _autoCollapseTimer.Tick += (_, _) => Collapse();
        Loaded += (_, _) =>
        {
            ChatInput.Focus();
            ResetAutoCollapse();
        };
        Closed += (_, _) => _requestCancellation?.Cancel();
    }

    public bool IsExpanded => IsVisible;
    public event EventHandler<string>? AssistantReplyAvailable;

    public static bool ShouldSend(Key key, ModifierKeys modifiers) =>
        key == Key.Enter && !modifiers.HasFlag(ModifierKeys.Shift);

    public void Toggle(Rect workArea, Rect petBounds)
    {
        if (IsVisible)
        {
            Collapse();
            return;
        }

        ShowForInput(workArea, petBounds);
    }

    public void ShowForInput(Rect workArea, Rect petBounds)
    {
        ShowAt(workArea, petBounds);
        Activate();
        ChatInput.Focus();
    }

    public void Reposition(Rect workArea, Rect petBounds)
    {
        if (!IsVisible)
            return;
        var position = DesktopChatPlacement.Place(workArea, petBounds, new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height));
        Left = position.X;
        Top = position.Y;
    }

    public void Collapse()
    {
        _autoCollapseTimer.Stop();
        Hide();
    }

    private void ShowAt(Rect workArea, Rect petBounds)
    {
        var position = DesktopChatPlacement.Place(workArea, petBounds, new Size(Width, Height));
        Left = position.X;
        Top = position.Y;
        if (!IsVisible)
            Show();
        ResetAutoCollapse();
    }

    private async Task SendAsync()
    {
        var input = ChatInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(input) || _requestCancellation is not null)
            return;

        ChatInput.Clear();
        SetBusy(true);
        _requestCancellation = new CancellationTokenSource();
        try
        {
            var result = await _agent.Conversation.SendAsync(
                new ConversationRequest(DesktopAgentRuntime.DailySessionId, input),
                _requestCancellation.Token);
            if (result.Success && !string.IsNullOrWhiteSpace(result.AssistantText))
            {
                AssistantReplyAvailable?.Invoke(this, result.AssistantText.Trim());
                Collapse();
            }
            else if (!result.Success)
            {
                AssistantReplyAvailable?.Invoke(this, result.UserFacingError ?? "暂时无法回复，请稍后再试。");
            }
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            SetBusy(false);
            ResetAutoCollapse();
        }
    }

    private void SetBusy(bool busy)
    {
        ChatInput.IsEnabled = !busy;
        SendButton.IsEnabled = !busy;
        SendButton.Content = busy ? "发送中" : "发送";
    }

    private void ResetAutoCollapse()
    {
        _autoCollapseTimer.Stop();
        _autoCollapseTimer.Start();
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();
    private void ChatInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ResetAutoCollapse();

    private async void ChatInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ShouldSend(e.Key, Keyboard.Modifiers))
            return;
        e.Handled = true;
        await SendAsync();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        Collapse();
    }
}

public sealed record ChatDisplayItem(string Text, HorizontalAlignment Alignment, Brush BubbleBrush)
{
    public static ChatDisplayItem User(string text) => new(text, HorizontalAlignment.Right, Brushes.WhiteSmoke);
    public static ChatDisplayItem Assistant(string text) => new(text, HorizontalAlignment.Left, new SolidColorBrush(Color.FromRgb(223, 233, 223)));
    public static ChatDisplayItem Error(string text) => new(text, HorizontalAlignment.Left, new SolidColorBrush(Color.FromRgb(243, 228, 213)));
    public static ChatDisplayItem From(AgentChatMessage message) => message.Role == AgentChatRole.User ? User(message.Content) : Assistant(message.Content);
}
