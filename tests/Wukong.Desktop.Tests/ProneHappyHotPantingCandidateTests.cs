using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class ProneHappyHotPantingCandidateTests
{
    private static readonly int[] Durations =
        [260, 180, 200, 420, 440, 440, 480, 120, 480, 440, 480, 440, 360, 200, 180, 260, 300];

    public static void ManifestFramesAndTimingStayByteExact()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var batchRoot = Path.Combine(output, "WukongAssets", "action-batches", ProneHappyHotPantingBehaviorIds.AssetBatch);
        var manifestPath = Path.Combine(batchRoot, "manifest.json");
        Assert(File.Exists(manifestPath), "happy hot-panting manifest was not copied");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        Assert(root.GetProperty("owner_preview_approved").GetBoolean(), "owner visual approval was not recorded");
        Assert(root.GetProperty("visual_approved").GetBoolean(), "visual approval was not recorded");
        Assert(root.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "renderer evidence not recorded");
        Assert(root.GetProperty("runtime_approved").GetBoolean() &&
               root.GetProperty("runtime_use").GetBoolean() &&
               root.GetProperty("production_asset").GetBoolean() &&
               !root.GetProperty("prototype_use").GetBoolean(), "approved runtime flags inconsistent");
        Assert(root.GetProperty("developer_preview").GetBoolean() &&
               root.GetProperty("autonomous_binding_enabled").GetBoolean(), "approved autonomous gate is inconsistent");

        var action = root.GetProperty("action");
        Assert(action.GetProperty("behavior_id").GetString() == ProneHappyHotPantingBehaviorIds.HappyHotPanting,
            "behavior id changed");
        var frames = action.GetProperty("frames").EnumerateArray().ToArray();
        Assert(frames.Length == 17, "happy hot-panting sequence must contain 17 frames");
        Assert(frames.Select(x => x.GetProperty("duration_ms").GetInt32()).SequenceEqual(Durations), "source timing changed");
        Assert(Durations.Sum() == 5680 && Durations.Skip(3).Take(10).Sum() == 4100, "declared timing totals changed");

        foreach (var item in frames)
        {
            var relative = item.GetProperty("path").GetString()!;
            var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"happy hot-panting frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"hash changed: {relative}");
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"frame dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains('a', StringComparison.OrdinalIgnoreCase), $"alpha channel missing: {relative}");
        }

        Assert(Sha256(Path.Combine(batchRoot, frames[0].GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))) ==
               Sha256(Path.Combine(batchRoot, frames[^1].GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar))),
            "sequence does not return to its exact neutral anchor");
    }

    public static void CandidateGatePoseCooldownAndPreviewStayIsolated()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var marker = Path.Combine(output, DesktopMotionCatalog.ProneHappyHotPantingReviewMarkerFileName);
        File.WriteAllText(marker, "local candidate review only");
        try
        {
            var catalog = DesktopMotionCatalog.Load(output);
            var motion = catalog.Find(ProneHappyHotPantingBehaviorIds.HappyHotPanting)
                ?? throw new InvalidOperationException("happy hot-panting candidate is missing from catalog");
            Assert(motion.RuntimeEnabled && motion.AutonomousBindingEnabled && motion.RuntimeApproved,
                "approved panting unavailable");
            Assert(motion.Phases.Single().FrameDurationsMs!.SequenceEqual(Durations), "catalog timing differs from source manifest");
            Assert(!motion.SupportsHorizontalMirror, "front-facing panting must not be mirrored");

            Assert(DesktopRuntimeHost.IsProneHappyHotPantingProfileAllowed(StablePosture.Prone, "prone.awake.front", true, false, 0.7, 0.2),
                "happy stable front-prone pose was rejected");
            Assert(!DesktopRuntimeHost.IsProneHappyHotPantingProfileAllowed(StablePosture.Prone, "prone.awake.left_front", false, false, 0.7, 0.2),
                "side-prone pose was accepted");
            Assert(!DesktopRuntimeHost.IsProneHappyHotPantingProfileAllowed(StablePosture.Stand, "stand.neutral", false, false, 0.7, 0.2),
                "standing pose was accepted");
            Assert(!DesktopRuntimeHost.IsProneHappyHotPantingProfileAllowed(StablePosture.Prone, "prone.awake.front", true, true, 0.7, 0.2),
                "busy pose was accepted");
            Assert(!DesktopRuntimeHost.IsProneHappyHotPantingProfileAllowed(StablePosture.Prone, "prone.awake.front", true, false, 0.2, 0.2),
                "unhappy state was accepted");
            Assert(!DesktopRuntimeHost.IsProneHappyHotPantingProfileAllowed(StablePosture.Prone, "prone.awake.front", true, false, 0.7, 0.8),
                "high-stress state was accepted");

            var now = DateTimeOffset.Parse("2026-09-25T12:00:00+08:00");
            Assert(!DesktopRuntimeHost.CanScheduleFrontProneExpression(now, now.AddSeconds(1), ProneHappyHotPantingBehaviorIds.HappyHotPanting, []),
                "shared cooldown was ignored");
            Assert(!DesktopRuntimeHost.CanScheduleFrontProneExpression(now, now, ProneHappyHotPantingBehaviorIds.HappyHotPanting,
                    [ProneHappyHotPantingBehaviorIds.HappyHotPanting]), "recent panting action repeated");

            var runtime = new DesktopRuntimeHost();
            Assert(runtime.AutonomousDailyCandidateMotions.Any(x =>
                    string.Equals(x.BehaviorId, ProneHappyHotPantingBehaviorIds.HappyHotPanting, StringComparison.OrdinalIgnoreCase)),
                "candidate is not visible in the existing base/autonomous asset panel data source");
            var postureBeforePreview = runtime.CurrentStablePosture;
            PetMotionRequest? request = null;
            runtime.MotionRequested += (_, value) => request = value;
            var result = runtime.SubmitDeveloperCandidateMotionAsync(ProneHappyHotPantingBehaviorIds.HappyHotPanting).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted, "developer preview request was rejected");
            Assert(request is { ExecutionMode: BehaviorExecutionMode.DeveloperPreview, Source: BehaviorRequestSource.DeveloperForced },
                "developer preview bypassed the existing request path");
            runtime.CompleteMotion(request!.RequestId, request.Motion.BehaviorId, "microexpression");
            Assert(runtime.CurrentStablePosture == postureBeforePreview, "developer preview polluted formal posture state");
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
