using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wukong.Application;

namespace Wukong.Infrastructure;

internal static class AgentJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
        }
        catch (JsonException) { return default; }
        catch (IOException) { return default; }
        catch (UnauthorizedAccessException) { return default; }
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed class FileChatProviderConfigurationRepository : IChatProviderConfigurationRepository
{
    private readonly string _path;

    public FileChatProviderConfigurationRepository(string rootDirectory) =>
        _path = Path.Combine(rootDirectory, "model-providers.json");

    public async Task<ChatProviderSettingsState> LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await AgentJson.ReadAsync<ChatProviderSettingsState>(_path, cancellationToken);
        return loaded ?? new(
            ChatProviderType.OpenAICompatible,
            Enum.GetValues<ChatProviderType>().ToDictionary(x => x, ChatProviderConfiguration.Default));
    }

    public Task SaveAsync(ChatProviderSettingsState state, CancellationToken cancellationToken = default) =>
        AgentJson.WriteAsync(_path, state, cancellationToken);
}

public sealed class WindowsCredentialAgentSecretStore : IAgentSecretStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private readonly string _targetPrefix;

    public WindowsCredentialAgentSecretStore(string targetPrefix = "Wukong.Desktop.Agent") =>
        _targetPrefix = targetPrefix;

    public Task<string?> ReadAsync(ChatProviderType provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        if (!CredRead(Target(provider), CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168)
                return Task.FromResult<string?>(null);
            throw new InvalidOperationException("Windows Credential Manager could not read the model credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return Task.FromResult<string?>(null);
            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task WriteAsync(ChatProviderType provider, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = Target(provider),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("Windows Credential Manager could not save the model credential.");
            return Task.CompletedTask;
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public Task DeleteAsync(ChatProviderType provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        if (!CredDelete(Target(provider), CredentialTypeGeneric, 0) && Marshal.GetLastWin32Error() != 1168)
            throw new InvalidOperationException("Windows Credential Manager could not delete the model credential.");
        return Task.CompletedTask;
    }

    private string Target(ChatProviderType provider) => $"{_targetPrefix}.{provider}";
    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Credential Manager is required for persisted API keys.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr credential);
}

public sealed class LocalAgentProfileStore : IAgentProfileStore
{
    private readonly string _profileDirectory;

    public LocalAgentProfileStore(string profileDirectory) => _profileDirectory = profileDirectory;

    public async Task<PetProfileSnapshot> LoadPetProfileAsync(CancellationToken cancellationToken = default) =>
        await AgentJson.ReadAsync<PetProfileSnapshot>(Path.Combine(_profileDirectory, "pet-profile.json"), cancellationToken)
        ?? PetProfileSnapshot.Default;

    public Task SavePetProfileAsync(PetProfileSnapshot profile, CancellationToken cancellationToken = default) =>
        AgentJson.WriteAsync(Path.Combine(_profileDirectory, "pet-profile.json"), profile, cancellationToken);

    public async Task<OwnerProfileSnapshot> LoadOwnerProfileAsync(CancellationToken cancellationToken = default)
    {
        var json = await AgentJson.ReadAsync<OwnerProfileSnapshot>(Path.Combine(_profileDirectory, "owner-profile.json"), cancellationToken);
        if (json is not null)
            return json;
        var legacyPath = Path.Combine(_profileDirectory, "owner-profile.txt");
        if (!File.Exists(legacyPath))
            return OwnerProfileSnapshot.Default;
        try
        {
            var values = (await File.ReadAllLinesAsync(legacyPath, cancellationToken))
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.OrdinalIgnoreCase);
            return new(
                Value(values, "call_name", OwnerProfileSnapshot.Default.CallName),
                Value(values, "schedule", ""),
                Value(values, "preference", ""),
                Value(values, "tone", OwnerProfileSnapshot.Default.Tone),
                Value(values, "notes", ""))
            {
                Birthday = Value(values, "birthday", ""),
                PetCallName = Value(values, "pet_call_name", OwnerProfileSnapshot.Default.PetCallName)
            };
        }
        catch (IOException) { return OwnerProfileSnapshot.Default; }
        catch (UnauthorizedAccessException) { return OwnerProfileSnapshot.Default; }
    }

    public Task SaveOwnerProfileAsync(OwnerProfileSnapshot profile, CancellationToken cancellationToken = default) =>
        AgentJson.WriteAsync(Path.Combine(_profileDirectory, "owner-profile.json"), profile, cancellationToken);

    public async Task<string> LoadPetPromptAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_profileDirectory, "pet-prompt.txt");
        if (!File.Exists(path))
            return "回答简短、自然，不编造没有记录的经历。";
        try { return await File.ReadAllTextAsync(path, cancellationToken); }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    public async Task SavePetPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_profileDirectory);
        var path = Path.Combine(_profileDirectory, "pet-prompt.txt");
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, prompt ?? string.Empty, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value : fallback;
}

