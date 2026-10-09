using System.IO;
using System.Net.Http;
using Wukong.Application;
using Wukong.Infrastructure;

namespace Wukong.Desktop;

public sealed class DesktopAgentRuntime : IDisposable
{
    public const string DailySessionId = "daily-companion";
    private readonly HttpClient _httpClient;

    private DesktopAgentRuntime(
        HttpClient httpClient,
        IContextualConversationService conversation,
        IChatModelRuntime models,
        IAgentProfileStore profiles,
        IAgentMemoryConfigurationStore memoryConfiguration,
        IAutonomousBehaviorPreferencesStore autonomousBehaviorPreferences,
        IConversationHistoryStore history,
        IConversationMemoryStore memory,
        IPetAgentStateStore agentState,
        ICompanionSessionStateStore companionSession,
        IPetDecisionMemorySource decisionMemory,
        IDeveloperSession developerSession,
        IDeveloperDiagnostics diagnostics,
        IMockContextController mockContext,
        PortableDataLayout dataPaths)
    {
        _httpClient = httpClient;
        Conversation = conversation;
        Models = models;
        Profiles = profiles;
        MemoryConfiguration = memoryConfiguration;
        AutonomousBehaviorPreferences = autonomousBehaviorPreferences;
        History = history;
        Memory = memory;
        AgentState = agentState;
        CompanionSession = companionSession;
        DecisionMemory = decisionMemory;
        DeveloperSession = developerSession;
        Diagnostics = diagnostics;
        MockContext = mockContext;
        DataPaths = dataPaths;
        AutonomyPolicy = new FileAutonomyPolicyStore(dataPaths.AgentDirectory);
    }

    public IContextualConversationService Conversation { get; }
    public IChatModelRuntime Models { get; }
    public IAgentProfileStore Profiles { get; }
    public IAgentMemoryConfigurationStore MemoryConfiguration { get; }
    public IAutonomousBehaviorPreferencesStore AutonomousBehaviorPreferences { get; }
    public IConversationHistoryStore History { get; }
    public IConversationMemoryStore Memory { get; }
    public IPetAgentStateStore AgentState { get; }
    public ICompanionSessionStateStore CompanionSession { get; }
    public IPetDecisionMemorySource DecisionMemory { get; }
    public IDeveloperSession DeveloperSession { get; }
    public IDeveloperDiagnostics Diagnostics { get; }
    public IMockContextController MockContext { get; }
    public PortableDataLayout DataPaths { get; }
    public IAutonomyPolicyStore AutonomyPolicy { get; }

    public static DesktopAgentRuntime CreateDefault(Func<PetRuntimeStateSnapshot>? liveRuntimeState = null)
    {
        var dataPaths = PortableDataLayout.CreateDefault();
        var profileRoot = dataPaths.ProfileDirectory;
        var agentRoot = dataPaths.AgentDirectory;
        var httpClient = new HttpClient();
        var configurations = new FileChatProviderConfigurationRepository(agentRoot);
        var secrets = new WindowsCredentialAgentSecretStore();
        var providers = new IChatModelProvider[]
        {
            new OpenAiChatModelProvider(httpClient, ChatProviderType.OpenAI),
            new OpenAiChatModelProvider(httpClient, ChatProviderType.OpenAICompatible),
            new AnthropicChatModelProvider(httpClient),
            new GeminiChatModelProvider(httpClient),
            new OllamaChatModelProvider(httpClient)
        };
        var models = new ConfiguredChatModelRuntime(configurations, secrets, providers);
        var profiles = new LocalAgentProfileStore(profileRoot);
        var memoryConfiguration = new FileAgentMemoryConfigurationStore(agentRoot);
        var autonomousBehaviorPreferences = new FileAutonomousBehaviorPreferencesStore(agentRoot);
        var history = new FileConversationHistoryStore(agentRoot);
        var memory = new FileConversationMemoryStore(agentRoot);
        var agentState = new FilePetAgentStateStore(agentRoot);
        var companionSession = new FileCompanionSessionStateStore(agentRoot);
        var developer = new DeveloperSession();
        var diagnostics = new DeveloperDiagnostics(developer);
        var mockState = new MockRuntimeContextStateProvider(developer, liveRuntimeState);
        var album = new AlbumMarkdownMemoryRetriever(() => ResolveAlbumRoot(dataPaths));
        var decisionMemory = new LocalPetDecisionMemorySource(memoryConfiguration, memory, album);
        var context = new LocalPetContextProvider(profiles, mockState, album, memory);
        var conversation = new ContextualConversationService(
            models,
            context,
            new AgentContextAssembler(),
            history,
            memory,
            diagnostics);
        return new(httpClient, conversation, models, profiles, memoryConfiguration, autonomousBehaviorPreferences,
            history, memory, agentState, companionSession, decisionMemory, developer, diagnostics, mockState, dataPaths);
    }

    public async Task AppendLocalAssistantMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        var messages = (await History.ReadAsync(DailySessionId, cancellationToken)).ToList();
        messages.Add(new AgentChatMessage(AgentChatRole.Assistant, text.Trim(), DateTimeOffset.Now));
        await History.ReplaceAsync(DailySessionId, messages, cancellationToken);
    }

    public async Task AppendLocalConversationTurnAsync(string ownerText, string assistantText, CancellationToken cancellationToken = default)
    {
        var messages = (await History.ReadAsync(DailySessionId, cancellationToken)).ToList();
        var now = DateTimeOffset.Now;
        messages.Add(new AgentChatMessage(AgentChatRole.User, ownerText.Trim(), now));
        messages.Add(new AgentChatMessage(AgentChatRole.Assistant, assistantText.Trim(), now));
        await History.ReplaceAsync(DailySessionId, messages.TakeLast(20).ToArray(), cancellationToken);
    }

    public async Task ReplaceLatestAssistantMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        var messages = (await History.ReadAsync(DailySessionId, cancellationToken)).ToList();
        var index = messages.FindLastIndex(message => message.Role == AgentChatRole.Assistant);
        if (index < 0)
            return;
        messages[index] = messages[index] with { Content = text.Trim() };
        await History.ReplaceAsync(DailySessionId, messages, cancellationToken);
    }

    public async Task ClearAllConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        foreach (var sessionId in new[] { DailySessionId, "model-debug-model", "model-debug-memory", "model-debug-pet" })
            await History.ClearAsync(sessionId, cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    public Task ClearPetSettingDebugHistoryAsync(CancellationToken cancellationToken = default) =>
        Conversation.ClearHistoryAsync("model-debug-pet", cancellationToken);

    private static string ResolveAlbumRoot(PortableDataLayout dataPaths) =>
        PortableAlbumBinding.Resolve(dataPaths.AlbumsDirectory, dataPaths.ProfileDirectory);
}
