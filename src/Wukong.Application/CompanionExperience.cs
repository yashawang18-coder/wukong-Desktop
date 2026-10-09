namespace Wukong.Application;

public sealed record CompanionTimeOptions
{
    public bool UseDeviceTimeZone { get; init; } = true;
    public string ManualTimeZoneId { get; init; } = "Pacific/Auckland";
}

public sealed record CompanionPresenceOptions
{
    public bool StartWithWindows { get; init; } = true;
    public bool StartupGreetingEnabled { get; init; } = true;
    public double StartupGreetingProbability { get; init; } = .65;
    public bool LongAbsenceGreetingEnabled { get; init; } = true;
    public double LongAbsenceHours { get; init; } = 24;
    public bool LateNightCareEnabled { get; init; } = true;
    public int LateNightStartHour { get; init; } = 23;
    public int LateNightEndHour { get; init; } = 3;
    public double LateNightMinimumIntervalHours { get; init; } = 18;
}

public sealed record CompanionSessionState
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset? LastActiveAtUtc { get; init; }
    public DateTimeOffset? LastStartupGreetingAtUtc { get; init; }
    public DateTimeOffset? LastLateNightCareAtUtc { get; init; }
    public int LaunchCount { get; init; }

    public CompanionSessionState Clamp() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        LaunchCount = Math.Clamp(LaunchCount, 0, 1_000_000),
        LastActiveAtUtc = Normalize(LastActiveAtUtc),
        LastStartupGreetingAtUtc = Normalize(LastStartupGreetingAtUtc),
        LastLateNightCareAtUtc = Normalize(LastLateNightCareAtUtc)
    };

    private static DateTimeOffset? Normalize(DateTimeOffset? value) => value?.ToUniversalTime();
}

public interface ICompanionSessionStateStore
{
    Task<CompanionSessionState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CompanionSessionState state, CancellationToken cancellationToken = default);
}

public enum CompanionGreetingKind
{
    None,
    Startup,
    LongAbsence,
    LateNightCare
}

public sealed record CompanionGreetingDecision(
    bool ShouldSpeak,
    CompanionGreetingKind Kind,
    InitiativeSpeechTopic Topic,
    string Text,
    string ReasonCode,
    DateTimeOffset CompanionLocalTime,
    bool CountsAsLateNightCare = false);

public sealed record CompanionGreetingContext(
    DateTimeOffset Now,
    bool IsStartup,
    CompanionSessionState Session,
    CompanionTimeOptions Time,
    CompanionPresenceOptions Presence,
    InitiativeSpeechOptions Speech,
    PetDecisionMemoryProfile Memory,
    StablePosture Posture,
    double RandomUnit);

public static class CompanionClock
{
    public const string AucklandIanaId = "Pacific/Auckland";
    public const string AucklandWindowsId = "New Zealand Standard Time";

    public static DateTimeOffset ToCompanionTime(DateTimeOffset instant, CompanionTimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var zone = options.UseDeviceTimeZone ? TimeZoneInfo.Local : Resolve(options.ManualTimeZoneId);
        return TimeZoneInfo.ConvertTime(instant, zone);
    }

    public static TimeZoneInfo Resolve(string? id)
    {
        foreach (var candidate in CandidateIds(id))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    public static string EffectiveId(CompanionTimeOptions options) =>
        (options.UseDeviceTimeZone ? TimeZoneInfo.Local : Resolve(options.ManualTimeZoneId)).Id;

    private static IEnumerable<string> CandidateIds(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) yield return id.Trim();
        if (string.Equals(id, AucklandIanaId, StringComparison.OrdinalIgnoreCase)) yield return AucklandWindowsId;
        if (string.Equals(id, AucklandWindowsId, StringComparison.OrdinalIgnoreCase)) yield return AucklandIanaId;
        if (!string.Equals(id, AucklandIanaId, StringComparison.OrdinalIgnoreCase)) yield return AucklandIanaId;
        if (!string.Equals(id, AucklandWindowsId, StringComparison.OrdinalIgnoreCase)) yield return AucklandWindowsId;
    }
}

