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
        IConversationHistoryStore history,
        IConversationMemoryStore memory,
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
        History = history;
        Memory = memory;
        DeveloperSession = developerSession;
        Diagnostics = diagnostics;
        MockContext = mockContext;
        DataPaths = dataPaths;
    }

    public IContextualConversationService Conversation { get; }
    public IChatModelRuntime Models { get; }
    public IAgentProfileStore Profiles { get; }
    public IAgentMemoryConfigurationStore MemoryConfiguration { get; }
    public IConversationHistoryStore History { get; }
    public IConversationMemoryStore Memory { get; }
    public IDeveloperSession DeveloperSession { get; }
    public IDeveloperDiagnostics Diagnostics { get; }
    public IMockContextController MockContext { get; }
    public PortableDataLayout DataPaths { get; }

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
        var history = new FileConversationHistoryStore(agentRoot);
        var memory = new FileConversationMemoryStore(agentRoot);
        var developer = new DeveloperSession();
        var diagnostics = new DeveloperDiagnostics(developer);
        var mockState = new MockRuntimeContextStateProvider(developer, liveRuntimeState);
        var album = new AlbumMarkdownMemoryRetriever(() => ResolveAlbumRoot(dataPaths));
        var context = new LocalPetContextProvider(profiles, mockState, album, memory);
        var conversation = new ContextualConversationService(
            models,
            context,
            new AgentContextAssembler(),
            history,
            memory,
            diagnostics);
        return new(httpClient, conversation, models, profiles, memoryConfiguration, history, memory, developer, diagnostics, mockState, dataPaths);
    }

    public async Task AppendLocalAssistantMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        var messages = (await History.ReadAsync(DailySessionId, cancellationToken)).ToList();
        messages.Add(new AgentChatMessage(AgentChatRole.Assistant, text.Trim(), DateTimeOffset.Now));
        await History.ReplaceAsync(DailySessionId, messages, cancellationToken);
    }

    public async Task ClearAllConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        foreach (var sessionId in new[] { DailySessionId, "model-debug-model", "model-debug-memory", "model-debug-pet" })
            await History.ClearAsync(sessionId, cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string? ResolveAlbumRoot(PortableDataLayout dataPaths)
    {
        var environment = Environment.GetEnvironmentVariable("WUKONG_ALBUM_ROOT");
        if (!string.IsNullOrWhiteSpace(environment) && Directory.Exists(environment))
            return environment;
        var preference = Path.Combine(dataPaths.ProfileDirectory, "album-root.txt");
        if (File.Exists(preference))
        {
            try
            {
                var path = File.ReadAllText(preference).Trim();
                if (Directory.Exists(path))
                    return path;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return dataPaths.AlbumsDirectory;
    }
}
