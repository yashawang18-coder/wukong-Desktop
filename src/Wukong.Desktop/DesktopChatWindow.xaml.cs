using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wukong.Application;

namespace Wukong.Desktop;

public partial class DesktopChatWindow : Window
{
    private readonly DesktopAgentRuntime _agent;
    private readonly ObservableCollection<ChatDisplayItem> _items = new();
    private readonly DispatcherTimer _autoCollapseTimer;
    private CancellationTokenSource? _requestCancellation;

    public DesktopChatWindow(DesktopAgentRuntime agent)
    {
        _agent = agent;
        InitializeComponent();
        ChatList.ItemsSource = _items;
        _autoCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _autoCollapseTimer.Tick += (_, _) => Collapse();
        Loaded += async (_, _) =>
        {
            await ReloadHistoryAsync();
            ChatInput.Focus();
            ResetAutoCollapse();
        };
    }

    public bool IsExpanded => IsVisible;

    public static bool ShouldSend(Key key, ModifierKeys modifiers) =>
        key == Key.Enter && !modifiers.HasFlag(ModifierKeys.Shift);

    public void Toggle(Rect workArea, Rect petBounds)
    {
        if (IsVisible)
        {
            Collapse();
            return;
        }

        var position = DesktopChatPlacement.Place(workArea, petBounds, new Size(Width, Height));
        Left = position.X;
        Top = position.Y;
        Show();
        Activate();
        ChatInput.Focus();
        ResetAutoCollapse();
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

    private async Task ReloadHistoryAsync()
    {
        var history = await _agent.Conversation.GetHistoryAsync(DesktopAgentRuntime.DailySessionId);
        _items.Clear();
        foreach (var message in history.Where(x => x.Role != AgentChatRole.System))
            _items.Add(ChatDisplayItem.From(message));
        if (_items.Count > 0)
            ChatList.ScrollIntoView(_items[^1]);
    }

    private async Task SendAsync()
    {
        var input = ChatInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(input) || _requestCancellation is not null)
            return;

        _items.Add(ChatDisplayItem.User(input));
        ChatInput.Clear();
        SetBusy(true, "悟空正在想...");
        _requestCancellation = new CancellationTokenSource();
        try
        {
            var result = await _agent.Conversation.SendAsync(
                new ConversationRequest(DesktopAgentRuntime.DailySessionId, input),
                _requestCancellation.Token);
            _items.Add(result.Success
                ? ChatDisplayItem.Assistant(result.AssistantText ?? string.Empty)
                : ChatDisplayItem.Error(result.UserFacingError ?? "请求失败，请检查模型配置。"));
            ChatStatus.Text = result.Success
                ? $"{result.Provider} / {result.Model} / {result.Duration.TotalSeconds:0.0}s"
                : result.UserFacingError ?? "请求失败";
            ChatList.ScrollIntoView(_items[^1]);
        }
        finally
        {
            _requestCancellation?.Dispose();
            _requestCancellation = null;
            SetBusy(false, ChatStatus.Text);
            ResetAutoCollapse();
        }
    }

    private void SetBusy(bool busy, string status)
    {
        ChatStatus.Text = status;
        SendButton.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetAutoCollapse()
    {
        _autoCollapseTimer.Stop();
        _autoCollapseTimer.Start();
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _requestCancellation?.Cancel();
    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();
    private void ChatInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ResetAutoCollapse();

    private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
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
