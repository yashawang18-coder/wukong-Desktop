using System.Net;
using System.Text;
using Wukong.Application;
using Wukong.Domain;
using Wukong.Infrastructure;

var tests = new (string Name, Func<Task> Run)[]
{
    ("fake model respects boundary", FakeModelRespectsBoundary),
    ("log redacts secrets", LogRedactsSecrets),
    ("file log rolls by retention and total bytes", FileLogRolls),
    ("file log failures do not throw", FileLogFailuresDoNotThrow),
    ("openai chat request format is correct", OpenAiRequestFormatIsCorrect),
    ("anthropic messages request format is correct", AnthropicRequestFormatIsCorrect),
    ("gemini generate content request format is correct", GeminiRequestFormatIsCorrect),
    ("ollama chat request format is correct", OllamaRequestFormatIsCorrect),
    ("provider switching keeps credentials isolated", ProviderCredentialsStayIsolated),
    ("provider status errors map safely", ProviderStatusErrorsMapSafely),
    ("provider timeout network and empty response map safely", ProviderTransportFailuresMapSafely),
    ("album markdown retrieval is relevant and bounded", AlbumMarkdownRetrievalWorks),
    ("missing and damaged album markdown degrades safely", AlbumMarkdownFailuresAreSafe),
    ("mock context editing requires developer session", MockContextRequiresDeveloperSession)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"[PASS] {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"[FAIL] {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

static async Task FakeModelRespectsBoundary()
{
    var response = await new FakeModelClient().SendAsync("摸摸悟空 sk-testSecret123456");
    Assert(response.RespectsModelBoundary, "fake model tried to force behavior or asset path");
    Assert(response.Intent?.Kind == SemanticIntentKind.Touch, "fake model did not return semantic touch intent");
    Assert(response.MemoryCandidate?.Summary.Contains("sk-test", StringComparison.OrdinalIgnoreCase) == false, "memory candidate leaked secret");
}

static Task LogRedactsSecrets()
{
    var secretText = "Authorization: Bearer abcdef token=secret123 API key=top sk-secret1234567890 C:\\Users\\alice\\AppData\\file.txt";
    var redacted = SensitiveDataRedactor.Redact(secretText);
    Assert(redacted.Contains("[redacted]", StringComparison.Ordinal), "log did not redact credential-like text");
    Assert(!redacted.Contains("abcdef", StringComparison.Ordinal), "authorization leaked");
    Assert(!redacted.Contains("secret123", StringComparison.Ordinal), "token leaked");
    Assert(!redacted.Contains("C:\\", StringComparison.Ordinal), "absolute path leaked");
    Assert(!redacted.Contains("alice", StringComparison.OrdinalIgnoreCase), "username leaked");
    Assert(RollingFileLogStore.DefaultRetention == TimeSpan.FromDays(30), "retention changed");
    Assert(RollingFileLogStore.DefaultTotalBytesLimit == 50L * 1024 * 1024, "size limit changed");
    return Task.CompletedTask;
}

static Task FileLogRolls()
{
    var root = TestLogRoot();
    var sizeRoot = TestLogRoot();
    try
    {
        var log = new RollingFileLogStore(root, TimeSpan.FromDays(30), totalBytesLimit: 4096);
        log.Append(RuntimeMode.Production, "event", new { message = "old", token = "token-secret" }, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        log.Append(RuntimeMode.Preview, "trace", new { message = "preview", path = "C:\\Users\\alice\\secret.txt" }, new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero));
        log.Append(RuntimeMode.Simulation, "trace", new { message = new string('x', 220) }, new DateTimeOffset(2026, 8, 10, 0, 1, 0, TimeSpan.Zero));
        log.Append(RuntimeMode.DeveloperForced, "trace", new { message = new string('y', 220) }, new DateTimeOffset(2026, 8, 10, 0, 2, 0, TimeSpan.Zero));

        var files = log.GetLogFiles();
        Assert(files.All(x => !x.FullName.Contains("20260101", StringComparison.Ordinal)), "retention did not remove oldest file");
        Assert(files.Any(x => x.FullName.Contains("preview", StringComparison.OrdinalIgnoreCase)) ||
               files.Any(x => x.FullName.Contains("simulation", StringComparison.OrdinalIgnoreCase)) ||
               files.Any(x => x.FullName.Contains("developerforced", StringComparison.OrdinalIgnoreCase)),
            "isolated runtime logs were not separated by mode");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file.FullName);
            Assert(!text.Contains("C:\\", StringComparison.Ordinal), "file log leaked path");
            Assert(!text.Contains("token-secret", StringComparison.Ordinal), "file log leaked token");
        }

        var sizeLog = new RollingFileLogStore(sizeRoot, TimeSpan.FromDays(30), totalBytesLimit: 520);
        sizeLog.Append(RuntimeMode.Production, "first", new { message = new string('a', 220) }, new DateTimeOffset(2026, 8, 10, 1, 0, 0, TimeSpan.Zero));
        sizeLog.Append(RuntimeMode.Production, "second", new { message = new string('b', 220) }, new DateTimeOffset(2026, 8, 11, 1, 1, 0, TimeSpan.Zero));
        var sizeFiles = sizeLog.GetLogFiles();
        foreach (var file in sizeFiles)
            file.Refresh();
        var totalSize = sizeFiles.Sum(x => x.Length);
        Assert(totalSize <= 520, $"total byte limit not enforced: {totalSize} across {string.Join(", ", sizeFiles.Select(x => x.Name + ":" + x.Length))}");
        Assert(sizeFiles.All(x => !x.FullName.Contains("20260810", StringComparison.Ordinal)), "total byte cleanup did not delete oldest file");
    }
    finally
    {
        TryDeleteDirectory(root);
        TryDeleteDirectory(sizeRoot);
    }
    return Task.CompletedTask;
}