public sealed class CompanionGreetingDecisionService
{
    public CompanionGreetingDecision Decide(CompanionGreetingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var local = CompanionClock.ToCompanionTime(context.Now, context.Time);
        if (!context.Speech.Enabled)
            return None("initiative_disabled", local);

        var absence = Absence(context.Now, context.Session.LastActiveAtUtc);
        var longAbsence = context.IsStartup && context.Presence.LongAbsenceGreetingEnabled &&
            absence >= TimeSpan.FromHours(context.Presence.LongAbsenceHours);
        var lateNight = IsWithin(local.Hour, context.Presence.LateNightStartHour, context.Presence.LateNightEndHour);

        if (longAbsence)
        {
            var text = lateNight && context.Presence.LateNightCareEnabled
                ? "老爸回来啦，我好想你。很晚啦，早点休息呀。"
                : SelectLongAbsenceMessage(context.Memory, context.Posture);
            return new(true, CompanionGreetingKind.LongAbsence, InitiativeSpeechTopic.Companionship,
                text, "long_absence", local, lateNight && context.Presence.LateNightCareEnabled);
        }

        if (context.Presence.LateNightCareEnabled && lateNight &&
            IsNewNight(local, context.Session.LastLateNightCareAtUtc, context.Time,
                TimeSpan.FromHours(context.Presence.LateNightMinimumIntervalHours), context.Now))
            return new(true, CompanionGreetingKind.LateNightCare, InitiativeSpeechTopic.Rest,
                SelectLateNightMessage(context.Posture), "late_night_care", local, true);

        if (!context.IsStartup)
            return None("no_scheduled_care_due", local);
        if (!context.Presence.StartupGreetingEnabled)
            return None("startup_greeting_disabled", local);
        if (context.Speech.IsQuietAt(local))
            return None("quiet_hours", local);
        if (!double.IsFinite(context.RandomUnit) || Math.Clamp(context.RandomUnit, 0, 1) > context.Presence.StartupGreetingProbability)
            return None("startup_greeting_probability", local);

        return new(true, CompanionGreetingKind.Startup, InitiativeSpeechTopic.Companionship,
            SelectStartupMessage(local, context.Memory), "startup_greeting", local);
    }

    private static TimeSpan? Absence(DateTimeOffset now, DateTimeOffset? lastActive)
    {
        if (lastActive is null) return null;
        var elapsed = now.ToUniversalTime() - lastActive.Value.ToUniversalTime();
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private static bool IsWithin(int hour, int start, int end) => start == end ||
        (start < end ? hour >= start && hour < end : hour >= start || hour < end);

    private static bool IsNewNight(
        DateTimeOffset localNow,
        DateTimeOffset? previous,
        CompanionTimeOptions time,
        TimeSpan minimumInterval,
        DateTimeOffset instant)
    {
        if (previous is null) return true;
        if (instant.ToUniversalTime() - previous.Value.ToUniversalTime() < minimumInterval) return false;
        var previousLocal = CompanionClock.ToCompanionTime(previous.Value, time);
        return NightKey(localNow) != NightKey(previousLocal);
    }

    private static DateOnly NightKey(DateTimeOffset local) =>
        DateOnly.FromDateTime((local.Hour < 12 ? local.AddDays(-1) : local).Date);

    private static string SelectLongAbsenceMessage(PetDecisionMemoryProfile memory, StablePosture posture)
    {
        var topic = StrongestMemoryTopic(memory);
        return topic switch
        {
            InitiativeSpeechTopic.Play => "老爸，你终于回来啦。我想和你玩。",
            InitiativeSpeechTopic.Curiosity => "老爸，我好想你呀。想一起看看外面。",
            InitiativeSpeechTopic.Hunger => "老爸，我想你啦，也想起饭饭啦。",
            _ when posture == StablePosture.Prone => "老爸，你回来啦。我一直想你呀。",
            _ => "老爸，你终于回来啦。我好想你呀。"
        };
    }

    private static string SelectStartupMessage(DateTimeOffset local, PetDecisionMemoryProfile memory)
    {
        var topic = StrongestMemoryTopic(memory);
        if (topic == InitiativeSpeechTopic.Play) return "老爸，我来啦。今天也想和你玩。";
        if (topic == InitiativeSpeechTopic.Curiosity) return "老爸，我来陪你啦。今天看点啥？";
        if (local.Hour < 11) return "老爸早呀，我来陪你啦。";
        if (local.Hour >= 18) return "老爸晚上好呀，我在这儿。";
        return "老爸，我来陪你啦。";
    }

    private static string SelectLateNightMessage(StablePosture posture) => posture == StablePosture.Prone
        ? "老爸，很晚啦。陪我一起休息呀。"
        : "老爸，很晚啦。早点休息呀。";

    private static InitiativeSpeechTopic StrongestMemoryTopic(PetDecisionMemoryProfile memory)
    {
        var values = Enum.GetValues<InitiativeSpeechTopic>()
            .Where(topic => topic != InitiativeSpeechTopic.None)
            .Select(topic => (Topic: topic, Weight: memory.InitiativeTopicWeight(topic)))
            .OrderByDescending(item => item.Weight)
            .ToArray();
        return values.Length > 0 && values[0].Weight >= .04 ? values[0].Topic : InitiativeSpeechTopic.Companionship;
    }

    private static CompanionGreetingDecision None(string reason, DateTimeOffset local) =>
        new(false, CompanionGreetingKind.None, InitiativeSpeechTopic.None, string.Empty, reason, local);
}
