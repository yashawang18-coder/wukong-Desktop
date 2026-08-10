using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wukong.Application;

namespace Wukong.Infrastructure;

public abstract class HttpChatModelProvider : IChatModelProvider
{
    protected HttpChatModelProvider(HttpClient httpClient) => HttpClient = httpClient;
    protected HttpClient HttpClient { get; }
    public abstract ChatProviderType ProviderType { get; }
    public abstract ChatProviderCapabilities Capabilities { get; }
    public abstract Task<ChatModelResponse> SendAsync(ChatProviderConnection connection, ChatModelRequest request, CancellationToken cancellationToken = default);

    protected async Task<string> SendJsonAsync(
        HttpRequestMessage request,
        object payload,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 300)));
        try
        {
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw MapStatus(response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (body.Length > 4_000_000)
                throw new ChatProviderException(ChatFailureKind.EmptyResponse, "模型返回内容过大，已停止读取。", "response_too_large");
            return body;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatProviderException(ChatFailureKind.Timeout, "模型请求超时，请检查网络或增大超时时间。", "timeout");
        }
        catch (HttpRequestException ex)
        {
            throw new ChatProviderException(ChatFailureKind.Network, "无法连接模型服务，请检查地址和网络。", "network", ex);
        }
    }

    protected static ChatProviderException MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new(ChatFailureKind.Authentication, "API Key 无效或已失效。", "http_401"),
        HttpStatusCode.Forbidden => new(ChatFailureKind.Forbidden, "当前凭证无权访问该模型。", "http_403"),
        HttpStatusCode.NotFound => new(ChatFailureKind.NotFound, "接口地址或模型名称不存在。", "http_404"),
        HttpStatusCode.TooManyRequests => new(ChatFailureKind.RateLimited, "模型服务请求过多，请稍后重试。", "http_429"),
        >= HttpStatusCode.InternalServerError => new(ChatFailureKind.Server, "模型服务暂时不可用，请稍后重试。", $"http_{(int)status}"),
        _ => new(ChatFailureKind.Unknown, "模型服务拒绝了请求，请检查配置。", $"http_{(int)status}")
    };

    protected static string RequireText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ChatProviderException(ChatFailureKind.EmptyResponse, "模型没有返回内容，请稍后重试。", "empty_response");
        return text.Trim();
    }

    protected static string Role(AgentChatRole role) => role switch
    {
        AgentChatRole.System => "system",
        AgentChatRole.Assistant => "assistant",
        _ => "user"
    };
}

public sealed class OpenAiChatModelProvider : HttpChatModelProvider
{
    private readonly ChatProviderType _providerType;

    public OpenAiChatModelProvider(HttpClient httpClient, ChatProviderType providerType = ChatProviderType.OpenAI)
        : base(httpClient)
    {
        if (providerType is not (ChatProviderType.OpenAI or ChatProviderType.OpenAICompatible))
            throw new ArgumentOutOfRangeException(nameof(providerType));
        _providerType = providerType;
    }

    public override ChatProviderType ProviderType => _providerType;
    public override ChatProviderCapabilities Capabilities { get; } = new(true, true, true, false);

    public override async Task<ChatModelResponse> SendAsync(ChatProviderConnection connection, ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        var config = connection.Configuration.Normalize();
        using var http = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl + "/chat/completions");
        http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        var payload = new
        {
            model = config.Model,
            messages = request.Messages.Select(x => new { role = Role(x.Role), content = x.Content }).ToArray(),
            temperature = request.Temperature,
            stream = false
        };
        var json = await SendJsonAsync(http, payload, config.TimeoutSeconds, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var choice = root.GetProperty("choices")[0];
        var text = choice.GetProperty("message").GetProperty("content").GetString();
        return new(RequireText(text), root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            choice.TryGetProperty("finish_reason", out var finish) ? finish.GetString() : null);
    }
}

public sealed class AnthropicChatModelProvider : HttpChatModelProvider
{
    public AnthropicChatModelProvider(HttpClient httpClient) : base(httpClient) { }
    public override ChatProviderType ProviderType => ChatProviderType.Anthropic;
    public override ChatProviderCapabilities Capabilities { get; } = new(true, true, true, false);

