using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Wukong.Domain;
using Wukong.Application;

namespace Wukong.Desktop;

public partial class ControlPanelWindow : Window
{
    private readonly DesktopRuntimeHost _runtime;
    private readonly DesktopAgentRuntime _agent;
    private readonly bool _ownsAgent;
    private readonly DispatcherTimer _previewTimer;
    private readonly ObservableCollection<AlbumFolderItem> _albumFolders = new();
    private readonly ObservableCollection<AlbumMediaItem> _albumMediaBindings = new();
    private readonly ObservableCollection<ChatDisplayItem> _chatItems = new();
    private readonly Dictionary<string, ObservableCollection<ChatDisplayItem>> _modelDebugItems = new(StringComparer.Ordinal);
    private readonly ObservableCollection<ConversationMemoryCandidate> _memoryCandidates = new();
    private readonly Dictionary<ChatProviderType, ChatProviderConfiguration> _providerConfigurations = new();
    private PlayableMotion? _previewMotion;
    private MotionPhase? _previewPhase;
    private IReadOnlyList<string> _previewFrames = Array.Empty<string>();
    private AlbumFolderItem? _selectedAlbum;
    private string _albumRoot = string.Empty;
    private bool _albumUnbindInProgress;
    private int _previewIndex;
    private bool _previewPaused;
    private bool _previewDark;
    private bool _modelUiReady;
    private string _activeModelTab = "Model";
    private AgentMemoryConfiguration _memoryConfiguration = AgentMemoryConfiguration.Default;
    private PetProfileSnapshot _loadedPetProfile = PetProfileSnapshot.Default;
    private OwnerProfileSnapshot _loadedOwnerProfile = OwnerProfileSnapshot.Default;
    private bool _changingDeveloperMode;
    private CancellationTokenSource? _agentRequestCancellation;

    public ControlPanelWindow(DesktopRuntimeHost runtime)
        : this(runtime, DesktopAgentRuntime.CreateDefault(), ownsAgent: true)
    {
    }

    public ControlPanelWindow(DesktopRuntimeHost runtime, DesktopAgentRuntime agent)
        : this(runtime, agent, ownsAgent: false)
    {
    }

