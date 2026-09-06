using Wukong.Application;
using Wukong.Domain;

namespace Wukong.Desktop;

public static class DesktopBehaviorCapabilityCatalog
{
    public static BehaviorCapabilityCatalog Create(IEnumerable<PlayableMotion> motions)
    {
        ArgumentNullException.ThrowIfNull(motions);
        return new BehaviorCapabilityCatalog(motions.Select(Project));
    }

    private static BehaviorCapability Project(PlayableMotion motion)
    {
        var category = CategoryFor(motion);
        var participation = ParticipationFor(motion, category);
        var startPosture = PostureFrom(motion.StartPose, StablePosture.Prone);
        var endPosture = PostureFrom(motion.EndPose, startPosture);
        var outcomeProfile = DesktopBehaviorOutcomeProfiles.Find(motion);
        return new BehaviorCapability(
            motion.BehaviorId,
            category,
            participation,
            motion.Interruptible ? BehaviorInterruptionPolicy.SafePreempt : BehaviorInterruptionPolicy.WaitForSafePoint,
            AllowedSourcesFor(motion, participation),
            new HashSet<StablePosture> { startPosture },
            endPosture,
            EffortFor(category),
            EpisodesFor(category),
            ProductionApproved: motion.EffectiveRuntimeApproved && !motion.IsExpired,
            RuntimeUse: motion.RuntimeEnabled && !motion.IsExpired,
            ProductionAsset: motion.RuntimeEnabled && motion.EffectiveRuntimeApproved && !motion.IsExpired,
            AutonomousBindingEnabled: motion.AutonomousBindingEnabled,
            SupportsWindowTranslation: motion.WindowMotionEnabled,
            MinimumDwell: MinimumDwellFor(category),
            Cooldown: CooldownFor(category),
            BaseWeight: BaseWeightFor(category),
            StateEffects: outcomeProfile?.StateEffects ?? EffectsFor(category))
        {
            StartPoseFamily = PetPoseCompatibility.FamilyFor(motion.StartPose, startPosture),
            EndPoseId = outcomeProfile?.EndPoseId ?? motion.EndPose
        };
    }

    private static BehaviorParticipationMode ParticipationFor(
        PlayableMotion motion,
        BehaviorSemanticCategory category)
    {
        if (category == BehaviorSemanticCategory.Magic)
            return BehaviorParticipationMode.ForcedByOwner;
        if (category == BehaviorSemanticCategory.OwnerCommand)
            return BehaviorParticipationMode.UsuallyCooperative;
        if (category == BehaviorSemanticCategory.OwnerInvitation)
            return BehaviorParticipationMode.StateSensitive;
        return motion.AutonomousBindingEnabled
            ? BehaviorParticipationMode.Autonomous
            : BehaviorParticipationMode.StateSensitive;
    }

