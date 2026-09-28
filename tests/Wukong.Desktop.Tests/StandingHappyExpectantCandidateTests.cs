using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class StandingHappyExpectantCandidateTests
{
    private static readonly int[] Durations =
        [320, 300, 260, 300, 480, 520, 520, 480, 480, 170, 480, 520, 300, 260, 300, 480];

    public static void ManifestAndFramesStayByteExact()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var batchRoot = Path.Combine(output, "WukongAssets", "action-batches", StandingHappyExpectantBehaviorIds.AssetBatch);
        var manifestPath = Path.Combine(batchRoot, "manifest.json");
        Assert(File.Exists(manifestPath), "standing happy-expectant manifest was not copied");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        Assert(root.GetProperty("visual_approved").GetBoolean(), "owner-approved source visual was not recorded");
        Assert(root.GetProperty("runtime_validation").GetString() == "pending_windows_renderer_qa", "candidate claimed Windows runtime approval");
        Assert(!root.GetProperty("runtime_approved").GetBoolean() &&
               !root.GetProperty("runtime_use").GetBoolean() &&
               !root.GetProperty("production_asset").GetBoolean() &&
               !root.GetProperty("prototype_use").GetBoolean(), "candidate formal runtime gate is open");
        Assert(root.GetProperty("developer_preview").GetBoolean() &&
               !root.GetProperty("autonomous_binding_enabled").GetBoolean(), "candidate preview gate is inconsistent");

        var frames = root.GetProperty("action").GetProperty("frames").EnumerateArray().ToArray();
        Assert(frames.Length == 16, "standing happy-expectant must contain 16 frames");
        Assert(frames.Select(x => x.GetProperty("duration_ms").GetInt32()).SequenceEqual(Durations), "source frame timing changed");
        Assert(root.GetProperty("action").GetProperty("total_duration_ms").GetInt32() == 6170, "source total duration changed");

        foreach (var item in frames)
        {
            var relative = item.GetProperty("path").GetString()!;
            var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"standing happy-expectant frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"hash changed: {relative}");
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"frame dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains('a', StringComparison.OrdinalIgnoreCase), $"alpha channel missing: {relative}");
        }

        var anchor = Path.Combine(output, "WukongAssets", "action-batches", LifecycleCandidateBehaviorIds.AssetBatch,
            "frames", "microloops", "stand-idle", "01.png");
        Assert(Sha256(anchor) == "77be067001a7de36f6fe436f73ab05735b9a827612746dba52e43e9d8bc07758", "current stand anchor changed");
        Assert(Sha256(Path.Combine(batchRoot, frames[0].GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))) == Sha256(anchor),
            "expression does not enter from the exact stand anchor");
        Assert(Sha256(Path.Combine(batchRoot, frames[^1].GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))) == Sha256(anchor),
            "expression does not return to the exact stand anchor");
    }

    public static void CandidateGatePostureAndCooldownStayIsolated()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var marker = Path.Combine(output, DesktopMotionCatalog.StandingHappyExpectantReviewMarkerFileName);
        File.WriteAllText(marker, "local candidate review only");
        try
        {
            var catalog = DesktopMotionCatalog.Load(output);
            var motion = catalog.Find(StandingHappyExpectantBehaviorIds.HappyExpectant)
                ?? throw new InvalidOperationException("standing happy-expectant action is missing from catalog");
            Assert(motion.RuntimeEnabled && motion.AutonomousBindingEnabled && !motion.RuntimeApproved,
                "candidate EXE gate did not stay isolated from formal approval");
            Assert(motion.Phases.Single().FrameDurationsMs!.SequenceEqual(Durations), "catalog timing differs from source manifest");
            Assert(!motion.SupportsHorizontalMirror, "left-front expression must not be mirrored");

            var panelRuntime = new DesktopRuntimeHost();
            Assert(panelRuntime.StandingExpressionCandidateMotions.Any(x =>
                    string.Equals(x.BehaviorId, StandingHappyExpectantBehaviorIds.HappyExpectant, StringComparison.OrdinalIgnoreCase)),
                "developer candidate list does not expose force playback for the standing expression");

            Assert(DesktopRuntimeHost.IsStandingHappyExpectantProfileAllowed(StablePosture.Stand, "stand.neutral.left_front", false),
                "stable compatible stand pose was rejected");
            Assert(!DesktopRuntimeHost.IsStandingHappyExpectantProfileAllowed(StablePosture.Sit, "sit.neutral.left_front", false),
                "sitting pose was accepted");
            Assert(!DesktopRuntimeHost.IsStandingHappyExpectantProfileAllowed(StablePosture.Prone, "prone.awake.front", false),
                "prone pose was accepted");
            Assert(!DesktopRuntimeHost.IsStandingHappyExpectantProfileAllowed(StablePosture.Stand, "stand.neutral.left_front", true),
                "busy pose was accepted");

            var now = DateTimeOffset.Parse("2026-09-22T12:00:00+08:00");
            Assert(!DesktopRuntimeHost.CanScheduleStandingHappyExpectant(now, now.AddSeconds(1), StandingHappyExpectantBehaviorIds.HappyExpectant),
                "cooldown was ignored");
            Assert(DesktopRuntimeHost.CanScheduleStandingHappyExpectant(now, now, StandingHappyExpectantBehaviorIds.HappyExpectant),
                "eligible expression was suppressed");

            var runtime = new DesktopRuntimeHost();
            var postureBeforePreview = runtime.CurrentStablePosture;
            PetMotionRequest? request = null;
            runtime.MotionRequested += (_, value) => request = value;
            var result = runtime.SubmitDeveloperCandidateMotionAsync(StandingHappyExpectantBehaviorIds.HappyExpectant).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted, "developer preview request was rejected");
            Assert(request is { ExecutionMode: BehaviorExecutionMode.DeveloperPreview, Source: BehaviorRequestSource.DeveloperForced },
                "developer preview bypassed the existing request path");
            runtime.CompleteMotion(request!.RequestId, request.Motion.BehaviorId, "microexpression");
            Assert(runtime.CurrentStablePosture == postureBeforePreview, "developer preview polluted formal posture state");

            var semanticRuntime = new DesktopRuntimeHost();
            semanticRuntime.UpdateBehaviorAgentMock(
                TemperamentProfile.Default,
                PetRuntimeState.Default with
                {
                    CurrentPosture = StablePosture.Stand,
                    CurrentPoseId = "stand.neutral.left_front",
                    Energy = 0.8,
                    Stress = 0.1,
                    MoodValence = 0.75
                },
                RelationshipState.Default,
                42);
            PetMotionRequest? semanticRequest = null;
            semanticRuntime.MotionRequested += (_, value) => semanticRequest = value;
            var semanticResult = semanticRuntime.SubmitExpressionIntentAsync(
                new SemanticIntent(SemanticIntentKind.PositiveExpression, StandingHappyExpectantBehaviorIds.HappyExpectant),
                BehaviorRequestSource.OwnerDialogue).GetAwaiter().GetResult();
            Assert(semanticResult == PetActionResult.Accepted, "structured positive expression intent was rejected");
            Assert(semanticRequest is { Source: BehaviorRequestSource.OwnerDialogue, ExecutionMode: BehaviorExecutionMode.Normal },
                "structured expression intent bypassed the unified request path");
            semanticRuntime.CompleteMotion(
                semanticRequest!.RequestId,
                semanticRequest.Motion.BehaviorId,
                semanticRequest.Motion.Phases.Last().Name);
            var unrelated = semanticRuntime.SubmitExpressionIntentAsync(
                new SemanticIntent(SemanticIntentKind.ModelSuggested, StandingHappyExpectantBehaviorIds.HappyExpectant),
                BehaviorRequestSource.OwnerDialogue).GetAwaiter().GetResult();
            Assert(unrelated == PetActionResult.Deferred, "unrelated semantic intent triggered the expression");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