static Task FileLogFailuresDoNotThrow()
{
    var root = TestLogRoot();
    Directory.CreateDirectory(Path.GetDirectoryName(root)!);
    File.WriteAllText(root, "not a directory");
    try
    {
        var log = new RollingFileLogStore(root);
        log.Append(RuntimeMode.Production, "event", new { apiKey = "sk-secret123456" });
    }
    finally
    {
        TryDeleteFile(root);
    }
    return Task.CompletedTask;
}

static async Task OpenAiRequestFormatIsCorrect()
{
    var handler = FakeHttpHandler.Json(HttpStatusCode.OK, "{\"id\":\"r1\",\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}");
    var provider = new OpenAiChatModelProvider(new HttpClient(handler));
    var response = await provider.SendAsync(Connection(ChatProviderType.OpenAI, "https://api.openai.com/v1", "gpt-test", "openai-secret"), Request());
    Assert(response.Text == "ok", "openai response was not parsed");
    Assert(handler.Last!.Uri.EndsWith("/v1/chat/completions", StringComparison.Ordinal), "openai endpoint wrong");
    Assert(handler.Last.Headers.TryGetValue("Authorization", out var auth) && auth == "Bearer openai-secret", "openai authorization wrong");
    Assert(handler.Last.Body.Contains("\"messages\"", StringComparison.Ordinal) && handler.Last.Body.Contains("\"model\":\"gpt-test\"", StringComparison.Ordinal), "openai payload wrong");
}

static async Task AnthropicRequestFormatIsCorrect()
{
    var handler = FakeHttpHandler.Json(HttpStatusCode.OK, "{\"id\":\"a1\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"stop_reason\":\"end_turn\"}");
    var provider = new AnthropicChatModelProvider(new HttpClient(handler));
    await provider.SendAsync(Connection(ChatProviderType.Anthropic, "https://api.anthropic.com", "claude-test", "anthropic-secret"), Request());
    Assert(handler.Last!.Uri.EndsWith("/v1/messages", StringComparison.Ordinal), "anthropic endpoint wrong");
    Assert(handler.Last.Headers["x-api-key"] == "anthropic-secret", "anthropic key header wrong");
    Assert(handler.Last.Headers.ContainsKey("anthropic-version"), "anthropic version missing");
    Assert(handler.Last.Body.Contains("\"system\"", StringComparison.Ordinal) && handler.Last.Body.Contains("\"max_tokens\":800", StringComparison.Ordinal), "anthropic payload wrong");
}

static async Task GeminiRequestFormatIsCorrect()
{
    var handler = FakeHttpHandler.Json(HttpStatusCode.OK, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]},\"finishReason\":\"STOP\"}]}");
    var provider = new GeminiChatModelProvider(new HttpClient(handler));
    await provider.SendAsync(Connection(ChatProviderType.Gemini, "https://generativelanguage.googleapis.com", "gemini-test", "gemini-secret"), Request());
    Assert(handler.Last!.Uri.Contains("/v1beta/models/gemini-test:generateContent", StringComparison.Ordinal), "gemini endpoint wrong");
    Assert(handler.Last.Headers["x-goog-api-key"] == "gemini-secret", "gemini key header wrong");
    Assert(handler.Last.Body.Contains("\"systemInstruction\"", StringComparison.Ordinal) && handler.Last.Body.Contains("\"role\":\"model\"", StringComparison.Ordinal), "gemini payload wrong");
}

