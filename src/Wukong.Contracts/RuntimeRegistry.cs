using System.Text.Json;
using System.Text.Json.Serialization;
using Wukong.Domain;

namespace Wukong.Contracts;

public sealed record RuntimeAssetRegistry(
    int SchemaVersion,
    int RegistryVersion,
    IReadOnlyList<RuntimeAssetBinding> Bindings,
    bool IsProduction);

public sealed record RuntimeAssetBinding(
    string BehaviorId,
    string AssetId,
    bool RuntimeApproved,
    bool RuntimeUse,
    AnimationLifecycle Lifecycle);

public sealed class RuntimeRegistryValidationException : InvalidOperationException
{
    public RuntimeRegistryValidationException(string message) : base(message)
    {
    }
}

public sealed class ProductionRuntimeRegistryLoader
{
    public RuntimeAssetRegistry Load(string registryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        var fullPath = Path.GetFullPath(registryPath);
        if (fullPath.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains($"{Path.DirectorySeparatorChar}Fixtures{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            throw new RuntimeRegistryValidationException("Production registry loader cannot read test fixtures.");

        var registry = RegistryDocument.Read(fullPath);
        foreach (var binding in registry.Bindings)
        {
            if (!binding.RuntimeApproved || !binding.RuntimeUse)
                throw new RuntimeRegistryValidationException(
                    $"Production binding {binding.BehaviorId} is not runtime-approved and enabled.");
            if (!binding.Lifecycle.IsComplete)
                throw new RuntimeRegistryValidationException(
                    $"Production binding {binding.BehaviorId} does not satisfy animation lifecycle requirements.");
        }

        return registry with { IsProduction = true };
    }
}

public sealed class FixtureRuntimeRegistryLoader
{
    public RuntimeAssetRegistry Load(string registryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        var fullPath = Path.GetFullPath(registryPath);
        if (!fullPath.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !fullPath.Contains($"{Path.DirectorySeparatorChar}Fixtures{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            throw new RuntimeRegistryValidationException("Fixture registry loader requires an explicit test fixture path.");

        return RegistryDocument.Read(fullPath) with { IsProduction = false };
    }
}

internal sealed class RegistryDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("registry_version")]
    public int RegistryVersion { get; init; } = 1;

    [JsonPropertyName("bindings")]
    public List<BindingDocument> Bindings { get; init; } = new();

    public static RuntimeAssetRegistry Read(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<RegistryDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
            throw new RuntimeRegistryValidationException("Runtime registry is empty or invalid.");

        return new RuntimeAssetRegistry(
            document.SchemaVersion,
            document.RegistryVersion,
            document.Bindings.Select(x => x.ToBinding()).ToList(),
            IsProduction: false);
    }
}

internal sealed class BindingDocument
{
    [JsonPropertyName("behavior_id")]
    public string BehaviorId { get; init; } = string.Empty;

    [JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = string.Empty;

    [JsonPropertyName("runtime_approved")]
    public bool RuntimeApproved { get; init; }

    [JsonPropertyName("runtime_use")]
    public bool RuntimeUse { get; init; }

    [JsonPropertyName("fallback_behavior_id")]
    public string? FallbackBehaviorId { get; init; }

    [JsonPropertyName("normal_path")]
    public List<string> NormalPath { get; init; } = new() { "Intro", "Loop", "Exit" };

    [JsonPropertyName("interrupt_path")]
    public List<string> InterruptPath { get; init; } = new() { "InterruptExit", "Fallback" };

    public RuntimeAssetBinding ToBinding()
    {
        if (string.IsNullOrWhiteSpace(BehaviorId))
            throw new RuntimeRegistryValidationException("Runtime binding requires behavior_id.");
        if (string.IsNullOrWhiteSpace(AssetId))
            throw new RuntimeRegistryValidationException($"Runtime binding {BehaviorId} requires asset_id.");

        return new RuntimeAssetBinding(
            BehaviorId.Trim(),
            AssetId.Trim(),
            RuntimeApproved,
            RuntimeUse,
            new AnimationLifecycle(
                BehaviorId.Trim(),
                NormalPath.Select(ParsePhase).ToList(),
                InterruptPath.Select(ParsePhase).ToList(),
                FallbackBehaviorId));
    }

    private static AnimationPhase ParsePhase(string value) =>
        Enum.TryParse<AnimationPhase>(value, ignoreCase: true, out var phase)
            ? phase
            : throw new RuntimeRegistryValidationException($"Unknown animation phase: {value}");
}