    private ControlPanelWindow(DesktopRuntimeHost runtime, DesktopAgentRuntime agent, bool ownsAgent)
    {
        _runtime = runtime;
        _agent = agent;
        _ownsAgent = ownsAgent;
        _albumRoot = AlbumFolderItem.GetDefaultAlbumRoot(
            _agent.DataPaths.AlbumsDirectory,
            _agent.DataPaths.ProfileDirectory);
        InitializeComponent();
        DataContext = _runtime;
        TraceList.ItemsSource = _runtime.TraceLines;
        AssetList.ItemsSource = _runtime.Motions
            .Where(IsBaseMotion)
            .OrderBy(x => x.BehaviorId)
            .ToList();
        PlayAssetList.ItemsSource = _runtime.CarRideCandidateMotions.ToList();
        CommandAssetList.ItemsSource = _runtime.Motions
            .Where(IsCommandMotion)
            .OrderByDescending(x => string.Equals(x.AssetBatch, CommandMockBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.BehaviorId)
            .ToList();
        MagicSpecialList.ItemsSource = _runtime.MagicMotions
            .Where(x => !string.Equals(x.BehaviorId, MagicBehaviorIds.PetrificusRelease, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.DisplayName)
            .ToList();
        CommandMotionList.ItemsSource = _runtime.Motions
            .Where(x => string.Equals(x.Category, "口令动作", StringComparison.Ordinal))
            .OrderBy(x => x.BehaviorId)
            .ToList();
        LifecycleCandidateList.ItemsSource = _runtime.LifecycleCandidateMotions
            .OrderBy(x => x.BehaviorId)
            .ToList();
        AutonomousDailyAssetList.ItemsSource = _runtime.AutonomousDailyCandidateMotions
            .OrderBy(x => x.BehaviorId)
            .ToList();
        InteractionReviewAssetList.ItemsSource = _runtime.Motions
            .Where(x => string.Equals(x.BehaviorId, Phase15BehaviorIds.ProneTouch, StringComparison.OrdinalIgnoreCase))
            .ToList();
        CarRideCandidateList.ItemsSource = _runtime.CarRideCandidateMotions.ToList();
        AlbumList.ItemsSource = _albumFolders;
        AlbumMediaList.ItemsSource = _albumMediaBindings;
        OwnerChatList.ItemsSource = _chatItems;
        _modelDebugItems["Model"] = new ObservableCollection<ChatDisplayItem>();
        _modelDebugItems["Memory"] = new ObservableCollection<ChatDisplayItem>();
        _modelDebugItems["Pet"] = new ObservableCollection<ChatDisplayItem>();
        ModelChatList.ItemsSource = _modelDebugItems[_activeModelTab];
        MemoryCandidateList.ItemsSource = _memoryCandidates;
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
        _previewTimer.Tick += (_, _) => AdvancePreview();
        RefreshAlbumView();
        BehaviorAgentMockMotionList.ItemsSource = _runtime.CommandMotionMockMotions;
        BehaviorAgentSnapshotText.Text = _runtime.BehaviorAgentSnapshot;
        Loaded += async (_, _) => await LoadAgentUiAsync();
        Closed += (_, _) =>
        {
            _agentRequestCancellation?.Cancel();
            _previewTimer.Stop();
            if (_ownsAgent)
                _agent.Dispose();
        };
    }

    private async void FakeModelButton_Click(object sender, RoutedEventArgs e) => await SendAgentMessageAsync(ModelInput);
    private async void ModelSend_Click(object sender, RoutedEventArgs e) => await SendAgentMessageAsync(ModelDebugInput);

    private async void Command_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string command })
            await _runtime.SubmitOwnerCommandAsync(command);
    }

    private void DeveloperToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_changingDeveloperMode)
            return;

        if (DeveloperToggle.IsChecked == true && !_agent.DeveloperSession.IsAuthenticated)
        {
            var dialog = new DeveloperLoginWindow(_agent.DeveloperSession) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                _changingDeveloperMode = true;
                DeveloperToggle.IsChecked = false;
                _changingDeveloperMode = false;
            }
        }
        else if (DeveloperToggle.IsChecked != true)
        {
            _agent.DeveloperSession.SignOut();
        }

        UpdateDeveloperVisibility();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page })
            return;

        if (page == "Developer" && !_agent.DeveloperSession.IsAuthenticated)
        {
            _changingDeveloperMode = true;
            DeveloperToggle.IsChecked = true;
            _changingDeveloperMode = false;
            var dialog = new DeveloperLoginWindow(_agent.DeveloperSession) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                DeveloperToggle.IsChecked = false;
                return;
            }
            UpdateDeveloperVisibility();
        }

        OwnerPage.Visibility = page == "Owner" ? Visibility.Visible : Visibility.Collapsed;
        ProfilePage.Visibility = page == "Profile" ? Visibility.Visible : Visibility.Collapsed;
        AlbumPage.Visibility = page == "Album" ? Visibility.Visible : Visibility.Collapsed;
        ModelPage.Visibility = page == "Model" ? Visibility.Visible : Visibility.Collapsed;
        AssetsPage.Visibility = page == "Assets" ? Visibility.Visible : Visibility.Collapsed;
        DeveloperPage.Visibility = page == "Developer" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChooseAlbumRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "\u9009\u62e9\u609f\u7a7a\u76f8\u518c\u76ee\u5f55",
            InitialDirectory = Directory.Exists(_albumRoot)
                ? _albumRoot
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog(this) == true)
        {
            _albumRoot = dialog.FolderName;
            AlbumFolderItem.SaveAlbumRootPreference(_albumRoot, _agent.DataPaths.ProfileDirectory);
            RefreshAlbumView();
        }
    }

    private void RefreshAlbum_Click(object sender, RoutedEventArgs e) => RefreshAlbumView();

    private void OpenAlbumRoot_Click(object sender, RoutedEventArgs e) => OpenFolder(_albumRoot);

    private void AlbumItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AlbumFolderItem item })
            SelectAlbum(item);
    }

    private void SaveAlbumDescription_Click(object sender, RoutedEventArgs e)
    {
        SaveSelectedAlbumMarkdown();
    }

    private void OpenSelectedAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlbum is not null)
            OpenFolder(_selectedAlbum.DirectoryPath);
    }

    private void DeleteSelectedAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (_albumUnbindInProgress)
            return;
        if (_selectedAlbum is null)
        {
            AlbumStatusText.Text = "请先选择要删除的子相册。";
            return;
        }

        var selected = _selectedAlbum;
        var confirm = MessageBox.Show(
            this,
            $"从悟空相册中删除子相册“{selected.Name}”？\n\n本地文件夹和原始图片会保留。",
            "删除子相册",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var result = AlbumFolderVisibility.RemoveFromCatalog(selected, markerPath =>
        {
            File.WriteAllText(markerPath, "hidden_from_wukong_album=true\n", System.Text.Encoding.UTF8);
            return true;
        });
        if (result.Status != AlbumFolderRemovalStatus.Success)
        {
            AlbumStatusText.Text = result.UserMessage;
            return;
        }

        RefreshAlbumView();
        AlbumStatusText.Text = result.UserMessage;
    }

    private void AlbumMediaList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectedAlbumMediaPreview();

    private void AlbumPreviousMedia_Click(object sender, RoutedEventArgs e) => MoveAlbumMediaSelection(-1);

    private void AlbumNextMedia_Click(object sender, RoutedEventArgs e) => MoveAlbumMediaSelection(1);

    private void MoveAlbumMediaSelection(int delta)
    {
        if (_albumMediaBindings.Count == 0)
        {
            UpdateSelectedAlbumMediaPreview();
            return;
        }

        var current = AlbumMediaList.SelectedIndex;
        var next = Math.Clamp(current < 0 ? 0 : current + delta, 0, _albumMediaBindings.Count - 1);
        AlbumMediaList.SelectedIndex = next;
        AlbumMediaList.ScrollIntoView(_albumMediaBindings[next]);
    }

    private void AddAlbumMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlbum is null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "\u65b0\u589e\u76f8\u518c\u56fe\u7247\u7d20\u6750",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        Directory.CreateDirectory(_selectedAlbum.DirectoryPath);
        foreach (var source in dialog.FileNames)
        {
            var fileName = MakeUniqueFileName(_selectedAlbum.DirectoryPath, Path.GetFileName(source));
            var target = Path.Combine(_selectedAlbum.DirectoryPath, fileName);
            if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target);
            if (!_albumMediaBindings.Any(x => string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                _albumMediaBindings.Add(CreateAlbumMediaItem(fileName));
        }

        SaveSelectedAlbumMarkdown();
    }

    private void UnbindAlbumMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_albumUnbindInProgress)
            return;

        _albumUnbindInProgress = true;
        try
        {
            var selected = AlbumMediaList.SelectedItem as AlbumMediaItem;
            var selectedIndex = AlbumMediaList.SelectedIndex;
            var result = AlbumMediaBindingEditor.Unbind(
                selected?.FileName,
                _albumMediaBindings,
                mediaFiles =>
                {
                    SaveSelectedAlbumMarkdown(mediaFiles, refreshView: false);
                    return true;
                });
            AlbumStatusText.Text = result.UserMessage;
            if (result.Status == AlbumMediaUnbindStatus.Success)
                SelectAlbumMediaAfterMutation(selectedIndex);
            else
                UpdateSelectedAlbumMediaPreview();
        }
        finally
        {
            _albumUnbindInProgress = false;
        }
    }

    private void DeleteAlbumMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_albumUnbindInProgress)
            return;

        var selected = AlbumMediaList.SelectedItem as AlbumMediaItem;
        if (selected is null)
        {
            AlbumStatusText.Text = "请先选择要删除的素材记录。";
            UpdateSelectedAlbumMediaPreview();
            return;
        }

        var confirm = MessageBox.Show(this, $"删除素材记录 {selected.FileName}？本操作不会删除本地原始图片。", "删除素材记录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        _albumUnbindInProgress = true;
        try
        {
            var selectedIndex = AlbumMediaList.SelectedIndex;
            var result = AlbumMediaBindingEditor.Delete(
                selected.FileName,
                _albumMediaBindings,
                mediaFiles =>
                {
                    SaveSelectedAlbumMarkdown(mediaFiles, refreshView: false);
                    return true;
                });
            AlbumStatusText.Text = result.UserMessage;
            if (result.Status == AlbumMediaUnbindStatus.Success)
                SelectAlbumMediaAfterMutation(selectedIndex);
            else
                UpdateSelectedAlbumMediaPreview();
        }
        finally
        {
            _albumUnbindInProgress = false;
        }
    }

    private void UploadPetAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "\u9009\u62e9\u609f\u7a7a\u5934\u50cf\u622a\u56fe",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var profileDir = _agent.DataPaths.ProfileDirectory;
        Directory.CreateDirectory(profileDir);
        var target = Path.Combine(profileDir, "pet-avatar" + Path.GetExtension(dialog.FileName));
        File.Copy(dialog.FileName, target, overwrite: true);
        OwnerAvatarImage.Source = LoadBitmap(target);
        OwnerAvatarFallback.Visibility = Visibility.Collapsed;
    }

    private void ProfileTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab })
            SelectProfileTab(tab);
    }

    private void SelectProfileTab(string tab)
    {
        var activeTab = tab is "Owner" or "Relation" or "Memory" ? tab : "Pet";
        ProfilePetPanel.Visibility = activeTab == "Pet" ? Visibility.Visible : Visibility.Collapsed;
        ProfileOwnerPanel.Visibility = activeTab == "Owner" ? Visibility.Visible : Visibility.Collapsed;
        ProfileRelationPanel.Visibility = activeTab == "Relation" ? Visibility.Visible : Visibility.Collapsed;
        ProfileMemoryPanel.Visibility = activeTab == "Memory" ? Visibility.Visible : Visibility.Collapsed;
        ProfilePetTabButton.Style = PanelTabStyle(activeTab == "Pet");
        ProfileOwnerTabButton.Style = PanelTabStyle(activeTab == "Owner");
        ProfileRelationTabButton.Style = PanelTabStyle(activeTab == "Relation");
        ProfileMemoryTabButton.Style = PanelTabStyle(activeTab == "Memory");
    }

    private void ModelTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab })
            SelectModelTab(tab);
    }

    private void SelectModelTab(string tab)
    {
        _activeModelTab = tab is "Memory" or "Pet" ? tab : "Model";
        ModelConfigPanel.Visibility = _activeModelTab == "Model" ? Visibility.Visible : Visibility.Collapsed;
        MemoryConfigPanel.Visibility = _activeModelTab == "Memory" ? Visibility.Visible : Visibility.Collapsed;
        PetSettingPanel.Visibility = _activeModelTab == "Pet" ? Visibility.Visible : Visibility.Collapsed;
        ModelConfigTabButton.Style = PanelTabStyle(_activeModelTab == "Model");
        MemoryConfigTabButton.Style = PanelTabStyle(_activeModelTab == "Memory");
        PetSettingTabButton.Style = PanelTabStyle(_activeModelTab == "Pet");
        ModelChatList.ItemsSource = _modelDebugItems[_activeModelTab];
        SetChatStatus(_activeModelTab switch
        {
            "Memory" => "记忆配置调试会话已切换。",
            "Pet" => "宠物设定调试会话已切换。",
            _ => "大模型调试会话已切换。"
        });
        ScrollChatToEnd();
    }

    private async void MemoryConfig_Changed(object sender, RoutedEventArgs e)
    {
        if (!_modelUiReady)
            return;

        _memoryConfiguration = ReadMemoryConfigurationFromUi();
        await _agent.MemoryConfiguration.SaveAsync(_memoryConfiguration);
        SetChatStatus("记忆配置已保存，将用于下一轮对话。");
    }

    private async void SavePetPrompt_Click(object sender, RoutedEventArgs e)
    {
        await _agent.Profiles.SavePetPromptAsync(PetPromptText.Text);
        ModelConfigStatus.Text = "宠物设定已保存并会用于后续对话。";
        SetChatStatus("宠物设定已保存，下一轮调试会重新注入最新提示词。");
    }

    private void PetPromptText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_modelUiReady)
            ModelConfigStatus.Text = "宠物设定已修改，保存后用于后续对话。";
    }

    private async void SavePetProfile_Click(object sender, RoutedEventArgs e)
    {
        await _agent.Profiles.SavePetProfileAsync(new(
            PetNameText.Text.Trim(),
            PetEnglishNameText.Text.Trim(),
            PetBirthDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            PetBreedText.Text.Trim(),
            PetLifeStageText.Text.Trim(),
            _loadedPetProfile.Harness));
    }

    private async void SaveOwnerProfile_Click(object sender, RoutedEventArgs e)
    {
        await _agent.Profiles.SaveOwnerProfileAsync(new(
            _loadedOwnerProfile.CallName,
            OwnerScheduleText.Text.Trim(),
            OwnerPreferenceText.Text.Trim(),
            _loadedOwnerProfile.Tone,
            OwnerNotesText.Text.Trim())
        {
            Birthday = OwnerBirthdayPicker.SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            PetCallName = OwnerPetCallNameText.Text.Trim()
        });
    }

    private async void SaveModelConfig_Click(object sender, RoutedEventArgs e) => await SaveModelConfigurationAsync();

    private async Task LoadAgentUiAsync()
    {
        var petTask = _agent.Profiles.LoadPetProfileAsync();
        var ownerTask = _agent.Profiles.LoadOwnerProfileAsync();
        var promptTask = _agent.Profiles.LoadPetPromptAsync();
        var configurationsTask = _agent.Models.GetConfigurationsAsync();
        var activeTask = _agent.Models.GetActiveConfigurationAsync();
        var memoryConfigurationTask = _agent.MemoryConfiguration.LoadAsync();
        await Task.WhenAll(petTask, ownerTask, promptTask, configurationsTask, activeTask, memoryConfigurationTask);

        var pet = await petTask;
        _loadedPetProfile = pet;
        PetNameText.Text = pet.Name;
        PetEnglishNameText.Text = pet.EnglishName;
        PetBirthDatePicker.SelectedDate = DateTime.TryParse(pet.BirthDate, out var birthDate) ? birthDate : null;
        PetBreedText.Text = pet.Breed;
        PetLifeStageText.Text = pet.LifeStage;

        var owner = await ownerTask;
        _loadedOwnerProfile = owner;
        OwnerBirthdayPicker.SelectedDate = DateTime.TryParse(owner.Birthday, out var ownerBirthday) ? ownerBirthday : null;
        OwnerPetCallNameText.Text = owner.PetCallName;
        OwnerScheduleText.Text = owner.Schedule;
        OwnerPreferenceText.Text = owner.CompanionPreference;
        OwnerNotesText.Text = owner.Notes;
        PetPromptText.Text = await promptTask;
        _memoryConfiguration = await memoryConfigurationTask;
        ApplyMemoryConfigurationToUi(_memoryConfiguration);
        LoadAvatarIfAvailable();

        _providerConfigurations.Clear();
        foreach (var configuration in await configurationsTask)
            _providerConfigurations[configuration.Provider] = configuration;
        ModelProviderCombo.ItemsSource = Enum.GetValues<ChatProviderType>();
        var active = await activeTask;
        ModelProviderCombo.SelectedItem = active.Provider;
        LoadProviderEditor(active);
        _modelUiReady = true;

        await ReloadChatHistoryAsync();
        await RefreshMemoryCandidatesAsync();
        UpdateDeveloperVisibility();
        SelectModelTab("Model");
    }

    private async void ModelProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_modelUiReady || ModelProviderCombo.SelectedItem is not ChatProviderType provider)
            return;
        await _agent.Models.SetActiveProviderAsync(provider);
        if (_providerConfigurations.TryGetValue(provider, out var configuration))
            LoadProviderEditor(configuration);
    }

    private void LoadProviderEditor(ChatProviderConfiguration configuration)
    {
        ModelApiUrlText.Text = configuration.BaseUrl;
        ModelNameText.Text = configuration.Model;
        ModelTimeoutText.Text = configuration.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ModelTemperatureText.Text = configuration.Temperature.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
        ModelApiKeyBox.Clear();
        var requiresKey = configuration.Provider != ChatProviderType.Ollama;
        ModelApiKeyBox.IsEnabled = requiresKey;
        ModelApiKeyStatus.Text = requiresKey
            ? configuration.ApiKeyConfigured ? "已安全保存（不会回显）" : "未配置"
            : "本地 Ollama 默认不需要 API Key";
    }

    private void ApplyMemoryConfigurationToUi(AgentMemoryConfiguration configuration)
    {
        UseLongTermMemoryCheck.IsChecked = configuration.UseLongTermMemory;
        UseAlbumMemoryCheck.IsChecked = configuration.UseAlbumMemory;
        UseShortTermMemoryCheck.IsChecked = configuration.UseShortTermMemory;
    }

    private AgentMemoryConfiguration ReadMemoryConfigurationFromUi() => new(
        UseLongTermMemoryCheck.IsChecked == true,
        UseAlbumMemoryCheck.IsChecked == true,
        UseShortTermMemoryCheck.IsChecked == true);

    private async Task<bool> SaveModelConfigurationAsync()
    {
        if (ModelProviderCombo.SelectedItem is not ChatProviderType provider)
            return false;
        if (!int.TryParse(ModelTimeoutText.Text, out var timeoutSeconds))
            timeoutSeconds = 60;
        if (!double.TryParse(ModelTemperatureText.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var temperature))
            temperature = 0.7;
        var existing = _providerConfigurations.TryGetValue(provider, out var value)
            ? value
            : ChatProviderConfiguration.Default(provider);
        var configuration = new ChatProviderConfiguration(
            provider,
            ModelApiUrlText.Text,
            ModelNameText.Text,
            timeoutSeconds,
            temperature,
            existing.ApiKeyConfigured);
        try
        {
            await _agent.Models.SaveConfigurationAsync(configuration, ModelApiKeyBox.Password);
            await _agent.Models.SetActiveProviderAsync(provider);
            var saved = (await _agent.Models.GetConfigurationsAsync()).Single(x => x.Provider == provider);
            _providerConfigurations[provider] = saved;
            LoadProviderEditor(saved);
            ModelConfigStatus.Text = $"已保存 {provider} 配置。API Key 不写入项目文件。";
            return true;
        }
        catch (Exception)
        {
            ModelConfigStatus.Text = "配置保存失败，请检查本机凭证存储权限。";
            return false;
        }
    }

    private async void TestModelConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveModelConfigurationAsync())
            return;
        ModelConfigStatus.Text = "正在测试连接...";
        try
        {
            var response = await _agent.Models.TestConnectionAsync();
            ModelConfigStatus.Text = string.IsNullOrWhiteSpace(response.Text) ? "连接成功。" : "连接成功，模型已返回响应。";
        }
        catch (ChatProviderException ex)
        {
            ModelConfigStatus.Text = ex.PublicMessage;
        }
        catch (Exception)
        {
            ModelConfigStatus.Text = "连接失败，请检查网络、地址和模型配置。";
        }
    }

    private async Task SendAgentMessageAsync(TextBox input)
    {
        var text = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || _agentRequestCancellation is not null)
            return;

        var chatItems = ActiveChatItems(input);
        chatItems.Add(ChatDisplayItem.User(text));
        input.Clear();
        SetChatStatus("悟空正在想...");
        _agentRequestCancellation = new CancellationTokenSource();
        try
        {
            var result = await _agent.Conversation.SendAsync(
                new ConversationRequest(SessionIdForInput(input), text, _memoryConfiguration),
                _agentRequestCancellation.Token);
            chatItems.Add(result.Success
                ? ChatDisplayItem.Assistant(result.AssistantText ?? string.Empty)
                : ChatDisplayItem.Error(result.UserFacingError ?? "请求失败，请检查模型配置。"));
            SetChatStatus(result.Success
                ? $"{result.Provider} / {result.Model} / {result.Duration.TotalSeconds:0.0}s / 相册记忆 {result.UsedAlbumMemoryCount} 条"
                : result.UserFacingError ?? "请求失败");
            ScrollChatToEnd();
            if (_agent.DeveloperSession.IsAuthenticated)
                RefreshDiagnosticsView();
        }
        finally
        {
            _agentRequestCancellation.Dispose();
            _agentRequestCancellation = null;
        }
    }

    private async Task ReloadChatHistoryAsync()
    {
        await ReloadChatHistoryAsync(DesktopAgentRuntime.DailySessionId, _chatItems);
        foreach (var key in _modelDebugItems.Keys.ToArray())
            await ReloadChatHistoryAsync(SessionIdForModelTab(key), _modelDebugItems[key]);
        ScrollChatToEnd();
    }

    private async Task ReloadChatHistoryAsync(string sessionId, ObservableCollection<ChatDisplayItem> target)
    {
        var history = await _agent.Conversation.GetHistoryAsync(sessionId);
        target.Clear();
        foreach (var message in history.Where(x => x.Role != AgentChatRole.System))
            target.Add(ChatDisplayItem.From(message));
    }

    private void ScrollChatToEnd()
    {
        if (_chatItems.Count > 0)
            OwnerChatList.ScrollIntoView(_chatItems[^1]);
        var active = _modelDebugItems[_activeModelTab];
        if (active.Count > 0)
            ModelChatList.ScrollIntoView(active[^1]);
    }

    private void SetChatStatus(string value)
    {
        OwnerChatStatus.Text = value;
        ModelChatStatus.Text = value;
    }

    private ObservableCollection<ChatDisplayItem> ActiveChatItems(TextBox input) =>
        ReferenceEquals(input, ModelDebugInput) ? _modelDebugItems[_activeModelTab] : _chatItems;

    private string SessionIdForInput(TextBox input) =>
        ReferenceEquals(input, ModelDebugInput) ? SessionIdForModelTab(_activeModelTab) : DesktopAgentRuntime.DailySessionId;

    private static string SessionIdForModelTab(string tab) => tab switch
    {
        "Memory" => "model-debug-memory",
        "Pet" => "model-debug-pet",
        _ => "model-debug-model"
    };

    private static bool IsInModelDebug(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = LogicalTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Name: "ModelDebugPanel" })
                return true;
        }
        return false;
    }

    private async void AgentInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
            return;
        e.Handled = true;
        if (sender is TextBox input)
            await SendAgentMessageAsync(input);
    }

    private void CancelAgentRequest_Click(object sender, RoutedEventArgs e) => _agentRequestCancellation?.Cancel();

    private async void ClearConversation_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = sender is Button button && IsInModelDebug(button)
            ? SessionIdForModelTab(_activeModelTab)
            : DesktopAgentRuntime.DailySessionId;
        await _agent.Conversation.ClearHistoryAsync(sessionId);
        if (sessionId == DesktopAgentRuntime.DailySessionId)
            _chatItems.Clear();
        else
            _modelDebugItems[_activeModelTab].Clear();
        SetChatStatus("当前对话已清空。");
    }

    private async void ClearAllConversationHistory_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "清空全部主人对话与三个调试会话？清空后便携数据目录中的历史文件会在无其他会话时删除。",
            "清空全部对话",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        await _agent.ClearAllConversationHistoryAsync();
        _chatItems.Clear();
        foreach (var items in _modelDebugItems.Values)
            items.Clear();
        SetChatStatus("全部对话历史已清空，可以打包为空白会话版本。");
    }

    private async void SaveMemoryCandidate_Click(object sender, RoutedEventArgs e)
    {
        var sessionId = sender is Button button && IsInModelDebug(button)
            ? SessionIdForModelTab(_activeModelTab)
            : DesktopAgentRuntime.DailySessionId;
        var candidate = await _agent.Conversation.SaveLatestTurnAsCandidateAsync(sessionId);
        SetChatStatus(candidate is null ? "没有可保存的完整对话轮次。" : "已保存为待人工确认的记忆候选。");
        await RefreshMemoryCandidatesAsync();
    }

    private async Task RefreshMemoryCandidatesAsync()
    {
        var items = await _agent.Memory.ReadAsync();
        _memoryCandidates.Clear();
        foreach (var item in items.OrderByDescending(x => x.CreatedAt))
            _memoryCandidates.Add(item);
        MemoryEmptyState.Visibility = _memoryCandidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RefreshMemory_Click(object sender, RoutedEventArgs e) => await RefreshMemoryCandidatesAsync();
    private async void ConfirmMemory_Click(object sender, RoutedEventArgs e) => await SetMemoryStatusAsync(sender, ConversationMemoryStatus.Confirmed);
    private async void RejectMemory_Click(object sender, RoutedEventArgs e) => await SetMemoryStatusAsync(sender, ConversationMemoryStatus.Rejected);

    private async Task SetMemoryStatusAsync(object sender, ConversationMemoryStatus status)
    {
        if (sender is Button { Tag: ConversationMemoryCandidate candidate })
        {
            await _agent.Memory.SetStatusAsync(candidate.Id, status);
            await RefreshMemoryCandidatesAsync();
        }
    }

    private async void DeleteMemory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ConversationMemoryCandidate candidate })
        {
            await _agent.Memory.DeleteAsync(candidate.Id);
            await RefreshMemoryCandidatesAsync();
        }
    }

    private void OpenDeveloper_Click(object sender, RoutedEventArgs e)
    {
        DeveloperToggle.IsChecked = true;
        if (!_agent.DeveloperSession.IsAuthenticated)
            return;
        OwnerPage.Visibility = Visibility.Collapsed;
        ProfilePage.Visibility = Visibility.Collapsed;
        AlbumPage.Visibility = Visibility.Collapsed;
        ModelPage.Visibility = Visibility.Collapsed;
        AssetsPage.Visibility = Visibility.Collapsed;
        DeveloperPage.Visibility = Visibility.Visible;
        RefreshDiagnosticsView();
    }

    private void UpdateDeveloperVisibility()
    {
        var visible = _agent.DeveloperSession.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;
        DeveloperDiagnosticsPanel.Visibility = visible;
        AutonomousDailyAssetsTabButton.Visibility = visible;
        TraceList.Visibility = visible;
        if (visible == Visibility.Visible)
            RefreshDiagnosticsView();
        else if (DeveloperPage.Visibility == Visibility.Visible)
        {
            DeveloperPage.Visibility = Visibility.Collapsed;
            OwnerPage.Visibility = Visibility.Visible;
        }
        if (visible == Visibility.Collapsed && AutonomousDailyAssetsPanel.Visibility == Visibility.Visible)
            SelectNormalAssetSubTab("Base");
    }

    private void RefreshDiagnostics_Click(object sender, RoutedEventArgs e) => RefreshDiagnosticsView();

    private void RefreshDiagnosticsView()
    {
        if (!_agent.DeveloperSession.IsAuthenticated)
            return;
        var diagnostics = _agent.Diagnostics.ReadLatest();
        if (diagnostics is null)
        {
            AgentDiagnosticsText.Text = "尚无 Agent 请求诊断。";
            return;
        }

        var context = diagnostics.Context;
        AgentDiagnosticsText.Text = string.Join(Environment.NewLine, new[]
        {
            $"provider={diagnostics.Provider}",
            $"model={diagnostics.Model}",
            $"status={diagnostics.Status}",
            $"duration_ms={diagnostics.Duration.TotalMilliseconds:0}",
            $"pet_fields={string.Join(",", context.PetFields)}",
            $"owner_fields={string.Join(",", context.OwnerFields)}",
            $"pet_setting={context.PetPromptSummary}",
            $"personality={context.Personality}",
            $"relationship={context.Relationship}",
            $"runtime={context.RuntimeState}",
            $"album_matches={string.Join("; ", context.AlbumMatches.Select(x => $"{x.Title}|{x.Date}|{x.Score:0.00}|{x.SourceName}"))}",
            $"history_messages={context.HistoryMessageCount}",
            $"truncated={context.WasTruncated}",
            $"degradations={string.Join(",", context.Degradations)}"
        });
    }

    private void ApplyMockState_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _agent.MockContext.Update(
                PersonalitySnapshot.Default with { Liveliness = MockLivelinessSlider.Value },
                RelationshipSnapshot.Default with { Trust = MockTrustSlider.Value },
                PetRuntimeStateSnapshot.Default with
                {
                    CurrentBehavior = MockBehaviorText.Text,
                    Fatigue = MockFatigueSlider.Value,
                    Stress = MockStressSlider.Value,
                    SocialDesire = MockSocialSlider.Value,
                    PlayDesire = MockPlaySlider.Value,
                    Curiosity = MockCuriositySlider.Value
                });
            MockStateStatus.Text = "Mock 状态已更新，将用于下一轮对话。";
        }
        catch (UnauthorizedAccessException)
        {
            MockStateStatus.Text = "开发者会话已失效，请重新登录。";
            UpdateDeveloperVisibility();
        }
    }

    private void BehaviorAgentMockToggle_Changed(object sender, RoutedEventArgs e)
    {
        _runtime.SetBehaviorAgentMockEnabled(BehaviorAgentMockToggle.IsChecked == true);
        BehaviorAgentSnapshotText.Text = _runtime.BehaviorAgentSnapshot;
    }

    private void ApplyBehaviorAgentMock_Click(object sender, RoutedEventArgs e)
    {
        _runtime.UpdateBehaviorAgentMock(
            new TemperamentProfile(
                (int)AgentActivitySlider.Value,
                (int)AgentAttachmentSlider.Value,
                (int)AgentSensitivitySlider.Value,
                (int)AgentIndependenceSlider.Value,
                (int)AgentMischiefSlider.Value),
            PetRuntimeState.Default with
            {
                CurrentPosture = SelectedAgentPosture(),
                Energy = 1.0 - MockFatigueSlider.Value,
                Hunger = 0.35,
                SocialNeed = MockSocialSlider.Value,
                Boredom = MockPlaySlider.Value,
                Stress = MockStressSlider.Value,
                MoodValence = Math.Clamp(1.0 - MockStressSlider.Value, 0, 1),
                Arousal = MockLivelinessSlider.Value
            },
            RelationshipState.Default with { Trust = MockTrustSlider.Value },
            ReadAgentSeed());
        BehaviorAgentSnapshotText.Text = _runtime.BehaviorAgentSnapshot;
        MockStateStatus.Text = "Behavior Agent Mock state applied.";
    }

    private async void BehaviorAgentCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;

        ApplyBehaviorAgentMock_Click(sender, e);
        var command = Enum.TryParse<OwnerCommandKind>(tag, ignoreCase: true, out var parsed)
            ? parsed
            : OwnerCommandKind.None;
        if (command == OwnerCommandKind.None)
        {
            var decision = _runtime.PreviewBehaviorAgentDecision(OwnerCommandKind.None, ReadAgentSeed());
            MockStateStatus.Text = $"Autonomous mock selected {decision.SelectedActionId}";
        }
        else
        {
            var result = await _runtime.SubmitBehaviorAgentCommandAsync(command, BehaviorRequestSource.ControlPanel);
            MockStateStatus.Text = $"Behavior Agent Mock {command}: {result}";
        }
        BehaviorAgentSnapshotText.Text = _runtime.BehaviorAgentSnapshot;
    }

    private StablePosture SelectedAgentPosture()
    {
        var value = (BehaviorAgentPostureCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        return Enum.TryParse<StablePosture>(value, ignoreCase: true, out var posture)
            ? posture
            : StablePosture.Prone;
    }

    private int ReadAgentSeed() =>
        int.TryParse(BehaviorAgentSeedText.Text, out var seed) ? seed : 2408;


    private async void ForceLifecycleCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion motion })
            return;
        if (!_agent.DeveloperSession.IsAuthenticated)
        {
            MockStateStatus.Text = "开发者会话已失效，请重新登录。";
            UpdateDeveloperVisibility();
            return;
        }

        var result = await _runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId);
        MockStateStatus.Text = $"候选预览 {motion.DisplayName}: {result}";
    }

    private async void ForceAutonomousDailyCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion motion })
            return;
        if (!_agent.DeveloperSession.IsAuthenticated)
        {
            MagicShowStatus.Text = "开发者会话已失效，请重新登录。";
            UpdateDeveloperVisibility();
            return;
        }

        var result = await _runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId);
        MagicShowStatus.Text = result == PetActionResult.Accepted
            ? $"审阅展示 {motion.DisplayName}：已发送到桌面；正式自主池仍关闭"
            : $"审阅展示 {motion.DisplayName}：{result}";
    }

    private void CandidateSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var pixels))
        {
            _runtime.RequestPetPixelSize(pixels);
            MockStateStatus.Text = $"候选尺寸已设为 {pixels}px";
        }
    }

    private async void ForceCommandMotion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion motion })
            return;
        if (!_agent.DeveloperSession.IsAuthenticated)
        {
            MockStateStatus.Text = "开发者会话已失效，请重新登录。";
            UpdateDeveloperVisibility();
            return;
        }

        var result = await _runtime.SubmitDeveloperMotionAsync(motion.BehaviorId);
        MockStateStatus.Text = $"开发者预览 {motion.DisplayName}: {result}";
    }

    private void AssetTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tab })
            return;

        NormalAssetsPanel.Visibility = tab == "Normal" ? Visibility.Visible : Visibility.Collapsed;
        PlayAssetsPanel.Visibility = Visibility.Collapsed;
        CommandAssetsPanel.Visibility = Visibility.Collapsed;
        AutonomousDailyAssetsPanel.Visibility = Visibility.Collapsed;
        MagicAssetsPanel.Visibility = tab == "Magic" ? Visibility.Visible : Visibility.Collapsed;
        NormalAssetSubTabs.Visibility = tab == "Normal" ? Visibility.Visible : Visibility.Collapsed;
        NormalAssetsTabButton.Style = PanelTabStyle(tab == "Normal");
        MagicAssetsTabButton.Style = PanelTabStyle(tab == "Magic");
        if (tab == "Normal")
            SelectNormalAssetSubTab("Base");
    }

    private void NormalAssetSubTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab })
            SelectNormalAssetSubTab(tab);
    }

    private void SelectNormalAssetSubTab(string tab)
    {
        AssetList.Visibility = Visibility.Visible;
        NormalAssetsPanel.Visibility = tab == "Base" ? Visibility.Visible : Visibility.Collapsed;
        PlayAssetsPanel.Visibility = tab == "Play" ? Visibility.Visible : Visibility.Collapsed;
        CommandAssetsPanel.Visibility = tab == "Command" ? Visibility.Visible : Visibility.Collapsed;
        AutonomousDailyAssetsPanel.Visibility = tab == "AutonomousDaily" ? Visibility.Visible : Visibility.Collapsed;
        BaseAssetsTabButton.Style = PanelTabStyle(tab == "Base");
        PlayAssetsTabButton.Style = PanelTabStyle(tab == "Play");
        CommandAssetsTabButton.Style = PanelTabStyle(tab == "Command");
        AutonomousDailyAssetsTabButton.Style = PanelTabStyle(tab == "AutonomousDaily");
    }

    private Style PanelTabStyle(bool selected) => (Style)FindResource(selected ? "PanelTabButtonSelected" : "PanelTabButton");

    private async void ShowMagic_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion motion })
            return;

        MagicShowStatus.Text = $"正在展示 {motion.DisplayName}...";
        var result = await _runtime.SubmitMagicAsync(motion.BehaviorId, BehaviorRequestSource.ControlPanel);
        MagicShowStatus.Text = result switch
        {
            PetActionResult.Accepted => $"{motion.DisplayName}: 正在展示",
            PetActionResult.Deferred => $"{motion.DisplayName}: {_runtime.CurrentReason}",
            PetActionResult.MissingAsset => $"{motion.DisplayName}: 素材缺失",
            PetActionResult.Interrupted => $"{motion.DisplayName}: 已停止",
            PetActionResult.Failed => $"{motion.DisplayName}: 展示失败并已恢复",
            _ => $"{motion.DisplayName}: {result}"
        };
    }

    private async void ShowCarRide_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion })
            return;

        MagicShowStatus.Text = "正在展示兜风...";
        var result = await _runtime.SubmitCarRideAsync(BehaviorRequestSource.ControlPanel);
        MagicShowStatus.Text = result switch
        {
            PetActionResult.Accepted => "兜风正在运行。",
            PetActionResult.Deferred => $"兜风暂时不能运行：{_runtime.CurrentReason}",
            PetActionResult.Rejected => "兜风请求被拒绝。",
            _ => result.ToString()
        };
    }

    private async void ShowPlayAsset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion motion })
            return;

        if (string.Equals(motion.BehaviorId, CarRideBehaviorIds.CarRide, StringComparison.OrdinalIgnoreCase))
        {
            await ShowCarRideFromPanelAsync();
            return;
        }

        MagicShowStatus.Text = $"{motion.DisplayName}: 暂未接入右键互动";
    }

    private async void ShowCommandAsset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion motion })
            return;

        if (!string.Equals(motion.AssetBatch, CommandMockBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) || !motion.RuntimeEnabled)
        {
            MagicShowStatus.Text = $"{motion.DisplayName}: 旧素材已过期，仅保留预览和动作参考";
            return;
        }

        var command = CommandLabelForMotion(motion.BehaviorId);
        if (string.IsNullOrWhiteSpace(command))
        {
            MagicShowStatus.Text = $"{motion.DisplayName}: 未找到对应口令入口";
            return;
        }

        MagicShowStatus.Text = $"正在展示口令动作：{motion.DisplayName}...";
        var result = await _runtime.SubmitOwnerCommandAsync(command);
        MagicShowStatus.Text = result switch
        {
            PetActionResult.Accepted => $"{motion.DisplayName}: 正在展示",
            PetActionResult.Deferred => $"{motion.DisplayName}: {_runtime.CurrentReason}",
            PetActionResult.MissingAsset => $"{motion.DisplayName}: 素材缺失",
            PetActionResult.Interrupted => $"{motion.DisplayName}: 已停止",
            PetActionResult.Failed => $"{motion.DisplayName}: 展示失败并已恢复",
            _ => $"{motion.DisplayName}: {result}"
        };
    }

    private async Task ShowCarRideFromPanelAsync()
    {
        MagicShowStatus.Text = "正在展示兜风...";
        var result = await _runtime.SubmitCarRideAsync(BehaviorRequestSource.ControlPanel);
        MagicShowStatus.Text = result switch
        {
            PetActionResult.Accepted => "兜风正在运行。",
            PetActionResult.Deferred => $"兜风暂时不能运行：{_runtime.CurrentReason}",
            PetActionResult.Rejected => "兜风请求被拒绝。",
            _ => result.ToString()
        };
    }

    private static string CommandLabelForMotion(string behaviorId) => behaviorId switch
    {
        MockCommandActionIds.Sit => "坐",
        MockCommandActionIds.Down => "卧",
        MockCommandActionIds.PawSit or MockCommandActionIds.PawProne or CommandBehaviorIds.PawRise => "手",
        MockCommandActionIds.Jump => "跳",
        MockCommandActionIds.Spin or CommandBehaviorIds.SpinApproachStopSit => "转圈",
        MockCommandActionIds.EatSit or MockCommandActionIds.EatProne or CommandBehaviorIds.PawEat => "吃",
        _ => string.Empty
    };
    private static bool IsCommandMotion(PlayableMotion motion) =>
        string.Equals(motion.Category, "口令动作", StringComparison.Ordinal);

    private static bool IsMagicMotion(PlayableMotion motion) =>
        string.Equals(motion.Category, "宠物魔法", StringComparison.Ordinal);

    private static bool IsCarRideMotion(PlayableMotion motion) =>
        string.Equals(motion.BehaviorId, CarRideBehaviorIds.CarRide, StringComparison.OrdinalIgnoreCase);

    private static bool IsBaseMotion(PlayableMotion motion) =>
        !IsCommandMotion(motion) && !IsMagicMotion(motion) && !IsCarRideMotion(motion);

    private void LoadAvatarIfAvailable()
    {
        var profileDirectory = _agent.DataPaths.ProfileDirectory;
        var avatar = Directory.Exists(profileDirectory)
            ? Directory.GetFiles(profileDirectory, "pet-avatar.*").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        OwnerAvatarImage.Source = LoadBitmap(avatar);
        OwnerAvatarFallback.Visibility = OwnerAvatarImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string SelectedComboText(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? combo.Text;

    private static void SelectComboText(ComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (!string.Equals(item.Content?.ToString(), value, StringComparison.Ordinal))
                continue;
            combo.SelectedItem = item;
            return;
        }
        combo.Text = value;
    }

    private void PreviewAsset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayableMotion motion })
            return;

        _previewMotion = motion;
        PreviewTitle.Text = $"{motion.DisplayName} - {motion.BehaviorId}";
        PreviewMeta.Text = $"{motion.Category} - {motion.Direction} - {motion.FrameCount} frames - {motion.Fps:F2} fps - {motion.RuntimeStatus} - profile: {motion.CandidateProfile}";
        PreviewPhaseCombo.ItemsSource = motion.Phases;
        PreviewPhaseCombo.DisplayMemberPath = nameof(MotionPhase.Name);
        PreviewPhaseCombo.SelectedIndex = 0;
        SelectPreviewPhase(motion.Phases.FirstOrDefault());
    }

    private void PreviewPhaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewPhaseCombo.SelectedItem is MotionPhase phase)
            SelectPreviewPhase(phase);
    }

    private void PreviewPause_Click(object sender, RoutedEventArgs e) =>
        _previewPaused = !_previewPaused;

    private void PreviewPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_previewFrames.Count == 0)
            return;
        _previewIndex = (_previewIndex - 1 + _previewFrames.Count) % _previewFrames.Count;
        ShowPreviewFrame();
    }

    private void PreviewNext_Click(object sender, RoutedEventArgs e)
    {
        if (_previewFrames.Count == 0)
            return;
        _previewIndex = (_previewIndex + 1) % _previewFrames.Count;
        ShowPreviewFrame();
    }

    private void PreviewBackground_Click(object sender, RoutedEventArgs e)
    {
        _previewDark = !_previewDark;
        PreviewStage.Background = new SolidColorBrush(_previewDark
            ? Color.FromRgb(28, 30, 27)
            : Color.FromRgb(248, 247, 243));
        PreviewBackgroundButton.Content = _previewDark ? "切换为明色背景" : "切换为深色背景";
        PreviewBackgroundStatus.Text = _previewDark ? "当前：深色背景" : "当前：明色背景";
    }

    private void SelectPreviewPhase(MotionPhase? phase)
    {
        _previewPhase = phase;
        _previewFrames = phase?.Frames ?? Array.Empty<string>();
        _previewIndex = 0;
        _previewPaused = false;
        _previewTimer.Interval = TimeSpan.FromMilliseconds(PreviewDurationForCurrentFrame());
        ShowPreviewFrame();
        if (_previewFrames.Count > 1)
            _previewTimer.Start();
        else
            _previewTimer.Stop();
    }

    private int PreviewDurationForCurrentFrame()
    {
        var fallback = _previewMotion?.FrameDurationMs ?? 125;
        return Math.Max(16, _previewPhase?.DurationForFrame(_previewIndex, fallback) ?? fallback);
    }

    private void AdvancePreview()
    {
        if (_previewPaused || _previewFrames.Count == 0)
            return;

        _previewIndex++;
        if (_previewIndex >= _previewFrames.Count)
        {
            if (PreviewLoopCheck.IsChecked == true || _previewPhase?.Loop == true)
                _previewIndex = 0;
            else
            {
                _previewIndex = _previewFrames.Count - 1;
                _previewPaused = true;
            }
        }
        _previewTimer.Interval = TimeSpan.FromMilliseconds(PreviewDurationForCurrentFrame());
        ShowPreviewFrame();
    }

    private void ShowPreviewFrame()
    {
        if (_previewFrames.Count == 0)
        {
            PreviewImage.Source = null;
            PreviewFrame.Text = "No preview frames found.";
            return;
        }

        var path = _previewFrames[Math.Clamp(_previewIndex, 0, _previewFrames.Count - 1)];
        try
        {
            PreviewImage.Source = LoadBitmap(path);
            PreviewFrame.Text = $"{_previewIndex + 1}/{_previewFrames.Count} - {Path.GetFileName(path)} - duration {PreviewDurationForCurrentFrame()} ms - phase {_previewPhase?.Name ?? "n/a"}";
        }
        catch (Exception ex)
        {
            PreviewImage.Source = null;
            PreviewFrame.Text = $"Failed to load {Path.GetFileName(path)} - {ex.GetType().Name}";
        }
    }

    private void RefreshAlbumView()
    {
        _albumFolders.Clear();
        AlbumRootPathText.Text = Directory.Exists(_albumRoot)
            ? _albumRoot
            : $"{_albumRoot} (not found)";

        if (!Directory.Exists(_albumRoot))
        {
            AlbumStatusText.Text = "0 albums";
            SelectAlbum(null);
            return;
        }

        foreach (var directory in Directory.GetDirectories(_albumRoot)
                     .Where(AlbumFolderVisibility.IsVisible)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _albumFolders.Add(AlbumFolderItem.FromDirectory(directory));

        AlbumStatusText.Text = $"{_albumFolders.Count} albums";
        SelectAlbum(_albumFolders.FirstOrDefault());
    }

    private void SelectAlbum(AlbumFolderItem? item)
    {
        _selectedAlbum = item;
        if (item is null)
        {
            AlbumPreviewImage.Source = null;
            AlbumSelectedNameText.Text = "\u672a\u9009\u62e9\u5b50\u76f8\u518c";
            AlbumDatePicker.SelectedDate = null;
            AlbumDescriptionText.Text = string.Empty;
            _albumMediaBindings.Clear();
            AlbumMarkdownPathText.Text = "\u9009\u62e9\u672c\u5730\u76f8\u518c\u76ee\u5f55\u540e\uff0c\u4f1a\u8bfb\u53d6\u6bcf\u4e2a\u5b50\u6587\u4ef6\u5939\u7684 markdown \u63cf\u8ff0\u3002";
            DeleteSelectedAlbumButton.IsEnabled = false;
            UpdateSelectedAlbumMediaPreview();
            return;
        }

        AlbumSelectedNameText.Text = item.Name;
        AlbumDatePicker.SelectedDate = DateTime.TryParse(item.DateText, out var date) ? date : null;
        AlbumDescriptionText.Text = item.Description;
        _albumMediaBindings.Clear();
        foreach (var fileName in item.MediaFiles)
            _albumMediaBindings.Add(CreateAlbumMediaItem(fileName));
        AlbumMediaList.SelectedIndex = _albumMediaBindings.Count > 0 ? 0 : -1;
        AlbumMarkdownPathText.Text = string.IsNullOrWhiteSpace(item.MarkdownPath)
            ? "\u672a\u627e\u5230 markdown\uff0c\u4fdd\u5b58\u540e\u4f1a\u521b\u5efa album.md"
            : item.MarkdownPath;
        DeleteSelectedAlbumButton.IsEnabled = true;
        UpdateSelectedAlbumMediaPreview();
    }

    private AlbumMediaItem CreateAlbumMediaItem(string fileName)
    {
        var fullPath = _selectedAlbum is null ? fileName : Path.Combine(_selectedAlbum.DirectoryPath, fileName);
        return new AlbumMediaItem(fileName, fullPath, File.Exists(fullPath) ? "已找到本地文件" : "本地文件缺失");
    }

    private void UpdateSelectedAlbumMediaPreview()
    {
        var selected = AlbumMediaList.SelectedItem as AlbumMediaItem;
        AlbumPreviewImage.Source = LoadBitmap(selected?.FullPath);
        AlbumPreviewImage.Opacity = 1.0;
        AlbumMediaStatusText.Text = selected is null
            ? (_albumMediaBindings.Count == 0 ? "暂无图片素材" : "请选择图片")
            : $"{AlbumMediaList.SelectedIndex + 1}/{_albumMediaBindings.Count} - {selected.FileName} - {selected.Status}";
        AlbumMediaEmptyState.Visibility = _albumMediaBindings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UnbindAlbumMediaButton.IsEnabled = selected is not null;
        DeleteAlbumMediaButton.IsEnabled = selected is not null;
    }

    private void SelectAlbumMediaAfterMutation(int previousIndex)
    {
        if (_albumMediaBindings.Count == 0)
        {
            AlbumMediaList.SelectedIndex = -1;
            UpdateSelectedAlbumMediaPreview();
            return;
        }

        AlbumMediaList.SelectedIndex = Math.Clamp(previousIndex, 0, _albumMediaBindings.Count - 1);
        AlbumMediaList.ScrollIntoView(_albumMediaBindings[AlbumMediaList.SelectedIndex]);
        UpdateSelectedAlbumMediaPreview();
    }

    private void SaveSelectedAlbumMarkdown() => SaveSelectedAlbumMarkdown(_albumMediaBindings);

    private void SaveSelectedAlbumMarkdown(IReadOnlyList<AlbumMediaItem> mediaFiles, bool refreshView = true)
    {
        if (_selectedAlbum is null)
            return;

        Directory.CreateDirectory(_selectedAlbum.DirectoryPath);
        var selectedPath = _selectedAlbum.DirectoryPath;
        var markdownPath = string.IsNullOrWhiteSpace(_selectedAlbum.MarkdownPath)
            ? Path.Combine(_selectedAlbum.DirectoryPath, "album.md")
            : _selectedAlbum.MarkdownPath;
        var boundFiles = mediaFiles.Select(x => x.FileName).ToArray();
        File.WriteAllText(markdownPath, _selectedAlbum.CreateMarkdown(CurrentAlbumDateText(), AlbumDescriptionText.Text, boundFiles));
        if (!refreshView)
            return;
        RefreshAlbumView();
        var updated = _albumFolders.FirstOrDefault(x => string.Equals(x.DirectoryPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (updated is not null)
            SelectAlbum(updated);
    }

    private string CurrentAlbumDateText() =>
        AlbumDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");

    private static string MakeUniqueFileName(string directory, string fileName)
    {
        var candidate = fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var index = 1;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{stem}-{index:00}{extension}";
            index++;
        }
        return candidate;
    }

    private static BitmapImage? LoadBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static DrawingBrush MakeCheckerBrush(Color a, Color b)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new Rect(10, 10, 10, 10))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 20, 20),
            ViewportUnits = BrushMappingMode.Absolute
        };
    }
}

