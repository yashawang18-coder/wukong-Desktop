using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class BehaviorTruthRuntimeTests
{
    public static void DialogueAndMenuCommandsUseTheSameRuntimePath()
    {
        var menu = new DesktopRuntimeHost();
        var dialogue = new DesktopRuntimeHost();
        PetMotionRequest? dialogueRequest = null;
        dialogue.MotionRequested += (_, request) => dialogueRequest = request;
        menu.UpdateBehaviorAgentMock(TemperamentProfile.Default,
            PetRuntimeState.Default with { CurrentPosture = StablePosture.Stand, CurrentPoseId = "stand.neutral.left_front" },
            RelationshipState.Default, 42);
        dialogue.UpdateBehaviorAgentMock(TemperamentProfile.Default,
            PetRuntimeState.Default with { CurrentPosture = StablePosture.Stand, CurrentPoseId = "stand.neutral.left_front" },
            RelationshipState.Default, 42);

        var menuResult = menu.SubmitOwnerCommandAsync("坐", BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
        var dialogueResult = dialogue.SubmitDialogueIntentAsync("坐下").GetAwaiter().GetResult();

        Assert(menuResult == PetActionResult.Accepted, "menu command was not accepted");
        Assert(dialogueResult.Result == PetActionResult.Accepted, "dialogue command was not accepted");
        Assert(dialogueResult.Intent.IntentId == "command.sit", "dialogue did not normalize to sit intent");
        Assert(menu.CurrentBehaviorId == dialogue.CurrentBehaviorId, "menu and dialogue selected different actions");
        Assert(dialogueRequest?.Source == BehaviorRequestSource.OwnerDialogue,
            "explicit owner chat was not separated from model dialogue source");
        Assert(dialogueResult.Act?.Type == DialogueActType.BehaviorAccepted, "accepted command did not produce an accepted dialogue act");
    }

    public static void DialogueCommitmentRequiresAStartedRequest()
    {
        var runtime = new DesktopRuntimeHost();
        var result = runtime.SubmitDialogueIntentAsync("去喝水").GetAwaiter().GetResult();
        Assert(result.Recognized, "drink request was not recognized");
        Assert(result.Result == PetActionResult.Accepted, "drink request was not accepted through its posture plan");
        Assert(result.RequestId is not null, "accepted request did not expose lifecycle identity");
        Assert(result.Act is { Type: DialogueActType.BehaviorAccepted }, "accepted request did not create a truthful act");
        Assert(runtime.BuildDialogueTruthSnapshot().ActiveRequestId == result.RequestId,
            "dialogue commitment was emitted before the request became active");
    }

    public static void DialogueSleepUsesApprovedPosturePreparation()
    {
        var runtime = new DesktopRuntimeHost();
        var requests = new List<PetMotionRequest>();
        runtime.MotionRequested += (_, request) => requests.Add(request);
        runtime.UpdateBehaviorAgentMock(
            TemperamentProfile.Default,
            PetRuntimeState.Default with { CurrentPosture = StablePosture.Stand, CurrentPoseId = "stand.neutral.left_front" },
            RelationshipState.Default,
            91);

        var result = runtime.SubmitDialogueIntentAsync("悟空，去睡觉吧").GetAwaiter().GetResult();
        Assert(result is { Recognized: true, Result: PetActionResult.Accepted }, "sleep intent was not accepted");
        Assert(requests.Count == 1 && requests[0].Motion.BehaviorId == AutonomousDailyCandidateBehaviorIds.StandToSit,
            "standing sleep did not begin with the approved stand-to-sit transition");
        runtime.CompleteMotion(requests[0].RequestId, requests[0].Motion.BehaviorId, "exit");
        Assert(requests.Last().Motion.BehaviorId == AutonomousDailyCandidateBehaviorIds.SitToProne,
            "sleep preparation omitted the approved sit-to-prone transition");
        runtime.CompleteMotion(requests.Last().RequestId, requests.Last().Motion.BehaviorId, "exit");
        Assert(requests.Last() is { Source: BehaviorRequestSource.OwnerDialogue, ExecutionMode: BehaviorExecutionMode.Normal } &&
               requests.Last().Motion.BehaviorId == SleepCandidateBehaviorIds.MainLifecycle,
            "sleep preparation did not reach the Normal sleep lifecycle");

        var statistics = runtime.BehaviorMechanisms.Single(item => item.BehaviorId == SleepCandidateBehaviorIds.MainLifecycle);
        Assert(statistics.SessionTriggerCount == 1 && statistics.TriggerSources.Contains("对话 1", StringComparison.Ordinal),
            "session action statistics did not record the real dialogue sleep request");
    }

    public static void DialogueWalkUsesApprovedPosturePreparationAndSpaceGate()
    {
        var runtime = new DesktopRuntimeHost();
        var requests = new List<PetMotionRequest>();
        runtime.MotionRequested += (_, request) => requests.Add(request);
        runtime.UpdateBehaviorAgentMock(
            TemperamentProfile.Default,
            PetRuntimeState.Default with { CurrentPosture = StablePosture.Prone, CurrentPoseId = "prone.awake.left_front" },
            RelationshipState.Default,
            92);

        var noSpace = runtime.SubmitDialogueIntentAsync("去走一走").GetAwaiter().GetResult();
        Assert(noSpace.Result == PetActionResult.Deferred && runtime.CurrentDecisionDetail.Contains("walk_workspace_unavailable", StringComparison.Ordinal),
            "walk intent ignored the desktop travel-space gate");

        runtime.UpdatePatrolTravelSpace(1000, 1000, 50);
        var result = runtime.SubmitDialogueIntentAsync("去走一走").GetAwaiter().GetResult();
        Assert(result is { Recognized: true, Result: PetActionResult.Accepted }, "walk intent was not accepted");
        Assert(requests.Count == 1 && requests[0].Motion.BehaviorId == AutonomousDailyCandidateBehaviorIds.ProneToSit,
            "prone walk did not begin with the approved prone-to-sit transition");
        runtime.CompleteMotion(requests[0].RequestId, requests[0].Motion.BehaviorId, "exit");
        Assert(requests.Last().Motion.BehaviorId == AutonomousDailyCandidateBehaviorIds.SitToStand,
            "walk preparation omitted the approved sit-to-stand transition");
        runtime.CompleteMotion(requests.Last().RequestId, requests.Last().Motion.BehaviorId, "exit");
        Assert(requests.Last() is { Source: BehaviorRequestSource.OwnerDialogue, ExecutionMode: BehaviorExecutionMode.Normal } &&
               PatrolWalkCandidateBehaviorIds.All.Contains(requests.Last().Motion.BehaviorId),
            "walk preparation did not reach the Normal patrol lifecycle");
    }

    public static void FalseAutonomousSpeechIsReplacedByCurrentFact()
    {
        var runtime = new DesktopRuntimeHost();
        var validation = runtime.ValidateDialogueReply("我正在散步");
        Assert(!validation.IsValid && validation.UsedSafeFallback, "false walking statement was displayed");
        Assert(validation.Text.Contains("趴", StringComparison.Ordinal), "fallback did not use the live prone posture");
    }

    public static void ContinuityRolloutOwnsDailyEpisodes()
    {
        var rollout = AutonomousAgentRolloutOptions.ContinuityV1;
        Assert(rollout.IsAuthoritative(PetEpisodeKind.Resting), "Resting is not authoritative");
        Assert(rollout.IsAuthoritative(PetEpisodeKind.Observing), "Observing is not authoritative");
        Assert(rollout.IsAuthoritative(PetEpisodeKind.Exploring), "Exploring is not authoritative");
        Assert(rollout.IsAuthoritative(PetEpisodeKind.Recovering), "Recovering is not authoritative");
        Assert(rollout.IsAuthoritative(PetEpisodeKind.Sleeping), "Sleeping is not authoritative");
        Assert(!rollout.LegacyFallbackOnInfrastructureFailure, "authoritative episode can fall through to random legacy selection");
    }

    public static void NormalOwnerCommandUsesReducerLifecycle()
    {
        var runtime = new DesktopRuntimeHost();
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        runtime.UpdateBehaviorAgentMock(
            TemperamentProfile.Default,
            PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Stand,
                CurrentPoseId = "stand.neutral.left_front",
                Energy = 0.70,
                Stress = 0.10
            },
            RelationshipState.Default,
            19);

        var result = runtime.SubmitOwnerCommandAsync("坐", BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
        Assert(result == PetActionResult.Accepted, "owner command was not admitted");
        var captured = request ?? throw new InvalidOperationException("owner command did not emit a motion request");
        Assert(captured is { ExecutionMode: BehaviorExecutionMode.Normal, TracksAgentLifecycle: true },
            "normal owner command bypassed the reducer-owned execution lifecycle");
        Assert(captured.RequestId != Guid.Empty, "reducer lifecycle request did not carry an execution identity");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
