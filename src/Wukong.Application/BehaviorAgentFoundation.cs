using Wukong.Domain;

namespace Wukong.Application;

public enum PetEpisodeKind
{
    Resting,
    Observing,
    Exploring,
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
        new(PetEpisodeKind.Resting, now, TimeSpan.FromMinutes(1), "default_resting");

    public bool CanSwitchAt(DateTimeOffset now) => now - StartedAt >= MinimumDwell;
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

        var desired = DesiredEpisode(state);
        if (desired == current.Kind)
            return Keep(current, "episode_still_matches_state");
        if (!current.CanSwitchAt(now) && !RequiresImmediateRecovery(state.Runtime))
            return Keep(current, "episode_minimum_dwell");

        var reason = $"episode_transition:{current.Kind}->{desired}";
        return new PetEpisodeSelection(
            new PetEpisodeState(desired, now, MinimumDwellFor(desired), reason),
            true,
            new[] { reason });
    }

    private static PetEpisodeKind DesiredEpisode(PetAgentState state)
    {
        var runtime = state.Runtime;
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

    private static TimeSpan MinimumDwellFor(PetEpisodeKind episode) => episode switch
    {
        PetEpisodeKind.Recovering => TimeSpan.FromMinutes(2),
        PetEpisodeKind.Resting => TimeSpan.FromMinutes(1),
        PetEpisodeKind.Socializing => TimeSpan.FromSeconds(50),
        PetEpisodeKind.Exploring => TimeSpan.FromSeconds(45),
        _ => TimeSpan.FromSeconds(35)
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
    public const int CurrentSchemaVersion = 1;

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
            RecentPositiveInteractions = Math.Max(0, Relationship.RecentPositiveInteractions),
            RecentNegativeInteractions = Math.Max(0, Relationship.RecentNegativeInteractions)
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

public sealed record PetEpisodeChanged(
    DateTimeOffset At,
    PetEpisodeKind Episode,
    TimeSpan MinimumDwell,
    string ReasonCode) : PetAgentEvent(At);

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
            PetEpisodeChanged episode => state with
            {
                Episode = new PetEpisodeState(episode.Episode, episode.At, episode.MinimumDwell, episode.ReasonCode)
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
        var runtime = state.Runtime with
        {
            Energy = state.Runtime.Energy - 0.000050 * seconds,
            Hunger = state.Runtime.Hunger + 0.000030 * seconds,
            Thirst = state.Runtime.Thirst + 0.000040 * seconds,
            SocialNeed = state.Runtime.SocialNeed + 0.000025 * seconds,
            Boredom = state.Runtime.Boredom + 0.000040 * seconds,
            Curiosity = state.Runtime.Curiosity + 0.000015 * seconds,
            Stress = state.Runtime.Stress - 0.000045 * seconds,
            Arousal = MoveToward(state.Runtime.Arousal, 0.35, 0.000025 * seconds),
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
        if (completed && finished.OwnerInteraction)
        {
            relationship = relationship with
            {
                Trust = relationship.Trust + 0.002,
                Familiarity = relationship.Familiarity + 0.004,
                RecentPositiveInteractions = relationship.RecentPositiveInteractions + 1
            };
        }
        return Append((state with { Runtime = runtime.Clamp(), Relationship = relationship }).Clamp(), new PetRecentExperience(
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
        return Append(state with { Runtime = runtime.Clamp() }, new PetRecentExperience(
            interaction.At, "owner_interaction", interaction.Kind,
            interaction.Positive ? ExecutionStatus.Completed : ExecutionStatus.Rejected,
            1, interaction.Positive ? "positive_interaction" : "repeated_or_negative_interaction"));
    }

    private static PetAgentState Append(PetAgentState state, PetRecentExperience experience) => state with
    {
        RecentExperience = state.RecentExperience.Append(experience).TakeLast(MaximumRecentExperience).ToArray()
    };

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
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
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
    DateTimeOffset? RetryAt = null);

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
        if (capability.Effort == BehaviorEffortLevel.High && runtime.Energy < 0.16)
            return Reject("energy_too_low", "悟空现在太累了，想先休息一下");
        if (capability.Effort == BehaviorEffortLevel.High && runtime.Stress > 0.82)
            return Reject("stress_too_high", "悟空现在有些紧张，不想做强烈动作");
        if (runtime.RepeatedActionCount >= 4 && runtime.Stress + state.Temperament.Sensitivity01 * 0.20 > 0.72)
            return Reject("repeated_command_tolerance_exceeded", "这个动作已经连续做了很多次，让悟空缓一缓");
        if (capability.ParticipationMode == BehaviorParticipationMode.StateSensitive &&
            cooperation < 0.35 && runtime.Stress > 0.65)
            return Reject("state_sensitive_refusal", "悟空现在更想保持安静");

        return Accept("normally_cooperative", "悟空愿意响应主人");
    }

    private static ParticipationDecision Accept(string reason, string message) =>
        new(RequestDisposition.Accepted, reason, message);
    private static ParticipationDecision Reject(string reason, string message) =>
        new(RequestDisposition.Rejected, reason, message);
    private static ParticipationDecision Defer(string reason, string message) =>
        new(RequestDisposition.Deferred, reason, message);
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
        var relationshipScore = capability.Category == BehaviorSemanticCategory.Social
            ? state.Relationship.Trust01 * 0.08 + state.Relationship.Familiarity01 * 0.06
            : 0;
        var preference = state.Preferences.TryGetValue(capability.BehaviorId, out var learned)
            ? learned.EffectiveWeight
            : 0;
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
            new ScoreComponent("owner_behavior_preference", ownerPreference),
            new ScoreComponent("episode_fit", episodeFit),
            new ScoreComponent("time_context", timeContext),
            new ScoreComponent("repetition_penalty", repetitionPenalty),
            new ScoreComponent("transition_cost", transitionCost),
            new ScoreComponent("interruption_risk", interruptionRisk),
            new ScoreComponent("seeded_jitter", jitter)
        };
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
