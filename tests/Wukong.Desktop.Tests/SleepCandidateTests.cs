using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Desktop;
using Wukong.Domain;

internal static class SleepCandidateTests
{
    public static void ManifestFramesAndGateAreValid()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var batchRoot = Path.Combine(output, "WukongAssets", "action-batches", SleepCandidateBehaviorIds.AssetBatch);
        var assetPath = Path.Combine(batchRoot, "asset.json");
        var manifestPath = Path.Combine(batchRoot, "manifest.json");
        Assert(File.Exists(assetPath), "sleep candidate asset.json was not copied");
        Assert(File.Exists(manifestPath), "sleep candidate manifest was not copied");

        using var assetDocument = JsonDocument.Parse(File.ReadAllText(assetPath));
        var asset = assetDocument.RootElement;
        Assert(!asset.GetProperty("owner_preview_approved").GetBoolean(), "v10 claimed owner preview approval before Windows review");
        Assert(!asset.GetProperty("visual_approved").GetBoolean(), "v10 claimed visual approval before Windows review");
        Assert(asset.GetProperty("runtime_validation").GetString() == "pending_owner_windows_renderer_qa", "sleep candidate validation gate changed");
        Assert(!asset.GetProperty("runtime_approved").GetBoolean(), "sleep candidate claimed runtime approval");
        Assert(!asset.GetProperty("runtime_use").GetBoolean(), "sleep candidate entered runtime use");
        Assert(!asset.GetProperty("production_asset").GetBoolean(), "sleep candidate claimed production status");
        Assert(!asset.GetProperty("prototype_use").GetBoolean(), "sleep candidate incorrectly enabled owner prototype use");
        Assert(asset.GetProperty("developer_preview").GetBoolean(), "sleep developer review was disabled");
        Assert(!asset.GetProperty("autonomous_binding_enabled").GetBoolean(), "sleep candidate entered the autonomous pool");
        Assert(asset.GetProperty("runtime_frame_format").GetString()!.Contains("copied byte-for-byte", StringComparison.Ordinal),
            "sleep v10 source-byte preservation was not recorded");

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = manifestDocument.RootElement;
        var inventory = manifest.GetProperty("frame_inventory").EnumerateArray().ToArray();
        Assert(inventory.Length == 48, "sleep candidate inventory must contain 48 runtime PNGs");
        Assert(manifest.GetProperty("actions").GetArrayLength() == 8, "sleep candidate must contain eight actions");
        foreach (var item in inventory)
        {
            var relative = item.GetProperty("path").GetString()!;
            var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"sleep candidate frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"sleep candidate byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"sleep candidate hash changed: {relative}");
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"sleep candidate dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains("a", StringComparison.OrdinalIgnoreCase), $"sleep candidate alpha channel missing: {relative}");
        }

        var actions = manifest.GetProperty("actions").EnumerateArray().ToArray();
        Assert(actions.Sum(x => x.GetProperty("frame_count").GetInt32()) == 48, "sleep candidate action frame total changed");
        foreach (var action in actions)
        {
            Assert(SleepCandidateBehaviorIds.All.Contains(action.GetProperty("behavior_id").GetString()!), "unknown sleep candidate behavior id");
            Assert(!action.GetProperty("owner_preview_approved").GetBoolean(), "sleep action claimed owner preview approval");
            Assert(!action.GetProperty("visual_approved").GetBoolean() &&
                   !action.GetProperty("runtime_approved").GetBoolean() &&
                   !action.GetProperty("runtime_use").GetBoolean() &&
                   !action.GetProperty("production_asset").GetBoolean() &&
                   !action.GetProperty("prototype_use").GetBoolean() &&
                   !action.GetProperty("autonomous_binding_enabled").GetBoolean(),
                "sleep candidate escaped its runtime gate");
            Assert(action.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "DeveloperPreview" }),
                "sleep candidate action source policy changed");
        }

        var rules = manifest.GetProperty("sequence_rules");
        Assert(!rules.GetProperty("append_prone_to_side_roll_after_main").GetBoolean(), "standalone roll was appended to the main lifecycle");
        Assert(!rules.GetProperty("hard_cut_between_incompatible_views").GetBoolean(), "incompatible sleep views can hard cut");
        Assert(!rules.GetProperty("reverse_main_as_wake").GetBoolean(), "sleep entry was reversed as an unapproved wake action");
        Assert(!rules.GetProperty("legacy_sleep_visual_fallback_allowed").GetBoolean(), "legacy sleep visuals can be used as fallback");
    }

    public static void DeveloperPreviewUsesBehaviorRequestAndAutonomousStaysClosed()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var catalog = DesktopMotionCatalog.Load(output);
        var motions = catalog.Motions.Where(x => x.AssetBatch == SleepCandidateBehaviorIds.AssetBatch).ToArray();
        Assert(motions.Length == 8, "catalog did not expose all eight v10 sleep review actions");
        Assert(!catalog.Motions.Any(x => x.SourceRoot.Contains("WK-CORE-SLEEP-BREATH-v2", StringComparison.OrdinalIgnoreCase)),
            "legacy sleep pixels remain discoverable through the desktop catalog");
        Assert(motions.All(x => !x.VisualApproved && !x.RuntimeEnabled && !x.RuntimeApproved && !x.PrototypeUse), "sleep candidate catalog gate changed");
        Assert(motions.All(x => !x.AutonomousBindingEnabled), "sleep candidate catalog autonomous binding was enabled");
        Assert(motions.All(x => x.RenderScaleOverride == 0.92), "sleep candidate lost its one global runtime scale");
        Assert(motions.All(x => !DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(x.BehaviorId)), "sleep candidate entered the autonomous allowlist");

        var runtime = new DesktopRuntimeHost();
        Assert(runtime.AutonomousDailyCandidateMotions.Count(x => x.AssetBatch == SleepCandidateBehaviorIds.AssetBatch) == 8,
            "sleep candidates are missing from the developer autonomous review page");
        var postureBefore = runtime.CurrentStablePosture;
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        foreach (var motion in motions)
        {
            request = null;
            var result = runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted, $"developer sleep request was not accepted: {motion.BehaviorId}");
            Assert(request is not null, $"developer sleep request did not emit a motion request: {motion.BehaviorId}");
            Assert(request!.Source == BehaviorRequestSource.DeveloperForced, "sleep candidate bypassed the developer request source");
            Assert(request.ExecutionMode == BehaviorExecutionMode.DeveloperPreview, "sleep candidate bypassed DeveloperPreview mode");
            Assert(request.Motion.BehaviorId == motion.BehaviorId, $"wrong sleep candidate was requested: {motion.BehaviorId}");
            runtime.CompleteMotion(request.Motion.BehaviorId, motion.Phases[0].Name);
            Assert(runtime.CurrentStablePosture == postureBefore, $"developer sleep preview changed production posture: {motion.BehaviorId}");
        }
    }

    public static void MissingV10FramesFailClosedWithoutLegacyFallback()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var source = Path.Combine(output, "WukongAssets", "action-batches", SleepCandidateBehaviorIds.AssetBatch);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"wukong-sleep-v10-missing-{Guid.NewGuid():N}");
        var candidateRoot = Path.Combine(temporaryRoot, "WukongAssets", "action-batches", SleepCandidateBehaviorIds.AssetBatch);
        Directory.CreateDirectory(candidateRoot);
        try
        {
            File.Copy(Path.Combine(source, "manifest.json"), Path.Combine(candidateRoot, "manifest.json"));
            var catalog = DesktopMotionCatalog.Load(temporaryRoot);
            Assert(!catalog.Motions.Any(x => x.AssetBatch == SleepCandidateBehaviorIds.AssetBatch),
                "sleep v10 with missing frames entered the catalog");
            Assert(!catalog.Motions.Any(x => x.SourceRoot.Contains("WK-CORE-SLEEP-BREATH-v2", StringComparison.OrdinalIgnoreCase)),
                "missing sleep v10 silently fell back to legacy sleep pixels");
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
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