public sealed record AlbumMediaItem(string FileName, string FullPath, string Status);

public enum AlbumFolderRemovalStatus
{
    Success,
    NoSelection,
    PersistenceFailed
}

public sealed record AlbumFolderRemovalResult(AlbumFolderRemovalStatus Status, string UserMessage);

public static class AlbumFolderVisibility
{
    public const string HiddenMarkerFileName = ".wukong-album-hidden";

    public static bool IsVisible(string directory) =>
        Directory.Exists(directory) && !File.Exists(Path.Combine(directory, HiddenMarkerFileName));

    public static AlbumFolderRemovalResult RemoveFromCatalog(
        AlbumFolderItem? selected,
        Func<string, bool> persistMarker)
    {
        if (selected is null)
            return new AlbumFolderRemovalResult(AlbumFolderRemovalStatus.NoSelection, "请先选择要删除的子相册。");

        var markerPath = Path.Combine(selected.DirectoryPath, HiddenMarkerFileName);
        try
        {
            if (!persistMarker(markerPath))
                throw new IOException("album visibility persistence returned false");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(markerPath)) File.Delete(markerPath); } catch { }
            return new AlbumFolderRemovalResult(AlbumFolderRemovalStatus.PersistenceFailed, $"删除子相册失败：{ex.GetType().Name}");
        }

        return new AlbumFolderRemovalResult(
            AlbumFolderRemovalStatus.Success,
            $"已从悟空相册中删除“{selected.Name}”，本地文件夹和原图未删除。");
    }
}

