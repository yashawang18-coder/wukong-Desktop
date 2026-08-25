using System.IO;
using System.Text.Json;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class LifecycleReviewCandidateTests
{
    private static readonly string[] ExpectedIds =
    {
        LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1,
        LifecycleReviewCandidateBehaviorIds.LivelyDailyExitV3R1,
        LifecycleReviewCandidateBehaviorIds.StandIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.SitIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1,
        LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
        LifecycleReviewCandidateBehaviorIds.FrontProneLickV4
    };

    public static void CatalogKeepsApprovedProfilesSeparate()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var catalog = DesktopMotionCatalog.Load(output);
        var candidates = catalog.Motions
            .Where(x => LifecycleReviewCandidateBehaviorIds.AssetBatches.Contains(x.AssetBatch))
            .OrderBy(x => x.BehaviorId)
            .ToArray();

        Assert(candidates.Length == 7, "review catalog must expose exactly seven lifecycle review cards");
        Assert(candidates.Select(x => x.BehaviorId).OrderBy(x => x).SequenceEqual(ExpectedIds.OrderBy(x => x)), "review behavior IDs changed");
        Assert(candidates.Sum(x => x.FrameCount) == 116, "approved runtime composition must reference 116 phase frames across seven entries");
        Assert(candidates.All(x => x.Category == "基础动作"), "approved lifecycle material escaped the basic-action category");
        Assert(candidates.All(x => x.RuntimeEnabled && x.RuntimeApproved && !x.PrototypeUse), "approved runtime gates were not loaded");
        Assert(candidates.All(x => x.AutonomousBindingEnabled), "an approved lifecycle entry is missing its autonomous binding");
        Assert(candidates.All(x => x.CandidateProfile == "runtime_approved_autonomous_daily"), "approved lifecycle profile changed");
        Assert(candidates.All(x => x.Description.Contains("expired_pixel_contribution=false", StringComparison.Ordinal)), "expired-pixel exclusion is missing");

        var v3 = candidates.Where(x => x.AssetBatch == LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch).ToArray();
        var v4 = candidates.Where(x => x.AssetBatch == LifecycleReviewCandidateBehaviorIds.V4AssetBatch).ToArray();
        Assert(v3.Length == 5 && v4.Length == 2, "V3R1 and V4 review profiles were not kept separate");
        Assert(v3.All(x => !x.StartPose.Equals("prone.awake.front", StringComparison.OrdinalIgnoreCase) && !x.EndPose.Equals("prone.awake.front", StringComparison.OrdinalIgnoreCase)), "V3R1 was mislabeled as forward prone");
        Assert(v4.All(x => x.StartPose == "prone.awake.front" && x.EndPose == "prone.awake.front"), "V4 left the forward-prone posture");

        var lick = candidates.Single(x => x.BehaviorId == LifecycleReviewCandidateBehaviorIds.FrontProneLickV4);
        Assert(lick.Phases.Count == 1 && !lick.Phases[0].Loop && lick.FrameCount == 12, "V4 lick must be a one-shot 12-frame microevent");
        var calm = candidates.Single(x => x.BehaviorId == LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4);
        Assert(calm.Phases.Count == 1 && calm.Phases[0].Loop && calm.FrameCount == 12, "V4 calm must remain an independent loop");
        var full = candidates.Single(x => x.BehaviorId == LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1);
        Assert(full.Phases.Select(x => x.Name).SequenceEqual(new[] { "intro", "loop", "exit" }), "V3R1 full lifecycle composition changed");
        Assert(full.Phases[1].Loop && full.FrameCount == 44, "V3R1 must use its approved 20/12/12 intro-loop-exit composition");
    }

    public static void DeveloperPreviewUsesTheExistingBehaviorRequestPath()
    {
        var runtime = new DesktopRuntimeHost();
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;

        var result = runtime.SubmitDeveloperCandidateMotionAsync(LifecycleReviewCandidateBehaviorIds.FrontProneLickV4)
            .GetAwaiter().GetResult();

        Assert(result == PetActionResult.Accepted, "developer review request was not accepted");
        Assert(request is not null, "developer review did not emit a motion request");
        Assert(request!.ExecutionMode == BehaviorExecutionMode.DeveloperPreview, "review bypassed DeveloperPreview isolation");
        Assert(request.Motion.BehaviorId == LifecycleReviewCandidateBehaviorIds.FrontProneLickV4, "wrong review action was selected");
        Assert(request.Motion.RuntimeEnabled && request.Motion.RuntimeApproved && !request.Motion.PrototypeUse, "developer preview changed approved runtime gates");
    }

    public static void ManifestsAndPanelShowApprovedRuntimeState()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        foreach (var batch in LifecycleReviewCandidateBehaviorIds.AssetBatches)
        {
            var manifestPath = Path.Combine(output, "WukongAssets", "action-batches", batch, "runtime-review-manifest.json");
            Assert(File.Exists(manifestPath), $"review manifest was not copied: {batch}");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            Assert(root.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "Windows renderer approval was not recorded");
            Assert(root.GetProperty("runtime_approved").GetBoolean(), "runtime approval was not opened");
            Assert(root.GetProperty("runtime_use").GetBoolean(), "runtime use was not opened");
            Assert(root.GetProperty("production_asset").GetBoolean(), "approved lifecycle batch is not a production asset");
            Assert(root.GetProperty("visual_approved").GetBoolean(), "owner visual approval was not recorded");
            foreach (var action in root.GetProperty("actions").EnumerateArray())
            {
                Assert(action.GetProperty("visual_approved").GetBoolean(), "reviewed action lost visual approval");
                Assert(action.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "action runtime renderer state changed");
                Assert(action.GetProperty("runtime_approved").GetBoolean() && action.GetProperty("runtime_use").GetBoolean(), "action runtime gate is closed");
                Assert(action.GetProperty("autonomous_binding_enabled").GetBoolean(), "approved action is missing autonomous binding");
                Assert(action.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "AutonomousTick", "DeveloperPreview" }), "approved action source policy changed");
            }
        }

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Wukong.Desktop", "ControlPanelWindow.xaml"));
        Assert(xaml.Contains("x:Name=\"LifecycleReviewCandidateList\"", StringComparison.Ordinal), "approved lifecycle list is missing");
        Assert(xaml.Contains("两组姿态仍保持独立", StringComparison.Ordinal) && xaml.Contains("禁止硬切拼接", StringComparison.Ordinal), "no-hard-splice warning is missing");
        Assert(xaml.Contains("runtime_approved=true / runtime_use=true", StringComparison.Ordinal), "approved runtime gate state is not visible");
        Assert(xaml.Contains("x:Name=\"CarRideCandidateList\"", StringComparison.Ordinal), "car ride developer area was removed");
        Assert(xaml.Contains("x:Name=\"LifecycleCandidateList\"", StringComparison.Ordinal), "V2 lifecycle developer area was removed");
    }

    public static void AutonomousTicksUseApprovedDailyAllowlistWithoutCommands()
    {
        var runtime = new DesktopRuntimeHost();
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        var nextDecision = typeof(DesktopRuntimeHost).GetField(
            "_nextAutonomousDecisionAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var currentStartedAt = typeof(DesktopRuntimeHost).GetField(
            "_currentStartedAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 128; attempt++)
        {
            runtime.UpdateBehaviorAgentMock(
                TemperamentProfile.Default,
                PetRuntimeState.Default with { CurrentPosture = StablePosture.Stand },
                RelationshipState.Default,
                seed: 260826 + attempt);
            runtime.StartIdle("review_isolation_test");
            nextDecision.SetValue(runtime, DateTimeOffset.MinValue);
            currentStartedAt.SetValue(runtime, DateTimeOffset.MinValue);
            request = null;
            runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
            if (request is null)
                continue;
            selected.Add(request.Motion.BehaviorId);
            Assert(request.Motion.BehaviorId != MockCommandActionIds.Jump, "autonomous tick selected jump");
            Assert(request.Motion.BehaviorId != MockCommandActionIds.Spin, "autonomous tick selected spin");
            runtime.CompleteMotion(request.Motion.BehaviorId, "exit");
        }

        Assert(selected.Contains(LifecycleReviewCandidateBehaviorIds.StandIdleV3R1) ||
               selected.Contains(LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1),
            "approved V3R1 material never entered the autonomous pool");
        Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(MockCommandActionIds.Jump), "jump entered the autonomous allowlist");
        Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(MockCommandActionIds.Spin), "spin entered the autonomous allowlist");
        Assert(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1), "V3R1 lifecycle is missing from the autonomous allowlist");
    }

    public static void ForwardProneProfileRequiresMatchingApprovedAnchor()
    {
        var runtime = new DesktopRuntimeHost();
        runtime.UpdateBehaviorAgentMock(
            TemperamentProfile.Default,
            PetRuntimeState.Default with { CurrentPosture = StablePosture.Prone },
            RelationshipState.Default,
            seed: 1508);
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;

        runtime.StartIdle("generic_prone");
        Assert(request is not null && request.Motion.BehaviorId == LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
            "generic prone posture hard-cut into the V4 forward-prone profile");

        request = null;
        var result = runtime.SubmitBehaviorAgentCommandAsync(OwnerCommandKind.Eat, BehaviorRequestSource.ControlPanel)
            .GetAwaiter().GetResult();
        Assert(result == PetActionResult.Accepted, "approved prone eat command was not accepted");
        Assert(request is not null && request.Motion.BehaviorId == MockCommandActionIds.EatProne,
            "prone eat did not select the exact V4 anchor source action");

        runtime.CompleteMotion(MockCommandActionIds.EatProne, "exit");
        var holdId = request!.Motion.BehaviorId;
        Assert(holdId.StartsWith("wk.runtime.posture_hold.", StringComparison.Ordinal), "command terminal hold was skipped");
        runtime.CompleteMotion(holdId, "loop");
        Assert(request.Motion.BehaviorId == LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
            "matching EatProne anchor did not enter the approved V4 calm profile");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
