using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class FrontProneExpressionCandidateTests
{
    private static readonly int[] Durations = [520, 150, 150, 160, 260, 220, 160, 160, 170, 240, 320, 520];

    public static void ManifestAndFramesStayByteExact()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var batchRoot = Path.Combine(output, "WukongAssets", "action-batches", FrontProneExpressionBehaviorIds.AssetBatch);
        var manifestPath = Path.Combine(batchRoot, "manifest.json");
        Assert(File.Exists(manifestPath), "front-prone expression manifest was not copied");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        Assert(root.GetProperty("visual_approved").GetBoolean(), "owner-approved source visuals were not recorded");
        Assert(root.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "renderer evidence not recorded");
        Assert(root.GetProperty("runtime_approved").GetBoolean() &&
               root.GetProperty("runtime_use").GetBoolean() &&
               root.GetProperty("production_asset").GetBoolean() &&
               !root.GetProperty("prototype_use").GetBoolean(), "approved runtime flags inconsistent");

        var inventory = root.GetProperty("frame_inventory").EnumerateArray().ToArray();
        Assert(inventory.Length == 36, "front-prone expression inventory must contain 36 PNGs");
        foreach (var item in inventory)
        {
            var relative = item.GetProperty("path").GetString()!;
            var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"front-prone expression frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"hash changed: {relative}");
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"frame dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains('a', StringComparison.OrdinalIgnoreCase), $"alpha channel missing: {relative}");
        }

        foreach (var action in root.GetProperty("actions").EnumerateArray())
        {
            var frames = action.GetProperty("frames").EnumerateArray().ToArray();
            Assert(frames.Length == 12, "expression action frame count changed");
            Assert(frames.Select(x => x.GetProperty("duration_ms").GetInt32()).SequenceEqual(Durations), "expression timing changed");
            Assert(action.GetProperty("total_duration_ms").GetInt32() == 3030, "expression duration total changed");
            var first = Path.Combine(batchRoot, frames[0].GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            var last = Path.Combine(batchRoot, frames[^1].GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert(Sha256(first) == Sha256(last), "expression does not return to the exact front-prone anchor");
        }
    }

    public static void CandidateReviewGateAndAutonomousRulesAreIsolated()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var marker = Path.Combine(output, DesktopMotionCatalog.FrontProneExpressionsReviewMarkerFileName);
        File.WriteAllText(marker, "local candidate review only");
        try
        {
            var catalog = DesktopMotionCatalog.Load(output);
            var motions = FrontProneExpressionBehaviorIds.All.Select(id => catalog.Find(id)).ToArray();
            Assert(motions.All(x => x is not null), "front-prone expression action is missing from catalog");
            Assert(motions.All(x => x!.RuntimeEnabled && x.AutonomousBindingEnabled && x.RuntimeApproved), "approved expression unavailable");
            Assert(motions.All(x => x!.Phases.Single().FrameDurationsMs!.SequenceEqual(Durations)), "catalog timing differs from source manifest");
            Assert(motions.All(x => !x!.SupportsHorizontalMirror), "front-facing expressions must not be mirrored");

            Assert(DesktopRuntimeHost.IsFrontProneExpressionProfileAllowed(StablePosture.Prone, "prone.awake.front", true, false), "stable front-prone pose was rejected");
            Assert(!DesktopRuntimeHost.IsFrontProneExpressionProfileAllowed(StablePosture.Prone, "prone.awake.left_front", false, false), "side-prone pose was accepted");
            Assert(!DesktopRuntimeHost.IsFrontProneExpressionProfileAllowed(StablePosture.Stand, "stand.neutral", false, false), "standing pose was accepted");
            Assert(!DesktopRuntimeHost.IsFrontProneExpressionProfileAllowed(StablePosture.Sit, "sit.neutral", false, false), "sitting pose was accepted");
            Assert(!DesktopRuntimeHost.IsFrontProneExpressionProfileAllowed(StablePosture.Prone, "prone.sleep.side", false, false), "sleep pose was accepted");
            Assert(!DesktopRuntimeHost.IsFrontProneExpressionProfileAllowed(StablePosture.Prone, "prone.awake.front", true, true), "busy pose was accepted");

            var now = DateTimeOffset.Parse("2026-09-16T12:00:00+08:00");
            Assert(!DesktopRuntimeHost.CanScheduleFrontProneExpression(now, now.AddSeconds(1), FrontProneExpressionBehaviorIds.SatisfiedSmile, []), "shared cooldown was ignored");
            Assert(!DesktopRuntimeHost.CanScheduleFrontProneExpression(now, now, FrontProneExpressionBehaviorIds.SatisfiedSmile, [FrontProneExpressionBehaviorIds.SatisfiedSmile]), "recent expression repeated");
            Assert(DesktopRuntimeHost.CanScheduleFrontProneExpression(now, now, FrontProneExpressionBehaviorIds.CuriousObserve, [FrontProneExpressionBehaviorIds.SatisfiedSmile]), "different expression was incorrectly suppressed");

            var runtime = new DesktopRuntimeHost();
            var postureBeforePreview = runtime.CurrentStablePosture;
            PetMotionRequest? request = null;
            runtime.MotionRequested += (_, value) => request = value;
            var result = runtime.SubmitDeveloperCandidateMotionAsync(FrontProneExpressionBehaviorIds.KnowingLook).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted, "developer preview request was rejected");
            Assert(request is { ExecutionMode: BehaviorExecutionMode.DeveloperPreview, Source: BehaviorRequestSource.DeveloperForced }, "developer preview bypassed the existing request path");
            runtime.CompleteMotion(request!.RequestId, request.Motion.BehaviorId, "microexpression");
            Assert(runtime.CurrentStablePosture == postureBeforePreview, "developer preview polluted formal posture state");

            var missingFrame = motions[0]!.Phases.Single().Frames[5];
            var heldFrame = missingFrame + ".missing-test";
            File.Move(missingFrame, heldFrame);
            try
            {
                var failClosedCatalog = DesktopMotionCatalog.Load(output);
                Assert(FrontProneExpressionBehaviorIds.All.All(id => failClosedCatalog.Find(id) is null),
                    "a missing production frame did not fail the expression batch closed");
            }
            finally
            {
                File.Move(heldFrame, missingFrame);
            }
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
