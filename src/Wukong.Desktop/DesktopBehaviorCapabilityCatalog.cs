using Wukong.Application;
using Wukong.Domain;

namespace Wukong.Desktop;

public sealed record DesktopBehaviorDefinition(
    string BehaviorId, BehaviorSemanticCategory Category, BehaviorEffortLevel Effort,
    IReadOnlySet<PetEpisodeKind> Episodes, IReadOnlySet<BehaviorRequestSource> Sources,
    BehaviorOutcomeProfile? Outcome, string? AssetBatch = null,
    string PreferenceGroup = "neutral", double PreferenceMultiplier = 1)
{
    public bool IsStablePresentation => Category == BehaviorSemanticCategory.StableIdle;
    public bool AutonomousAllowed => Sources.Contains(BehaviorRequestSource.AutonomousTick);
}

// One semantic table. Asset loaders own approval and frame validation; these
// definitions can restrict their gates but cannot approve or enable an asset.
public static class DesktopBehaviorDefinitionCatalog
{
    private static readonly IReadOnlyDictionary<string, DesktopBehaviorDefinition> Definitions = Build();
    public static IReadOnlyCollection<DesktopBehaviorDefinition> All => Definitions.Values.ToArray();
    public static DesktopBehaviorDefinition? Find(string id) => Definitions.GetValueOrDefault(id);
    public static bool IsAutonomous(string id) => Find(id)?.AutonomousAllowed == true;
    public static IReadOnlySet<string> ForEpisode(PetEpisodeKind episode) => Definitions.Values
        .Where(item => item.AutonomousAllowed && item.Episodes.Contains(episode))
        .Select(item => item.BehaviorId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, DesktopBehaviorDefinition> Build()
    {
        var definitions = new Dictionary<string, DesktopBehaviorDefinition>(StringComparer.OrdinalIgnoreCase);
        var resting = new[] { PetEpisodeKind.Resting, PetEpisodeKind.Observing, PetEpisodeKind.Recovering };
        var idleEpisodes = resting.Append(PetEpisodeKind.Exploring).ToArray();
        void Add(string id, BehaviorSemanticCategory category, StablePosture end, string pose,
            PetStateEffects effects, PetEpisodeKind[]? episodes = null, bool owner = false,
            BehaviorEffortLevel effort = BehaviorEffortLevel.Low, string? batch = null,
            string preference = "neutral", double multiplier = 1, bool dialogue = false)
        {
            var sources = new HashSet<BehaviorRequestSource> { BehaviorRequestSource.DeveloperPreview };
            if (episodes is { Length: > 0 }) sources.Add(BehaviorRequestSource.AutonomousTick);
            if (owner || category == BehaviorSemanticCategory.StableIdle)
                sources.UnionWith(new[] { BehaviorRequestSource.OwnerUi, BehaviorRequestSource.OwnerContextMenu, BehaviorRequestSource.ControlPanel });
            if (owner || dialogue) sources.Add(BehaviorRequestSource.OwnerDialogue);
            if (episodes is { Length: > 0 }) sources.Add(BehaviorRequestSource.ControlPanel);
            var outcome = category == BehaviorSemanticCategory.StableIdle ? null : new BehaviorOutcomeProfile(
                id, end, pose, effects, owner, MemoryEligibility: true, PartialEffectPolicy.Proportional);
            definitions.Add(id, new(id, category, effort, (episodes ?? Array.Empty<PetEpisodeKind>()).ToHashSet(),
                sources, outcome, batch, preference, multiplier));
        }
        void Idle(string id, StablePosture posture, string pose, string batch, double multiplier = 1) =>
            Add(id, BehaviorSemanticCategory.StableIdle, posture, pose, new(), idleEpisodes,
                batch: batch, preference: posture.ToString().ToLowerInvariant(), multiplier: multiplier);
        Idle(LifecycleCandidateBehaviorIds.StandIdleMicroloop, StablePosture.Stand, "stand.neutral.left_front", LifecycleCandidateBehaviorIds.AssetBatch);
        Idle(LifecycleCandidateBehaviorIds.SitIdleMicroloop, StablePosture.Sit, "sit.neutral.left_front", LifecycleCandidateBehaviorIds.AssetBatch);
        Idle(LifecycleCandidateBehaviorIds.ProneIdleMicroloop, StablePosture.Prone, "prone.awake.left_front", LifecycleCandidateBehaviorIds.AssetBatch, .45);
        Idle(LifecycleReviewCandidateBehaviorIds.StandIdleV3R1, StablePosture.Stand, "stand.neutral.left_front", LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch);
        Idle(LifecycleReviewCandidateBehaviorIds.SitIdleV3R1, StablePosture.Sit, "sit.neutral.left_front", LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch);
        Idle(LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1, StablePosture.Prone, "prone.awake.left_front", LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch, .45);
        Idle(LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4, StablePosture.Prone, "prone.awake.front", LifecycleReviewCandidateBehaviorIds.V4AssetBatch);
        Add(Phase15BehaviorIds.ProneIdle, BehaviorSemanticCategory.StableIdle, StablePosture.Prone, "prone.awake.left_front", new());
        Add(Phase15BehaviorIds.ProneBreath, BehaviorSemanticCategory.Observe, StablePosture.Prone, "prone.awake.left_front", new(Energy: -.004), owner: true);
        Add(Phase15BehaviorIds.SafeStand, BehaviorSemanticCategory.PostureTransition, StablePosture.Stand, "stand.neutral.left_front", new(Energy: -.004), owner: true);
        Add(Phase15BehaviorIds.LookAround, BehaviorSemanticCategory.Observe, StablePosture.Prone, "prone.awake.left_front", new(Energy: -.004), owner: true);
        Add(Phase15BehaviorIds.StrokeEnjoy, BehaviorSemanticCategory.Social, StablePosture.Prone, "prone.awake.left_front", new(Energy: -.004), owner: true);
        Add(LifecycleCandidateBehaviorIds.LivelyDailyP2, BehaviorSemanticCategory.Rest, StablePosture.Stand,
            "stand.neutral.left_front", new(Energy: -.06, Hunger: .012, Boredom: -.14, MoodValence: .015),
            new[] { PetEpisodeKind.Resting }, batch: LifecycleCandidateBehaviorIds.AssetBatch, preference: "prone", multiplier: .20);
        Add(LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1, BehaviorSemanticCategory.Rest, StablePosture.Stand,
            "stand.neutral.left_front", new(Energy: -.055, Boredom: -.13, MoodValence: .015),
            new[] { PetEpisodeKind.Resting }, batch: LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch, preference: "prone", multiplier: .20);
        Add(LifecycleReviewCandidateBehaviorIds.LivelyDailyExitV3R1, BehaviorSemanticCategory.PostureTransition,
            StablePosture.Stand, "stand.neutral.left_front", new(Energy: -.004), batch: LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch);
        Add(AutonomousDailyCandidateBehaviorIds.StandToSit, BehaviorSemanticCategory.PostureTransition, StablePosture.Sit,
            "sit.neutral.left_front", new(), resting, effort: BehaviorEffortLevel.Medium, batch: AutonomousDailyCandidateBehaviorIds.AssetBatch, preference: "prone");
        Add(AutonomousDailyCandidateBehaviorIds.SitToProne, BehaviorSemanticCategory.PostureTransition, StablePosture.Prone,
            "prone.awake.left_front", new(), resting, effort: BehaviorEffortLevel.Medium, batch: AutonomousDailyCandidateBehaviorIds.AssetBatch, preference: "prone");
        Add(AutonomousDailyCandidateBehaviorIds.ProneToSit, BehaviorSemanticCategory.PostureTransition, StablePosture.Sit,
            "sit.neutral.left_front", new(), new[] { PetEpisodeKind.Exploring }, effort: BehaviorEffortLevel.Medium, batch: AutonomousDailyCandidateBehaviorIds.AssetBatch);
        Add(AutonomousDailyCandidateBehaviorIds.SitToStand, BehaviorSemanticCategory.PostureTransition, StablePosture.Stand,
            "stand.neutral.left_front", new(), new[] { PetEpisodeKind.Exploring }, effort: BehaviorEffortLevel.Medium, batch: AutonomousDailyCandidateBehaviorIds.AssetBatch);
        Add(ProneHeadCandidateBehaviorIds.HeadLowerTurnV4, BehaviorSemanticCategory.Observe, StablePosture.Prone,
            "prone.awake.diagonal.high_head.candidate_v4", new(), new[] { PetEpisodeKind.Observing }, batch: ProneHeadCandidateBehaviorIds.AssetBatch, preference: "prone");
        Add(LifecycleReviewCandidateBehaviorIds.FrontProneLickV4, BehaviorSemanticCategory.Observe, StablePosture.Prone,
            "prone.awake.front", new(Energy: -.004, Boredom: -.02, MoodValence: .002), new[] { PetEpisodeKind.Observing }, batch: LifecycleReviewCandidateBehaviorIds.V4AssetBatch);
        Add(FrontProneExpressionBehaviorIds.SatisfiedSmile, BehaviorSemanticCategory.Observe, StablePosture.Prone,
            "prone.awake.front", new(Stress: -.004, MoodValence: .004), new[] { PetEpisodeKind.Observing, PetEpisodeKind.Resting }, batch: FrontProneExpressionBehaviorIds.AssetBatch, multiplier: 1.20);
        Add(FrontProneExpressionBehaviorIds.CuriousObserve, BehaviorSemanticCategory.Observe, StablePosture.Prone,
            "prone.awake.front", new(Boredom: -.008), new[] { PetEpisodeKind.Observing, PetEpisodeKind.Resting }, batch: FrontProneExpressionBehaviorIds.AssetBatch, multiplier: 1.05);
        Add(FrontProneExpressionBehaviorIds.KnowingLook, BehaviorSemanticCategory.Observe, StablePosture.Prone,
            "prone.awake.front", new(Boredom: -.006), new[] { PetEpisodeKind.Observing, PetEpisodeKind.Resting }, batch: FrontProneExpressionBehaviorIds.AssetBatch, multiplier: .90);
        Add(ProneHappyHotPantingBehaviorIds.HappyHotPanting, BehaviorSemanticCategory.Observe, StablePosture.Prone,
            "prone.awake.front", new(Energy: -.006, Boredom: -.014, Stress: -.006, MoodValence: .006),
            new[] { PetEpisodeKind.Observing, PetEpisodeKind.Resting }, batch: ProneHappyHotPantingBehaviorIds.AssetBatch, multiplier: .85);
        Add(StandingHappyExpectantBehaviorIds.HappyExpectant, BehaviorSemanticCategory.Observe, StablePosture.Stand,
            "stand.neutral.left_front", new(SocialNeed: -.012, Boredom: -.012, Stress: -.004, MoodValence: .006),
            new[] { PetEpisodeKind.Observing, PetEpisodeKind.Socializing }, batch: StandingHappyExpectantBehaviorIds.AssetBatch);
        Add(SleepCandidateBehaviorIds.MainLifecycle, BehaviorSemanticCategory.Rest, StablePosture.Prone,
            "sleep.side.stable", new(Energy: .08, Stress: -.04, Arousal: -.08, Comfort: .04),
            new[] { PetEpisodeKind.Sleeping }, batch: SleepCandidateBehaviorIds.AssetBatch, preference: "sleep", dialogue: true);
        Add(SleepCandidateBehaviorIds.SprawledFrontBreath, BehaviorSemanticCategory.Rest, StablePosture.Prone,
            "sleep.prone.sprawled.front", new(Energy: .04, Stress: -.025, Arousal: -.04, Comfort: .025),
            new[] { PetEpisodeKind.Sleeping }, batch: SleepCandidateBehaviorIds.AssetBatch, preference: "sleep", dialogue: true);
        foreach (var (id, pose) in new[]
        {
            (SleepCandidateBehaviorIds.ProneToSideRoll, "sleep.side.stable"),
            (SleepCandidateBehaviorIds.SprawledLeftSideBreath, "sleep.side.sprawled.left"),
            (SleepCandidateBehaviorIds.SprawledRightSideBreath, "sleep.side.sprawled.right"),
            (SleepCandidateBehaviorIds.CompactProneBreath, "sleep.prone.compact"),
            (SleepCandidateBehaviorIds.CurledSideBreath, "sleep.side.curled")
        }) Add(id, BehaviorSemanticCategory.Rest, StablePosture.Prone, pose, new(Energy: -.004), owner: true, batch: SleepCandidateBehaviorIds.AssetBatch);
        foreach (var (id, pose) in new[] { (PatrolWalkCandidateBehaviorIds.WalkLeft, "stand.walk.left"), (PatrolWalkCandidateBehaviorIds.WalkRight, "stand.walk.right") })
            Add(id, BehaviorSemanticCategory.Explore, StablePosture.Stand, pose, new(Energy: -.025, Boredom: -.08, MoodValence: .01),
                new[] { PetEpisodeKind.Exploring }, effort: BehaviorEffortLevel.High, batch: PatrolWalkCandidateBehaviorIds.AssetBatch, preference: "walk", dialogue: true);
        Add(FoodWaterCandidateBehaviorIds.EatKibbleStandingV5, BehaviorSemanticCategory.Food, StablePosture.Stand,
            "stand.neutral.left_front", new(Energy: .04, Hunger: -.35, Boredom: -.02), owner: true, batch: FoodWaterCandidateBehaviorIds.AssetBatch);
        Add(FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5, BehaviorSemanticCategory.Drink, StablePosture.Stand,
            "stand.neutral.left_front", new(Thirst: -.40, Stress: -.01), owner: true, batch: FoodWaterCandidateBehaviorIds.AssetBatch);
        void Command(string id, StablePosture end, string pose, PetStateEffects effects, BehaviorEffortLevel effort) =>
            Add(id, BehaviorSemanticCategory.OwnerCommand, end, pose, effects, owner: true, effort: effort, batch: CommandMockBehaviorIds.AssetBatch);
        Command(MockCommandActionIds.Sit, StablePosture.Sit, "sit.neutral.left_front", new(Energy: -.004, SocialNeed: -.01), BehaviorEffortLevel.Low);
        Command(MockCommandActionIds.Down, StablePosture.Prone, "prone.awake.left_front", new(Energy: -.003, Comfort: .008), BehaviorEffortLevel.Low);
        Command(MockCommandActionIds.PawSit, StablePosture.Sit, "sit.neutral.left_front", new(Energy: -.008, SocialNeed: -.025, MoodValence: .006), BehaviorEffortLevel.Medium);
        Command(MockCommandActionIds.PawProne, StablePosture.Prone, "prone.awake.left_front", new(Energy: -.006, SocialNeed: -.025, MoodValence: .006), BehaviorEffortLevel.Medium);
        Command(MockCommandActionIds.Jump, StablePosture.Stand, "stand.neutral.left_front", new(Energy: -.075, Boredom: -.08, Arousal: .04, MoodValence: .01), BehaviorEffortLevel.High);
        Command(MockCommandActionIds.Spin, StablePosture.Stand, "stand.neutral.left_front", new(Energy: -.065, Boredom: -.07, Arousal: .035, MoodValence: .008), BehaviorEffortLevel.High);
        Command(MockCommandActionIds.EatSit, StablePosture.Sit, "sit.neutral.left_front", new(Hunger: -.10, SocialNeed: -.01), BehaviorEffortLevel.Medium);
        Command(MockCommandActionIds.EatProne, StablePosture.Prone, "prone.awake.front", new(Hunger: -.10, SocialNeed: -.01), BehaviorEffortLevel.Medium);
        void Wake(string id, StablePosture end, string pose, PetStateEffects effects, bool restingAllowed = false) =>
            Add(id, BehaviorSemanticCategory.PostureTransition, end, pose, effects,
                restingAllowed ? new[] { PetEpisodeKind.Recovering, PetEpisodeKind.Resting } : new[] { PetEpisodeKind.Recovering },
                effort: BehaviorEffortLevel.Medium, batch: WakeRiseCandidateBehaviorIds.AssetBatch);
        Wake(WakeRiseCandidateBehaviorIds.SideWake, StablePosture.Prone, "prone.awake.left_front", new(Energy: .010, Arousal: .035));
        Wake(WakeRiseCandidateBehaviorIds.SideInterrupt, StablePosture.Prone, "prone.awake.left_front", new(Energy: .005, Stress: .004, Arousal: .040));
        Wake(WakeRiseCandidateBehaviorIds.LowInterrupt, StablePosture.Prone, "prone.awake.left_front", new(Energy: .004, Stress: .003, Arousal: .030));
        Wake(WakeRiseCandidateBehaviorIds.FrontWake, StablePosture.Prone, "prone.awake.front", new(Energy: .008, Arousal: .030));
        Wake(WakeRiseCandidateBehaviorIds.FrontRiseSit, StablePosture.Sit, "sit.neutral.left_front", new(Energy: -.008, Arousal: .010), true);
        Wake(WakeRiseCandidateBehaviorIds.FrontSitStand, StablePosture.Stand, "stand.neutral.left_front", new(Energy: -.009, Arousal: .012), true);
        Wake(WakeRiseCandidateBehaviorIds.FrontRiseFull, StablePosture.Stand, "stand.neutral.left_front", new(Energy: -.016, Arousal: .016), true);
        Add(CarRideBehaviorIds.CarRide, BehaviorSemanticCategory.OwnerInvitation, StablePosture.Stand, "stand.neutral.right",
            new(Energy: -.03, Boredom: -.10, MoodValence: .02), owner: true, effort: BehaviorEffortLevel.Medium, batch: CarRideBehaviorIds.AssetBatch);
        foreach (var id in MagicBehaviorIds.PrototypeWhitelist)
            Add(id, BehaviorSemanticCategory.Magic, StablePosture.Prone, "prone.awake.left_front",
                id == MagicBehaviorIds.AccioBroom ? new(Energy: -.035, Boredom: -.08, Arousal: .03) : new(Energy: -.004),
                owner: true, effort: BehaviorEffortLevel.High, batch: MagicBehaviorIds.AssetBatch);
        return definitions;
    }
}

public static class DesktopBehaviorCapabilityCatalog
{
    public static BehaviorCapabilityCatalog Create(IEnumerable<PlayableMotion> motions, AutonomyPolicyProfile? policy = null) =>
        new(motions.Select(motion => Project(motion, policy ?? AutonomyPolicyProfile.Default)));
    private static BehaviorCapability Project(PlayableMotion motion, AutonomyPolicyProfile policy)
    {
        var definition = DesktopBehaviorDefinitionCatalog.Find(motion.BehaviorId);
        var category = definition?.Category ?? BehaviorSemanticCategory.OwnerInvitation;
        var tuning = policy.Categories[category];
        var start = PostureFromPose(motion.StartPose);
        var end = definition?.Outcome?.EndPosture ?? PostureFromPose(motion.EndPose);
        var known = definition is not null &&
            (definition.AssetBatch is null || definition.AssetBatch.Equals(motion.AssetBatch, StringComparison.OrdinalIgnoreCase));
        var autonomous = known && definition!.AutonomousAllowed && motion.AutonomousBindingEnabled;
        var sources = new HashSet<BehaviorRequestSource>(definition?.Sources ?? new HashSet<BehaviorRequestSource> { BehaviorRequestSource.DeveloperPreview });
        if (!autonomous) sources.Remove(BehaviorRequestSource.AutonomousTick);
        var participation = category switch
        {
            BehaviorSemanticCategory.Magic => BehaviorParticipationMode.ForcedByOwner,
            BehaviorSemanticCategory.OwnerCommand => BehaviorParticipationMode.UsuallyCooperative,
            _ => autonomous ? BehaviorParticipationMode.Autonomous : BehaviorParticipationMode.StateSensitive
        };
        return new(motion.BehaviorId, category, participation,
            motion.Interruptible ? BehaviorInterruptionPolicy.SafePreempt : BehaviorInterruptionPolicy.WaitForSafePoint,
            sources, new HashSet<StablePosture> { start }, end, definition?.Effort ?? BehaviorEffortLevel.Low,
            definition?.Episodes ?? new HashSet<PetEpisodeKind>(),
            known && motion.EffectiveRuntimeApproved && !motion.IsExpired,
            known && motion.RuntimeEnabled && !motion.IsExpired,
            known && motion.RuntimeEnabled && motion.EffectiveRuntimeApproved && !motion.IsExpired,
            autonomous, motion.WindowMotionEnabled, TimeSpan.FromSeconds(tuning.MinimumDwellSeconds),
            TimeSpan.FromSeconds(tuning.CooldownSeconds), tuning.BaseWeight,
            definition?.Outcome?.StateEffects ?? new PetStateEffects())
        {
            StartPoseFamily = PetPoseCompatibility.FamilyFor(motion.StartPose, start),
            EndPoseId = definition?.Outcome?.EndPoseId ?? motion.EndPose
        };
    }
    public static StablePosture PostureFromPose(string pose) => pose.Split('.')[0].ToLowerInvariant() switch
    {
        "stand" or "standing" => StablePosture.Stand,
        "sit" or "sitting" => StablePosture.Sit,
        _ => StablePosture.Prone
    };
}

// Compatibility projections; neither owns a second behavior table.
public static class DesktopAutonomousEpisodeBindings
{
    public static IReadOnlySet<string> For(PetEpisodeKind episode) => DesktopBehaviorDefinitionCatalog.ForEpisode(episode);
    public static IReadOnlySet<PetEpisodeKind> AvailableEpisodes(IBehaviorCapabilityCatalog catalog, PetRuntimeState runtime)
    {
        var available = new HashSet<PetEpisodeKind> { PetEpisodeKind.Resting, PetEpisodeKind.Recovering };
        foreach (var capability in catalog.Capabilities.Where(item => item.ProductionApproved && item.RuntimeUse &&
                     item.ProductionAsset && item.AutonomousBindingEnabled && item.AllowedSources.Contains(BehaviorRequestSource.AutonomousTick) &&
                     item.Category != BehaviorSemanticCategory.StableIdle && item.StartPostures.Contains(runtime.CurrentPosture) &&
                     PetPoseCompatibility.IsCompatible(item.StartPoseFamily, runtime)))
            available.UnionWith(capability.AllowedEpisodes);
        return available;
    }
}

public static class DesktopBehaviorOutcomeProfiles
{
    public static IReadOnlySet<string> ReducerOwnedBehaviorIds => DesktopBehaviorDefinitionCatalog.All
        .Where(item => item.Outcome is not null).Select(item => item.BehaviorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static BehaviorOutcomeProfile? Find(PlayableMotion motion) => Find(motion.BehaviorId);
    public static BehaviorOutcomeProfile? Find(string behaviorId) => DesktopBehaviorDefinitionCatalog.Find(behaviorId)?.Outcome;
}
