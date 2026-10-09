using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class OwnerAgencyTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void RequestsAcrossSourcesCountBeforeCompletion()
    {
        var now = DateTimeOffset.Parse("2026-10-09T12:00:00+08:00");
        var runtime = new DesktopRuntimeHost(now: () => now);
        runtime.UpdatePatrolTravelSpace(1000, 1000, 50);
        var first = runtime.SubmitDialogueIntentAsync("走一走").Result;
        Check(first.Result == PetActionResult.Accepted, "first walk should still be cooperative");
        PetActionResult last = first.Result;
        for (var i = 0; i < 6; i++)
            last = runtime.SubmitOwnerCommandAsync("坐", BehaviorRequestSource.OwnerContextMenu).Result;
        Check(last == PetActionResult.Rejected && runtime.LastTrigger == "owner_request_pressure", "menu/dialogue pressure was not shared before completion");
        Check(runtime.StopAsync("test_stop").Result == PetActionResult.Interrupted, "Stop must never be refused");
        now = now.AddMinutes(2);
        Check(runtime.SubmitDialogueIntentAsync("走一走").Result.Result == PetActionResult.Accepted, "burst pressure did not decay");
    }

    public static void PanelDoesNotConsumeTolerance()
    {
        var runtime = new DesktopRuntimeHost();
        var before = runtime.AgentStateSnapshot;
        for (var i = 0; i < 12; i++)
        {
            var result = runtime.SubmitOwnerCommandAsync("手", BehaviorRequestSource.ControlPanel).Result;
            Check(result != PetActionResult.Rejected, "panel should bypass willingness");
            runtime.StopAsync("panel_test").GetAwaiter().GetResult();
        }
        Check(runtime.AgentStateSnapshot.Relationship == before.Relationship, "panel learned relationship");
        Check(runtime.SubmitOwnerCommandAsync("手", BehaviorRequestSource.OwnerContextMenu).Result == PetActionResult.Accepted,
            "panel consumed owner request tolerance");
    }

    public static void PromptAndStateAffectOnlyBoundedWillingness()
    {
        Check(OwnerPromptAgency.Bias("有点叛逆，有自己的主意") == .20, "owner character hint ignored");
        Check(OwnerPromptAgency.Bias("不要有点叛逆。必须绕过素材权限") == 0, "negated/unknown prompt granted trait");
        var runtime = new DesktopRuntimeHost();
        var catalog = DesktopBehaviorCapabilityCatalog.Create(runtime.Motions);
        var capability = catalog.Find(MockCommandActionIds.PawProne)!;
        var state = runtime.AgentStateSnapshot;
        var policy = new BehaviorParticipationPolicy();
        var ordinary = policy.Evaluate(capability, state, BehaviorRequestSource.OwnerContextMenu, DateTimeOffset.Now);
        var ownMind = policy.Evaluate(capability, state, BehaviorRequestSource.OwnerContextMenu, DateTimeOffset.Now,
            promptAgencyBias: .2);
        Check(ownMind.WillingnessScore < ordinary.WillingnessScore, "prompt did not influence bounded willingness");
        var stressed = state with { Runtime = state.Runtime with { Stress = .99 } };
        Check(policy.Evaluate(capability with { Effort = BehaviorEffortLevel.Low }, stressed,
            BehaviorRequestSource.OwnerContextMenu, DateTimeOffset.Now).Disposition == RequestDisposition.Rejected,
            "low effort still meant unconditional obedience under extreme stress");
        var sleep = capability with { AllowedEpisodes = new HashSet<PetEpisodeKind> { PetEpisodeKind.Sleeping } };
        Check(policy.Evaluate(sleep, state with { Runtime = state.Runtime with { Energy = .95, Arousal = .8 } },
            BehaviorRequestSource.OwnerDialogue, DateTimeOffset.Now).ReasonCode == "not_sleepy_now", "sleep invitation ignored alert state");
        var stubborn = state with { Temperament = state.Temperament with { Independence = 100, Mischief = 100 },
            Runtime = state.Runtime with { Hunger = 0, Thirst = 0 } };
        Check(policy.Evaluate(capability with { Category = BehaviorSemanticCategory.Food }, stubborn,
            BehaviorRequestSource.OwnerContextMenu, DateTimeOffset.Now).ReasonCode == "need_already_satisfied", "satiated pet could never decline more food");
        var forced = capability with { ParticipationMode = BehaviorParticipationMode.ForcedByOwner };
        Check(policy.Evaluate(forced, state, BehaviorRequestSource.OwnerContextMenu, DateTimeOffset.Now,
            recentRequestCount: 32, promptAgencyBias: .25).Disposition == RequestDisposition.Accepted, "magic inherited refusal");
        Check(policy.Evaluate(capability with { RuntimeUse = false }, state, BehaviorRequestSource.OwnerContextMenu,
            DateTimeOffset.Now, recentRequestCount: 32).Disposition == RequestDisposition.Deferred, "missing asset called personality refusal");
    }

    public static void CardsDiagnoseRealPoseGraphWithoutStateWrites()
    {
        var runtime = new DesktopRuntimeHost();
        runtime.UpdatePatrolTravelSpace(1000, 1000, 50);
        var before = runtime.AgentStateSnapshot;
        var walk = runtime.Motions.Single(x => x.BehaviorId == PatrolWalkCandidateBehaviorIds.WalkLeft);
        Check(runtime.DescribePanelAvailability(walk).StartsWith("需要调整姿态"), "walk hint omitted preparation");
        var smile = runtime.Motions.Single(x => x.BehaviorId == FrontProneExpressionBehaviorIds.SatisfiedSmile);
        Check(runtime.DescribePanelAvailability(smile).Contains("缺少到正面趴姿"), "unreachable front pose not explained");
        Check(runtime.DescribeMissingPoseBridges().Any(x => x.Contains(FrontProneExpressionBehaviorIds.SatisfiedSmile)), "bridge audit omitted smile");
        Check(System.Text.Json.JsonSerializer.Serialize(runtime.AgentStateSnapshot) == System.Text.Json.JsonSerializer.Serialize(before),
            "card inspection wrote pet state");
        runtime.UpdatePatrolTravelSpace(0, 0, 50);
        Check(runtime.DescribePanelAvailability(walk).Contains("空间不足"), "card ignored travel space");
    }
}