    public override async Task<ChatModelResponse> SendAsync(ChatProviderConnection connection, ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        var config = connection.Configuration.Normalize();
        using var http = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl + "/v1/messages");
        http.Headers.TryAddWithoutValidation("x-api-key", connection.ApiKey);
        http.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        var system = string.Join("\n\n", request.Messages.Where(x => x.Role == AgentChatRole.System).Select(x => x.Content));
        var payload = new
        {
            model = config.Model,
            max_tokens = 800,
            temperature = request.Temperature,
            system,
            messages = request.Messages.Where(x => x.Role != AgentChatRole.System)
                .Select(x => new { role = x.Role == AgentChatRole.Assistant ? "assistant" : "user", content = x.Content }).ToArray()
        };
        var json = await SendJsonAsync(http, payload, config.TimeoutSeconds, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = root.GetProperty("content").EnumerateArray()
            .Where(x => x.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Select(x => x.GetProperty("text").GetString()).FirstOrDefault();
        return new(RequireText(text), root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            root.TryGetProperty("stop_reason", out var stop) ? stop.GetString() : null);
    }
}

public sealed class GeminiChatModelProvider : HttpChatModelProvider
{
    public GeminiChatModelProvider(HttpClient httpClient) : base(httpClient) { }
    public override ChatProviderType ProviderType => ChatProviderType.Gemini;
    public override ChatProviderCapabilities Capabilities { get; } = new(true, true, true, false);

    public override async Task<ChatModelResponse> SendAsync(ChatProviderConnection connection, ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        var config = connection.Configuration.Normalize();
        var model = Uri.EscapeDataString(config.Model);
        using var http = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl + $"/v1beta/models/{model}:generateContent");
        http.Headers.TryAddWithoutValidation("x-goog-api-key", connection.ApiKey);
        var system = string.Join("\n\n", request.Messages.Where(x => x.Role == AgentChatRole.System).Select(x => x.Content));
        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = request.Messages.Where(x => x.Role != AgentChatRole.System)
                .Select(x => new { role = x.Role == AgentChatRole.Assistant ? "model" : "user", parts = new[] { new { text = x.Content } } }).ToArray(),
            generationConfig = new { temperature = request.Temperature }
        };
        var json = await SendJsonAsync(http, payload, config.TimeoutSeconds, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var candidate = document.RootElement.GetProperty("candidates")[0];
        var text = candidate.GetProperty("content").GetProperty("parts").EnumerateArray()
            .Select(x => x.TryGetProperty("text", out var value) ? value.GetString() : null)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return new(RequireText(text), "", candidate.TryGetProperty("finishReason", out var finish) ? finish.GetString() : null);
    }
}

public sealed class OllamaChatModelProvider : HttpChatModelProvider
{
    public OllamaChatModelProvider(HttpClient httpClient) : base(httpClient) { }
    public override ChatProviderType ProviderType => ChatProviderType.Ollama;
    public override ChatProviderCapabilities Capabilities { get; } = new(false, true, true, true);

    public override async Task<ChatModelResponse> SendAsync(ChatProviderConnection connection, ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        var config = connection.Configuration.Normalize();
        using var http = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl + "/api/chat");
        var payload = new
        {
            model = config.Model,
            messages = request.Messages.Select(x => new { role = Role(x.Role), content = x.Content }).ToArray(),
            stream = false,
            options = new { temperature = request.Temperature }
        };
        var json = await SendJsonAsync(http, payload, config.TimeoutSeconds, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = root.GetProperty("message").GetProperty("content").GetString();
        return new(RequireText(text), "", root.TryGetProperty("done_reason", out var reason) ? reason.GetString() : null);
    }
}

public sealed record ChatProviderSettingsState(
    ChatProviderType ActiveProvider,
    IReadOnlyDictionary<ChatProviderType, ChatProviderConfiguration> Configurations);

public interface IChatProviderConfigurationRepository
{
    Task<ChatProviderSettingsState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ChatProviderSettingsState state, CancellationToken cancellationToken = default);
}

public interface IAgentSecretStore
{
    Task<string?> ReadAsync(ChatProviderType provider, CancellationToken cancellationToken = default);
    Task WriteAsync(ChatProviderType provider, string secret, CancellationToken cancellationToken = default);
    Task DeleteAsync(ChatProviderType provider, CancellationToken cancellationToken = default);
}

public sealed class ConfiguredChatModelRuntime : IChatModelRuntime
{
    private readonly IChatProviderConfigurationRepository _configurationRepository;
    private readonly IAgentSecretStore _secretStore;
    private readonly IReadOnlyDictionary<ChatProviderType, IChatModelProvider> _providers;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConfiguredChatModelRuntime(
        IChatProviderConfigurationRepository configurationRepository,
        IAgentSecretStore secretStore,
        IEnumerable<IChatModelProvider> providers)
    {
        _configurationRepository = configurationRepository;
        _secretStore = secretStore;
        _providers = providers.ToDictionary(x => x.ProviderType);
    }

