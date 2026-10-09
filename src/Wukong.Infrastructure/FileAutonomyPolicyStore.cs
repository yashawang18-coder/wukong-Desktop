using System.Text.Json;
using Wukong.Application;

namespace Wukong.Infrastructure;

public sealed class FileAutonomyPolicyStore : IAutonomyPolicyStore
{
    public const string FileName = "autonomy-policy.json";
    private readonly string _path;
    private readonly string _legacyPreferences;
    public FileAutonomyPolicyStore(string root)
    {
        _path = Path.Combine(root, FileName);
        _legacyPreferences = Path.Combine(root, "autonomous-behavior-preferences.json");
    }
    public async Task<AutonomyPolicyLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var status = File.Exists(_path) ? "loaded" : "default";
        var profile = AutonomyPolicyProfile.Default;
        try
        {
            if (File.Exists(_path))
            {
                await using var stream = File.OpenRead(_path);
                var parsed = await JsonSerializer.DeserializeAsync<AutonomyPolicyProfile>(stream, AgentJson.Options, cancellationToken);
                var errors = parsed?.Validate() ?? new[] { "empty_profile" };
                if (errors.Count == 0) profile = parsed!;
                else status = "invalid_preserved:" + string.Join(',', errors);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        { status = "unreadable_preserved:" + ex.GetType().Name; }
        if (profile.OwnerPreferences is null)
        {
            var legacy = await AgentJson.ReadAsync<AutonomousBehaviorPreferences>(_legacyPreferences, cancellationToken);
            profile = profile with { OwnerPreferences = (legacy ?? AutonomousBehaviorPreferences.Default).Clamp() };
            if (legacy is not null) status += ";legacy_preferences_imported";
        }
        return new(profile, status);
    }
    public Task SaveAsync(AutonomyPolicyProfile profile, CancellationToken cancellationToken = default)
    {
        var errors = profile.Validate();
        if (errors.Count != 0) throw new ArgumentException(string.Join(',', errors), nameof(profile));
        return AgentJson.WriteAsync(_path, profile, cancellationToken);
    }
}
