namespace Wukong.Application;

public sealed record BehaviorTuning(double BaseWeight, double MinimumDwellSeconds, double CooldownSeconds);

public sealed record CommandParticipationOptions
{
    public double RepeatWindowMinutes { get; init; } = 10;
    public double HighEffortMinimumEnergy { get; init; } = .16;
    public double HighEffortMaximumStress { get; init; } = .82;
    public int RepeatLimit { get; init; } = 5;
    public double LowEffortMaximumStress { get; init; } = .94;
    public double HighAcceptThreshold { get; init; } = .58;
    public double HighRejectThreshold { get; init; } = .34;
    public double MediumAcceptThreshold { get; init; } = .46;
    public double MediumRejectThreshold { get; init; } = .26;
    public double RetrySeconds { get; init; } = 20;
    public double RequestBurstSeconds { get; init; } = 60;
    public int RequestBurstLimit { get; init; } = 5;
    public double Rebelliousness { get; init; } = .35;
}

public sealed record InitiativeSpeechOptions
{
    public bool Enabled { get; init; } = true;
    public bool QuietHoursEnabled { get; init; } = true;
    public int QuietStartHour { get; init; } = 23;
    public int QuietEndHour { get; init; } = 7;
    public double RecentInteractionSeconds { get; init; } = 90;
    public double MaximumStress { get; init; } = .72;
    public int EightHourBudget { get; init; } = 6;
    public int UnansweredBudget { get; init; } = 4;
    public double MinimumCooldownMinutes { get; init; } = 7;
    public double MaximumCooldownMinutes { get; init; } = 20;
    public double BaseCooldownMinutes { get; init; } = 16;
    public double FrequencyMultiplier { get; init; } = 1;
    public double SelectionThreshold { get; init; } = .76;
    public bool IsQuietAt(DateTimeOffset now) => QuietHoursEnabled &&
        (QuietStartHour == QuietEndHour || (QuietStartHour < QuietEndHour
            ? now.Hour >= QuietStartHour && now.Hour < QuietEndHour
            : now.Hour >= QuietStartHour || now.Hour < QuietEndHour));
}

