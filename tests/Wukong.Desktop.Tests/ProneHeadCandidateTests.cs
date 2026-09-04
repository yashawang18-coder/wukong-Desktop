using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class ProneHeadCandidateTests
{
    public static void ManifestFramesAndGateAreValid()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var batchRoot = Path.Combine(output, "WukongAssets", "action-batches", ProneHeadCandidateBehaviorIds.AssetBatch);
        var assetPath = Path.Combine(batchRoot, "asset.json");
        var manifestPath = Path.Combine(batchRoot, "manifest.json");
        Assert(File.Exists(assetPath), "prone head candidate asset.json was not copied");
        Assert(File.Exists(manifestPath), "prone head candidate manifest was not copied");

        using var assetDocument = JsonDocument.Parse(File.ReadAllText(assetPath));
        var asset = assetDocument.RootElement;
        Assert(asset.GetProperty("visual_approved").GetBoolean(), "prone head visual approval missing");
        Assert(asset.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "prone head Windows validation missing");
        Assert(asset.GetProperty("runtime_approved").GetBoolean(), "prone head runtime approval missing");
        Assert(asset.GetProperty("runtime_use").GetBoolean(), "prone head runtime use was disabled");
        Assert(asset.GetProperty("production_asset").GetBoolean(), "prone head production status missing");
        Assert(!asset.GetProperty("prototype_use").GetBoolean(), "approved prone head action still uses prototype mode");
        Assert(asset.GetProperty("developer_preview").GetBoolean(), "developer review was disabled");
        Assert(asset.GetProperty("autonomous_binding_enabled").GetBoolean(), "prone head autonomous binding missing");
        Assert(asset.GetProperty("internal_handoff_exact").GetBoolean(), "internal low-head handoff was not recorded");
        Assert(!asset.GetProperty("current_runtime_prone_anchor_exact").GetBoolean(), "candidate falsely claimed an existing runtime anchor");
        Assert(asset.GetProperty("approved_runtime_profile").GetString() == "non_front_prone_owner_validated", "prone head approved posture profile changed");

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = manifestDocument.RootElement;
        var inventory = manifest.GetProperty("frame_inventory").EnumerateArray().ToArray();
        Assert(inventory.Length == 24, "candidate source frame inventory must contain 24 PNGs");
        foreach (var item in inventory)
        {
            var relative = item.GetProperty("path").GetString()!;
            var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"candidate frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"candidate byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"candidate hash changed: {relative}");
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"candidate dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains("a", StringComparison.OrdinalIgnoreCase), $"candidate alpha channel missing: {relative}");
        }

        var action = manifest.GetProperty("actions").EnumerateArray().Single();
        Assert(action.GetProperty("behavior_id").GetString() == ProneHeadCandidateBehaviorIds.HeadLowerTurnV4, "candidate behavior id changed");
        Assert(action.GetProperty("visual_approved").GetBoolean() &&
               action.GetProperty("runtime_approved").GetBoolean() &&
               action.GetProperty("runtime_use").GetBoolean() &&
               action.GetProperty("production_asset").GetBoolean() &&
               action.GetProperty("autonomous_binding_enabled").GetBoolean(),
            "prone head action approved gate is incomplete");
        Assert(!action.GetProperty("prototype_use").GetBoolean(), "approved prone head action still uses prototype mode");
        Assert(action.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "AutonomousTick", "DeveloperPreview" }),
            "candidate action source policy changed");

        var phases = action.GetProperty("phases").EnumerateArray().ToArray();
        Assert(phases.Select(x => x.GetProperty("name").GetString()).SequenceEqual(new[] { "intro", "action", "exit" }),
            "candidate lifecycle phase order changed");
        Assert(phases.Select(x => x.GetProperty("frames").GetArrayLength()).SequenceEqual(new[] { 12, 22, 10 }),
            "candidate round-trip frame plan changed");
        var firstPath = phases[0].GetProperty("frames")[0].GetProperty("path").GetString();
        var lastFrames = phases[^1].GetProperty("frames");
        var lastPath = lastFrames[lastFrames.GetArrayLength() - 1].GetProperty("path").GetString();
        Assert(firstPath == lastPath, "candidate does not close on its imported high-head anchor");

        var lowerHandoff = inventory.Single(x => x.GetProperty("path").GetString() == "frames/head-lower/frame-011.png");
        var turnHandoff = inventory.Single(x => x.GetProperty("path").GetString() == "frames/head-turn/frame-001.png");
        Assert(lowerHandoff.GetProperty("sha256").GetString() == turnHandoff.GetProperty("sha256").GetString(),
            "candidate internal low-head handoff no longer matches");
    }

    public static void ApprovedMicroeventUsesAutonomousAllowlistAndDeveloperPreviewStaysIsolated()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var catalog = DesktopMotionCatalog.Load(output);
        var motion = catalog.Motions.Single(x => x.BehaviorId == ProneHeadCandidateBehaviorIds.HeadLowerTurnV4);
        Assert(motion.RuntimeEnabled && motion.RuntimeApproved && !motion.PrototypeUse, "prone head catalog approval is incomplete");
        Assert(motion.AutonomousBindingEnabled, "prone head catalog autonomous binding is missing");
        Assert(motion.Phases.Select(x => x.Frames.Count).SequenceEqual(new[] { 12, 22, 10 }), "catalog did not preserve round-trip phases");
        Assert(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(motion.BehaviorId), "prone head action is missing from the autonomous allowlist");
        Assert(DesktopRuntimeHost.IsProneHeadAutonomousProfileAllowed(StablePosture.Prone, frontProneProfileActive: false),
            "compatible non-front prone profile was rejected");
        Assert(!DesktopRuntimeHost.IsProneHeadAutonomousProfileAllowed(StablePosture.Prone, frontProneProfileActive: true),
            "forward-prone profile can select an incompatible prone head action");
        Assert(!DesktopRuntimeHost.IsProneHeadAutonomousProfileAllowed(StablePosture.Stand, frontProneProfileActive: false),
            "non-prone posture can select the prone head action");

        var runtime = new DesktopRuntimeHost();
        Assert(runtime.AutonomousDailyCandidateMotions.Any(x => x.BehaviorId == motion.BehaviorId),
            "candidate is missing from the developer autonomous review page");
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        var result = runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId).GetAwaiter().GetResult();
        Assert(result == PetActionResult.Accepted, "developer candidate request was not accepted");
        Assert(request is not null, "developer candidate did not emit a motion request");
        Assert(request!.Source == BehaviorRequestSource.DeveloperForced, "candidate bypassed the developer request source");
        Assert(request.ExecutionMode == BehaviorExecutionMode.DeveloperPreview, "candidate bypassed DeveloperPreview mode");
        Assert(request.Motion.BehaviorId == ProneHeadCandidateBehaviorIds.HeadLowerTurnV4, "wrong candidate motion was requested");
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