    public async Task<ChatProviderConfiguration> GetActiveConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadNormalizedAsync(cancellationToken);
        return state.Configurations[state.ActiveProvider];
    }

    public async Task<IReadOnlyList<ChatProviderConfiguration>> GetConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadNormalizedAsync(cancellationToken);
        return Enum.GetValues<ChatProviderType>().Select(x => state.Configurations[x]).ToArray();
    }

    public async Task SaveConfigurationAsync(ChatProviderConfiguration configuration, string? apiKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadNormalizedAsync(cancellationToken);
            var existing = state.Configurations[configuration.Provider];
            var requiresKey = configuration.Provider != ChatProviderType.Ollama;
            if (requiresKey && !string.IsNullOrWhiteSpace(apiKey))
                await _secretStore.WriteAsync(configuration.Provider, apiKey.Trim(), cancellationToken);
            var updated = configuration.Normalize() with
            {
                ApiKeyConfigured = requiresKey && (!string.IsNullOrWhiteSpace(apiKey) || existing.ApiKeyConfigured)
            };
            var all = state.Configurations.ToDictionary(x => x.Key, x => x.Value);
            all[configuration.Provider] = updated;
            await _configurationRepository.SaveAsync(new(state.ActiveProvider, all), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetActiveProviderAsync(ChatProviderType provider, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadNormalizedAsync(cancellationToken);
            await _configurationRepository.SaveAsync(state with { ActiveProvider = provider }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChatModelResponse> SendAsync(ChatModelRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(cancellationToken);
        return await _providers[connection.Configuration.Provider].SendAsync(connection, request, cancellationToken);
    }

    public async Task<ChatModelResponse> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var request = new ChatModelRequest(new[]
        {
            new AgentChatMessage(AgentChatRole.System, "Return a short acknowledgement only.", DateTimeOffset.UtcNow),
            new AgentChatMessage(AgentChatRole.User, "Connection test", DateTimeOffset.UtcNow)
        }, 0);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<ChatProviderConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var config = await GetActiveConfigurationAsync(cancellationToken);
        if (!_providers.TryGetValue(config.Provider, out var provider))
            throw new ChatProviderException(ChatFailureKind.Configuration, "当前模型服务商尚未安装。", "provider_missing");
        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ChatProviderException(ChatFailureKind.Configuration, "API 地址无效。", "invalid_base_url");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new ChatProviderException(ChatFailureKind.Configuration, "请填写模型名称。", "model_missing");
        var key = provider.Capabilities.RequiresApiKey
            ? await _secretStore.ReadAsync(config.Provider, cancellationToken)
            : null;
        if (provider.Capabilities.RequiresApiKey && string.IsNullOrWhiteSpace(key))
            throw new ChatProviderException(ChatFailureKind.Configuration, "请先保存该服务商的 API Key。", "api_key_missing");
        return new(config, key);
    }

    private async Task<ChatProviderSettingsState> LoadNormalizedAsync(CancellationToken cancellationToken)
    {
        var loaded = await _configurationRepository.LoadAsync(cancellationToken);
        var configurations = loaded.Configurations.ToDictionary(x => x.Key, x => x.Value.Normalize());
        foreach (var provider in Enum.GetValues<ChatProviderType>())
            configurations.TryAdd(provider, ChatProviderConfiguration.Default(provider));
        var active = configurations.ContainsKey(loaded.ActiveProvider) ? loaded.ActiveProvider : ChatProviderType.OpenAICompatible;
        return new(active, configurations);
    }
}

public sealed class InMemoryChatProviderConfigurationRepository : IChatProviderConfigurationRepository
{
    private ChatProviderSettingsState _state = new(
        ChatProviderType.OpenAICompatible,
        Enum.GetValues<ChatProviderType>().ToDictionary(x => x, ChatProviderConfiguration.Default));

    public Task<ChatProviderSettingsState> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_state);
    }

    public Task SaveAsync(ChatProviderSettingsState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state = new(state.ActiveProvider, state.Configurations.ToDictionary(x => x.Key, x => x.Value));
        return Task.CompletedTask;
    }
}

public sealed class InMemoryAgentSecretStore : IAgentSecretStore
{
    private readonly Dictionary<ChatProviderType, string> _secrets = new();
    public Task<string?> ReadAsync(ChatProviderType provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_secrets.TryGetValue(provider, out var value) ? value : null);
    }
    public Task WriteAsync(ChatProviderType provider, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets[provider] = secret;
        return Task.CompletedTask;
    }
    public Task DeleteAsync(ChatProviderType provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _secrets.Remove(provider);
        return Task.CompletedTask;
    }
}
