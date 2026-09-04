using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
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
        var v5ManifestPath = Path.Combine(output, "WukongAssets", "action-batches", SideProneFrontBehaviorIds.AssetBatch, "manifest.json");
        using var v5Document = JsonDocument.Parse(File.ReadAllText(v5ManifestPath));
        var v5Enabled = v5Document.RootElement.GetProperty("runtime_approved").GetBoolean() &&
                        v5Document.RootElement.GetProperty("runtime_use").GetBoolean();

        Assert(candidates.Length == 7, "review catalog must expose exactly seven lifecycle review cards");
        Assert(candidates.Select(x => x.BehaviorId).OrderBy(x => x).SequenceEqual(ExpectedIds.OrderBy(x => x)), "review behavior IDs changed");
        Assert(candidates.Sum(x => x.FrameCount) == (v5Enabled ? 140 : 116), "approved runtime composition frame total does not match the v5 promotion gate");
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
        var expectedPhases = v5Enabled
            ? new[] { "intro", "bridge-to-front", "side-prone-front-calm", "bridge-to-legacy", "exit" }
            : new[] { "intro", "loop", "exit" };
        Assert(full.Phases.Select(x => x.Name).SequenceEqual(expectedPhases), "V3R1 full lifecycle composition does not match the v5 promotion gate");
        Assert(full.FrameCount == (v5Enabled ? 68 : 44), "V3R1 lifecycle frame count does not match the v5 promotion gate");
        Assert(v5Enabled ? full.Phases[2].Loop : full.Phases[1].Loop, "V3R1 lifecycle calm phase must loop");
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
        Assert(selected.Contains(AutonomousDailyCandidateBehaviorIds.StandToSit) ||
               selected.Overlaps(PatrolWalkCandidateBehaviorIds.All),
            "newly approved daily transition or patrol gait never entered deterministic autonomous sampling");
        Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(MockCommandActionIds.Jump), "jump entered the autonomous allowlist");
        Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(MockCommandActionIds.Spin), "spin entered the autonomous allowlist");
        Assert(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1), "V3R1 lifecycle is missing from the autonomous allowlist");
        Assert(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(ProneHeadCandidateBehaviorIds.HeadLowerTurnV4), "prone head V4 is missing from the autonomous allowlist");
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

    public static void RuntimeExtensionsPassWindowsWpfDecode()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var assetRoot = Path.Combine(output, "WukongAssets", "action-batches");
        var v5ManifestPath = Path.Combine(assetRoot, SideProneFrontBehaviorIds.AssetBatch, "manifest.json");
        var roadGazeManifestPath = Path.Combine(assetRoot, CarRideBehaviorIds.RoadGazeAssetBatch, "manifest.json");
        var rejectedRoadGazeV12ManifestPath = Path.Combine(assetRoot, "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v12", "manifest.json");
        var rejectedRoadGazeV11ManifestPath = Path.Combine(assetRoot, "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v11", "manifest.json");
        var rejectedRoadGazeV10ManifestPath = Path.Combine(assetRoot, "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v10", "manifest.json");
        var supersededRoadGazeManifestPath = Path.Combine(assetRoot, "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v9", "manifest.json");
        Assert(File.Exists(v5ManifestPath), "side-prone v5 manifest was not copied to Windows output");
        Assert(File.Exists(roadGazeManifestPath), "road-gaze v13 manifest was not copied to Windows output");
        Assert(File.Exists(rejectedRoadGazeV12ManifestPath), "rejected road-gaze v12 evidence was not copied to Windows output");
        Assert(File.Exists(rejectedRoadGazeV11ManifestPath), "rejected road-gaze v11 evidence was not copied to Windows output");
        Assert(File.Exists(rejectedRoadGazeV10ManifestPath), "rejected road-gaze v10 evidence was not copied to Windows output");
        Assert(File.Exists(supersededRoadGazeManifestPath), "superseded road-gaze v9 evidence was not copied to Windows output");

        using var v5Document = JsonDocument.Parse(File.ReadAllText(v5ManifestPath));
        var v5 = v5Document.RootElement;
        Assert(v5.GetProperty("frame_count").GetInt32() == 36, "side-prone v5 must contain 36 runtime frames");
        Assert(v5.GetProperty("owner_runtime_enable_requested").GetBoolean(), "side-prone v5 owner enable request was lost");
        var v5VisualApproved = v5.GetProperty("visual_approved").GetBoolean();
        var v5Approved = v5.GetProperty("runtime_approved").GetBoolean();
        Assert(v5Approved == v5.GetProperty("runtime_use").GetBoolean(), "side-prone v5 runtime gates diverged");
        Assert(v5Approved == v5.GetProperty("production_asset").GetBoolean(), "side-prone v5 production gate diverged");
        Assert(v5Approved == v5.GetProperty("autonomous_binding_enabled").GetBoolean(), "side-prone v5 autonomous gate diverged");
        var expectedV5Validation = v5Approved
            ? "passed_windows_renderer_qa"
            : v5VisualApproved
                ? "pending_windows_renderer_ci"
                : "pending_owner_visual_review_and_windows_renderer_ci";
        Assert(v5.GetProperty("runtime_validation").GetString() == expectedV5Validation,
            "side-prone v5 validation state does not match its visual/runtime gates");
        Assert(!v5Approved || v5VisualApproved, "side-prone v5 runtime approval bypassed owner visual approval");

        var v5Frames = v5.GetProperty("phases")
            .EnumerateArray()
            .SelectMany(phase => phase.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("path").GetString()!))
            .ToArray();
        Assert(v5Frames.Length == 36, "side-prone v5 phase inventory changed");
        DecodeFrames(Path.GetDirectoryName(v5ManifestPath)!, v5Frames, "side-prone v5");

        using var roadGazeDocument = JsonDocument.Parse(File.ReadAllText(roadGazeManifestPath));
        var roadGaze = roadGazeDocument.RootElement;
        Assert(roadGaze.GetProperty("asset_id").GetString() == CarRideBehaviorIds.RoadGazeAssetBatch, "road-gaze v13 asset id changed");
        Assert(roadGaze.GetProperty("status").GetString() == "runtime_candidate_owner_visual_qa_pending", "road-gaze v13 review status changed");
        Assert(roadGaze.GetProperty("runtime_validation").GetString() == "pending_owner_windows_renderer_qa", "road-gaze v13 validation gate changed");
        Assert(!roadGaze.GetProperty("visual_approved").GetBoolean(), "road-gaze v13 claimed owner visual approval");
        Assert(!roadGaze.GetProperty("owner_runtime_enable_requested").GetBoolean(), "road-gaze v13 claimed owner enablement");
        Assert(!roadGaze.GetProperty("runtime_approved").GetBoolean(), "road-gaze v13 entered approved runtime");
        Assert(!roadGaze.GetProperty("runtime_use").GetBoolean(), "road-gaze v13 entered normal runtime");
        Assert(roadGaze.GetProperty("prototype_use").GetBoolean(), "road-gaze v13 cannot enter explicit local review");
        Assert(!roadGaze.GetProperty("production_asset").GetBoolean(), "road-gaze v13 was marked production");
        Assert(roadGaze.GetProperty("pixel_policy").GetProperty("generation_strategy").GetString() is string strategy &&
               strategy.Contains("complete dog", StringComparison.Ordinal),
            "road-gaze v13 did not record complete-scene generation");
        Assert(!roadGaze.GetProperty("pixel_policy").GetProperty("local_region_composite_used").GetBoolean(),
            "road-gaze v13 reintroduced local compositing");
        Assert(!roadGaze.GetProperty("pixel_policy").GetProperty("head_only_edit_used").GetBoolean(),
            "road-gaze v13 reintroduced a head-only edit");

        var roadGazeFrames = roadGaze.GetProperty("sequences")
            .EnumerateObject()
            .SelectMany(sequence => sequence.Value.EnumerateArray().Select(frame => frame.GetProperty("path").GetString()!))
            .ToArray();
        Assert(roadGazeFrames.Length == 36, "road-gaze v13 must contain 36 review frames");
        DecodeFrames(Path.GetDirectoryName(roadGazeManifestPath)!, roadGazeFrames, "road-gaze v13");

        using var rejectedV12Document = JsonDocument.Parse(File.ReadAllText(rejectedRoadGazeV12ManifestPath));
        var rejectedV12 = rejectedV12Document.RootElement;
        Assert(rejectedV12.GetProperty("status").GetString() == "failed_owner_visual_qa_identity_consistency",
            "road-gaze v12 owner rejection was not preserved");
        Assert(rejectedV12.GetProperty("runtime_validation").GetString() == "failed_owner_visual_qa",
            "road-gaze v12 failed validation state changed");
        Assert(!rejectedV12.GetProperty("runtime_approved").GetBoolean() &&
               !rejectedV12.GetProperty("runtime_use").GetBoolean() &&
               !rejectedV12.GetProperty("prototype_use").GetBoolean(),
            "road-gaze v12 escaped its failed gate");

        using var rejectedV11Document = JsonDocument.Parse(File.ReadAllText(rejectedRoadGazeV11ManifestPath));
        var rejectedV11 = rejectedV11Document.RootElement;
        Assert(rejectedV11.GetProperty("status").GetString() == "superseded_owner_visual_qa_failed",
            "road-gaze v11 owner rejection was not recorded");
        Assert(rejectedV11.GetProperty("superseded_by").GetString() == "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v12",
            "road-gaze v11 does not point to v12");
        Assert(!rejectedV11.GetProperty("visual_approved").GetBoolean() &&
               !rejectedV11.GetProperty("runtime_approved").GetBoolean() &&
               !rejectedV11.GetProperty("runtime_use").GetBoolean() &&
               !rejectedV11.GetProperty("prototype_use").GetBoolean(),
            "road-gaze v11 can still enter a playback path");

        using var rejectedV10Document = JsonDocument.Parse(File.ReadAllText(rejectedRoadGazeV10ManifestPath));
        var rejectedV10 = rejectedV10Document.RootElement;
        Assert(rejectedV10.GetProperty("status").GetString() == "superseded_owner_visual_qa_failed",
            "road-gaze v10 owner rejection was not recorded");
        Assert(rejectedV10.GetProperty("superseded_by").GetString() == "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v11",
            "road-gaze v10 does not point to v11");
        Assert(!rejectedV10.GetProperty("visual_approved").GetBoolean() &&
               !rejectedV10.GetProperty("runtime_approved").GetBoolean() &&
               !rejectedV10.GetProperty("runtime_use").GetBoolean() &&
               !rejectedV10.GetProperty("prototype_use").GetBoolean(),
            "road-gaze v10 can still enter a playback path");

        using var supersededDocument = JsonDocument.Parse(File.ReadAllText(supersededRoadGazeManifestPath));
        var superseded = supersededDocument.RootElement;
        Assert(superseded.GetProperty("status").GetString() == "superseded_visual_rework_required", "road-gaze v9 was not superseded");
        Assert(!superseded.GetProperty("visual_approved").GetBoolean(), "road-gaze v9 retained visual approval");
        Assert(!superseded.GetProperty("runtime_approved").GetBoolean() &&
               !superseded.GetProperty("runtime_use").GetBoolean() &&
               !superseded.GetProperty("prototype_use").GetBoolean(),
            "road-gaze v9 can still enter a playback path");

        var catalog = DesktopMotionCatalog.Load(output);
        var car = catalog.Motions.Single(x => x.BehaviorId == CarRideBehaviorIds.CarRide);
        var hasRoadGaze = car.NamedSequences?.ContainsKey("road-gaze/left") == true &&
                          car.NamedSequences.ContainsKey("road-gaze/right");
        Assert(!hasRoadGaze, "road-gaze v13 escaped its normal runtime gate");
    }

    public static void RoadGazeReviewMarkerOpensOnlyPendingCandidate()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var marker = Path.Combine(output, DesktopMotionCatalog.RoadGazeReviewMarkerFileName);
        Assert(!File.Exists(marker), "normal test output unexpectedly contains the local road-gaze review marker");

        var normalCatalog = DesktopMotionCatalog.Load(output);
        var normalCar = normalCatalog.Motions.Single(x => x.BehaviorId == CarRideBehaviorIds.CarRide);
        Assert(!normalCatalog.CarRideRoadGazeReviewEnabled, "normal catalog enabled local road-gaze review");
        Assert(normalCar.NamedSequences?.Keys.All(x => !x.StartsWith("road-gaze/", StringComparison.OrdinalIgnoreCase)) == true,
            "pending road-gaze frames entered the normal catalog");

        try
        {
            File.WriteAllText(marker, "local_windows_review_only=true\n", System.Text.Encoding.UTF8);
            var reviewCatalog = DesktopMotionCatalog.Load(output);
            var reviewCar = reviewCatalog.Motions.Single(x => x.BehaviorId == CarRideBehaviorIds.CarRide);
            Assert(reviewCatalog.CarRideRoadGazeReviewEnabled, "local review marker state was not reported");
            Assert(reviewCatalog.LoadSummary.Contains(CarRideBehaviorIds.RoadGazeAssetBatch, StringComparison.Ordinal),
                "startup load summary omitted the actual road-gaze manifest");
            Assert(reviewCar.NamedSequences?.ContainsKey("road-gaze/left") == true &&
                   reviewCar.NamedSequences.ContainsKey("road-gaze/right"),
                "local marker did not open the pending v13 road-gaze candidate");
            Assert(reviewCar.NamedSequenceFrameDurations?.ContainsKey("road-gaze/left") == true &&
                   reviewCar.NamedSequenceFrameDurations.ContainsKey("road-gaze/right"),
                "local marker loaded v13 frames without their declared timing");
            var roadGazeDurations = reviewCar.NamedSequenceFrameDurations!;
            Assert(roadGazeDurations["road-gaze/left"].Sum() == 2770 &&
                   roadGazeDurations["road-gaze/right"].Sum() == 2770,
                "v13 road-gaze timing no longer matches the reviewed manifest");
        }
        finally
        {
            if (File.Exists(marker))
                File.Delete(marker);
        }

        var restoredCatalog = DesktopMotionCatalog.Load(output);
        var restoredCar = restoredCatalog.Motions.Single(x => x.BehaviorId == CarRideBehaviorIds.CarRide);
        Assert(!restoredCatalog.CarRideRoadGazeReviewEnabled &&
               restoredCar.NamedSequences?.Keys.All(x => !x.StartsWith("road-gaze/", StringComparison.OrdinalIgnoreCase)) == true,
            "removing the local review marker did not restore the production gate");
    }

    private static void DecodeFrames(string root, IEnumerable<string> relativePaths, string label)
    {
        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"{label} frame is missing: {relative}");
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"{label} frame dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains("a", StringComparison.OrdinalIgnoreCase), $"{label} frame lost alpha: {relative}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
