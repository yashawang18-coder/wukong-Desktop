namespace Wukong.Application;

public enum ChatProviderType { OpenAI, OpenAICompatible, Anthropic, Gemini, Ollama }
public enum AgentChatRole { System, User, Assistant }
public enum ChatFailureKind { Configuration, Authentication, Forbidden, NotFound, RateLimited, Server, Timeout, Cancelled, Network, EmptyResponse, Unknown }

public sealed record AgentChatMessage(AgentChatRole Role, string Content, DateTimeOffset CreatedAt);

public sealed record ChatProviderConfiguration(
    ChatProviderType Provider, string BaseUrl, string Model, int TimeoutSeconds, double Temperature, bool ApiKeyConfigured)
{
    public static ChatProviderConfiguration Default(ChatProviderType provider) => provider switch
    {
        ChatProviderType.OpenAI => new(provider, "https://api.openai.com/v1", "gpt-4.1-mini", 60, 0.7, false),
        ChatProviderType.OpenAICompatible => new(provider, "https://api.deepseek.com/v1", "deepseek-chat", 60, 0.7, false),
        ChatProviderType.Anthropic => new(provider, "https://api.anthropic.com", "claude-sonnet-4-5", 60, 0.7, false),
        ChatProviderType.Gemini => new(provider, "https://generativelanguage.googleapis.com", "gemini-2.5-flash", 60, 0.7, false),
        ChatProviderType.Ollama => new(provider, "http://127.0.0.1:11434", "llama3.2", 120, 0.7, false),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    public ChatProviderConfiguration Normalize() => this with
    {
        BaseUrl = (BaseUrl ?? string.Empty).Trim().TrimEnd('/'),
        Model = (Model ?? string.Empty).Trim(),
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 300),
        Temperature = Math.Clamp(Temperature, 0, 2)
    };
}

public sealed record ChatProviderConnection(ChatProviderConfiguration Configuration, string? ApiKey);
public sealed record ChatProviderCapabilities(bool RequiresApiKey, bool SupportsSystemMessages, bool SupportsCustomBaseUrl, bool SupportsLocalModels);
public sealed record ChatModelRequest(IReadOnlyList<AgentChatMessage> Messages, double Temperature);
public sealed record ChatModelResponse(string Text, string ProviderResponseId, string? FinishReason = null);

public sealed class ChatProviderException : Exception
{
    public ChatProviderException(ChatFailureKind kind, string publicMessage, string? diagnosticCode = null, Exception? innerException = null)
        : base(publicMessage, innerException)
    {
        Kind = kind;
        PublicMessage = publicMessage;
        DiagnosticCode = diagnosticCode ?? kind.ToString().ToLowerInvariant();
    }

    public ChatFailureKind Kind { get; }
    public string PublicMessage { get; }
    public string DiagnosticCode { get; }
}

public interface IChatModelProvider
{
    ChatProviderType ProviderType { get; }
    ChatProviderCapabilities Capabilities { get; }
    Task<ChatModelResponse> SendAsync(ChatProviderConnection connection, ChatModelRequest request, CancellationToken cancellationToken = default);
}