public sealed record AutonomyPolicyProfile
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string ProfileId { get; init; } = "companion-v1";
    public Dictionary<StablePosture, StablePostureAutonomyPolicy> Postures { get; init; } =
        new(AutonomousAgentRolloutOptions.CreateDefaultPosturePolicies());
    public Dictionary<PetEpisodeKind, Dictionary<StablePosture, StablePostureAutonomyPolicy>> EpisodePostures { get; init; } = new();
    public Dictionary<BehaviorSemanticCategory, BehaviorTuning> Categories { get; init; } = DefaultCategories();
    public Dictionary<string, double> BehaviorMultipliers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    // Null permits one-time compatibility import of the previous owner's preferences.
    public AutonomousBehaviorPreferences? OwnerPreferences { get; init; }
    public InitiativeSpeechOptions Speech { get; init; } = new();
    public CommandParticipationOptions Commands { get; init; } = new();
    public CompanionTimeOptions Time { get; init; } = new();
    public CompanionPresenceOptions Presence { get; init; } = new();
    public double WakeRiseMinimumDwellSeconds { get; init; } = 45;
    public double PatrolSharedCooldownSeconds { get; init; } = 45;
    public int FrontExpressionCooldownMinimumSeconds { get; init; } = 45;
    public int FrontExpressionCooldownMaximumSeconds { get; init; } = 120;
    public int StandingExpressionCooldownMinimumSeconds { get; init; } = 75;
    public int StandingExpressionCooldownMaximumSeconds { get; init; } = 150;
    public int RetryMinimumSeconds { get; init; } = 14;
    public int RetryMaximumSeconds { get; init; } = 24;
    public static AutonomyPolicyProfile Default => new();
    public AutonomousBehaviorPreferences EffectivePreferences => (OwnerPreferences ?? AutonomousBehaviorPreferences.Default).Clamp();

    public StablePostureAutonomyPolicy PostureFor(StablePosture posture, PetEpisodeKind? episode = null) =>
        episode is { } kind && EpisodePostures.TryGetValue(kind, out var overrides) && overrides.TryGetValue(posture, out var specific)
            ? specific : Postures[posture];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        void Range(double value, double minimum, double maximum, string field)
        { if (!double.IsFinite(value) || value < minimum || value > maximum) errors.Add(field); }
        void Posture(StablePostureAutonomyPolicy value, string field)
        {
            Range(value.MinimumDwell.TotalSeconds, 1, 86400, field + ".minimum");
            Range(value.MaximumDwell.TotalSeconds, value.MinimumDwell.TotalSeconds + 1, 86400, field + ".maximum");
            Range(value.DecisionDelayMinimum.TotalSeconds, value.MinimumDwell.TotalSeconds, 86400, field + ".delay_min");
            Range(value.DecisionDelayMaximum.TotalSeconds, value.DecisionDelayMinimum.TotalSeconds + 1, 86400, field + ".delay_max");
            Range(value.IdlePreferenceWeight, .05, 2, field + ".weight");
        }
        if (SchemaVersion != CurrentSchemaVersion) errors.Add("schema_version");
        if (string.IsNullOrWhiteSpace(ProfileId) || ProfileId.Length > 80) errors.Add("profile_id");
        if (Postures is null || EpisodePostures is null || Categories is null || BehaviorMultipliers is null ||
            Speech is null || Commands is null || Time is null || Presence is null)
            return new[] { "required_section_missing" };
        foreach (var posture in Enum.GetValues<StablePosture>())
            if (Postures.TryGetValue(posture, out var value) && value is not null) Posture(value, posture.ToString());
            else errors.Add($"postures.{posture}");
        foreach (var (episode, overrides) in EpisodePostures)
        {
            if (!Enum.IsDefined(episode) || overrides is null) { errors.Add("episode_override"); continue; }
            foreach (var (posture, value) in overrides)
                if (!Enum.IsDefined(posture) || value is null) errors.Add("posture_override");
                else Posture(value, $"{episode}.{posture}");
        }
        foreach (var category in Enum.GetValues<BehaviorSemanticCategory>())
        {
            if (!Categories.TryGetValue(category, out var tuning) || tuning is null) { errors.Add($"category.{category}"); continue; }
            Range(tuning.BaseWeight, 0, 3, $"{category}.weight");
            Range(tuning.MinimumDwellSeconds, 0, 3600, $"{category}.dwell");
            Range(tuning.CooldownSeconds, 0, 86400, $"{category}.cooldown");
        }
        foreach (var (id, value) in BehaviorMultipliers) Range(value, .05, 3, $"multiplier.{id}");
        if (BehaviorMultipliers.Count > 256) errors.Add("too_many_multipliers");
        Range(WakeRiseMinimumDwellSeconds, 1, 86400, "wake_dwell");
        Range(PatrolSharedCooldownSeconds, 1, 86400, "patrol_cooldown");
        Range(FrontExpressionCooldownMinimumSeconds, 1, 86400, "expression_min");
        Range(FrontExpressionCooldownMaximumSeconds, FrontExpressionCooldownMinimumSeconds, 86400, "expression_max");
        Range(StandingExpressionCooldownMinimumSeconds, 1, 86400, "standing_expression_min");
        Range(StandingExpressionCooldownMaximumSeconds, StandingExpressionCooldownMinimumSeconds, 86400, "standing_expression_max");
        Range(RetryMinimumSeconds, 1, 3600, "retry_min");
        Range(RetryMaximumSeconds, RetryMinimumSeconds, 3600, "retry_max");
        Range(Speech.QuietStartHour, 0, 23, "quiet_start");
        Range(Speech.QuietEndHour, 0, 23, "quiet_end");
        Range(Speech.RecentInteractionSeconds, 0, 3600, "speech_recent_interaction");
        Range(Speech.MaximumStress, .1, .9, "speech_stress");
        Range(Speech.EightHourBudget, 1, 24, "speech_budget");
        Range(Speech.UnansweredBudget, 1, Speech.EightHourBudget, "speech_unanswered_budget");
        Range(Speech.MinimumCooldownMinutes, 1, 120, "speech_cooldown_min");
        Range(Speech.MaximumCooldownMinutes, Speech.MinimumCooldownMinutes, 240, "speech_cooldown_max");
        Range(Speech.BaseCooldownMinutes, Speech.MinimumCooldownMinutes, Speech.MaximumCooldownMinutes, "speech_cooldown_base");
        Range(Speech.FrequencyMultiplier, .25, 2, "speech_frequency");
        Range(Speech.SelectionThreshold, .4, 1.2, "speech_threshold");
        Range(Commands.RepeatWindowMinutes, 1, 120, "command_repeat_window");
        Range(Commands.HighEffortMinimumEnergy, .1, .5, "command_energy");
        Range(Commands.HighEffortMaximumStress, .5, .9, "command_stress");
        Range(Commands.RepeatLimit, 2, 12, "command_repeat_limit");
        Range(Commands.LowEffortMaximumStress, .8, 1, "command_low_stress");
        Range(Commands.HighRejectThreshold, 0, 1, "command_high_reject");
        Range(Commands.HighAcceptThreshold, Commands.HighRejectThreshold + .01, 1, "command_high_accept");
        Range(Commands.MediumRejectThreshold, 0, 1, "command_medium_reject");
        Range(Commands.MediumAcceptThreshold, Commands.MediumRejectThreshold + .01, 1, "command_medium_accept");
        Range(Commands.RetrySeconds, 1, 600, "command_retry");
        Range(Commands.RequestBurstSeconds, 10, 600, "request_burst_seconds");
        Range(Commands.RequestBurstLimit, 3, 16, "request_burst_limit");
        Range(Commands.Rebelliousness, 0, 1, "rebelliousness");
        if (string.IsNullOrWhiteSpace(Time.ManualTimeZoneId) || Time.ManualTimeZoneId.Length > 128)
            errors.Add("manual_time_zone");
        Range(Presence.StartupGreetingProbability, 0, 1, "startup_greeting_probability");
        Range(Presence.LongAbsenceHours, 1, 720, "long_absence_hours");
        Range(Presence.LateNightStartHour, 0, 23, "late_night_start");
        Range(Presence.LateNightEndHour, 0, 23, "late_night_end");
        Range(Presence.LateNightMinimumIntervalHours, 6, 72, "late_night_interval");
        return errors;
    }

    private static Dictionary<BehaviorSemanticCategory, BehaviorTuning> DefaultCategories() =>
        Enum.GetValues<BehaviorSemanticCategory>().ToDictionary(category => category, category => category switch
        {
            BehaviorSemanticCategory.StableIdle => new BehaviorTuning(.64, 8, 0),
            BehaviorSemanticCategory.Rest => new(.72, 12, 60),
            BehaviorSemanticCategory.Observe => new(.48, 6, 45),
            BehaviorSemanticCategory.Explore => new(.32, 10, 70),
            BehaviorSemanticCategory.PostureTransition => new(.40, 6, 20),
            BehaviorSemanticCategory.Social => new(.34, 6, 20),
            _ => new(.30, 6, 20)
        });
}

public sealed record AutonomyPolicyLoadResult(AutonomyPolicyProfile Profile, string Status);
public interface IAutonomyPolicyStore
{
    Task<AutonomyPolicyLoadResult> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AutonomyPolicyProfile profile, CancellationToken cancellationToken = default);
}
