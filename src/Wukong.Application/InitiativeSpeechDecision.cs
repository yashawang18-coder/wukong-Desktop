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
    public IReadOnlyList<PetRecentExperience> RecentExperience { get; init; } = Array.Empty<PetRecentExperience>();
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
            .Select(candidate => ApplyMemoryAndRelationship(context, candidate))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Topic)
            .ToArray();
        var selected = candidates[0];

        // The gate is deterministic for a fixed state/seed. Relationship affects
        // willingness to initiate, while independence affects frequency, not facts.
        var gate = 0.76 + new Random(context.RandomSeed ^ 0x51A7).NextDouble() * 0.14
            + context.Temperament.Independence01 * 0.08
            - context.Relationship.InitiativeAcceptance01 * 0.08
            - context.Relationship.Trust01 * 0.04;
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

        var recentInitiatives = context.RecentExperience
            .Where(item => item.EventKind == "initiative_speech" && context.Now - item.At <= TimeSpan.FromHours(8))
            .OrderByDescending(item => item.At)
            .ToArray();
        if (recentInitiatives.Length >= 6)
            return "initiative_budget_exhausted";

        var urgency = NeedUrgency(state);
        var cooldownMinutes = 16.0
            - context.Relationship.InitiativeAcceptance01 * 3.0
            - context.Temperament.Attachment01 * 2.0
            + context.Temperament.Independence01 * 3.0
            + state.Stress * 4.0
            - urgency * 4.0;
        var cooldown = TimeSpan.FromMinutes(Math.Clamp(cooldownMinutes, 7, 20));
        var lastSpeechAt = context.LastSpokenAt ?? recentInitiatives.FirstOrDefault()?.At;
        var unanswered = lastSpeechAt is not null &&
            (state.LastInteractionAt is null || state.LastInteractionAt.Value <= lastSpeechAt.Value);
        if (unanswered && urgency < 0.82)
            cooldown = TimeSpan.FromTicks((long)(cooldown.Ticks * 1.6));
        if (lastSpeechAt is not null && context.Now - lastSpeechAt.Value < cooldown)
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

    private static InitiativeSpeechCandidate ApplyMemoryAndRelationship(
        InitiativeSpeechContext context,
        InitiativeSpeechCandidate candidate)
    {
        var lastSameTopic = context.RecentExperience
            .Where(item => item.EventKind == "initiative_speech" &&
                           string.Equals(item.BehaviorId, candidate.Topic.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.At)
            .FirstOrDefault();
        var repeatPenalty = lastSameTopic is null
            ? 0
            : (context.Now - lastSameTopic.At).TotalMinutes switch
            {
                < 20 => 0.34,
                < 45 => 0.20,
                < 90 => 0.08,
                _ => 0
            };
        var relationshipBonus = candidate.Topic switch
        {
            InitiativeSpeechTopic.Companionship =>
                context.Relationship.Trust01 * 0.06 + context.Relationship.Familiarity01 * 0.06,
            InitiativeSpeechTopic.Play or InitiativeSpeechTopic.Curiosity => context.Relationship.Trust01 * 0.03,
            _ => 0
        };
        return candidate with
        {
            Score = Math.Clamp(candidate.Score + relationshipBonus - repeatPenalty, 0, 1.5),
            ReasonCodes = candidate.ReasonCodes
                .Append($"relationship={relationshipBonus:0.00}")
                .Append($"topic_repeat_penalty={repeatPenalty:0.00}")
                .ToArray()
        };
    }

    private static InitiativeSpeechCandidate Candidate(InitiativeSpeechTopic topic, double score, string driver, double value) =>
        new(topic, Math.Clamp(score, 0, 1.5), new[] { $"{driver}={Math.Clamp(value, 0, 1):0.00}" });
}
