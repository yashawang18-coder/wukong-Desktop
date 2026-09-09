using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class BehaviorAgentRolloutTests
{
    public static void DefaultRolloutPromotesOnlyResting()
    {
        var runtime = new DesktopRuntimeHost();
        Assert(runtime.EnableBehaviorAgentShadow, "agent shadow comparison is disabled");
        Assert(runtime.AuthoritativeAgentEpisodes.SetEquals(new[] { PetEpisodeKind.Resting }),
            "default rollout must make only Resting authoritative");
        Assert(!runtime.AuthoritativeAgentEpisodes.Contains(PetEpisodeKind.Observing),
            "Observing was promoted before its independent acceptance gate");
    }

    public static void EpisodeBindingsUseExplicitSafeAllowlists()
    {
        var motionCatalog = DesktopMotionCatalog.Load(AppContext.BaseDirectory);
        var capabilityCatalog = DesktopBehaviorCapabilityCatalog.Create(motionCatalog.Motions);

        AssertBindings(PetEpisodeKind.Resting, capabilityCatalog,
            BehaviorSemanticCategory.StableIdle,
            BehaviorSemanticCategory.Rest,
            BehaviorSemanticCategory.PostureTransition);
        AssertBindings(PetEpisodeKind.Observing, capabilityCatalog,
            BehaviorSemanticCategory.StableIdle,
            BehaviorSemanticCategory.Observe,
            BehaviorSemanticCategory.PostureTransition);

        var resting = DesktopAutonomousEpisodeBindings.For(PetEpisodeKind.Resting)!;
        var observing = DesktopAutonomousEpisodeBindings.For(PetEpisodeKind.Observing)!;
        Assert(!resting.Contains(ProneHeadCandidateBehaviorIds.HeadLowerTurnV4),
            "Observing microevent leaked into Resting rollout");
        Assert(observing.Contains(ProneHeadCandidateBehaviorIds.HeadLowerTurnV4),
            "approved head-turn microevent is absent from Observing rollout");
        Assert(observing.Contains(LifecycleReviewCandidateBehaviorIds.FrontProneLickV4),
            "approved lick microevent is absent from Observing rollout");
    }

    public static void TenThousandAutonomousDecisionsNeverSelectForbiddenCapabilities()
    {
        var motionCatalog = DesktopMotionCatalog.Load(AppContext.BaseDirectory);
        var capabilityCatalog = DesktopBehaviorCapabilityCatalog.Create(motionCatalog.Motions);
        var engine = new BehaviorDecisionEngine();
        var now = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < 10_000; index++)
        {
            var episode = index % 2 == 0 ? PetEpisodeKind.Resting : PetEpisodeKind.Observing;
            var poseId = index % 4 < 2 ? "prone.awake.left_front" : "prone.awake.front";
            var state = PetAgentState.CreateDefault(now) with
            {
                Episode = new PetEpisodeState(episode, now.Subtract(TimeSpan.FromMinutes(2)), TimeSpan.Zero, "test"),
                Runtime = PetRuntimeState.Default with
                {
                    CurrentPosture = StablePosture.Prone,
                    CurrentPoseId = poseId,
                    Curiosity = episode == PetEpisodeKind.Observing ? 0.85 : 0.35,
                    IsBusy = false
                }
            };
            var allowlist = DesktopAutonomousEpisodeBindings.For(episode)
                ?? throw new InvalidOperationException($"missing allowlist for {episode}");
            var decision = engine.Decide(state, capabilityCatalog, new BehaviorDecisionInput(
                BehaviorRequestSource.AutonomousTick,
                now,
                LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
                now.Subtract(TimeSpan.FromMinutes(2)),
                true,
                new Dictionary<string, DateTimeOffset>(),
                Array.Empty<string>(),
                index,
                false,
                true,
                allowlist));

            if (decision.SelectedBehaviorId is null)
                continue;
            Assert(allowlist.Contains(decision.SelectedBehaviorId), "decision escaped the Episode allowlist");
            var selected = capabilityCatalog.Find(decision.SelectedBehaviorId)
                ?? throw new InvalidOperationException("selected capability is absent from catalog");
            Assert(selected.ParticipationMode == BehaviorParticipationMode.Autonomous,
                $"non-autonomous behavior selected: {selected.BehaviorId}");
            Assert(selected.Category is not (
                    BehaviorSemanticCategory.OwnerCommand or
                    BehaviorSemanticCategory.OwnerInvitation or
                    BehaviorSemanticCategory.Food or
                    BehaviorSemanticCategory.Drink or
                    BehaviorSemanticCategory.Magic or
                    BehaviorSemanticCategory.Explore),
                $"forbidden autonomous category selected: {selected.Category}");
        }
    }

    public static void ReducerOwnedProfilesKeepActionSpecificOutcomes()
    {
        var p2 = DesktopBehaviorOutcomeProfiles.Find(LifecycleCandidateBehaviorIds.LivelyDailyP2)
            ?? throw new InvalidOperationException("P2 lifecycle reducer profile missing");
        var v3 = DesktopBehaviorOutcomeProfiles.Find(LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1)
            ?? throw new InvalidOperationException("V3R1 lifecycle reducer profile missing");
        var headTurn = DesktopBehaviorOutcomeProfiles.Find(ProneHeadCandidateBehaviorIds.HeadLowerTurnV4)
            ?? throw new InvalidOperationException("head-turn reducer profile missing");

        Assert(p2.StateEffects.Energy != v3.StateEffects.Energy,
            "action-specific lifecycle effects were flattened into a category default");
        Assert(p2.EndPosture == StablePosture.Stand && v3.EndPosture == StablePosture.Stand,
            "lifecycle end posture changed during reducer migration");
        Assert(headTurn.EndPosture == StablePosture.Prone && headTurn.EndPoseId.Contains("prone", StringComparison.Ordinal),
            "observing microevent lost its compatible prone terminal pose");
        Assert(DesktopBehaviorOutcomeProfiles.ReducerOwnedBehaviorIds.SetEquals(new[]
        {
            LifecycleCandidateBehaviorIds.LivelyDailyP2,
            LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1,
            AutonomousDailyCandidateBehaviorIds.StandToSit,
            AutonomousDailyCandidateBehaviorIds.SitToProne,
            ProneHeadCandidateBehaviorIds.HeadLowerTurnV4,
            LifecycleReviewCandidateBehaviorIds.FrontProneLickV4
        }), "reducer ownership expanded beyond the reviewed Resting/Observing batch");
    }

    private static void AssertBindings(
        PetEpisodeKind episode,
        BehaviorCapabilityCatalog catalog,
        params BehaviorSemanticCategory[] allowedCategories)
    {
        var bindings = DesktopAutonomousEpisodeBindings.For(episode)
            ?? throw new InvalidOperationException($"missing allowlist for {episode}");
        Assert(bindings.Count > 0, $"{episode} allowlist is empty");
        foreach (var behaviorId in bindings)
        {
            var capability = catalog.Find(behaviorId)
                ?? throw new InvalidOperationException($"allowlisted capability missing: {behaviorId}");
            Assert(capability.ParticipationMode == BehaviorParticipationMode.Autonomous,
                $"allowlist contains non-autonomous behavior: {behaviorId}");
            Assert(capability.AllowedSources.Contains(BehaviorRequestSource.AutonomousTick),
                $"allowlist item does not allow AutonomousTick: {behaviorId}");
            Assert(allowedCategories.Contains(capability.Category),
                $"{episode} allowlist contains category {capability.Category}: {behaviorId}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
