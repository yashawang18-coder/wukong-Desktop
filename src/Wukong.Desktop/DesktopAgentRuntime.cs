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
        IConversationMemoryStore memory,
        IDeveloperSession developerSession,
        IDeveloperDiagnostics diagnostics,
        IMockContextController mockContext)
    {
        _httpClient = httpClient;
        Conversation = conversation;
        Models = models;
        Profiles = profiles;
        Memory = memory;
        DeveloperSession = developerSession;
        Diagnostics = diagnostics;
        MockContext = mockContext;
    }

    public IContextualConversationService Conversation { get; }
    public IChatModelRuntime Models { get; }
    public IAgentProfileStore Profiles { get; }
    public IConversationMemoryStore Memory { get; }
    public IDeveloperSession DeveloperSession { get; }
    public IDeveloperDiagnostics Diagnostics { get; }
    public IMockContextController MockContext { get; }

    public static DesktopAgentRuntime CreateDefault()
    {
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wukong");
        var profileRoot = Path.Combine(localRoot, "profile");
        var agentRoot = Path.Combine(localRoot, "agent");
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
        var history = new FileConversationHistoryStore(agentRoot);
        var memory = new FileConversationMemoryStore(agentRoot);
        var developer = new DeveloperSession();
        var diagnostics = new DeveloperDiagnostics(developer);
        var mockState = new MockRuntimeContextStateProvider(developer);
        var album = new AlbumMarkdownMemoryRetriever(ResolveAlbumRoot);
        var context = new LocalPetContextProvider(profiles, mockState, album, memory);
        var conversation = new ContextualConversationService(
            models,
            context,
            new AgentContextAssembler(),
            history,
            memory,
            diagnostics);
        return new(httpClient, conversation, models, profiles, memory, developer, diagnostics, mockState);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string? ResolveAlbumRoot()
    {
        var environment = Environment.GetEnvironmentVariable("WUKONG_ALBUM_ROOT");
        if (!string.IsNullOrWhiteSpace(environment) && Directory.Exists(environment))
            return environment;
        var preference = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wukong", "profile", "album-root.txt");
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
        return AlbumFolderItem.GetDefaultAlbumRoot();
    }
}