static async Task OllamaRequestFormatIsCorrect()
{
    var handler = FakeHttpHandler.Json(HttpStatusCode.OK, "{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"done\":true,\"done_reason\":\"stop\"}");
    var provider = new OllamaChatModelProvider(new HttpClient(handler));
    await provider.SendAsync(Connection(ChatProviderType.Ollama, "http://127.0.0.1:11434", "qwen-test", null), Request());
    Assert(handler.Last!.Uri.EndsWith("/api/chat", StringComparison.Ordinal), "ollama endpoint wrong");
    Assert(handler.Last.Body.Contains("\"stream\":false", StringComparison.Ordinal) && handler.Last.Body.Contains("\"options\"", StringComparison.Ordinal), "ollama payload wrong");
    Assert(!handler.Last.Headers.ContainsKey("Authorization"), "ollama received an unrelated key");
}

static async Task ProviderCredentialsStayIsolated()
{
    var repository = new InMemoryChatProviderConfigurationRepository();
    var secrets = new InMemoryAgentSecretStore();
    var openAi = new CapturingProvider(ChatProviderType.OpenAI);
    var anthropic = new CapturingProvider(ChatProviderType.Anthropic);
    var runtime = new ConfiguredChatModelRuntime(repository, secrets, new IChatModelProvider[] { openAi, anthropic });
    await runtime.SaveConfigurationAsync(ChatProviderConfiguration.Default(ChatProviderType.OpenAI), "openai-key");
    await runtime.SaveConfigurationAsync(ChatProviderConfiguration.Default(ChatProviderType.Anthropic), "anthropic-key");
    await runtime.SetActiveProviderAsync(ChatProviderType.OpenAI);
    await runtime.SendAsync(Request());
    await runtime.SetActiveProviderAsync(ChatProviderType.Anthropic);
    await runtime.SendAsync(Request());
    Assert(openAi.LastKey == "openai-key", "openai used another provider key");
    Assert(anthropic.LastKey == "anthropic-key", "anthropic used another provider key");
}

static async Task ProviderStatusErrorsMapSafely()
{
    var expected = new[]
    {
        (HttpStatusCode.Unauthorized, ChatFailureKind.Authentication),
        (HttpStatusCode.Forbidden, ChatFailureKind.Forbidden),
        (HttpStatusCode.NotFound, ChatFailureKind.NotFound),
        (HttpStatusCode.TooManyRequests, ChatFailureKind.RateLimited),
        (HttpStatusCode.InternalServerError, ChatFailureKind.Server)
    };
    foreach (var (status, kind) in expected)
    {
        var provider = new OpenAiChatModelProvider(new HttpClient(FakeHttpHandler.Json(status, "{\"error\":\"redacted\"}")));
        var actual = await CaptureFailure(() => provider.SendAsync(Connection(ChatProviderType.OpenAI, "https://example.test/v1", "model", "key"), Request()));
        Assert(actual == kind, $"status {(int)status} mapped to {actual}");
    }
}

static async Task ProviderTransportFailuresMapSafely()
{
    var timeout = new OpenAiChatModelProvider(new HttpClient(FakeHttpHandler.Throw(new TaskCanceledException())));
    Assert(await CaptureFailure(() => timeout.SendAsync(Connection(ChatProviderType.OpenAI, "https://example.test/v1", "model", "key"), Request())) == ChatFailureKind.Timeout, "timeout was not mapped");
    var network = new OpenAiChatModelProvider(new HttpClient(FakeHttpHandler.Throw(new HttpRequestException("private network detail"))));
    Assert(await CaptureFailure(() => network.SendAsync(Connection(ChatProviderType.OpenAI, "https://example.test/v1", "model", "key"), Request())) == ChatFailureKind.Network, "network error was not mapped");
    var empty = new OpenAiChatModelProvider(new HttpClient(FakeHttpHandler.Json(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"\"}}]}")));
    Assert(await CaptureFailure(() => empty.SendAsync(Connection(ChatProviderType.OpenAI, "https://example.test/v1", "model", "key"), Request())) == ChatFailureKind.EmptyResponse, "empty response was not mapped");
}