    private static BehaviorSemanticCategory CategoryFor(PlayableMotion motion)
    {
        if (motion.Effect is DesktopMotionEffect.BroomFlight or DesktopMotionEffect.Apparate or
            DesktopMotionEffect.Petrify or DesktopMotionEffect.PetrifyRelease or DesktopMotionEffect.Scourgify)
            return BehaviorSemanticCategory.Magic;
        if (motion.Effect == DesktopMotionEffect.CarRide)
            return BehaviorSemanticCategory.OwnerInvitation;
        if (string.Equals(motion.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            return BehaviorSemanticCategory.Explore;
        if (string.Equals(motion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            return BehaviorSemanticCategory.Rest;
        if (string.Equals(motion.AssetBatch, ProneHeadCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(motion.BehaviorId, LifecycleReviewCandidateBehaviorIds.FrontProneLickV4, StringComparison.OrdinalIgnoreCase))
            return BehaviorSemanticCategory.Observe;
        if (string.Equals(motion.AssetBatch, AutonomousDailyCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            return BehaviorSemanticCategory.PostureTransition;
        if (motion.BehaviorId is LifecycleCandidateBehaviorIds.LivelyDailyP2 or LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1)
            return BehaviorSemanticCategory.Rest;
        if (motion.BehaviorId.Contains("idle", StringComparison.OrdinalIgnoreCase) || motion.Phases.All(phase => phase.Loop))
            return BehaviorSemanticCategory.StableIdle;
        if (motion.BehaviorId.StartsWith("wk.command.", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(motion.Category, "口令动作", StringComparison.OrdinalIgnoreCase))
            return BehaviorSemanticCategory.OwnerCommand;
        if (motion.BehaviorId.Contains("eat", StringComparison.OrdinalIgnoreCase))
            return BehaviorSemanticCategory.Food;
        if (motion.BehaviorId.Contains("drink", StringComparison.OrdinalIgnoreCase))
            return BehaviorSemanticCategory.Drink;
        return motion.AutonomousBindingEnabled
            ? BehaviorSemanticCategory.Observe
            : BehaviorSemanticCategory.OwnerInvitation;
    }

    private static IReadOnlySet<BehaviorRequestSource> AllowedSourcesFor(
        PlayableMotion motion,
        BehaviorParticipationMode participation)
    {
        var sources = new HashSet<BehaviorRequestSource> { BehaviorRequestSource.DeveloperPreview };
        if (participation == BehaviorParticipationMode.Autonomous && motion.AutonomousBindingEnabled)
            sources.Add(BehaviorRequestSource.AutonomousTick);
        if (participation is BehaviorParticipationMode.ForcedByOwner or BehaviorParticipationMode.UsuallyCooperative or BehaviorParticipationMode.StateSensitive)
        {
            sources.Add(BehaviorRequestSource.OwnerContextMenu);
            sources.Add(BehaviorRequestSource.ControlPanel);
        }
        return sources;
    }

    private static IReadOnlySet<PetEpisodeKind> EpisodesFor(BehaviorSemanticCategory category) => category switch
    {
        BehaviorSemanticCategory.Rest => Set(PetEpisodeKind.Resting, PetEpisodeKind.Recovering),
        BehaviorSemanticCategory.Observe => Set(PetEpisodeKind.Observing, PetEpisodeKind.Resting),
        BehaviorSemanticCategory.Explore => Set(PetEpisodeKind.Exploring, PetEpisodeKind.Observing),
        BehaviorSemanticCategory.Social => Set(PetEpisodeKind.Socializing),
        _ => Set(PetEpisodeKind.Resting, PetEpisodeKind.Observing, PetEpisodeKind.Exploring, PetEpisodeKind.Socializing, PetEpisodeKind.Recovering)
    };

    private static IReadOnlySet<PetEpisodeKind> Set(params PetEpisodeKind[] values) => values.ToHashSet();

    private static BehaviorEffortLevel EffortFor(BehaviorSemanticCategory category) => category switch
    {
        BehaviorSemanticCategory.Explore or BehaviorSemanticCategory.Magic => BehaviorEffortLevel.High,
        BehaviorSemanticCategory.OwnerCommand or BehaviorSemanticCategory.OwnerInvitation or BehaviorSemanticCategory.PostureTransition => BehaviorEffortLevel.Medium,
        _ => BehaviorEffortLevel.Low
    };

    private static TimeSpan MinimumDwellFor(BehaviorSemanticCategory category) => category switch
    {
        BehaviorSemanticCategory.StableIdle => TimeSpan.FromSeconds(8),
        BehaviorSemanticCategory.Rest => TimeSpan.FromSeconds(12),
        BehaviorSemanticCategory.Explore => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(6)
    };

    private static TimeSpan CooldownFor(BehaviorSemanticCategory category) => category switch
    {
        BehaviorSemanticCategory.StableIdle => TimeSpan.Zero,
        BehaviorSemanticCategory.Observe => TimeSpan.FromSeconds(45),
        BehaviorSemanticCategory.Explore => TimeSpan.FromSeconds(70),
        BehaviorSemanticCategory.Rest => TimeSpan.FromSeconds(60),
        _ => TimeSpan.FromSeconds(20)
    };

    private static double BaseWeightFor(BehaviorSemanticCategory category) => category switch
    {
        BehaviorSemanticCategory.StableIdle => 0.64,
        BehaviorSemanticCategory.Rest => 0.72,
        BehaviorSemanticCategory.Observe => 0.48,
        BehaviorSemanticCategory.Explore => 0.32,
        BehaviorSemanticCategory.PostureTransition => 0.40,
        BehaviorSemanticCategory.Social => 0.34,
        _ => 0.30
    };

    private static PetStateEffects EffectsFor(BehaviorSemanticCategory category) => category switch
    {
        BehaviorSemanticCategory.Rest => new PetStateEffects(Energy: 0.025, Stress: -0.02, Arousal: -0.025, Comfort: 0.02),
        BehaviorSemanticCategory.Explore => new PetStateEffects(Energy: -0.035, Boredom: -0.10, MoodValence: 0.01, Arousal: 0.02),
        BehaviorSemanticCategory.Observe => new PetStateEffects(Energy: -0.004, Boredom: -0.025, Stress: -0.004),
        BehaviorSemanticCategory.PostureTransition => new PetStateEffects(Energy: -0.006),
        BehaviorSemanticCategory.Social => new PetStateEffects(SocialNeed: -0.05, Boredom: -0.03, MoodValence: 0.02),
        BehaviorSemanticCategory.Food => new PetStateEffects(Hunger: -0.35, Energy: 0.04),
        BehaviorSemanticCategory.Drink => new PetStateEffects(Thirst: -0.40),
        _ => new PetStateEffects(Energy: -0.002)
    };

    private static StablePosture PostureFrom(string? value, StablePosture fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (value.Contains("stand", StringComparison.OrdinalIgnoreCase))
            return StablePosture.Stand;
        if (value.Contains("sit", StringComparison.OrdinalIgnoreCase))
            return StablePosture.Sit;
        if (value.Contains("prone", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("sleep", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("lying", StringComparison.OrdinalIgnoreCase))
            return StablePosture.Prone;
        return fallback;
    }
}

public static class DesktopAutonomousEpisodeBindings
{
    private static readonly IReadOnlySet<string> Resting = Set(
        LifecycleCandidateBehaviorIds.StandIdleMicroloop,
        LifecycleCandidateBehaviorIds.SitIdleMicroloop,
        LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
        LifecycleCandidateBehaviorIds.LivelyDailyP2,
        LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1,
        LifecycleReviewCandidateBehaviorIds.StandIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.SitIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
        AutonomousDailyCandidateBehaviorIds.StandToSit,
        AutonomousDailyCandidateBehaviorIds.SitToProne);

    private static readonly IReadOnlySet<string> Observing = Set(
        LifecycleCandidateBehaviorIds.StandIdleMicroloop,
        LifecycleCandidateBehaviorIds.SitIdleMicroloop,
        LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
        LifecycleReviewCandidateBehaviorIds.StandIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.SitIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
        AutonomousDailyCandidateBehaviorIds.StandToSit,
        AutonomousDailyCandidateBehaviorIds.SitToProne,
        ProneHeadCandidateBehaviorIds.HeadLowerTurnV4,
        LifecycleReviewCandidateBehaviorIds.FrontProneLickV4);

    public static IReadOnlySet<string>? For(PetEpisodeKind episode) => episode switch
    {
        PetEpisodeKind.Resting => Resting,
        PetEpisodeKind.Observing => Observing,
        _ => null
    };

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}

public static class DesktopBehaviorOutcomeProfiles
{
    private static readonly IReadOnlyDictionary<string, BehaviorOutcomeProfile> Profiles =
        new[]
        {
            Profile(
                LifecycleCandidateBehaviorIds.LivelyDailyP2,
                StablePosture.Stand,
                "stand.neutral.left_front",
                new PetStateEffects(Energy: -0.06, Hunger: 0.012, Boredom: -0.14, MoodValence: 0.015)),
            Profile(
                LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1,
                StablePosture.Stand,
                "stand.neutral.left_front",
                new PetStateEffects(Energy: -0.055, Boredom: -0.13, MoodValence: 0.015)),
            Profile(
                AutonomousDailyCandidateBehaviorIds.StandToSit,
                StablePosture.Sit,
                "sit.neutral.left_front",
                new PetStateEffects()),
            Profile(
                AutonomousDailyCandidateBehaviorIds.SitToProne,
                StablePosture.Prone,
                "prone.awake.left_front",
                new PetStateEffects()),
            Profile(
                ProneHeadCandidateBehaviorIds.HeadLowerTurnV4,
                StablePosture.Prone,
                "prone.awake.diagonal.high_head.candidate_v4",
                new PetStateEffects()),
            Profile(
                LifecycleReviewCandidateBehaviorIds.FrontProneLickV4,
                StablePosture.Prone,
                "prone.awake.front",
                new PetStateEffects(Energy: -0.004, Boredom: -0.02, MoodValence: 0.002))
        }.ToDictionary(item => item.BehaviorId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> ReducerOwnedBehaviorIds { get; } =
        Profiles.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static BehaviorOutcomeProfile? Find(PlayableMotion motion) =>
        Profiles.TryGetValue(motion.BehaviorId, out var profile) ? profile : null;

    public static BehaviorOutcomeProfile? Find(string behaviorId) =>
        Profiles.TryGetValue(behaviorId, out var profile) ? profile : null;

    private static BehaviorOutcomeProfile Profile(
        string behaviorId,
        StablePosture endPosture,
        string endPoseId,
        PetStateEffects effects) =>
        new(
            behaviorId,
            endPosture,
            endPoseId,
            effects,
            OwnerInteraction: false,
            MemoryEligibility: false,
            PartialEffectPolicy.Proportional);
}
