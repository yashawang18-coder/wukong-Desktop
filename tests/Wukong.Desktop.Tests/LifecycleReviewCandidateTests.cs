using System.IO;
using System.Text.Json;
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

    public static void CatalogKeepsBothProfilesSeparateAndGated()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var catalog = DesktopMotionCatalog.Load(output);
        var candidates = catalog.Motions
            .Where(x => LifecycleReviewCandidateBehaviorIds.AssetBatches.Contains(x.AssetBatch))
            .OrderBy(x => x.BehaviorId)
            .ToArray();

        Assert(candidates.Length == 7, "review catalog must expose exactly seven lifecycle review cards");
        Assert(candidates.Select(x => x.BehaviorId).OrderBy(x => x).SequenceEqual(ExpectedIds.OrderBy(x => x)), "review behavior IDs changed");
        Assert(candidates.Sum(x => x.FrameCount) == 92, "review frame count must remain 92");
        Assert(candidates.All(x => x.Category == "基础动作候审"), "review candidates escaped the isolated category");
        Assert(candidates.All(x => !x.RuntimeEnabled && !x.PrototypeUse), "review candidate opened a runtime or prototype gate");
        Assert(candidates.All(x => x.CandidateProfile == "production_candidate_owner_qa_pending"), "review stage changed");
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
        Assert(!request.Motion.RuntimeEnabled && !request.Motion.PrototypeUse, "developer review mutated candidate gates");
    }

    public static void ManifestsAndPanelKeepReviewGatesClosed()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        foreach (var batch in LifecycleReviewCandidateBehaviorIds.AssetBatches)
        {
            var manifestPath = Path.Combine(output, "WukongAssets", "action-batches", batch, "runtime-review-manifest.json");
            Assert(File.Exists(manifestPath), $"review manifest was not copied: {batch}");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            Assert(root.GetProperty("runtime_validation").GetString() == "owner_visual_qa_passed_runtime_behavior_pending", "owner visual QA state was not recorded");
            Assert(!root.GetProperty("runtime_approved").GetBoolean(), "runtime approval was opened");
            Assert(!root.GetProperty("runtime_use").GetBoolean(), "runtime use was opened");
            Assert(!root.GetProperty("production_asset").GetBoolean(), "candidate became a production asset");
            Assert(root.GetProperty("visual_approved").GetBoolean(), "owner visual approval was not recorded");
            foreach (var action in root.GetProperty("actions").EnumerateArray())
            {
                Assert(action.GetProperty("visual_approved").GetBoolean(), "reviewed action lost visual approval");
                Assert(action.GetProperty("runtime_validation").GetString() == "owner_visual_qa_passed_runtime_behavior_pending", "action runtime state did not remain pending behavior QA");
                Assert(!action.GetProperty("autonomous_binding_enabled").GetBoolean(), "candidate entered autonomous bindings");
                Assert(action.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "DeveloperPreview" }), "candidate source policy changed");
            }
        }

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Wukong.Desktop", "ControlPanelWindow.xaml"));
        Assert(xaml.Contains("x:Name=\"LifecycleReviewCandidateList\"", StringComparison.Ordinal), "基础动作候审 list is missing");
        Assert(xaml.Contains("V3R1 侧身生命周期与 V4 正向趴姿为两组独立候审素材", StringComparison.Ordinal), "no-hard-splice warning is missing");
        Assert(xaml.Contains("runtime_approved=false / runtime_use=false", StringComparison.Ordinal), "review gate state is not visible");
        Assert(xaml.Contains("x:Name=\"CarRideCandidateList\"", StringComparison.Ordinal), "car ride developer area was removed");
        Assert(xaml.Contains("x:Name=\"LifecycleCandidateList\"", StringComparison.Ordinal), "V2 lifecycle developer area was removed");
    }

    public static void AutonomousTicksNeverSelectReviewCandidates()
    {
        var runtime = new DesktopRuntimeHost();
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        var nextDecision = typeof(DesktopRuntimeHost).GetField(
            "_nextAutonomousDecisionAt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        for (var attempt = 0; attempt < 16; attempt++)
        {
            runtime.StartIdle("review_isolation_test");
            nextDecision.SetValue(runtime, DateTimeOffset.MinValue);
            request = null;
            runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
            if (request is null)
                continue;
            Assert(!LifecycleReviewCandidateBehaviorIds.AssetBatches.Contains(request.Motion.AssetBatch), "autonomous tick selected an owner-QA candidate");
            runtime.CompleteMotion(request.Motion.BehaviorId, "exit");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