public enum AlbumMediaUnbindStatus
{
    Success,
    NoSelection,
    NotFound,
    PersistenceFailed
}

public sealed record AlbumMediaUnbindResult(AlbumMediaUnbindStatus Status, string UserMessage);

public static class AlbumMediaBindingEditor
{
    public static AlbumMediaUnbindResult Unbind(
        string? selectedFileName,
        IList<AlbumMediaItem> mediaBindings,
        Func<IReadOnlyList<AlbumMediaItem>, bool> persist)
    {
        if (string.IsNullOrWhiteSpace(selectedFileName))
            return new AlbumMediaUnbindResult(AlbumMediaUnbindStatus.NoSelection, "请先选择要解绑的素材。");

        var index = IndexOf(mediaBindings, selectedFileName);
        if (index < 0)
            return new AlbumMediaUnbindResult(AlbumMediaUnbindStatus.NotFound, "未找到要解绑的素材。");

        var original = mediaBindings[index];
        mediaBindings.RemoveAt(index);
        try
        {
            if (!persist(mediaBindings.ToArray()))
                throw new IOException("album media persistence returned false");
        }
        catch (Exception ex)
        {
            mediaBindings.Insert(Math.Min(index, mediaBindings.Count), original);
            return new AlbumMediaUnbindResult(AlbumMediaUnbindStatus.PersistenceFailed, $"解绑失败：{ex.GetType().Name}");
        }

        return new AlbumMediaUnbindResult(AlbumMediaUnbindStatus.Success, $"已解绑 {original.FileName}，本地原图未删除。");
    }


