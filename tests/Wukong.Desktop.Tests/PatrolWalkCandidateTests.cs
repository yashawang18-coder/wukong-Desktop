using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Desktop;
using Wukong.Domain;

internal static class PatrolWalkCandidateTests
{
    private const string SourceZipSha256 = "a96ff60c48c0fe79e8c7a20d1d62b1658eebc8175e959b70d6b81f40c4f958ed";

    public static void ManifestFramesAndGateAreValid()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var batchRoot = Path.Combine(output, "WukongAssets", "action-batches", PatrolWalkCandidateBehaviorIds.AssetBatch);
        var assetPath = Path.Combine(batchRoot, "asset.json");
        var manifestPath = Path.Combine(batchRoot, "manifest.json");
        Assert(File.Exists(assetPath), "patrol walk asset.json was not copied");
        Assert(File.Exists(manifestPath), "patrol walk manifest was not copied");

        using var assetDocument = JsonDocument.Parse(File.ReadAllText(assetPath));
        var asset = assetDocument.RootElement;
        Assert(asset.GetProperty("source_zip_sha256").GetString() == SourceZipSha256, "patrol walk source ZIP hash changed");
        Assert(asset.GetProperty("owner_preview_approved").GetBoolean(), "patrol walk owner approval missing");
        Assert(asset.GetProperty("visual_approved").GetBoolean(), "patrol walk visual approval missing");
        Assert(asset.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "patrol walk Windows validation missing");
        Assert(asset.GetProperty("runtime_approved").GetBoolean(), "patrol walk runtime approval missing");
        Assert(asset.GetProperty("runtime_use").GetBoolean(), "patrol walk runtime use was disabled");
        Assert(asset.GetProperty("production_asset").GetBoolean(), "patrol walk production status missing");
        Assert(!asset.GetProperty("prototype_use").GetBoolean(), "patrol walk enabled owner prototype use");
        Assert(asset.GetProperty("developer_preview").GetBoolean(), "patrol walk developer preview was disabled");
        Assert(asset.GetProperty("autonomous_binding_enabled").GetBoolean(), "patrol walk autonomous binding missing");
        Assert(!asset.GetProperty("window_motion_enabled").GetBoolean(), "in-place gait approval must not enable window translation");
        Assert(asset.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "AutonomousTick", "DeveloperPreview" }),
            "patrol walk source policy changed");

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = manifestDocument.RootElement;
        var inventory = manifest.GetProperty("frame_inventory").EnumerateArray().ToArray();
        Assert(inventory.Length == 24, "patrol walk inventory must contain 24 runtime PNGs");
        Assert(inventory.Select(x => x.GetProperty("path").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 24,
            "patrol walk inventory contains duplicate paths");
        foreach (var item in inventory)
        {
            var relative = item.GetProperty("path").GetString()!;
            var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"patrol walk frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"patrol walk byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"patrol walk hash changed: {relative}");
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"patrol walk dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains('a', StringComparison.OrdinalIgnoreCase), $"patrol walk alpha channel missing: {relative}");
        }

        var actions = manifest.GetProperty("actions").EnumerateArray().ToArray();
        Assert(actions.Length == 2, "patrol walk must contain left and right actions");
        Assert(actions.Select(x => x.GetProperty("behavior_id").GetString()).ToHashSet(StringComparer.Ordinal)
            .SetEquals(PatrolWalkCandidateBehaviorIds.All), "patrol walk behavior IDs changed");
        foreach (var action in actions)
        {
            Assert(action.GetProperty("frame_count").GetInt32() == 12, "patrol walk direction must contain 12 frames");
            Assert(action.GetProperty("frame_duration_ms").GetInt32() == 110, "patrol walk frame duration changed");
            Assert(action.GetProperty("total_duration_ms").GetInt32() == 1320, "patrol walk cycle duration changed");
            Assert(action.GetProperty("loop").GetBoolean(), "patrol walk gait must loop in review");
            Assert(action.GetProperty("visual_approved").GetBoolean() &&
                   action.GetProperty("runtime_approved").GetBoolean() &&
                   action.GetProperty("runtime_use").GetBoolean() &&
                   action.GetProperty("production_asset").GetBoolean() &&
                   !action.GetProperty("prototype_use").GetBoolean() &&
                   action.GetProperty("autonomous_binding_enabled").GetBoolean(),
                "patrol walk approved gate is incomplete");
            Assert(action.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "patrol walk action validation missing");
            Assert(action.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "AutonomousTick", "DeveloperPreview" }),
                "patrol walk source policy changed");
        }
    }

    public static void ApprovedGaitUsesAutonomousAllowlistAndDeveloperPreviewStaysIsolated()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var catalog = DesktopMotionCatalog.Load(output);
        var motions = catalog.Motions.Where(x => x.AssetBatch == PatrolWalkCandidateBehaviorIds.AssetBatch).ToArray();
        Assert(motions.Length == 2, "catalog did not expose both patrol walk review actions");
        Assert(motions.All(x => x.VisualApproved && x.RuntimeEnabled && x.RuntimeApproved && !x.PrototypeUse),
            "patrol walk catalog approval is incomplete");
        Assert(motions.All(x => x.AutonomousBindingEnabled), "patrol walk catalog autonomous binding is missing");
        Assert(motions.All(x => x.RenderScaleOverride == 0.92), "patrol walk lost its shared review scale");
        Assert(motions.All(x => DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(x.BehaviorId)),
            "patrol walk is missing from the autonomous allowlist");

        var runtime = new DesktopRuntimeHost();
        Assert(runtime.AutonomousDailyCandidateMotions.Count(x => x.AssetBatch == PatrolWalkCandidateBehaviorIds.AssetBatch) == 2,
            "patrol walk actions are missing from the developer review page");
        var postureBefore = runtime.CurrentStablePosture;
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        foreach (var motion in motions)
        {
            request = null;
            var result = runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted, $"developer patrol walk request was not accepted: {motion.BehaviorId}");
            Assert(request is not null, $"developer patrol walk request did not emit a motion request: {motion.BehaviorId}");
            Assert(request!.Source == BehaviorRequestSource.DeveloperForced, "patrol walk bypassed the developer request source");
            Assert(request.ExecutionMode == BehaviorExecutionMode.DeveloperPreview, "patrol walk bypassed DeveloperPreview mode");
            runtime.CompleteMotion(request.Motion.BehaviorId, motion.Phases[0].Name);
            Assert(runtime.CurrentStablePosture == postureBefore, "developer patrol walk preview changed production posture");
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
