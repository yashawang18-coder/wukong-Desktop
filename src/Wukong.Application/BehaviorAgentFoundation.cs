using Wukong.Domain;

namespace Wukong.Application;

public enum PetEpisodeKind
{
    Resting,
    Sleeping,
    Observing,
    Exploring,
    Eating,
    Drinking,
    Playing,
    OwnerInteraction,
    MagicActivity,
    VehicleActivity,
    Socializing,
    Recovering
}

public enum BehaviorParticipationMode
{
    ForcedByOwner,
    UsuallyCooperative,
    StateSensitive,
    Autonomous
}

public enum BehaviorInterruptionPolicy
{
    SafePreempt,
    WaitForSafePoint,
    CannotPreempt
}

public enum BehaviorEffortLevel
{
    Low,
    Medium,
    High
}

public enum BehaviorSemanticCategory
{
    StableIdle,
    PostureTransition,
    Rest,
    Observe,
    Explore,
    Social,
    OwnerCommand,
    OwnerInvitation,
    Food,
    Drink,
    Magic
}

public sealed record PetEpisodeState(
    PetEpisodeKind Kind,
    DateTimeOffset StartedAt,
    TimeSpan MinimumDwell,
    string ReasonCode)
{
    public static PetEpisodeState Resting(DateTimeOffset now) =>
        BehaviorEpisodeCatalog.Start(PetEpisodeKind.Resting, now, "default_resting");

    public Guid EpisodeId { get; init; } = Guid.NewGuid();
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public TimeSpan PreferredDuration { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MaximumDuration { get; init; } = TimeSpan.FromMinutes(4);

    public bool CanSwitchAt(DateTimeOffset now) => now - StartedAt >= MinimumDwell;
}

public sealed record BehaviorEpisodeDefinition(
    PetEpisodeKind EpisodeId,
    IReadOnlySet<StablePosture> AllowedEntryPostures,
    TimeSpan MinimumDuration,
    TimeSpan PreferredDuration,
    TimeSpan MaximumDuration,
    double SwitchMargin,
    TimeSpan Cooldown,
    bool OrdinaryAutonomousInterruptible);

public static class BehaviorEpisodeCatalog
{
    private static readonly IReadOnlyDictionary<PetEpisodeKind, BehaviorEpisodeDefinition> Definitions =
        new[]
        {
            Define(PetEpisodeKind.Resting, 45, 120, 240, 0.16, 45, true, StablePosture.Stand, StablePosture.Sit, StablePosture.Prone),
            Define(PetEpisodeKind.Sleeping, 180, 600, 1200, 0.30, 300, false, StablePosture.Prone),
            Define(PetEpisodeKind.Observing, 20, 60, 120, 0.14, 30, true, StablePosture.Stand, StablePosture.Sit, StablePosture.Prone),
            Define(PetEpisodeKind.Exploring, 20, 90, 180, 0.18, 60, true, StablePosture.Stand),
            Define(PetEpisodeKind.Eating, 10, 15, 40, 0.30, 90, false, StablePosture.Stand),
            Define(PetEpisodeKind.Drinking, 10, 15, 40, 0.30, 90, false, StablePosture.Stand),
            Define(PetEpisodeKind.Playing, 15, 45, 120, 0.20, 60, true, StablePosture.Stand, StablePosture.Sit),
            Define(PetEpisodeKind.OwnerInteraction, 1, 20, 120, 0.30, 0, false, StablePosture.Stand, StablePosture.Sit, StablePosture.Prone),
            Define(PetEpisodeKind.MagicActivity, 1, 20, 180, 1.00, 0, false, StablePosture.Stand, StablePosture.Sit, StablePosture.Prone),
            Define(PetEpisodeKind.VehicleActivity, 10, 15, 180, 0.50, 0, false, StablePosture.Stand, StablePosture.Sit, StablePosture.Prone),
            Define(PetEpisodeKind.Socializing, 50, 90, 180, 0.18, 45, true, StablePosture.Stand, StablePosture.Sit, StablePosture.Prone),
            Define(PetEpisodeKind.Recovering, 120, 240, 600, 0.24, 90, false, StablePosture.Sit, StablePosture.Prone)
        }.ToDictionary(item => item.EpisodeId);

    public static BehaviorEpisodeDefinition Get(PetEpisodeKind episode) => Definitions[episode];

    public static PetEpisodeState Start(PetEpisodeKind episode, DateTimeOffset now, string reason, Guid? correlationId = null)
    {
        var definition = Get(episode);
        return new PetEpisodeState(episode, now, definition.MinimumDuration, reason)
        {
            EpisodeId = Guid.NewGuid(),
            CorrelationId = correlationId ?? Guid.NewGuid(),
            PreferredDuration = definition.PreferredDuration,
            MaximumDuration = definition.MaximumDuration
        };
    }

    private static BehaviorEpisodeDefinition Define(
        PetEpisodeKind kind,
        int minimumSeconds,
        int preferredSeconds,
        int maximumSeconds,
        double switchMargin,
        int cooldownSeconds,
        bool ordinaryAutonomousInterruptible,
        params StablePosture[] postures) =>
        new(kind, postures.ToHashSet(), TimeSpan.FromSeconds(minimumSeconds), TimeSpan.FromSeconds(preferredSeconds),
            TimeSpan.FromSeconds(maximumSeconds), switchMargin, TimeSpan.FromSeconds(cooldownSeconds), ordinaryAutonomousInterruptible);
}

public sealed record PetEpisodeSelection(
    PetEpisodeState Episode,
    bool Changed,
    IReadOnlyList<string> ReasonCodes);

public sealed record AutonomousAgentRolloutOptions(
    bool ShadowEnabled,
    IReadOnlySet<PetEpisodeKind> AuthoritativeEpisodes,
    bool LegacyFallbackOnInfrastructureFailure)
{
    public static AutonomousAgentRolloutOptions RestingFirst { get; } = new(
        ShadowEnabled: true,
        new HashSet<PetEpisodeKind> { PetEpisodeKind.Resting },
        LegacyFallbackOnInfrastructureFailure: false);

    public static AutonomousAgentRolloutOptions ContinuityV1 { get; } = new(
        ShadowEnabled: true,
        new HashSet<PetEpisodeKind>
        {
            PetEpisodeKind.Resting,
            PetEpisodeKind.Observing,
            PetEpisodeKind.Exploring,
            PetEpisodeKind.Sleeping
        },
        LegacyFallbackOnInfrastructureFailure: false);

    public static AutonomousAgentRolloutOptions ShadowOnly { get; } = new(
        ShadowEnabled: true,
        new HashSet<PetEpisodeKind>(),
        LegacyFallbackOnInfrastructureFailure: false);

    public bool IsAuthoritative(PetEpisodeKind episode) => AuthoritativeEpisodes.Contains(episode);
}

public sealed class PetEpisodePolicy
{
    public PetEpisodeSelection Evaluate(PetAgentState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        state = state.Clamp();
        var current = state.Episode;
        if (state.Runtime.IsBusy)
            return Keep(current, "active_behavior_holds_episode");

        if (current.Kind == PetEpisodeKind.Sleeping && now - current.StartedAt < current.MinimumDwell)
            return Keep(current, "sleeping_minimum_duration");

        var desired = DesiredEpisode(state);
        if (desired == current.Kind)
            return Keep(current, "episode_still_matches_state");
        var currentDefinition = BehaviorEpisodeCatalog.Get(current.Kind);
        var elapsed = now - current.StartedAt;
        if (!current.CanSwitchAt(now) && !RequiresImmediateRecovery(state.Runtime))
            return Keep(current, "episode_minimum_dwell");

        var currentScore = Score(current.Kind, state);
        var desiredScore = Score(desired, state);
        if (elapsed < current.MaximumDuration &&
            desiredScore <= currentScore + currentDefinition.SwitchMargin &&
            !RequiresImmediateRecovery(state.Runtime))
            return Keep(current, "episode_switch_margin");

        var reason = $"episode_transition:{current.Kind}->{desired}";
        return new PetEpisodeSelection(
            BehaviorEpisodeCatalog.Start(desired, now, reason, current.CorrelationId),
            true,
            new[] { reason });
    }

    private static PetEpisodeKind DesiredEpisode(PetAgentState state)
    {
        var runtime = state.Runtime;
        if (state.Episode.Kind == PetEpisodeKind.Sleeping)
            return PetEpisodeKind.Sleeping;
        if (RequiresImmediateRecovery(runtime) || runtime.Energy < 0.26 || runtime.Stress > 0.68)
            return PetEpisodeKind.Recovering;
        if (runtime.SocialNeed > 0.72 &&
            state.Temperament.Attachment01 - state.Temperament.Independence01 * 0.35 > 0.38)
            return PetEpisodeKind.Socializing;
        if (runtime.Boredom > 0.66 && runtime.Energy > 0.46 && runtime.Stress < 0.52)
            return PetEpisodeKind.Exploring;
        if (runtime.Curiosity > 0.62 || runtime.Focus > 0.70)
            return PetEpisodeKind.Observing;
        return PetEpisodeKind.Resting;
    }

    private static bool RequiresImmediateRecovery(PetRuntimeState runtime) =>
        runtime.Energy < 0.10 || runtime.Stress > 0.88;

    private static double Score(PetEpisodeKind episode, PetAgentState state) => episode switch
    {
        PetEpisodeKind.Sleeping => (1 - state.Runtime.Energy) * 0.70 + state.Runtime.Comfort * 0.20 - state.Runtime.Stress * 0.20,
        PetEpisodeKind.Recovering => (1 - state.Runtime.Energy) * 0.58 + state.Runtime.Stress * 0.48,
        PetEpisodeKind.Exploring => state.Runtime.Boredom * 0.48 + state.Runtime.Energy * 0.35 - state.Runtime.Stress * 0.40,
        // A clear curiosity/focus signal must be able to cross Resting's hysteresis.
        // Near-threshold signals still remain below the switch margin and keep the
        // current episode stable.
        PetEpisodeKind.Observing => state.Runtime.Curiosity * 0.60 + state.Runtime.Focus * 0.35,
        PetEpisodeKind.Socializing => state.Runtime.SocialNeed * 0.48 + state.Temperament.Attachment01 * 0.20,
        _ => state.Runtime.Comfort * 0.35 + (1 - state.Runtime.Arousal) * 0.20 + (1 - state.Runtime.Stress) * 0.20
    };

    private static PetEpisodeSelection Keep(PetEpisodeState current, string reason) =>
        new(current, false, new[] { reason });
}

public sealed record PetRecentExperience(
    DateTimeOffset At,
    string EventKind,
    string BehaviorId,
    ExecutionStatus Status,
    double CompletionRatio,
    string ReasonCode);

public sealed record LearnedBehaviorPreference(
    string BehaviorId,
    double Weight,
    double Confidence,
    string Source,
    DateTimeOffset UpdatedAt)
{
    public double EffectiveWeight => Math.Clamp(Weight * Math.Clamp(Confidence, 0, 1), -0.15, 0.15);
}

public sealed record PetAgentClockState(
    DateTimeOffset LastUpdatedAt,
    TimeSpan AppliedElapsed,
    TimeSpan SkippedOfflineElapsed);

public sealed record PetAgentState(
    int SchemaVersion,
    TemperamentProfile Temperament,
    RelationshipState Relationship,
    PetRuntimeState Runtime,
    PetEpisodeState Episode,
    IReadOnlyList<PetRecentExperience> RecentExperience,
    IReadOnlyDictionary<string, LearnedBehaviorPreference> Preferences,
    PetAgentClockState Clock)
{
    public const int CurrentSchemaVersion = 2;

    public static PetAgentState CreateDefault(DateTimeOffset now) => new(
        CurrentSchemaVersion,
        TemperamentProfile.Default,
        RelationshipState.Default,
        PetRuntimeState.Default,
        PetEpisodeState.Resting(now),
        Array.Empty<PetRecentExperience>(),
        new Dictionary<string, LearnedBehaviorPreference>(StringComparer.OrdinalIgnoreCase),
        new PetAgentClockState(now, TimeSpan.Zero, TimeSpan.Zero));

    public PetAgentState Clamp()
    {
        var temperament = Temperament with
        {
            Activity = Math.Clamp(Temperament.Activity, 0, 100),
            Attachment = Math.Clamp(Temperament.Attachment, 0, 100),
            Sensitivity = Math.Clamp(Temperament.Sensitivity, 0, 100),
            Independence = Math.Clamp(Temperament.Independence, 0, 100),
            Mischief = Math.Clamp(Temperament.Mischief, 0, 100),
            CommandCooperativeness = Math.Clamp(Temperament.CommandCooperativeness, 0, 1)
        };
        var relationship = Relationship with
        {
            Trust = Math.Clamp(Relationship.Trust, 0, 1),
            Familiarity = Math.Clamp(Relationship.Familiarity, 0, 1),
            TouchAcceptance = Math.Clamp(Relationship.TouchAcceptance, 0, 1),
            InitiativeAcceptance = Math.Clamp(Relationship.InitiativeAcceptance, 0, 1),
            RecentPositiveInteractions = Math.Clamp(Relationship.RecentPositiveInteractions, 0, 1000),
            RecentNegativeInteractions = Math.Clamp(Relationship.RecentNegativeInteractions, 0, 1000)
        };
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Temperament = temperament,
            Relationship = relationship,
            Runtime = Runtime.Clamp(),
            RecentExperience = RecentExperience.TakeLast(PetStateReducer.MaximumRecentExperience).ToArray(),
            Preferences = Preferences
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value with
                {
                    Weight = Math.Clamp(pair.Value.Weight, -0.15, 0.15),
                    Confidence = Math.Clamp(pair.Value.Confidence, 0, 1)
                }, StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed record PetStateEffects(
    double Energy = 0,
    double Hunger = 0,
    double Thirst = 0,
    double SocialNeed = 0,
    double Boredom = 0,
    double Stress = 0,
    double MoodValence = 0,
    double Arousal = 0,
    double Comfort = 0);

public abstract record PetAgentEvent(DateTimeOffset OccurredAt);

public sealed record PetTimeAdvanced(DateTimeOffset At) : PetAgentEvent(At);

public sealed record PetBehaviorStarted(
    DateTimeOffset At,
    Guid ExecutionId,
    string BehaviorId,
    string Phase,
    bool Interruptible,
    BehaviorRequestSource Source,
    BehaviorExecutionMode ExecutionMode) : PetAgentEvent(At);

public enum PartialEffectPolicy
{
    None,
    Proportional
}

public sealed record PetBehaviorFinished(
    DateTimeOffset At,
    Guid ExecutionId,
    string BehaviorId,
    ExecutionStatus Status,
    double CompletionRatio,
    StablePosture EndPosture,
    string EndPoseId,
    PetStateEffects Effects,
    bool OwnerInteraction,
    bool MemoryEligible,
    PartialEffectPolicy PartialEffectPolicy,
    BehaviorRequestSource Source,
    BehaviorExecutionMode ExecutionMode,
    string ReasonCode) : PetAgentEvent(At);

public sealed record BehaviorOutcomeProfile(
    string BehaviorId,
    StablePosture EndPosture,
    string EndPoseId,
    PetStateEffects StateEffects,
    bool OwnerInteraction,
    bool MemoryEligibility,
    PartialEffectPolicy PartialEffectPolicy);

public sealed record PetOwnerInteractionObserved(
    DateTimeOffset At,
    string Kind,
    int RepetitionCount,
    bool Positive) : PetAgentEvent(At);

public sealed record PetInitiativeSpeechOccurred(
    DateTimeOffset At,
    string Topic) : PetAgentEvent(At);

public sealed record PetEpisodeChanged(
    DateTimeOffset At,
    PetEpisodeKind Episode,
    TimeSpan MinimumDwell,
    string ReasonCode,
    Guid? CorrelationId = null) : PetAgentEvent(At);

public sealed class PetStateReducer
{
    public const int MaximumRecentExperience = 32;
    public static TimeSpan MaximumContinuousElapsed { get; } = TimeSpan.FromMinutes(5);

    public PetAgentState Reduce(PetAgentState state, PetAgentEvent agentEvent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(agentEvent);
        state = state.Clamp();
        return agentEvent switch
        {
            PetTimeAdvanced elapsed => Advance(state, elapsed.At),
            PetBehaviorStarted started => StartBehavior(state, started),
            PetBehaviorFinished finished => FinishBehavior(state, finished),
            PetOwnerInteractionObserved interaction => ApplyInteraction(state, interaction),
            PetInitiativeSpeechOccurred speech => ApplyInitiativeSpeech(state, speech),
            PetEpisodeChanged episode => state with
            {
                Episode = BehaviorEpisodeCatalog.Start(
                    episode.Episode,
                    episode.At,
                    episode.ReasonCode,
                    episode.CorrelationId ?? state.Episode.CorrelationId)
            },
            _ => state
        };
    }

    private static PetAgentState Advance(PetAgentState state, DateTimeOffset now)
    {
        var rawElapsed = now - state.Clock.LastUpdatedAt;
        if (rawElapsed <= TimeSpan.Zero)
            return state;

        var applied = rawElapsed <= MaximumContinuousElapsed ? rawElapsed : MaximumContinuousElapsed;
        var skipped = rawElapsed - applied;
        var seconds = applied.TotalSeconds;
        var sleeping = state.Episode.Kind == PetEpisodeKind.Sleeping;
        var runtime = state.Runtime with
        {
            Energy = state.Runtime.Energy + (sleeping ? 0.00018 : -0.000050) * seconds,
            Hunger = state.Runtime.Hunger + 0.000030 * seconds,
            Thirst = state.Runtime.Thirst + 0.000040 * seconds,
            SocialNeed = state.Runtime.SocialNeed + (sleeping ? 0.000010 : 0.000025) * seconds,
            Boredom = state.Runtime.Boredom + (sleeping ? 0.000005 : 0.000040) * seconds,
            Curiosity = state.Runtime.Curiosity + (sleeping ? 0 : 0.000015) * seconds,
            Stress = state.Runtime.Stress - (sleeping ? 0.000090 : 0.000045) * seconds,
            Arousal = MoveToward(state.Runtime.Arousal, sleeping ? 0.12 : 0.35, (sleeping ? 0.000080 : 0.000025) * seconds),
            MoodValence = MoveToward(state.Runtime.MoodValence, 0.58, 0.000012 * seconds)
        };
        return (state with
        {
            Runtime = runtime.Clamp(),
            Clock = state.Clock with
            {
                LastUpdatedAt = now,
                AppliedElapsed = state.Clock.AppliedElapsed + applied,
                SkippedOfflineElapsed = state.Clock.SkippedOfflineElapsed + skipped
            }
        }).Clamp();
    }

    private static PetAgentState StartBehavior(PetAgentState state, PetBehaviorStarted started)
    {
        if (started.ExecutionMode != BehaviorExecutionMode.Normal)
            return state;
        if (state.Runtime.ActiveExecutionId == started.ExecutionId)
            return state;
        if (state.Runtime.ActiveExecutionId is not null)
            return state;

        var runtime = state.Runtime with
        {
            ActiveExecutionId = started.ExecutionId,
            ActiveActionId = started.BehaviorId,
            ActiveActionStartedAt = started.At,
            CurrentPhase = started.Phase,
            IsInterruptible = started.Interruptible,
            IsBusy = true
        };
        return Append(state with { Runtime = runtime.Clamp() }, new PetRecentExperience(
            started.At, "behavior_started", started.BehaviorId, ExecutionStatus.Started, 0, "started"));
    }

    private static PetAgentState FinishBehavior(PetAgentState state, PetBehaviorFinished finished)
    {
        if (finished.ExecutionMode != BehaviorExecutionMode.Normal ||
            state.Runtime.ActiveExecutionId != finished.ExecutionId ||
            !string.Equals(state.Runtime.ActiveActionId, finished.BehaviorId, StringComparison.OrdinalIgnoreCase))
            return state;

        var completed = finished.Status == ExecutionStatus.Completed;
        var partialFactor = completed
            ? 1.0
            : finished.PartialEffectPolicy == PartialEffectPolicy.Proportional
                ? Math.Clamp(finished.CompletionRatio, 0, 1)
                : 0;
        var effects = Scale(finished.Effects, partialFactor);
        var repeated = completed && string.Equals(state.Runtime.LastActionId, finished.BehaviorId, StringComparison.OrdinalIgnoreCase)
            ? state.Runtime.RepeatedActionCount + 1
            : 0;
        var runtime = state.Runtime with
        {
            CurrentPosture = completed ? finished.EndPosture : state.Runtime.CurrentPosture,
            CurrentPoseId = completed ? finished.EndPoseId : state.Runtime.CurrentPoseId,
            LastActionId = completed ? finished.BehaviorId : state.Runtime.LastActionId,
            RepeatedActionCount = completed ? repeated : state.Runtime.RepeatedActionCount,
            ActiveExecutionId = null,
            ActiveActionId = null,
            ActiveActionStartedAt = null,
            CurrentPhase = "idle",
            IsInterruptible = true,
            IsBusy = false,
            Energy = state.Runtime.Energy + effects.Energy,
            Hunger = state.Runtime.Hunger + effects.Hunger,
            Thirst = state.Runtime.Thirst + effects.Thirst,
            SocialNeed = state.Runtime.SocialNeed + effects.SocialNeed,
            Boredom = state.Runtime.Boredom + effects.Boredom,
            Stress = state.Runtime.Stress + effects.Stress + (finished.Status == ExecutionStatus.Failed ? 0.03 : 0),
            MoodValence = state.Runtime.MoodValence + effects.MoodValence,
            Arousal = state.Runtime.Arousal + effects.Arousal,
            Comfort = state.Runtime.Comfort + effects.Comfort,
            LastInteractionAt = completed && finished.OwnerInteraction ? finished.At : state.Runtime.LastInteractionAt
        };
        var relationship = state.Relationship;
        var preferences = state.Preferences;
        if (completed && finished.OwnerInteraction)
        {
            relationship = relationship with
            {
                Trust = relationship.Trust + 0.002,
                Familiarity = relationship.Familiarity + 0.004,
                RecentPositiveInteractions = relationship.RecentPositiveInteractions + 1
            };
        }
        if (finished.MemoryEligible)
            preferences = ReinforcePreference(preferences, finished.BehaviorId, finished.Status, finished.OwnerInteraction, finished.At);
        return Append((state with { Runtime = runtime.Clamp(), Relationship = relationship, Preferences = preferences }).Clamp(), new PetRecentExperience(
            finished.At, "behavior_finished", finished.BehaviorId, finished.Status,
            Math.Clamp(finished.CompletionRatio, 0, 1), finished.ReasonCode));
    }

    private static PetAgentState ApplyInteraction(PetAgentState state, PetOwnerInteractionObserved interaction)
    {
        var repetitions = Math.Max(1, interaction.RepetitionCount);
        var stressDelta = interaction.Positive
            ? -0.008
            : (0.008 + state.Temperament.Sensitivity01 * 0.012) * repetitions;
        var runtime = state.Runtime with
        {
            LastInteractionAt = interaction.At,
            Stress = state.Runtime.Stress + stressDelta,
            SocialNeed = state.Runtime.SocialNeed + (interaction.Positive ? -0.018 : 0),
            MoodValence = state.Runtime.MoodValence + (interaction.Positive ? 0.008 : -0.004 * repetitions),
            Arousal = state.Runtime.Arousal + (interaction.Positive ? 0.005 : 0.008 * repetitions)
        };
        var recentInitiative = state.Runtime.LastInitiativeSpeechAt is not null &&
            interaction.At - state.Runtime.LastInitiativeSpeechAt.Value <= TimeSpan.FromMinutes(10);
        var touchInteraction = interaction.Kind.Contains("Touch", StringComparison.OrdinalIgnoreCase) ||
            interaction.Kind.Contains("Stroke", StringComparison.OrdinalIgnoreCase);
        var relationship = state.Relationship with
        {
            Trust = state.Relationship.Trust + (interaction.Positive ? 0.0015 : -0.001 * repetitions),
            Familiarity = state.Relationship.Familiarity + (interaction.Positive ? 0.002 : 0),
            TouchAcceptance = state.Relationship.TouchAcceptance + (touchInteraction
                ? interaction.Positive ? 0.003 : -0.006 * repetitions
                : 0),
            InitiativeAcceptance = state.Relationship.InitiativeAcceptance + (recentInitiative
                ? interaction.Positive ? 0.003 : -0.004 * repetitions
                : 0),
            RecentPositiveInteractions = state.Relationship.RecentPositiveInteractions + (interaction.Positive ? 1 : 0),
            RecentNegativeInteractions = state.Relationship.RecentNegativeInteractions + (interaction.Positive ? 0 : 1)
        };
        var preferenceTarget = state.Runtime.ActiveActionId ?? state.Runtime.LastActionId;
        var preferences = string.IsNullOrWhiteSpace(preferenceTarget)
            ? state.Preferences
            : ReinforcePreference(
                state.Preferences,
                preferenceTarget,
                interaction.Positive ? ExecutionStatus.Completed : ExecutionStatus.Rejected,
                ownerInteraction: true,
                interaction.At);
        return Append((state with { Runtime = runtime.Clamp(), Relationship = relationship, Preferences = preferences }).Clamp(), new PetRecentExperience(
            interaction.At, "owner_interaction", interaction.Kind,
            interaction.Positive ? ExecutionStatus.Completed : ExecutionStatus.Rejected,
            1, interaction.Positive ? "positive_interaction" : "repeated_or_negative_interaction"));
    }

    private static PetAgentState ApplyInitiativeSpeech(PetAgentState state, PetInitiativeSpeechOccurred speech)
    {
        var companionship = string.Equals(speech.Topic, "Companionship", StringComparison.OrdinalIgnoreCase);
        var runtime = state.Runtime with
        {
            SocialNeed = state.Runtime.SocialNeed - (companionship ? 0.025 : 0.008),
            LastInitiativeSpeechAt = speech.At
        };
        return Append(state with { Runtime = runtime.Clamp() }, new PetRecentExperience(
            speech.At, "initiative_speech", speech.Topic, ExecutionStatus.Completed, 1, "initiative_spoken"));
    }

    private static PetAgentState Append(PetAgentState state, PetRecentExperience experience) => state with
    {
        RecentExperience = state.RecentExperience.Append(experience).TakeLast(MaximumRecentExperience).ToArray()
    };

    private static IReadOnlyDictionary<string, LearnedBehaviorPreference> ReinforcePreference(
        IReadOnlyDictionary<string, LearnedBehaviorPreference> source,
        string behaviorId,
        ExecutionStatus status,
        bool ownerInteraction,
        DateTimeOffset at)
    {
        var result = new Dictionary<string, LearnedBehaviorPreference>(source, StringComparer.OrdinalIgnoreCase);
        result.TryGetValue(behaviorId, out var current);
        current ??= new LearnedBehaviorPreference(behaviorId, 0, 0, "runtime_feedback", at);
        var delta = status switch
        {
            ExecutionStatus.Completed when ownerInteraction => 0.008,
            ExecutionStatus.Completed => 0.001,
            ExecutionStatus.Failed => -0.010,
            ExecutionStatus.Rejected => -0.012,
            ExecutionStatus.Interrupted => -0.003,
            _ => 0
        };
        result[behaviorId] = current with
        {
            Weight = Math.Clamp(current.Weight + delta, -0.15, 0.15),
            Confidence = Math.Clamp(current.Confidence + (ownerInteraction ? 0.08 : 0.02), 0, 1),
            Source = ownerInteraction ? "owner_feedback" : "runtime_outcome",
            UpdatedAt = at
        };
        return result;
    }

    private static PetStateEffects Scale(PetStateEffects value, double factor) => new(
        value.Energy * factor,
        value.Hunger * factor,
        value.Thirst * factor,
        value.SocialNeed * factor,
        value.Boredom * factor,
        value.Stress * factor,
        value.MoodValence * factor,
        value.Arousal * factor,
        value.Comfort * factor);

    private static double MoveToward(double value, double target, double maximumDelta) =>
        value < target ? Math.Min(value + maximumDelta, target) : Math.Max(value - maximumDelta, target);
}

public sealed record BehaviorCapability(
    string BehaviorId,
    BehaviorSemanticCategory Category,
    BehaviorParticipationMode ParticipationMode,
    BehaviorInterruptionPolicy InterruptionPolicy,
    IReadOnlySet<BehaviorRequestSource> AllowedSources,
    IReadOnlySet<StablePosture> StartPostures,
    StablePosture EndPosture,
    BehaviorEffortLevel Effort,
    IReadOnlySet<PetEpisodeKind> AllowedEpisodes,
    bool ProductionApproved,
    bool RuntimeUse,
    bool ProductionAsset,
    bool AutonomousBindingEnabled,
    bool SupportsWindowTranslation,
    TimeSpan MinimumDwell,
    TimeSpan Cooldown,
    double BaseWeight,
    PetStateEffects StateEffects)
{
    public string StartPoseFamily { get; init; } = string.Empty;
    public string EndPoseId { get; init; } = string.Empty;
}

public static class PetPoseCompatibility
{
    public static string FamilyFor(string? poseId, StablePosture posture)
    {
        var value = poseId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.StartsWith("stand", StringComparison.Ordinal))
            return "stand";
        if (value.StartsWith("sit", StringComparison.Ordinal))
            return "sit";
        if (value.Contains("sleep", StringComparison.Ordinal) || value.Contains("side_lying", StringComparison.Ordinal))
            return "sleep";
        if (value.StartsWith("prone", StringComparison.Ordinal))
            return value.Contains("front", StringComparison.Ordinal) && !value.Contains("left_front", StringComparison.Ordinal)
                ? "prone.front"
                : "prone.non_front";
        return posture switch
        {
            StablePosture.Stand => "stand",
            StablePosture.Sit => "sit",
            _ => "prone.non_front"
        };
    }

    public static bool IsCompatible(string requiredFamily, PetRuntimeState runtime) =>
        string.IsNullOrWhiteSpace(requiredFamily) ||
        string.Equals(requiredFamily, FamilyFor(runtime.CurrentPoseId, runtime.CurrentPosture), StringComparison.OrdinalIgnoreCase);
}

public interface IBehaviorCapabilityCatalog
{
    IReadOnlyList<BehaviorCapability> Capabilities { get; }
    BehaviorCapability? Find(string behaviorId);
}

public sealed class BehaviorCapabilityCatalog : IBehaviorCapabilityCatalog
{
    private readonly IReadOnlyDictionary<string, BehaviorCapability> _byId;

    public BehaviorCapabilityCatalog(IEnumerable<BehaviorCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _byId = capabilities
            .GroupBy(item => item.BehaviorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.ProductionApproved && item.RuntimeUse && item.ProductionAsset)
                    .ThenByDescending(item => item.ProductionApproved)
                    .ThenByDescending(item => item.RuntimeUse)
                    .ThenByDescending(item => item.ProductionAsset)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
        Capabilities = _byId.Values.OrderBy(item => item.BehaviorId, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<BehaviorCapability> Capabilities { get; }

    public BehaviorCapability? Find(string behaviorId) =>
        _byId.TryGetValue(behaviorId, out var capability) ? capability : null;
}

public sealed record ParticipationDecision(
    RequestDisposition Disposition,
    string ReasonCode,
    string UserFacingReason,
    DateTimeOffset? RetryAt = null)
{
    public double WillingnessScore { get; init; } = 1;
    public IReadOnlyDictionary<string, double> Components { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
}

public sealed class BehaviorParticipationPolicy
{
    public ParticipationDecision Evaluate(
        BehaviorCapability capability,
        PetAgentState state,
        BehaviorRequestSource source,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(state);
        state = state.Clamp();

        if (!capability.ProductionApproved || !capability.RuntimeUse || !capability.ProductionAsset)
            return Defer("capability_unavailable", "对应动作素材尚未具备正式运行资格");
        if (!capability.AllowedSources.Contains(source))
            return Defer("source_not_allowed", "当前来源不能触发该行为");
        if (state.Runtime.IsBusy && !state.Runtime.IsInterruptible &&
            !string.Equals(state.Runtime.ActiveActionId, capability.BehaviorId, StringComparison.OrdinalIgnoreCase))
            return Defer("current_not_interruptible", "当前动作需要先到达安全中断点");
        if (!capability.StartPostures.Contains(state.Runtime.CurrentPosture))
            return Defer("posture_transition_required", "需要先完成兼容的姿态转换");

        if (capability.ParticipationMode == BehaviorParticipationMode.ForcedByOwner)
            return Accept("forced_owner_safe_admission", "主人特辑将在安全中断后执行");
        if (capability.ParticipationMode == BehaviorParticipationMode.Autonomous)
            return Accept("autonomous_candidate", "自主候选通过参与门禁");

        var runtime = state.Runtime;
        var cooperation = state.Temperament.CommandCooperativeness01;
        var recentRepeats = state.RecentExperience
            .Where(item => now - item.At <= TimeSpan.FromMinutes(10) &&
                           string.Equals(item.BehaviorId, capability.BehaviorId, StringComparison.OrdinalIgnoreCase) &&
                           item.Status == ExecutionStatus.Completed)
            .Count();
        var repeatCount = Math.Max(runtime.RepeatedActionCount, recentRepeats);
        if (capability.Effort == BehaviorEffortLevel.High && runtime.Energy < 0.16)
            return Reject("energy_too_low", "悟空现在太累了，想先休息一下");
        if (capability.Effort == BehaviorEffortLevel.High && runtime.Stress > 0.82)
            return Reject("stress_too_high", "悟空现在有些紧张，不想做强烈动作");
        if (repeatCount >= 5 ||
            (repeatCount >= 4 && runtime.Stress + state.Temperament.Sensitivity01 * 0.20 > 0.72))
            return Reject("repeated_command_tolerance_exceeded", "这个动作已经连续做了很多次，让悟空缓一缓");

        var effortEnergy = capability.Effort switch
        {
            BehaviorEffortLevel.Low => 1.0,
            BehaviorEffortLevel.Medium => runtime.Energy,
            _ => Math.Max(0, (runtime.Energy - 0.12) / 0.88)
        };
        var components = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["command_cooperativeness"] = cooperation * 0.50,
            ["relationship_trust"] = state.Relationship.Trust01 * 0.14,
            ["relationship_familiarity"] = state.Relationship.Familiarity01 * 0.06,
            ["current_mood"] = runtime.MoodValence * 0.08,
            ["available_energy"] = effortEnergy * 0.12,
            ["stress_safety"] = (1 - runtime.Stress) * 0.10,
            ["repetition_penalty"] = -Math.Min(0.30, repeatCount * (0.035 + state.Temperament.Sensitivity01 * 0.015))
        };
        var willingness = Math.Clamp(components.Values.Sum(), 0, 1);

        // Low-effort commands are intentionally highly cooperative. Independence
        // affects style and initiative, never obedience to an explicit owner command.
        if (capability.Effort == BehaviorEffortLevel.Low && runtime.Stress < 0.94)
            return Accept("low_effort_cooperative", "悟空愿意响应主人", willingness, components);

        var acceptThreshold = capability.Effort == BehaviorEffortLevel.High ? 0.58 : 0.46;
        var rejectThreshold = capability.Effort == BehaviorEffortLevel.High ? 0.34 : 0.26;
        if (willingness >= acceptThreshold)
            return Accept("normally_cooperative", "悟空愿意响应主人", willingness, components);
        if (willingness >= rejectThreshold)
            return Defer("command_state_delay", "悟空需要先缓一缓", now.AddSeconds(20), willingness, components);
        return Reject("command_state_refusal", "悟空现在状态不太好，想先休息", willingness, components);
    }

    private static ParticipationDecision Accept(
        string reason,
        string message,
        double willingness = 1,
        IReadOnlyDictionary<string, double>? components = null) =>
        new(RequestDisposition.Accepted, reason, message)
        {
            WillingnessScore = willingness,
            Components = components ?? new Dictionary<string, double>()
        };
    private static ParticipationDecision Reject(
        string reason,
        string message,
        double willingness = 0,
        IReadOnlyDictionary<string, double>? components = null) =>
        new(RequestDisposition.Rejected, reason, message)
        {
            WillingnessScore = willingness,
            Components = components ?? new Dictionary<string, double>()
        };
    private static ParticipationDecision Defer(
        string reason,
        string message,
        DateTimeOffset? retryAt = null,
        double willingness = 0,
        IReadOnlyDictionary<string, double>? components = null) =>
        new(RequestDisposition.Deferred, reason, message, retryAt)
        {
            WillingnessScore = willingness,
            Components = components ?? new Dictionary<string, double>()
        };
}

public sealed record BehaviorDecisionInput(
    BehaviorRequestSource Source,
    DateTimeOffset Now,
    string CurrentBehaviorId,
    DateTimeOffset CurrentBehaviorStartedAt,
    bool CurrentBehaviorInterruptible,
    IReadOnlyDictionary<string, DateTimeOffset> Cooldowns,
    IReadOnlyList<string> RecentBehaviorHistory,
    int RandomSeed,
    bool AllowInitiative,
    bool WindowMotionAvailable,
    IReadOnlySet<string>? CandidateBehaviorIds = null,
    IReadOnlyDictionary<string, double>? BehaviorWeightMultipliers = null);

public sealed record BehaviorDecisionCandidate(
    string BehaviorId,
    IReadOnlyList<ScoreComponent> Components,
    double FinalScore,
    bool Selected,
    IReadOnlyList<string> GateReasons);

public sealed record BehaviorAgentDecision(
    RequestDisposition Disposition,
    string? SelectedBehaviorId,
    PetEpisodeKind Episode,
    string ReasonCode,
    IReadOnlyList<BehaviorDecisionCandidate> Candidates,
    DateTimeOffset CreatedAt);

public sealed class BehaviorDecisionEngine
{
    public BehaviorAgentDecision Decide(
        PetAgentState state,
        IBehaviorCapabilityCatalog catalog,
        BehaviorDecisionInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(input);
        state = state.Clamp();
        var random = new Random(input.RandomSeed);
        var candidates = new List<BehaviorDecisionCandidate>();

        foreach (var capability in catalog.Capabilities.OrderBy(item => item.BehaviorId, StringComparer.Ordinal))
        {
            var gateReasons = Gate(capability, state, input);
            if (gateReasons.Count > 0)
            {
                candidates.Add(new BehaviorDecisionCandidate(
                    capability.BehaviorId, Array.Empty<ScoreComponent>(), double.NegativeInfinity, false, gateReasons));
                continue;
            }

            var components = Score(capability, state, input, random);
            candidates.Add(new BehaviorDecisionCandidate(
                capability.BehaviorId, components, components.Sum(item => item.Value), false, Array.Empty<string>()));
        }

        var eligible = candidates.Where(item => item.GateReasons.Count == 0).OrderByDescending(item => item.FinalScore).ToArray();
        if (eligible.Length == 0)
            return new BehaviorAgentDecision(RequestDisposition.Deferred, null, state.Episode.Kind,
                "no_eligible_capability", candidates, input.Now);

        var maximum = eligible[0].FinalScore;
        var nearMaximumThreshold = maximum - Math.Max(0.05, Math.Abs(maximum) * 0.15);
        var band = eligible.Where(item => item.FinalScore >= nearMaximumThreshold).ToArray();
        var floor = band.Min(item => item.FinalScore);
        var weights = band.Select(item => Math.Max(0.01, item.FinalScore - floor + 0.05)).ToArray();
        var draw = random.NextDouble() * weights.Sum();
        var selected = band[^1];
        for (var index = 0; index < band.Length; index++)
        {
            draw -= weights[index];
            if (draw <= 0)
            {
                selected = band[index];
                break;
            }
        }

        var marked = candidates.Select(item => item with
        {
            Selected = string.Equals(item.BehaviorId, selected.BehaviorId, StringComparison.OrdinalIgnoreCase)
        }).ToArray();
        return new BehaviorAgentDecision(RequestDisposition.Accepted, selected.BehaviorId, state.Episode.Kind,
            "selected_from_near_max_band", marked, input.Now);
    }

    private static IReadOnlyList<string> Gate(
        BehaviorCapability capability,
        PetAgentState state,
        BehaviorDecisionInput input)
    {
        var reasons = new List<string>();
        if (input.CandidateBehaviorIds is not null && !input.CandidateBehaviorIds.Contains(capability.BehaviorId))
            reasons.Add("episode_rollout_not_bound");
        if (!capability.ProductionApproved || !capability.RuntimeUse || !capability.ProductionAsset)
            reasons.Add("runtime_capability_unavailable");
        if (!capability.AllowedSources.Contains(input.Source))
            reasons.Add("source_not_allowed");
        if (input.Source == BehaviorRequestSource.AutonomousTick &&
            (capability.ParticipationMode != BehaviorParticipationMode.Autonomous || !capability.AutonomousBindingEnabled))
            reasons.Add("not_autonomous_capability");
        if (!capability.StartPostures.Contains(state.Runtime.CurrentPosture))
            reasons.Add("posture_mismatch");
        if (!PetPoseCompatibility.IsCompatible(capability.StartPoseFamily, state.Runtime))
            reasons.Add("pose_profile_mismatch");
        if (!capability.AllowedEpisodes.Contains(state.Episode.Kind))
            reasons.Add("episode_mismatch");
        if (capability.SupportsWindowTranslation && !input.WindowMotionAvailable)
            reasons.Add("window_motion_unavailable");
        if (state.Runtime.IsBusy && !input.CurrentBehaviorInterruptible &&
            !string.Equals(input.CurrentBehaviorId, capability.BehaviorId, StringComparison.OrdinalIgnoreCase))
            reasons.Add("current_not_interruptible");
        if (!string.Equals(input.CurrentBehaviorId, capability.BehaviorId, StringComparison.OrdinalIgnoreCase) &&
            input.Now - input.CurrentBehaviorStartedAt < capability.MinimumDwell)
            reasons.Add("minimum_dwell");
        if (input.Cooldowns.TryGetValue(capability.BehaviorId, out var lastAccepted) &&
            input.Now - lastAccepted < capability.Cooldown)
            reasons.Add("cooldown");
        return reasons;
    }

    private static IReadOnlyList<ScoreComponent> Score(
        BehaviorCapability capability,
        PetAgentState state,
        BehaviorDecisionInput input,
        Random random)
    {
        var runtime = state.Runtime;
        var temperament = state.Temperament;
        var temperamentScore = capability.Category switch
        {
            BehaviorSemanticCategory.Rest => (1 - temperament.Activity01) * 0.18 + temperament.Independence01 * 0.08,
            BehaviorSemanticCategory.Observe => temperament.Activity01 * 0.10 + temperament.Sensitivity01 * 0.08,
            BehaviorSemanticCategory.Explore => temperament.Activity01 * 0.24 + temperament.Mischief01 * 0.08,
            BehaviorSemanticCategory.Social => temperament.Attachment01 * 0.24 - temperament.Independence01 * 0.10,
            _ => 0.04
        };
        var stateScore = capability.Category switch
        {
            BehaviorSemanticCategory.Rest => (1 - runtime.Energy) * 0.42 + runtime.Stress * 0.20 + runtime.Comfort * 0.12,
            BehaviorSemanticCategory.Observe => runtime.Curiosity * 0.30 + runtime.Focus * 0.12 + runtime.SocialNeed * 0.05,
            BehaviorSemanticCategory.Explore => runtime.Boredom * 0.34 + runtime.Energy * 0.24 - runtime.Stress * 0.32,
            BehaviorSemanticCategory.Social => runtime.SocialNeed * 0.38 - runtime.Stress * 0.20,
            BehaviorSemanticCategory.StableIdle => runtime.Comfort * 0.18 + (1 - runtime.Arousal) * 0.12,
            BehaviorSemanticCategory.PostureTransition => runtime.Comfort * 0.08,
            _ => 0
        };
        var relationshipBalance = RelationshipBalance(state.Relationship);
        var relationshipScore = capability.Category switch
        {
            BehaviorSemanticCategory.Social =>
                state.Relationship.Trust01 * 0.10 + state.Relationship.Familiarity01 * 0.08 + relationshipBalance * 0.06,
            BehaviorSemanticCategory.Observe =>
                state.Relationship.Familiarity01 * 0.05 + state.Relationship.Trust01 * 0.03 + relationshipBalance * 0.03,
            BehaviorSemanticCategory.Explore =>
                state.Relationship.Trust01 * 0.05 + relationshipBalance * 0.03,
            BehaviorSemanticCategory.Rest or BehaviorSemanticCategory.StableIdle =>
                state.Relationship.Familiarity01 * 0.035 + relationshipBalance * 0.02,
            BehaviorSemanticCategory.PostureTransition => state.Relationship.Trust01 * 0.02,
            _ => 0
        };
        var preference = state.Preferences.TryGetValue(capability.BehaviorId, out var learned)
            ? learned.EffectiveWeight
            : 0;
        var experienceMemory = ExperienceMemoryScore(state, capability.BehaviorId, input.Now);
        var ownerMultiplier = input.BehaviorWeightMultipliers is not null &&
                              input.BehaviorWeightMultipliers.TryGetValue(capability.BehaviorId, out var configured) &&
                              double.IsFinite(configured)
            ? Math.Clamp(configured, AutonomousBehaviorPreferences.MinimumWeight, AutonomousBehaviorPreferences.MaximumWeight)
            : 1.0;
        var ownerPreference = capability.BaseWeight * (ownerMultiplier - 1.0);
        var episodeFit = capability.AllowedEpisodes.Contains(state.Episode.Kind) ? 0.18 : 0;
        var workQuiet = input.Now.Hour is >= 9 and <= 18;
        var timeContext = workQuiet && capability.Category is BehaviorSemanticCategory.Rest or BehaviorSemanticCategory.StableIdle
            ? 0.08
            : 0;
        var repeatCount = input.RecentBehaviorHistory.TakeLast(6)
            .Count(item => string.Equals(item, capability.BehaviorId, StringComparison.OrdinalIgnoreCase));
        var repetitionPenalty = -0.12 * repeatCount;
        var transitionCost = capability.EndPosture == runtime.CurrentPosture ? 0 : -0.14;
        var interruptionRisk = string.Equals(input.CurrentBehaviorId, capability.BehaviorId, StringComparison.OrdinalIgnoreCase)
            ? 0
            : capability.InterruptionPolicy == BehaviorInterruptionPolicy.WaitForSafePoint ? -0.10 : -0.03;
        var jitter = (random.NextDouble() * 2 - 1) * 0.03;
        return new[]
        {
            new ScoreComponent("base_weight", capability.BaseWeight),
            new ScoreComponent("temperament_affinity", temperamentScore),
            new ScoreComponent("runtime_state", stateScore),
            new ScoreComponent("relationship", relationshipScore),
            new ScoreComponent("memory_preference", preference),
            new ScoreComponent("experience_memory", experienceMemory),
            new ScoreComponent("owner_behavior_preference", ownerPreference),
            new ScoreComponent("episode_fit", episodeFit),
            new ScoreComponent("time_context", timeContext),
            new ScoreComponent("repetition_penalty", repetitionPenalty),
            new ScoreComponent("transition_cost", transitionCost),
            new ScoreComponent("interruption_risk", interruptionRisk),
            new ScoreComponent("seeded_jitter", jitter)
        };
    }

    private static double RelationshipBalance(RelationshipState relationship)
    {
        var total = relationship.RecentPositiveInteractions + relationship.RecentNegativeInteractions;
        if (total <= 0)
            return 0;
        return Math.Clamp(
            (relationship.RecentPositiveInteractions - relationship.RecentNegativeInteractions) / (double)Math.Min(20, total),
            -1,
            1);
    }

    private static double ExperienceMemoryScore(PetAgentState state, string behaviorId, DateTimeOffset now)
    {
        var score = 0.0;
        foreach (var item in state.RecentExperience.Where(item =>
                     string.Equals(item.BehaviorId, behaviorId, StringComparison.OrdinalIgnoreCase)))
        {
            var ageHours = Math.Max(0, (now - item.At).TotalHours);
            var recency = Math.Exp(-ageHours / 12.0);
            score += item.Status switch
            {
                ExecutionStatus.Completed => 0.018 * recency,
                ExecutionStatus.Interrupted => -0.025 * recency,
                ExecutionStatus.Failed => -0.060 * recency,
                ExecutionStatus.Rejected => -0.040 * recency,
                _ => 0
            };
        }
        return Math.Clamp(score, -0.12, 0.08);
    }
}

public sealed record PetAgentDialogueProjection(
    PersonalitySnapshot Personality,
    RelationshipSnapshot Relationship,
    PetRuntimeStateSnapshot RuntimeState);

public sealed class DialogueStateProjector
{
    public PetAgentDialogueProjection Project(PetAgentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state = state.Clamp();
        var runtime = state.Runtime;
        var currentAction = runtime.ActiveActionId ?? runtime.LastActionId ?? IdleFor(runtime.CurrentPosture);
        return new PetAgentDialogueProjection(
            new PersonalitySnapshot(
                state.Temperament.Activity01,
                state.Temperament.Attachment01,
                state.Temperament.Sensitivity01,
                state.Temperament.Independence01,
                state.Temperament.Mischief01)
            {
                CommandCooperativeness = state.Temperament.CommandCooperativeness01
            },
            new RelationshipSnapshot(
                state.Relationship.Trust01,
                state.Relationship.Familiarity01,
                state.Relationship.TouchAcceptance01,
                state.Relationship.InitiativeAcceptance01),
            new PetRuntimeStateSnapshot(
                currentAction,
                runtime.Arousal,
                runtime.Stress,
                runtime.SocialNeed,
                Math.Clamp(runtime.Boredom * 0.6 + runtime.Energy * 0.4, 0, 1),
                runtime.Curiosity,
                1 - runtime.Energy,
                Math.Clamp(runtime.Comfort * (1 - runtime.Stress * 0.5), 0, 1))
            {
                CurrentPosture = runtime.CurrentPosture.ToString().ToLowerInvariant(),
                CurrentAction = currentAction,
                MoodValence = runtime.MoodValence,
                Energy = runtime.Energy,
                Hunger = runtime.Hunger,
                Thirst = runtime.Thirst,
                Episode = state.Episode.Kind.ToString().ToLowerInvariant(),
                IsBusy = runtime.IsBusy
            });
    }

    private static string IdleFor(StablePosture posture) => posture switch
    {
        StablePosture.Stand => "stable_stand_idle",
        StablePosture.Sit => "stable_sit_idle",
        _ => "stable_prone_idle"
    };
}