    public static AlbumMediaUnbindResult Delete(
        string? selectedFileName,
        IList<AlbumMediaItem> mediaBindings,
        Func<IReadOnlyList<AlbumMediaItem>, bool> persist)
    {
        if (string.IsNullOrWhiteSpace(selectedFileName))
            return new AlbumMediaUnbindResult(AlbumMediaUnbindStatus.NoSelection, "请先选择要删除的素材记录。");

        var index = IndexOf(mediaBindings, selectedFileName);
        if (index < 0)
            return new AlbumMediaUnbindResult(AlbumMediaUnbindStatus.NotFound, "未找到要删除的素材记录。");

        var removed = mediaBindings[index];
        mediaBindings.RemoveAt(index);
        try
        {
            if (!persist(mediaBindings.ToArray()))
                throw new IOException("album media persistence returned false");
        }
        catch (Exception ex)
        {
            mediaBindings.Insert(Math.Min(index, mediaBindings.Count), removed);
            return new AlbumMediaUnbindResult(AlbumMediaUnbindStatus.PersistenceFailed, $"删除失败：{ex.GetType().Name}");
        }

        return new AlbumMediaUnbindResult(AlbumMediaUnbindStatus.Success, $"已删除素材记录 {removed.FileName}，本地原图未删除。");
    }