public interface IChatModelRuntime
{
    Task<ChatProviderConfiguration> GetActiveConfigurationAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatProviderConfiguration>> GetConfigurationsAsync(CancellationToken cancellationToken = default);
    Task SaveConfigurationAsync(ChatProviderConfiguration configuration, string? apiKey, CancellationToken cancellationToken = default);
    Task SetActiveProviderAsync(ChatProviderType provider, CancellationToken cancellationToken = default);
    Task<ChatModelResponse> SendAsync(ChatModelRequest request, CancellationToken cancellationToken = default);
    Task<ChatModelResponse> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed record PetProfileSnapshot(string Name, string EnglishName, string BirthDate, string Breed, string LifeStage, string Harness)
{
    public static PetProfileSnapshot Default { get; } = new("悟空", "Wukong", "", "日本柴犬", "成年", "橙色背带");
}

public sealed record OwnerProfileSnapshot(string CallName, string Schedule, string CompanionPreference, string Tone, string Notes)
{
    public string Birthday { get; init; } = "";
    public string PetCallName { get; init; } = "悟空";
    public static OwnerProfileSnapshot Default { get; } = new("主人", "", "", "亲近自然", "") { PetCallName = "悟空" };
}

public sealed record PersonalitySnapshot(double Liveliness, double Affection, double Sensitivity, double Independence, double Mischievousness)
{
    public PersonalitySnapshot Clamp() => this with
    {
        Liveliness = Clamp01(Liveliness),
        Affection = Clamp01(Affection),
        Sensitivity = Clamp01(Sensitivity),
        Independence = Clamp01(Independence),
        Mischievousness = Clamp01(Mischievousness)
    };
    public static PersonalitySnapshot Default { get; } = new(0.58, 0.76, 0.48, 0.62, 0.42);
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}

public sealed record RelationshipSnapshot(double Trust, double Familiarity, double TouchAcceptance, double InitiativeAcceptance)
{
    public RelationshipSnapshot Clamp() => this with
    {
        Trust = Clamp01(Trust),
        Familiarity = Clamp01(Familiarity),
        TouchAcceptance = Clamp01(TouchAcceptance),
        InitiativeAcceptance = Clamp01(InitiativeAcceptance)
    };
    public static RelationshipSnapshot Default { get; } = new(0.82, 0.78, 0.72, 0.58);
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}

public sealed record PetRuntimeStateSnapshot(
    string CurrentBehavior, double Arousal, double Stress, double SocialDesire,
    double PlayDesire, double Curiosity, double Fatigue, double Safety)
{
    public string CurrentPosture { get; init; } = "prone";
    public string CurrentAction { get; init; } = "quiet_prone";
    public double MoodValence { get; init; } = 0.55;

    public PetRuntimeStateSnapshot Clamp() => this with
    {
        CurrentBehavior = string.IsNullOrWhiteSpace(CurrentBehavior) ? "quiet_prone" : CurrentBehavior.Trim(),
        CurrentPosture = NormalizePosture(CurrentPosture),
        CurrentAction = string.IsNullOrWhiteSpace(CurrentAction)
            ? (string.IsNullOrWhiteSpace(CurrentBehavior) ? "quiet_prone" : CurrentBehavior.Trim())
            : CurrentAction.Trim(),
        MoodValence = Clamp01(MoodValence),
        Arousal = Clamp01(Arousal),
        Stress = Clamp01(Stress),
        SocialDesire = Clamp01(SocialDesire),
        PlayDesire = Clamp01(PlayDesire),
        Curiosity = Clamp01(Curiosity),
        Fatigue = Clamp01(Fatigue),
        Safety = Clamp01(Safety)
    };
    public static PetRuntimeStateSnapshot Default { get; } = new("quiet_prone", 0.32, 0.12, 0.54, 0.35, 0.48, 0.28, 0.95);
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
    private static string NormalizePosture(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "stand" or "standing" => "stand",
        "sit" or "sitting" => "sit",
        "prone" or "lying" or "lie_down" => "prone",
        _ => "prone"
    };
}

public sealed record RelevantAlbumMemory(
    string MemoryId, string AlbumTitle, string Date, string Excerpt,
    IReadOnlyList<string> MediaReferences, string SourceMarkdownPath, double RelevanceScore);

public sealed record AgentMemoryConfiguration(
    bool UseLongTermMemory,
    bool UseAlbumMemory,
    bool UseShortTermMemory)
{
    public static AgentMemoryConfiguration Default { get; } = new(true, true, true);
}

public sealed record PetContextRequest(
    string UserMessage,
    int MaximumAlbumMemories = 5,
    AgentMemoryConfiguration? MemoryConfiguration = null);
public sealed record PetContextSnapshot(
    PetProfileSnapshot PetProfile, OwnerProfileSnapshot OwnerProfile, string CustomPetPrompt,
    PersonalitySnapshot Personality, RelationshipSnapshot Relationship, PetRuntimeStateSnapshot RuntimeState,
    IReadOnlyList<RelevantAlbumMemory> RelevantMemories, IReadOnlyList<string> ConfirmedLongTermMemories);

public interface IPetContextProvider
{
    Task<PetContextSnapshot> GetSnapshotAsync(PetContextRequest request, CancellationToken cancellationToken = default);
}

public interface IRuntimeContextStateProvider
{
    Task<(PersonalitySnapshot Personality, RelationshipSnapshot Relationship, PetRuntimeStateSnapshot RuntimeState)> GetStateAsync(CancellationToken cancellationToken = default);
}

public interface IAlbumMemoryRetriever
{
    Task<IReadOnlyList<RelevantAlbumMemory>> SearchAsync(string query, int maximumResults, CancellationToken cancellationToken = default);
}

public interface IAgentProfileStore
{
    Task<PetProfileSnapshot> LoadPetProfileAsync(CancellationToken cancellationToken = default);
    Task SavePetProfileAsync(PetProfileSnapshot profile, CancellationToken cancellationToken = default);
    Task<OwnerProfileSnapshot> LoadOwnerProfileAsync(CancellationToken cancellationToken = default);
    Task SaveOwnerProfileAsync(OwnerProfileSnapshot profile, CancellationToken cancellationToken = default);
    Task<string> LoadPetPromptAsync(CancellationToken cancellationToken = default);
    Task SavePetPromptAsync(string prompt, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryConfigurationStore
{
    Task<AgentMemoryConfiguration> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AgentMemoryConfiguration configuration, CancellationToken cancellationToken = default);
}

public interface IConversationHistoryStore
{
    Task<IReadOnlyList<AgentChatMessage>> ReadAsync(string sessionId, CancellationToken cancellationToken = default);
    Task ReplaceAsync(string sessionId, IReadOnlyList<AgentChatMessage> messages, CancellationToken cancellationToken = default);
    Task ClearAsync(string sessionId, CancellationToken cancellationToken = default);
}

public enum ConversationMemoryStatus { Pending, Confirmed, Rejected }
public sealed record ConversationMemoryCandidate(
    Guid Id, string SessionId, string Content, string Source, DateTimeOffset CreatedAt, ConversationMemoryStatus Status);

public interface IConversationMemoryStore
{
    Task<IReadOnlyList<ConversationMemoryCandidate>> ReadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ConversationMemoryCandidate candidate, CancellationToken cancellationToken = default);
    Task SetStatusAsync(Guid id, ConversationMemoryStatus status, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record ContextAssemblyDiagnostics(
    IReadOnlyList<string> PetFields, IReadOnlyList<string> OwnerFields, string PetPromptSummary,
    PersonalitySnapshot Personality, RelationshipSnapshot Relationship, PetRuntimeStateSnapshot RuntimeState,
    IReadOnlyList<(string Title, string Date, double Score, string SourceName)> AlbumMatches,
    int HistoryMessageCount, bool WasTruncated, IReadOnlyList<string> Degradations);

public sealed record AgentTurnDiagnostics(
    DateTimeOffset StartedAt, string Provider, string Model, TimeSpan Duration,
    string Status, string DiagnosticCode, ContextAssemblyDiagnostics Context);

public interface IDeveloperSession
{
    bool IsAuthenticated { get; }
    bool Authenticate(string password);
    void SignOut();
}

public interface IDeveloperDiagnostics
{
    void Record(AgentTurnDiagnostics diagnostics);
    AgentTurnDiagnostics? ReadLatest();
}

public interface IMockContextController
{
    void Update(PersonalitySnapshot personality, RelationshipSnapshot relationship, PetRuntimeStateSnapshot runtimeState);
}

public sealed record ConversationRequest(
    string SessionId,
    string UserMessage,
    AgentMemoryConfiguration? MemoryConfiguration = null);
public sealed record ConversationTurnResult(
    bool Success, string? AssistantText, string? UserFacingError, ChatFailureKind? FailureKind,
    string Provider, string Model, TimeSpan Duration, int UsedAlbumMemoryCount);

public interface IContextualConversationService
{
    Task<ConversationTurnResult> SendAsync(ConversationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentChatMessage>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default);
    Task ClearHistoryAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ConversationMemoryCandidate?> SaveLatestTurnAsCandidateAsync(string sessionId, CancellationToken cancellationToken = default);
}
