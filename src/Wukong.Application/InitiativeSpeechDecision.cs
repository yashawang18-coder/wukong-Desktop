namespace Wukong.Application;

public enum InitiativeSpeechTopic
{
    None,
    Companionship,
    Hunger,
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
    int RandomSeed);

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
        var nextCheck = TimeSpan.FromSeconds(new Random(context.RandomSeed).Next(75, 151));
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

        var cooldownMinutes = 12.0
            - context.Relationship.InitiativeAcceptance01 * 3.0
            - context.Temperament.Attachment01 * 2.0
            + context.Temperament.Independence01 * 2.0;
        var cooldown = TimeSpan.FromMinutes(Math.Clamp(cooldownMinutes, 6, 14));
        if (context.LastSpokenAt is not null && context.Now - context.LastSpokenAt.Value < cooldown)
            return "initiative_cooldown";
        return null;
    }

    private static IEnumerable<InitiativeSpeechCandidate> BuildCandidates(InitiativeSpeechContext context)
    {
        var state = context.State;
        var temperament = context.Temperament;
        var relationship = context.Relationship;
        yield return Candidate(
            InitiativeSpeechTopic.Hunger,
            0.12 + state.Hunger * 0.92 - state.Stress * 0.20,
            "hunger", state.Hunger);
        yield return Candidate(
            InitiativeSpeechTopic.Companionship,
            0.10 + state.SocialNeed * 0.62 + temperament.Attachment01 * 0.24 + relationship.Familiarity01 * 0.12 - temperament.Independence01 * 0.16,
            "social_need", state.SocialNeed);
        yield return Candidate(
            InitiativeSpeechTopic.Play,
            0.05 + state.Boredom * 0.42 + state.Energy * 0.30 + temperament.Activity01 * 0.16 + temperament.Mischief01 * 0.10 - state.Stress * 0.35,
            "boredom", state.Boredom);
        yield return Candidate(
            InitiativeSpeechTopic.Curiosity,
            0.08 + state.Curiosity * 0.54 + state.Focus * 0.16 + temperament.Activity01 * 0.10 - state.Stress * 0.28,
            "curiosity", state.Curiosity);
        yield return Candidate(
            InitiativeSpeechTopic.Rest,
            0.10 + (1 - state.Energy) * 0.68 + state.Comfort * 0.16 - state.Arousal * 0.12,
            "low_energy", 1 - state.Energy);
    }

    private static InitiativeSpeechCandidate Candidate(InitiativeSpeechTopic topic, double score, string driver, double value) =>
        new(topic, Math.Clamp(score, 0, 1.5), new[] { $"{driver}={Math.Clamp(value, 0, 1):0.00}" });
}