    private static int IndexOf(IList<AlbumMediaItem> mediaBindings, string selectedFileName)
    {
        for (var i = 0; i < mediaBindings.Count; i++)
        {
            if (string.Equals(mediaBindings[i].FileName, selectedFileName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}

public sealed record AlbumFolderItem(
    string Name,
    string DirectoryPath,
    string DateText,
    int PhotoCount,
    string Description,
    string MarkdownPath,
    string ThumbnailPath,
    string Status,
    IReadOnlyList<string> MediaFiles)
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    private static readonly string[] MarkdownNames = { "album.md", "README.md", "readme.md", "description.md", "\u63cf\u8ff0.md" };

    public static string GetDefaultAlbumRoot(string? portableAlbumRoot = null, string? profileDirectory = null)
    {
        var configured = Environment.GetEnvironmentVariable("WUKONG_ALBUM_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var preference = AlbumRootPreferencePath(profileDirectory);
        if (File.Exists(preference))
        {
            var path = File.ReadAllText(preference).Trim();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return path;
        }

        return !string.IsNullOrWhiteSpace(portableAlbumRoot)
            ? portableAlbumRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Wukong");
    }

    public static void SaveAlbumRootPreference(string path, string? profileDirectory = null)
    {
        var directory = AlbumProfileDirectory(profileDirectory);
        Directory.CreateDirectory(directory);
        File.WriteAllText(AlbumRootPreferencePath(directory), path);
    }

    public static AlbumFolderItem FromDirectory(string directory)
    {
        var images = Directory.GetFiles(directory)
            .Where(x => ImageExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var markdown = FindMarkdown(directory);
        var metadata = string.IsNullOrWhiteSpace(markdown)
            ? MarkdownAlbumMetadata.Empty
            : MarkdownAlbumMetadata.Read(markdown);
        var orderedImages = string.IsNullOrWhiteSpace(markdown)
            ? images
            : metadata.MediaFiles
                .Select(x => Path.Combine(directory, x))
                .Where(File.Exists)
                .ToList();
        var description = string.IsNullOrWhiteSpace(markdown)
            ? "\u672a\u627e\u5230 markdown \u63cf\u8ff0"
            : metadata.Description;
        var status = string.IsNullOrWhiteSpace(markdown)
            ? "\u5f85\u8865\u63cf\u8ff0"
            : "\u5df2\u8bfb\u53d6\u63cf\u8ff0";

        return new AlbumFolderItem(
            string.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileName(directory) : metadata.Title,
            directory,
            NormalizeDateText(string.IsNullOrWhiteSpace(metadata.TimeText) ? Directory.GetLastWriteTime(directory).ToString("yyyy-MM-dd") : metadata.TimeText),
            images.Count,
            description,
            markdown,
            orderedImages.FirstOrDefault() ?? string.Empty,
            status,
            orderedImages.Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray());
    }

    public string CreateMarkdown(string timeText, string description) =>
        CreateMarkdown(timeText, description, MediaFiles);

    public string CreateMarkdown(string timeText, string description, IReadOnlyList<string> mediaFiles)
    {
        var title = string.IsNullOrWhiteSpace(Name) ? Path.GetFileName(DirectoryPath) : Name;
        return File.Exists(MarkdownPath)
            ? MarkdownAlbumMetadata.UpdateExisting(MarkdownPath, title, timeText, description, mediaFiles)
            : MarkdownAlbumMetadata.CreateNew(title, timeText, description, mediaFiles);
    }

    private static string AlbumProfileDirectory(string? profileDirectory = null) =>
        string.IsNullOrWhiteSpace(profileDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wukong", "profile")
            : profileDirectory;

    private static string AlbumRootPreferencePath(string? profileDirectory = null) =>
        Path.Combine(AlbumProfileDirectory(profileDirectory), "album-root.txt");

    internal static string BuildMarkdown(string title, string timeText, string description, IReadOnlyList<string> mediaFiles, IReadOnlyList<string>? preservedFrontMatter = null, IReadOnlyList<string>? preservedBodySections = null)
    {
        var mediaLines = mediaFiles.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, mediaFiles.Select(x => $"  - \"{x}\""));
        var body = description.Trim();
        var frontMatter = preservedFrontMatter is { Count: > 0 }
            ? string.Join(Environment.NewLine, preservedFrontMatter.Where(x => !string.IsNullOrWhiteSpace(x))) + Environment.NewLine
            : string.Empty;
        var preserved = preservedBodySections is { Count: > 0 }
            ? $"{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, preservedBodySections).Trim()}"
            : string.Empty;
        return
            $"---{Environment.NewLine}" +
            frontMatter +
            $"title: \"{title}\"{Environment.NewLine}" +
            $"time: \"{NormalizeDateText(timeText)}\"{Environment.NewLine}" +
            $"media:{Environment.NewLine}{mediaLines}{Environment.NewLine}" +
            $"---{Environment.NewLine}{Environment.NewLine}" +
            $"# {title}{Environment.NewLine}{Environment.NewLine}" +
            $"\u65f6\u95f4: {NormalizeDateText(timeText)}{Environment.NewLine}{Environment.NewLine}" +
            $"## \u6b63\u6587{Environment.NewLine}{Environment.NewLine}" +
            $"{body}{preserved}{Environment.NewLine}{Environment.NewLine}" +
            $"## \u7d20\u6750{Environment.NewLine}{Environment.NewLine}" +
            string.Join(Environment.NewLine, mediaFiles.Select(x => $"- `{x}`")) +
            Environment.NewLine;
    }

    private static string FindMarkdown(string directory)
    {
        var preferred = MarkdownNames
            .Select(x => Path.Combine(directory, x))
            .FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred;

        return Directory.GetFiles(directory, "*.md")
            .Concat(Directory.GetFiles(directory, "*.markdown"))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string NormalizeDateText(string value)
    {
        value = value.Trim();
        if (value.Length >= 10 &&
            char.IsDigit(value[0]) &&
            char.IsDigit(value[1]) &&
            char.IsDigit(value[2]) &&
            char.IsDigit(value[3]) &&
            value[4] == '-' &&
            char.IsDigit(value[5]) &&
            char.IsDigit(value[6]) &&
            value[7] == '-' &&
            char.IsDigit(value[8]) &&
            char.IsDigit(value[9]))
        {
            return value[..10];
        }

        return DateTime.TryParse(value, out var date)
            ? date.ToString("yyyy-MM-dd")
            : value;
    }
}

public sealed record MarkdownAlbumMetadata(string Title, string TimeText, string Description, IReadOnlyList<string> MediaFiles)
{
    public static MarkdownAlbumMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty, Array.Empty<string>());

    public static string CreateNew(string title, string timeText, string description, IReadOnlyList<string> mediaFiles) =>
        AlbumFolderItem.BuildMarkdown(title, timeText, description, mediaFiles);

    public static string UpdateExisting(string path, string title, string timeText, string description, IReadOnlyList<string> mediaFiles)
    {
        var text = File.ReadAllText(path);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var frontMatter = ExtractUnknownFrontMatter(lines, out var bodyStart);
        var bodySections = ExtractUnknownBodySections(lines.Skip(bodyStart).ToList());
        return AlbumFolderItem.BuildMarkdown(title, timeText, description, mediaFiles, frontMatter, bodySections);
    }

    public static MarkdownAlbumMetadata Read(string path)
    {
        var text = File.ReadAllText(path).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return Empty;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var title = string.Empty;
        var time = string.Empty;
        var media = new List<string>();
        var bodyStart = 0;

        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            for (var i = 1; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line == "---")
                {
                    bodyStart = i + 1;
                    break;
                }

                if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                    title = Unquote(line["title:".Length..].Trim());
                else if (line.StartsWith("time:", StringComparison.OrdinalIgnoreCase))
                    time = Unquote(line["time:".Length..].Trim());
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                    media.Add(Unquote(line[2..].Trim()));
            }
        }

        var bodyLines = lines.Skip(bodyStart).ToList();
        if (string.IsNullOrWhiteSpace(title))
            title = ReadHeading(bodyLines);
        if (string.IsNullOrWhiteSpace(time))
            time = ReadChineseTime(bodyLines);

        return new MarkdownAlbumMetadata(title, time, ReadBodyDescription(bodyLines), media);
    }

    private static IReadOnlyList<string> ExtractUnknownFrontMatter(IReadOnlyList<string> lines, out int bodyStart)
    {
        bodyStart = 0;
        if (lines.Count == 0 || lines[0].Trim() != "---")
            return Array.Empty<string>();

        var result = new List<string>();
        var skippingMedia = false;
        for (var i = 1; i < lines.Count; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line == "---")
            {
                bodyStart = i + 1;
                break;
            }

            if (skippingMedia)
            {
                var isMediaItem = line.StartsWith("- ", StringComparison.Ordinal) || raw.StartsWith(" ", StringComparison.Ordinal);
                if (isMediaItem)
                    continue;
                skippingMedia = false;
            }

            if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("time:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("media:", StringComparison.OrdinalIgnoreCase))
            {
                skippingMedia = true;
                continue;
            }

            result.Add(raw);
        }

        return result;
    }

    private static IReadOnlyList<string> ExtractUnknownBodySections(IReadOnlyList<string> lines)
    {
        var result = new List<string>();
        var skippingManagedSection = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal) ||
                line.StartsWith("时间:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                skippingManagedSection =
                    line.Equals("## 正文", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("## 素材", StringComparison.OrdinalIgnoreCase) ||
                    LooksLikeLegacyMediaSection(lines, i + 1);
                if (!skippingManagedSection)
                    result.Add(raw);
                continue;
            }

            if (!skippingManagedSection && !string.IsNullOrWhiteSpace(raw))
                result.Add(raw);
        }

        return result;
    }

    private static bool LooksLikeLegacyMediaSection(IReadOnlyList<string> lines, int start)
    {
        var inspected = 0;
        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("## ", StringComparison.Ordinal))
                return false;
            inspected++;
            if (!line.StartsWith("- ", StringComparison.Ordinal))
                return false;
            var lower = line.ToLowerInvariant();
            if (lower.Contains(".png") || lower.Contains(".jpg") || lower.Contains(".jpeg") || lower.Contains(".webp") || lower.Contains(".bmp"))
                return true;
            if (inspected >= 3)
                return false;
        }
        return false;
    }

