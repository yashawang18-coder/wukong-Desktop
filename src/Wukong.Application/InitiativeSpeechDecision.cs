namespace Wukong.Application;

public enum InitiativeSpeechTopic
{
    None,
    Companionship,
    Hunger,
    Thirst,
    Play,
    Curiosity,
    Rest
}

public sealed record InitiativeSpeechContext(
    PetRuntimeState State,
    TemperamentProfile Temperament,
    RelationshipState Relationship,
    DateTimeOffset Now,
    DateTimeOffset? LastSpokenAt,
    bool IsStableIdle,
    bool IsPetrified,
    bool IsChatExpanded,
    bool IsQuietHours,
    int RandomSeed)
{
    public PetEpisodeKind Episode { get; init; } = PetEpisodeKind.Resting;
}

public sealed record InitiativeSpeechCandidate(
    InitiativeSpeechTopic Topic,
    double Score,
    IReadOnlyList<string> ReasonCodes);

public sealed record InitiativeSpeechDecision(
    bool ShouldSpeak,
    InitiativeSpeechTopic Topic,
    string ReasonCode,
    TimeSpan NextCheck,
    IReadOnlyList<InitiativeSpeechCandidate> Candidates);

public sealed class InitiativeSpeechDecisionService
{
    public InitiativeSpeechDecision Decide(InitiativeSpeechContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var nextCheck = NextCheck(context);
        var suppression = SuppressionReason(context);
        if (suppression is not null)
            return new InitiativeSpeechDecision(false, InitiativeSpeechTopic.None, suppression, nextCheck, Array.Empty<InitiativeSpeechCandidate>());

        var state = context.State.Clamp();
        var candidates = BuildCandidates(context with { State = state })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Topic)
            .ToArray();
        var selected = candidates[0];

        // A seeded gate prevents every scheduler check from becoming speech while
        // retaining deterministic tests and state-driven topic selection.
        var gate = 0.74 + new Random(context.RandomSeed ^ 0x51A7).NextDouble() * 0.18;
        return selected.Score >= gate
            ? new InitiativeSpeechDecision(true, selected.Topic, "state_threshold_met", nextCheck, candidates)
            : new InitiativeSpeechDecision(false, InitiativeSpeechTopic.None, "initiative_threshold_not_met", nextCheck, candidates);
    }

    private static string? SuppressionReason(InitiativeSpeechContext context)
    {
        var state = context.State.Clamp();
        if (context.IsPetrified)
            return "petrified";
        if (context.IsChatExpanded)
            return "chat_expanded";
        if (!context.IsStableIdle || state.IsBusy)
            return "behavior_not_stable_idle";
        if (context.IsQuietHours)
            return "quiet_hours";
        if (state.Stress >= 0.72)
            return "stress_safety_limit";
        if (context.Relationship.InitiativeAcceptance01 < 0.25)
            return "initiative_acceptance_low";

        var urgency = NeedUrgency(state);
        var cooldownMinutes = 16.0
            - context.Relationship.InitiativeAcceptance01 * 3.0
            - context.Temperament.Attachment01 * 2.0
            + context.Temperament.Independence01 * 3.0
            + state.Stress * 4.0
            - urgency * 4.0;
        var cooldown = TimeSpan.FromMinutes(Math.Clamp(cooldownMinutes, 7, 20));
        if (context.LastSpokenAt is not null && context.Now - context.LastSpokenAt.Value < cooldown)
            return "initiative_cooldown";
        return null;
    }

    private static IEnumerable<InitiativeSpeechCandidate> BuildCandidates(InitiativeSpeechContext context)
    {
        var state = context.State;
        var temperament = context.Temperament;
        var relationship = context.Relationship;
        var inactivityBonus = InactivityBonus(context);
        yield return Candidate(
            InitiativeSpeechTopic.Hunger,
            0.12 + state.Hunger * 0.92 - state.Stress * 0.20,
            "hunger", state.Hunger);
        yield return Candidate(
            InitiativeSpeechTopic.Thirst,
            0.10 + state.Thirst * 0.94 - state.Stress * 0.20,
            "thirst", state.Thirst);
        yield return Candidate(
            InitiativeSpeechTopic.Companionship,
            0.10 + state.SocialNeed * 0.62 + temperament.Attachment01 * 0.24 + relationship.Familiarity01 * 0.12 - temperament.Independence01 * 0.16 +
            (context.Episode == PetEpisodeKind.Socializing ? 0.12 : 0) + inactivityBonus,
            "social_need", state.SocialNeed);
        yield return Candidate(
            InitiativeSpeechTopic.Play,
            0.05 + state.Boredom * 0.42 + state.Energy * 0.30 + temperament.Activity01 * 0.16 + temperament.Mischief01 * 0.10 - state.Stress * 0.35,
            "boredom", state.Boredom);
        yield return Candidate(
            InitiativeSpeechTopic.Curiosity,
            0.08 + state.Curiosity * 0.54 + state.Focus * 0.16 + temperament.Activity01 * 0.10 - state.Stress * 0.28 +
            (context.Episode == PetEpisodeKind.Observing ? 0.14 : 0),
            "curiosity", state.Curiosity);
        yield return Candidate(
            InitiativeSpeechTopic.Rest,
            0.10 + (1 - state.Energy) * 0.68 + state.Comfort * 0.16 - state.Arousal * 0.12 +
            (context.Episode == PetEpisodeKind.Recovering ? 0.14 : 0),
            "low_energy", 1 - state.Energy);
    }

    private static TimeSpan NextCheck(InitiativeSpeechContext context)
    {
        var urgency = NeedUrgency(context.State.Clamp());
        var random = new Random(context.RandomSeed ^ 0x2C71);
        var (minimumSeconds, maximumSeconds) = urgency switch
        {
            >= 0.82 => (60, 121),
            >= 0.62 => (90, 181),
            _ => (150, 301)
        };
        return TimeSpan.FromSeconds(random.Next(minimumSeconds, maximumSeconds));
    }

    private static double NeedUrgency(PetRuntimeState state) => Math.Max(
        Math.Max(state.Hunger, state.Thirst),
        Math.Max(state.SocialNeed, Math.Max(state.Boredom * 0.85, (1 - state.Energy) * 0.85)));

    private static double InactivityBonus(InitiativeSpeechContext context)
    {
        if (context.State.LastInteractionAt is null)
            return 0.22;
        var elapsed = context.Now - context.State.LastInteractionAt.Value;
        return elapsed.TotalMinutes switch
        {
            >= 60 => 0.22,
            >= 30 => 0.12,
            >= 15 => 0.06,
            _ => 0
        };
    }

    private static InitiativeSpeechCandidate Candidate(InitiativeSpeechTopic topic, double score, string driver, double value) =>
        new(topic, Math.Clamp(score, 0, 1.5), new[] { $"{driver}={Math.Clamp(value, 0, 1):0.00}" });
}
