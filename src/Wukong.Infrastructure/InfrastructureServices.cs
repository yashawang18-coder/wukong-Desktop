using System.Text.Json;
using System.Text.RegularExpressions;
using Wukong.Application;
using Wukong.Contracts;
using Wukong.Domain;

namespace Wukong.Infrastructure;

public sealed class RuntimeRegistryAssetCatalog : IRuntimeAssetCatalog
{
    private readonly RuntimeAssetRegistry _registry;

    public RuntimeRegistryAssetCatalog(RuntimeAssetRegistry registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public bool IsProduction => _registry.IsProduction;

    public AnimationLifecycle? FindLifecycle(string behaviorId, RuntimeMode runtimeMode)
    {
        if (_registry.IsProduction && runtimeMode != RuntimeMode.Production)
            return null;
        if (!_registry.IsProduction && runtimeMode == RuntimeMode.Production)
            return null;

        return _registry.Bindings
            .FirstOrDefault(x =>
                string.Equals(x.BehaviorId, behaviorId, StringComparison.OrdinalIgnoreCase) &&
                x.RuntimeApproved &&
                x.RuntimeUse)
            ?.Lifecycle;
    }
}

public sealed class NoopAnimationPlayer : IAnimationPlayer
{
    public Task PlayPhaseAsync(string behaviorId, AnimationPhase phase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class FakeModelClient : IModelClient
{
    public Task<ModelResponse> SendAsync(string ownerMessage, CancellationToken cancellationToken = default)
    {
        var intent = ownerMessage.Contains("touch", StringComparison.OrdinalIgnoreCase) ||
                     ownerMessage.Contains("摸", StringComparison.Ordinal)
            ? new SemanticIntent(SemanticIntentKind.Touch, "wk.interaction.prone_touch", 0.9)
            : new SemanticIntent(SemanticIntentKind.None, Confidence: 0.0);

        var memory = string.IsNullOrWhiteSpace(ownerMessage)
            ? null
            : new MemoryCandidate(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "fake_model",
                SensitiveDataRedactor.Redact(ownerMessage),
                0.1,
                ProductionEligible: false);

        return Task.FromResult(new ModelResponse("ok", intent, memory));
    }
}

public static class SensitiveDataRedactor
{
    private static readonly Regex CredentialPattern = new(
        "(authorization\\s*[:=]\\s*)(bearer\\s+)?[^\\s,;]+|(api[-_ ]?key|token|secret|password)\\s*[:=]\\s*[^\\s,;]+|sk-[A-Za-z0-9_-]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WindowsPathPattern = new(
        "[A-Za-z]:\\\\(?:[^\\\\\\s\"']+\\\\)*[^\\\\\\s\"']*",
        RegexOptions.Compiled);

    private static readonly Regex UserHomePattern = new(
        "\\\\Users\\\\[^\\\\\\s\"']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var redacted = CredentialPattern.Replace(value, match =>
            match.Value.StartsWith("authorization", StringComparison.OrdinalIgnoreCase)
                ? "Authorization: [redacted]"
                : "[redacted]");

        redacted = WindowsPathPattern.Replace(redacted, "[path]");
        redacted = UserHomePattern.Replace(redacted, "\\Users\\[user]");

        var userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
            redacted = redacted.Replace(userName, "[user]", StringComparison.OrdinalIgnoreCase);

        return redacted;
    }

    public static string RedactPayload(object? payload)
    {
        if (payload is null)
            return string.Empty;

        var text = payload is string value
            ? value
            : JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        return Redact(text);
    }
}

public sealed class RollingFileLogStore
{
    public const long DefaultTotalBytesLimit = 50L * 1024 * 1024;
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

    private readonly DirectoryInfo _root;
    private readonly TimeSpan _retention;
    private readonly long _totalBytesLimit;

    public RollingFileLogStore(string rootDirectory, TimeSpan? retention = null, long totalBytesLimit = DefaultTotalBytesLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = new DirectoryInfo(rootDirectory);
        _retention = retention ?? DefaultRetention;
        _totalBytesLimit = totalBytesLimit;
    }

    public static RollingFileLogStore CreateDefault()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = string.IsNullOrWhiteSpace(local)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : Path.Combine(local, "Wukong", "logs");
        return new RollingFileLogStore(root);
    }

    public void Append(RuntimeMode runtimeMode, string category, object? payload, DateTimeOffset? at = null)
    {
        try
        {
            var timestamp = at ?? DateTimeOffset.UtcNow;
            var modeName = runtimeMode.ToString().ToLowerInvariant();
            var modeDirectory = Directory.CreateDirectory(Path.Combine(_root.FullName, modeName));
            var safeCategory = Regex.Replace(category, "[^A-Za-z0-9_.-]", "_");
            var path = Path.Combine(modeDirectory.FullName, $"{timestamp:yyyyMMdd}.log");
            var line = JsonSerializer.Serialize(new
            {
                at = timestamp,
                mode = runtimeMode.ToString(),
                category = safeCategory,
                payload = SensitiveDataRedactor.RedactPayload(payload)
            });

            File.AppendAllText(path, line + Environment.NewLine);
            EnforceRetention(timestamp);
            EnforceTotalBytesLimit();
        }
        catch
        {
            // Logging must never block the desktop pet runtime path.
        }
    }

    public IReadOnlyList<FileInfo> GetLogFiles() =>
        _root.Exists
            ? _root.EnumerateFiles("*.log", SearchOption.AllDirectories)
                .OrderBy(GetLogDateUtc)
                .ThenBy(x => x.LastWriteTimeUtc)
                .ToList()
            : Array.Empty<FileInfo>();

    private void EnforceRetention(DateTimeOffset now)
    {
        if (!_root.Exists)
            return;

        var cutoff = now - _retention;
        foreach (var file in GetLogFiles().Where(x => GetLogDateUtc(x) < cutoff.UtcDateTime))
            TryDelete(file);
    }

    private void EnforceTotalBytesLimit()
    {
        var files = GetLogFiles().ToList();
        foreach (var file in files)
            file.Refresh();

        var total = files.Sum(x => x.Length);
        foreach (var file in files)
        {
            if (total <= _totalBytesLimit)
                break;
            total -= file.Length;
            TryDelete(file);
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
        }
    }

    private static DateTime GetLogDateUtc(FileInfo file) =>
        DateTime.TryParseExact(
            Path.GetFileNameWithoutExtension(file.Name),
            "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var date)
            ? date
            : file.LastWriteTimeUtc;
}
