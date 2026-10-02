using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Desktop;
using Wukong.Domain;
using Wukong.Application;

internal static class PatrolWalkCandidateTests
{
    public static void ManifestFramesAndGateAreValid()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var root = Path.Combine(output, "WukongAssets", "action-batches", PatrolWalkCandidateBehaviorIds.AssetBatch);
        using var asset = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "asset.json")));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        Assert(asset.RootElement.GetRawText() == document.RootElement.GetRawText(), "asset/manifest drift");
        var manifest = document.RootElement;
        Assert(manifest.GetProperty("owner_preview_approved").GetBoolean(), "owner art approval missing");
        Assert(manifest.GetProperty("visual_approved").GetBoolean(), "visual approval missing");
        Assert(manifest.GetProperty("source_png_byte_identity").GetBoolean(), "source byte preservation missing");
        Assert(!manifest.GetProperty("prototype_use").GetBoolean(), "walk must not bypass gate with prototype mode");
        var inventory = manifest.GetProperty("frame_inventory").EnumerateArray().ToArray();
        Assert(inventory.Length == 13 && inventory.Select(x => x.GetProperty("path").GetString()).Distinct().Count() == 13,
            "walk must store exactly thirteen unique PNGs, not duplicate mirror variants");
        foreach (var item in inventory)
        {
            var path = Path.Combine(root, item.GetProperty("path").GetString()!);
            var bytes = File.ReadAllBytes(path);
            Assert(bytes.Length == item.GetProperty("bytes").GetInt32(), "byte size mismatch");
            Assert(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == item.GetProperty("sha256").GetString(), "SHA mismatch");
            Assert(item.GetProperty("source_sha256").GetString() == item.GetProperty("sha256").GetString(), "import changed PNG bytes");
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024 && frame.Format.ToString().Contains('a', StringComparison.OrdinalIgnoreCase), "PNG format mismatch");
        }
        var actions = manifest.GetProperty("actions").EnumerateArray().ToArray();
        Assert(actions.Length == 2, "left/right bindings missing");
        foreach (var action in actions)
        {
            Assert(action.GetProperty("total_duration_ms").GetInt32() == 4100, "single-cycle lifecycle timing changed");
            var phases = action.GetProperty("phases").EnumerateArray().ToArray();
            Assert(phases.Select(x => x.GetProperty("name").GetString()).SequenceEqual(new[] { "intro", "loop", "exit" }), "phase order wrong");
            Assert(phases.Select(x => x.GetProperty("frame_count").GetInt32()).SequenceEqual(new[] { 4, 8, 4 }), "phase frame count wrong");
            Assert(action.GetProperty("runtime_use").GetBoolean() == manifest.GetProperty("runtime_use").GetBoolean(), "approval inconsistent");
        }
    }

    public static void ApprovedGaitUsesAutonomousAllowlistAndDeveloperPreviewStaysIsolated()
    {
        var runtime = new DesktopRuntimeHost();
        var motions = runtime.Motions.Where(x => x.AssetBatch == PatrolWalkCandidateBehaviorIds.AssetBatch).ToArray();
        Assert(motions.Length == 2, "catalog omitted v8 directions");
        Assert(motions.All(x => x.VisualApproved && !x.PrototypeUse && x.WindowMotionEnabled), "walk capabilities wrong");
        Assert(motions.All(x => x.RenderScaleOverride == 0.68 && x.SupportsHorizontalMirror), "shared group scale/mirror missing");
        Assert(motions[0].Phases.SelectMany(x => x.Frames).SequenceEqual(motions[1].Phases.SelectMany(x => x.Frames)), "direction made duplicate PNG paths");
        Assert(motions.All(x => DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(x.BehaviorId)), "stable behavior IDs lost allowlist");
        var before = runtime.AgentStateSnapshot;
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        foreach (var motion in motions)
        {
            var result = runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted && request is not null, "preview failed");
            Assert(request!.ExecutionMode == BehaviorExecutionMode.DeveloperPreview, "preview changed execution mode");
            Assert(request.MirrorHorizontally == (motion.Direction == "right"), "preview direction disagrees with movement");
            Assert(PatrolWalkTiming.DurationMs(motion, 2) == 5700, "intro/exit incorrectly repeated with loops");
            runtime.CompleteMotion(request.RequestId, motion.BehaviorId, "exit");
            Assert(runtime.AgentStateSnapshot.Relationship == before.Relationship &&
                   runtime.AgentStateSnapshot.RecentExperience.SequenceEqual(before.RecentExperience), "preview wrote production memory");
        }
    }

    public static void WindowTravelIsDirectionalAndWorkAreaBounded()
    {
        var area = new System.Windows.Rect(0, 0, 1920, 1080);
        var start = new System.Windows.Point(820, 700);
        foreach (var direction in new[] { "left", "right" })
        {
            var target = MainWindow.ChoosePatrolWalkTarget(start, area, 280, 280, direction, TimeSpan.FromMilliseconds(5700));
            Assert(direction == "left" ? target.X < start.X : target.X > start.X, "walk direction reversed");
            Assert(target.Y == start.Y && target.X >= area.Left && target.X + 280 <= area.Right, "walk escaped work area");
        }
        var motion = new DesktopRuntimeHost().Motions.First(x => x.AssetBatch == PatrolWalkCandidateBehaviorIds.AssetBatch);
        var intro = motion.Phases[0];
        var exit = motion.Phases[2];
        Assert(PatrolWalkTiming.TravelSeconds(intro, 200, 500) == 0, "standing intro slid across desktop");
        Assert(PatrolWalkTiming.TravelSeconds(exit, 200, 740) == PatrolWalkTiming.TravelSeconds(exit, 200, 1440), "terminal stand still moving");
        foreach (var phase in motion.Phases)
        {
            var last = 0.0;
            for (var time = 0; time <= phase.DurationTotalMs(200); time++)
            {
                var next = PatrolWalkTiming.TravelSeconds(phase, 200, time);
                Assert(next >= last, "walking trajectory reversed mid-stride");
                last = next;
            }
        }
    }

    public static void AutonomousSelectionUsesApprovedV8AndLocksFacing()
    {
        var now = new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);
        var runtime = new DesktopRuntimeHost(now: () => now);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        var walks = 0;
        var mirrors = new HashSet<bool>();
        for (var seed = 0; seed < 100; seed++)
        {
            now += TimeSpan.FromMinutes(2);
            runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default,
                PetRuntimeState.Default with { CurrentPosture = StablePosture.Stand, Energy=0.85, Boredom=0.9, Stress=0.05 },
                RelationshipState.Default, seed);
            runtime.StartIdle("test");
            typeof(DesktopRuntimeHost).GetField("_petAgentState", flags)!.SetValue(runtime,
                runtime.AgentStateSnapshot with { Episode = new PetEpisodeState(PetEpisodeKind.Exploring, now, TimeSpan.FromMinutes(2), "test_exploring") });
            typeof(DesktopRuntimeHost).GetField("_currentStartedAt", flags)!.SetValue(runtime, now-TimeSpan.FromSeconds(60));
            typeof(DesktopRuntimeHost).GetField("_nextAutonomousDecisionAt", flags)!.SetValue(runtime, now-TimeSpan.FromSeconds(1));
            runtime.UpdatePatrolTravelSpace(seed%2 == 0 ? 1000 : 0, seed%2 == 1 ? 1000 : 0, 50);
            request = null;
            runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
            if (request?.Motion.AssetBatch != PatrolWalkCandidateBehaviorIds.AssetBatch) continue;
            walks++;
            var walk = request;
            Assert(walk.Source == BehaviorRequestSource.AutonomousTick && walk.ExecutionMode == BehaviorExecutionMode.Normal, "walk not on normal autonomous path");
            Assert(walk.LoopCycles is >= 2 and <= 4, "natural walk cycle range changed");
            Assert(walk.Motion.RuntimeApproved && walk.Motion.RuntimeEnabled, "unapproved art selected");
            Assert(walk.MirrorHorizontally == (walk.Motion.Direction == "right"), "normal direction mismatch");
            Assert(walk.Motion.Direction == (seed%2 == 0 ? "left" : "right"), "selected blocked screen direction");
            mirrors.Add(walk.MirrorHorizontally);
            runtime.CompleteMotion(walk.RequestId, walk.Motion.BehaviorId, "exit");
            Assert(runtime.CurrentStablePosture == StablePosture.Stand && !runtime.AgentStateSnapshot.Runtime.IsBusy, "normal walk did not settle into standing");
            Assert(request!.MirrorHorizontally == walk.MirrorHorizontally, "idle flipped direction after walking");
            Assert(request.RequestId != walk.RequestId, "idle reused walking execution ID");
        }
        Assert(walks > 0 && mirrors.Count == 2, "100 seeded autonomous decisions did not exercise both directions");
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