static async Task AlbumMarkdownRetrievalWorks()
{
    var root = TestAgentRoot();
    try
    {
        var album = Path.Combine(root, "home-day");
        Directory.CreateDirectory(album);
        await File.WriteAllTextAsync(Path.Combine(album, "home.md"), "---\ntitle: \"第一次回家\"\ntime: \"2025-12-13\"\nmedia:\n  - \"car.webp\"\n---\n## 正文\n第一次坐车回南京，头晕晕。", Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(album, "other.md"), "---\ntitle: \"无关记录\"\ntime: \"2026-01-01\"\n---\n## 正文\n今天在家睡觉。", Encoding.UTF8);
        var retriever = new AlbumMarkdownMemoryRetriever(() => root);
        var result = await retriever.SearchAsync("第一次坐车回南京", 3);
        Assert(result.Count == 1, "retrieval returned unrelated markdown");
        Assert(result[0].AlbumTitle == "第一次回家" && result[0].Date == "2025-12-13", "retrieval metadata wrong");
        Assert(result[0].MediaReferences.SequenceEqual(new[] { "car.webp" }), "media references missing");
        Assert(result[0].RelevanceScore > 0, "relevance score missing");
    }
    finally { TryDeleteDirectory(root); }
}

static async Task AlbumMarkdownFailuresAreSafe()
{
    var missing = new AlbumMarkdownMemoryRetriever(() => Path.Combine(TestAgentRoot(), "missing"));
    Assert((await missing.SearchAsync("悟空", 5)).Count == 0, "missing album root did not degrade to empty");
    var root = TestAgentRoot();
    try
    {
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, "broken.md"), new byte[] { 0xff, 0xfe, 0x00 });
        var retriever = new AlbumMarkdownMemoryRetriever(() => root);
        _ = await retriever.SearchAsync("悟空", 5);
    }
    finally { TryDeleteDirectory(root); }
}

static Task MockContextRequiresDeveloperSession()
{
    var session = new DeveloperSession();
    var mock = new MockRuntimeContextStateProvider(session);
    var blocked = false;
    try { mock.Update(PersonalitySnapshot.Default, RelationshipSnapshot.Default, PetRuntimeStateSnapshot.Default); }
    catch (UnauthorizedAccessException) { blocked = true; }
    Assert(blocked, "mock state changed without developer authentication");
    session.Authenticate("0714");
    mock.Update(PersonalitySnapshot.Default with { Affection = 0.2 }, RelationshipSnapshot.Default, PetRuntimeStateSnapshot.Default);
    Assert(Math.Abs(mock.GetStateAsync().Result.Personality.Affection - 0.2) < 0.001, "authenticated mock update failed");
    return Task.CompletedTask;
}

static ChatProviderConnection Connection(ChatProviderType provider, string baseUrl, string model, string? key) =>
    new(new(provider, baseUrl, model, 10, 0.7, !string.IsNullOrWhiteSpace(key)), key);

static ChatModelRequest Request() => new(new[]
{
    new AgentChatMessage(AgentChatRole.System, "system", DateTimeOffset.UtcNow),
    new AgentChatMessage(AgentChatRole.User, "hello", DateTimeOffset.UtcNow),
    new AgentChatMessage(AgentChatRole.Assistant, "hi", DateTimeOffset.UtcNow)
}, 0.5);

static async Task<ChatFailureKind> CaptureFailure(Func<Task<ChatModelResponse>> action)
{
    try { await action(); }
    catch (ChatProviderException ex) { return ex.Kind; }
    throw new InvalidOperationException("expected provider failure");
}

static string TestAgentRoot() => Path.Combine(Path.GetTempPath(), "wukong-agent-tests", Guid.NewGuid().ToString("N"));

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string TestLogRoot() =>
    Path.Combine(Directory.GetCurrentDirectory(), ".wukong-log-tests", Guid.NewGuid().ToString("N"));

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    catch
    {
    }
}

static void TryDeleteFile(string path)
{
    try
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    catch
    {
    }
}

sealed record RequestSnapshot(string Uri, IReadOnlyDictionary<string, string> Headers, string Body);

sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
    private FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
    public RequestSnapshot? Last { get; private set; }

    public static FakeHttpHandler Json(HttpStatusCode status, string json) => new((_, _) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }));

    public static FakeHttpHandler Throw(Exception exception) => new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(x => x.Key, x => string.Join(",", x.Value), StringComparer.OrdinalIgnoreCase);
        Last = new(request.RequestUri?.ToString() ?? "", headers, body);
        return await _send(request, cancellationToken);
    }
}

sealed class CapturingProvider : IChatModelProvider
{
    public CapturingProvider(ChatProviderType providerType) => ProviderType = providerType;
    public ChatProviderType ProviderType { get; }
    public ChatProviderCapabilities Capabilities { get; } = new(true, true, true, false);
    public string? LastKey { get; private set; }
    public Task<ChatModelResponse> SendAsync(ChatProviderConnection connection, ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        LastKey = connection.ApiKey;
        return Task.FromResult(new ChatModelResponse("ok", "capture"));
    }
}