public sealed class FileConversationHistoryStore : IConversationHistoryStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileConversationHistoryStore(string rootDirectory) =>
        _path = Path.Combine(rootDirectory, "conversation-history.json");

    public async Task<IReadOnlyList<AgentChatMessage>> ReadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadAllAsync(cancellationToken);
            return sessions.TryGetValue(sessionId, out var messages) ? messages : Array.Empty<AgentChatMessage>();
        }
        finally { _gate.Release(); }
    }

    public async Task ReplaceAsync(string sessionId, IReadOnlyList<AgentChatMessage> messages, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadAllAsync(cancellationToken);
            sessions[sessionId] = messages.TakeLast(40)
                .Select(x => x with { Content = RedactAndClip(x.Content, 8_000) })
                .ToArray();
            await AgentJson.WriteAsync(_path, sessions, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var sessions = await ReadAllAsync(cancellationToken);
            sessions.Remove(sessionId);
            if (sessions.Count == 0)
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            else
            {
                await AgentJson.WriteAsync(_path, sessions, cancellationToken);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, IReadOnlyList<AgentChatMessage>>> ReadAllAsync(CancellationToken cancellationToken) =>
        await AgentJson.ReadAsync<Dictionary<string, IReadOnlyList<AgentChatMessage>>>(_path, cancellationToken)
        ?? new(StringComparer.Ordinal);

    private static string RedactAndClip(string value, int maximum)
    {
        var redacted = SensitiveDataRedactor.Redact(value);
        return redacted.Length <= maximum ? redacted : redacted[..maximum];
    }
}

public sealed class FileConversationMemoryStore : IConversationMemoryStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileConversationMemoryStore(string rootDirectory) =>
        _path = Path.Combine(rootDirectory, "memory-candidates.json");

    public async Task<IReadOnlyList<ConversationMemoryCandidate>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadItemsAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(ConversationMemoryCandidate candidate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadItemsAsync(cancellationToken)).ToList();
            items.RemoveAll(x => x.Id == candidate.Id);
            items.Add(candidate with { Content = SensitiveDataRedactor.Redact(candidate.Content) });
            await AgentJson.WriteAsync(_path, items.TakeLast(200).ToArray(), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task SetStatusAsync(Guid id, ConversationMemoryStatus status, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadItemsAsync(cancellationToken)).ToList();
            var index = items.FindIndex(x => x.Id == id);
            if (index >= 0)
                items[index] = items[index] with { Status = status };
            await AgentJson.WriteAsync(_path, items, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadItemsAsync(cancellationToken)).Where(x => x.Id != id).ToArray();
            await AgentJson.WriteAsync(_path, items, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<ConversationMemoryCandidate>> ReadItemsAsync(CancellationToken cancellationToken) =>
        await AgentJson.ReadAsync<IReadOnlyList<ConversationMemoryCandidate>>(_path, cancellationToken)
        ?? Array.Empty<ConversationMemoryCandidate>();
}

public sealed class FileAgentMemoryConfigurationStore : IAgentMemoryConfigurationStore
{
    private readonly string _path;

    public FileAgentMemoryConfigurationStore(string rootDirectory) =>
        _path = Path.Combine(rootDirectory, "memory-configuration.json");

    public async Task<AgentMemoryConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        await AgentJson.ReadAsync<AgentMemoryConfiguration>(_path, cancellationToken)
        ?? AgentMemoryConfiguration.Default;

    public Task SaveAsync(AgentMemoryConfiguration configuration, CancellationToken cancellationToken = default) =>
        AgentJson.WriteAsync(_path, configuration, cancellationToken);
}

public sealed class MockRuntimeContextStateProvider : IRuntimeContextStateProvider, IMockContextController
{
    private readonly IDeveloperSession? _developerSession;
    private readonly Func<PetRuntimeStateSnapshot>? _liveRuntimeState;
    private readonly object _gate = new();
    private PersonalitySnapshot _personality = PersonalitySnapshot.Default;
    private RelationshipSnapshot _relationship = RelationshipSnapshot.Default;
    private PetRuntimeStateSnapshot _runtimeState = PetRuntimeStateSnapshot.Default;
    private bool _hasDeveloperOverride;

    public MockRuntimeContextStateProvider(
        IDeveloperSession? developerSession = null,
        Func<PetRuntimeStateSnapshot>? liveRuntimeState = null)
    {
        _developerSession = developerSession;
        _liveRuntimeState = liveRuntimeState;
    }

    public Task<(PersonalitySnapshot Personality, RelationshipSnapshot Relationship, PetRuntimeStateSnapshot RuntimeState)> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var activeDeveloperOverride = _hasDeveloperOverride &&
                                          (_developerSession is null || _developerSession.IsAuthenticated);
            var runtimeState = !activeDeveloperOverride && _liveRuntimeState is not null
                ? _liveRuntimeState().Clamp()
                : _runtimeState;
            return Task.FromResult((_personality, _relationship, runtimeState));
        }
    }

    public void Update(PersonalitySnapshot personality, RelationshipSnapshot relationship, PetRuntimeStateSnapshot runtimeState)
    {
        if (_developerSession is not null && !_developerSession.IsAuthenticated)
            throw new UnauthorizedAccessException("Mock context editing requires an authenticated developer session.");
        lock (_gate)
        {
            _personality = personality.Clamp();
            _relationship = relationship.Clamp();
            _runtimeState = runtimeState.Clamp();
            _hasDeveloperOverride = true;
        }
    }
}

public sealed class LocalPetContextProvider : IPetContextProvider
{
    private readonly IAgentProfileStore _profiles;
    private readonly IRuntimeContextStateProvider _runtimeState;
    private readonly IAlbumMemoryRetriever _albumMemory;
    private readonly IConversationMemoryStore _conversationMemory;

    public LocalPetContextProvider(
        IAgentProfileStore profiles,
        IRuntimeContextStateProvider runtimeState,
        IAlbumMemoryRetriever albumMemory,
        IConversationMemoryStore conversationMemory)
    {
        _profiles = profiles;
        _runtimeState = runtimeState;
        _albumMemory = albumMemory;
        _conversationMemory = conversationMemory;
    }

    public async Task<PetContextSnapshot> GetSnapshotAsync(PetContextRequest request, CancellationToken cancellationToken = default)
    {
        var petTask = _profiles.LoadPetProfileAsync(cancellationToken);
        var ownerTask = _profiles.LoadOwnerProfileAsync(cancellationToken);
        var promptTask = _profiles.LoadPetPromptAsync(cancellationToken);
        var stateTask = _runtimeState.GetStateAsync(cancellationToken);
        var memoryConfiguration = request.MemoryConfiguration ?? AgentMemoryConfiguration.Default;
        var albumTask = memoryConfiguration.UseAlbumMemory
            ? _albumMemory.SearchAsync(request.UserMessage, Math.Clamp(request.MaximumAlbumMemories, 0, 5), cancellationToken)
            : Task.FromResult<IReadOnlyList<RelevantAlbumMemory>>(Array.Empty<RelevantAlbumMemory>());
        var memoryTask = memoryConfiguration.UseLongTermMemory
            ? _conversationMemory.ReadAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<ConversationMemoryCandidate>>(Array.Empty<ConversationMemoryCandidate>());
        await Task.WhenAll(petTask, ownerTask, promptTask, stateTask, albumTask, memoryTask);
        var state = await stateTask;
        var confirmed = SelectConfirmedMemories(await memoryTask, request.UserMessage);
        return new(
            await petTask,
            await ownerTask,
            await promptTask,
            state.Personality.Clamp(),
            state.Relationship.Clamp(),
            state.RuntimeState.Clamp(),
            await albumTask,
            confirmed);
    }

    private static IReadOnlyList<string> SelectConfirmedMemories(
        IReadOnlyList<ConversationMemoryCandidate> memories,
        string query)
    {
        var terms = AlbumMarkdownMemoryRetriever.Tokenize(query).ToArray();
        return memories.Where(x => x.Status == ConversationMemoryStatus.Confirmed)
            .Select(x => new { x.Content, Score = terms.Count(term => x.Content.Contains(term, StringComparison.OrdinalIgnoreCase)) })
            .Where(x => terms.Length == 0 || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .Select(x => x.Content)
            .ToArray();
    }
}

public sealed class AlbumMarkdownMemoryRetriever : IAlbumMemoryRetriever
{
    private const int MaximumFiles = 500;
    private const long MaximumMarkdownBytes = 512 * 1024;
    private readonly Func<string?> _albumRoot;

    public AlbumMarkdownMemoryRetriever(Func<string?> albumRoot) => _albumRoot = albumRoot;

    public Task<IReadOnlyList<RelevantAlbumMemory>> SearchAsync(string query, int maximumResults, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<RelevantAlbumMemory>>(() => Search(query, maximumResults, cancellationToken), cancellationToken);

    private IReadOnlyList<RelevantAlbumMemory> Search(string query, int maximumResults, CancellationToken cancellationToken)
    {
        var root = _albumRoot();
        if (maximumResults <= 0 || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<RelevantAlbumMemory>();
        var terms = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (terms.Length == 0)
            return Array.Empty<RelevantAlbumMemory>();

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Take(MaximumFiles).ToArray(); }
        catch (IOException) { return Array.Empty<RelevantAlbumMemory>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<RelevantAlbumMemory>(); }

        var matches = new List<RelevantAlbumMemory>();
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = TryRead(path);
            if (document is null)
                continue;
            var score = Score(document, terms);
            if (score <= 0)
                continue;
            matches.Add(new(
                StableId(path),
                document.Title,
                document.Date,
                MatchedExcerpt(document.Body, terms),
                document.Media,
                path,
                score));
        }
        return matches.OrderByDescending(x => x.RelevanceScore)
            .ThenByDescending(x => x.Date, StringComparer.Ordinal)
            .Take(Math.Clamp(maximumResults, 0, 5))
            .ToArray();
    }

    internal static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        var buffer = new StringBuilder();
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || IsCjk(character))
                buffer.Append(character);
            else
            {
                foreach (var token in Expand(buffer.ToString())) yield return token;
                buffer.Clear();
            }
        }
        foreach (var token in Expand(buffer.ToString())) yield return token;
    }

    private static IEnumerable<string> Expand(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;
        if (value.Any(IsCjk))
        {
            if (value.Length >= 2) yield return value;
            for (var index = 0; index < value.Length - 1; index++)
                yield return value.Substring(index, 2);
        }
        else if (value.Length >= 2)
        {
            yield return value;
        }
    }

    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9fff';

    private static AlbumDocument? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumMarkdownBytes)
                return null;
            var lines = File.ReadAllLines(path);
            var title = Path.GetFileNameWithoutExtension(path);
            var date = "";
            var media = new List<string>();
            var body = new StringBuilder();
            var inMedia = false;
            var inBody = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                    title = Unquote(line[6..]);
                else if (line.StartsWith("time:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = Unquote(line[(line.IndexOf(':') + 1)..]);
                    date = value.Length >= 10 ? value[..10] : value;
                }
                else if (line.Equals("media:", StringComparison.OrdinalIgnoreCase))
                    inMedia = true;
                else if (inMedia && line.StartsWith("- ", StringComparison.Ordinal))
                    media.Add(Unquote(line[2..]).Trim('`'));
                else if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    inBody = line.Equals("## 正文", StringComparison.OrdinalIgnoreCase);
                    inMedia = false;
                }
                else if (inBody && !string.IsNullOrWhiteSpace(line))
                    body.AppendLine(line);
            }
            if (body.Length == 0)
            {
                foreach (var line in lines.Where(x => !x.TrimStart().StartsWith("---", StringComparison.Ordinal)))
                    body.AppendLine(line);
            }
            return new(title, date, body.ToString().Trim(), media.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static double Score(AlbumDocument document, IReadOnlyList<string> terms)
    {
        double score = 0;
        foreach (var term in terms)
        {
            if (document.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 5;
            if (document.Date.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 6;
            if (document.Body.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 2;
            if (document.Media.Any(x => x.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 1;
        }
        return score;
    }

    private static string MatchedExcerpt(string body, IReadOnlyList<string> terms)
    {
        var normalized = string.Join(" ", body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= 320)
            return normalized;
        var index = terms.Select(x => normalized.IndexOf(x, StringComparison.OrdinalIgnoreCase)).Where(x => x >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, index - 80);
        var length = Math.Min(320, normalized.Length - start);
        return (start > 0 ? "..." : "") + normalized.Substring(start, length) + (start + length < normalized.Length ? "..." : "");
    }

    private static string StableId(string path)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string Unquote(string value) => value.Trim().Trim('"', '\'');
    private sealed record AlbumDocument(string Title, string Date, string Body, IReadOnlyList<string> Media);
}