    private static string ReadBodyDescription(IReadOnlyList<string> lines)
    {
        var body = new List<string>();
        var inBody = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Equals("## \u6b63\u6587", StringComparison.OrdinalIgnoreCase))
            {
                inBody = true;
                continue;
            }
            if (inBody && trimmed.StartsWith("## ", StringComparison.Ordinal))
                break;
            if (inBody)
                body.Add(line);
        }

        if (body.Count > 0)
            return string.Join(Environment.NewLine, body).Trim();

        return string.Join(
            Environment.NewLine,
            lines
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !x.TrimStart().StartsWith("#", StringComparison.Ordinal))
            .Where(x => !x.TrimStart().StartsWith("date:", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.TrimStart().StartsWith("\u65f6\u95f4:", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.TrimStart().StartsWith("- `", StringComparison.Ordinal)))
            .Trim();
    }

    private static string ReadHeading(IEnumerable<string> lines) =>
        lines.Select(x => x.Trim())
            .FirstOrDefault(x => x.StartsWith("# ", StringComparison.Ordinal))?
            .TrimStart('#', ' ') ?? string.Empty;

    private static string ReadChineseTime(IEnumerable<string> lines)
    {
        const string prefix = "\u65f6\u95f4:";
        return lines.Select(x => x.Trim())
            .FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
            [prefix.Length..].Trim() ?? string.Empty;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;
    }
}
