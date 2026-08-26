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

    public static void RuntimeExtensionsPassWindowsWpfDecode()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var assetRoot = Path.Combine(output, "WukongAssets", "action-batches");
        var v5ManifestPath = Path.Combine(assetRoot, SideProneFrontBehaviorIds.AssetBatch, "manifest.json");
        var roadGazeManifestPath = Path.Combine(assetRoot, CarRideBehaviorIds.RoadGazeAssetBatch, "manifest.json");
        Assert(File.Exists(v5ManifestPath), "side-prone v5 manifest was not copied to Windows output");
        Assert(File.Exists(roadGazeManifestPath), "road-gaze v9 manifest was not copied to Windows output");

        using var v5Document = JsonDocument.Parse(File.ReadAllText(v5ManifestPath));
        var v5 = v5Document.RootElement;
        Assert(v5.GetProperty("frame_count").GetInt32() == 36, "side-prone v5 must contain 36 runtime frames");
        Assert(v5.GetProperty("visual_approved").GetBoolean(), "side-prone v5 owner visual approval request was lost");
        Assert(v5.GetProperty("owner_runtime_enable_requested").GetBoolean(), "side-prone v5 owner enable request was lost");
        var v5Approved = v5.GetProperty("runtime_approved").GetBoolean();
        Assert(v5Approved == v5.GetProperty("runtime_use").GetBoolean(), "side-prone v5 runtime gates diverged");
        Assert(v5Approved == v5.GetProperty("production_asset").GetBoolean(), "side-prone v5 production gate diverged");
        Assert(v5Approved == v5.GetProperty("autonomous_binding_enabled").GetBoolean(), "side-prone v5 autonomous gate diverged");
        Assert(
            v5.GetProperty("runtime_validation").GetString() == (v5Approved ? "passed_windows_renderer_qa" : "pending_windows_renderer_ci"),
            "side-prone v5 validation state does not match its runtime gate");

        var v5Frames = v5.GetProperty("phases")
            .EnumerateArray()
            .SelectMany(phase => phase.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("path").GetString()!))
            .ToArray();
        Assert(v5Frames.Length == 36, "side-prone v5 phase inventory changed");
        DecodeFrames(Path.GetDirectoryName(v5ManifestPath)!, v5Frames, "side-prone v5");

        using var roadGazeDocument = JsonDocument.Parse(File.ReadAllText(roadGazeManifestPath));
        var roadGaze = roadGazeDocument.RootElement;
        Assert(roadGaze.GetProperty("visual_approved").GetBoolean(), "road-gaze v9 owner visual approval request was lost");
        Assert(roadGaze.GetProperty("owner_runtime_enable_requested").GetBoolean(), "road-gaze v9 owner enable request was lost");
        var roadGazeApproved = roadGaze.GetProperty("runtime_approved").GetBoolean();
        Assert(roadGazeApproved == roadGaze.GetProperty("runtime_use").GetBoolean(), "road-gaze v9 runtime gates diverged");
        Assert(roadGazeApproved == roadGaze.GetProperty("production_asset").GetBoolean(), "road-gaze v9 production gate diverged");
        Assert(
            roadGaze.GetProperty("runtime_validation").GetString() == (roadGazeApproved ? "passed_windows_renderer_qa" : "pending_windows_renderer_ci"),
            "road-gaze v9 validation state does not match its runtime gate");

        var roadGazeFrames = roadGaze.GetProperty("sequences")
            .EnumerateObject()
            .SelectMany(sequence => sequence.Value.EnumerateArray().Select(frame => frame.GetProperty("path").GetString()!))
            .ToArray();
        Assert(roadGazeFrames.Length == 36, "road-gaze v9 must contain 36 runtime frames");
        DecodeFrames(Path.GetDirectoryName(roadGazeManifestPath)!, roadGazeFrames, "road-gaze v9");

        var catalog = DesktopMotionCatalog.Load(output);
        var car = catalog.Motions.Single(x => x.BehaviorId == CarRideBehaviorIds.CarRide);
        var hasRoadGaze = car.NamedSequences?.ContainsKey("road-gaze/left") == true &&
                          car.NamedSequences.ContainsKey("road-gaze/right");
        Assert(hasRoadGaze == roadGazeApproved, "road-gaze v9 catalog exposure does not match its runtime gate");
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
