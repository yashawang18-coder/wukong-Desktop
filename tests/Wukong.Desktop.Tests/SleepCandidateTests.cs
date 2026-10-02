using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Application;
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
        Assert(File.Exists(assetPath), "sleep v11 asset.json was not copied");
        Assert(File.Exists(manifestPath), "sleep v11 manifest was not copied");

        using var assetDocument = JsonDocument.Parse(File.ReadAllText(assetPath));
        var asset = assetDocument.RootElement;
        foreach (var flag in new[] { "owner_preview_approved", "visual_approved", "runtime_approved", "runtime_use",
                     "production_asset", "developer_preview", "autonomous_binding_enabled" })
            Assert(asset.GetProperty(flag).GetBoolean(), $"approved sleep v11 closed {flag}");
        Assert(!asset.GetProperty("prototype_use").GetBoolean(), "sleep v11 depends on PrototypePreview");
        Assert(asset.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "sleep v11 renderer approval missing");
        Assert(asset.GetProperty("runtime_render_scale").GetDouble() == 0.78, "sleep v11 global scale changed");
        Assert(asset.GetProperty("runtime_frame_count").GetInt32() == 29, "sleep v11 runtime count changed");
        Assert(asset.GetProperty("sequence_frame_reference_count").GetInt32() == 44, "sleep v11 sequence references changed");
        Assert(!asset.GetProperty("walk_review_included").GetBoolean(), "rejected walking art entered the sleep batch");

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = manifestDocument.RootElement;
        var inventory = manifest.GetProperty("frame_inventory").EnumerateArray().ToArray();
        Assert(inventory.Length == 29, "sleep v11 inventory must contain 29 runtime PNGs");
        Assert(manifest.GetProperty("actions").GetArrayLength() == 7, "sleep v11 must contain seven actions");
        foreach (var item in inventory)
        {
            var relative = item.GetProperty("path").GetString()!;
            Assert(!relative.Contains("walk", StringComparison.OrdinalIgnoreCase), "walking frame entered sleep v11");
            var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"sleep v11 frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"sleep v11 byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"sleep v11 hash changed: {relative}");
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"sleep v11 dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains("a", StringComparison.OrdinalIgnoreCase), $"sleep v11 alpha channel missing: {relative}");
        }

        var actions = manifest.GetProperty("actions").EnumerateArray().ToArray();
        Assert(actions.Sum(x => x.GetProperty("frame_count").GetInt32()) == 44, "sleep v11 action references changed");
        foreach (var action in actions)
        {
            var behaviorId = action.GetProperty("behavior_id").GetString()!;
            Assert(SleepCandidateBehaviorIds.All.Contains(behaviorId), "unknown sleep v11 behavior");
            Assert(action.GetProperty("runtime_render_scale").GetDouble() == 0.78, "sleep action escaped the global scale");
            Assert(!action.GetProperty("deprecated").GetBoolean(), "approved sleep v11 is deprecated");
            foreach (var flag in new[] { "owner_preview_approved", "visual_approved", "runtime_approved", "runtime_use",
                         "production_asset", "developer_preview" })
                Assert(action.GetProperty(flag).GetBoolean(), $"approved action closed {flag}: {behaviorId}");
            Assert(!action.GetProperty("prototype_use").GetBoolean(), $"approved action depends on prototype: {behaviorId}");
            Assert(action.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "action renderer approval missing");
            var autonomous = SleepCandidateBehaviorIds.AutonomousAllowed.Contains(behaviorId);
            Assert(action.GetProperty("autonomous_binding_enabled").GetBoolean() == autonomous, "sleep autonomy policy drifted");
            var sources = action.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
            var expected = autonomous
                ? new HashSet<string?> { "AutonomousTick", "DeveloperPreview", "OwnerDialogue" }
                : new HashSet<string?> { "DeveloperPreview" };
            Assert(sources.SetEquals(expected), "sleep source policy drifted");
        }

        var rules = manifest.GetProperty("sequence_rules");
        Assert(!rules.GetProperty("append_prone_to_side_roll_after_main").GetBoolean(), "standalone roll was appended to the main lifecycle");
        Assert(!rules.GetProperty("hard_cut_between_incompatible_views").GetBoolean(), "incompatible sleep views can hard cut");
        Assert(!rules.GetProperty("reverse_main_as_wake").GetBoolean(), "sleep entry was reversed as an unapproved wake action");
        Assert(!rules.GetProperty("legacy_sleep_visual_fallback_allowed").GetBoolean(), "legacy sleep visuals can be used as fallback");
    }

    public static void ApprovedSleepUsesCompatibleAutonomyAndManualRuntime()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var catalog = DesktopMotionCatalog.Load(output);
        var motions = catalog.Motions.Where(x => x.AssetBatch == SleepCandidateBehaviorIds.AssetBatch).ToArray();
        Assert(motions.Length == 7, "sleep v11 catalog count changed");
        Assert(motions.All(x => !x.IsExpired && x.VisualApproved && x.RuntimeEnabled && x.RuntimeApproved && !x.PrototypeUse),
            "approved sleep v11 gate closed");
        Assert(SleepCandidateBehaviorIds.RuntimeApproved.SetEquals(SleepCandidateBehaviorIds.All), "sleep approval set is incomplete");
        Assert(SleepCandidateBehaviorIds.AutonomousAllowed.SetEquals(new[]
        {
            SleepCandidateBehaviorIds.MainLifecycle,
            SleepCandidateBehaviorIds.SprawledFrontBreath
        }), "sleep autonomous allowlist broadened");
        Assert(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(SleepCandidateBehaviorIds.MainLifecycle), "main sleep is not autonomous");
        Assert(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(SleepCandidateBehaviorIds.SprawledFrontBreath), "front sleep breathing is not autonomous");
        foreach (var id in SleepCandidateBehaviorIds.All.Except(SleepCandidateBehaviorIds.AutonomousAllowed))
            Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(id), $"manual sleep view entered autonomy: {id}");
        Assert(DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(
            SleepCandidateBehaviorIds.MainLifecycle, StablePosture.Prone, frontProneProfileActive: false), "compatible side-prone sleep was blocked");
        Assert(!DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(
            SleepCandidateBehaviorIds.MainLifecycle, StablePosture.Prone, frontProneProfileActive: true), "main sleep can hard-cut from front prone");
        Assert(DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(
            SleepCandidateBehaviorIds.SprawledFrontBreath, StablePosture.Prone, frontProneProfileActive: true), "front breathing was blocked");
        Assert(!DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(
            SleepCandidateBehaviorIds.SprawledFrontBreath, StablePosture.Prone, frontProneProfileActive: false), "front breathing can hard-cut from side prone");

        foreach (var motion in motions)
        {
            var runtime = new DesktopRuntimeHost();
            PetMotionRequest? request = null;
            runtime.MotionRequested += (_, value) => request = value;
            var before = JsonSerializer.Serialize(runtime.AgentStateSnapshot);
            var result = runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted && request is not null, $"approved sleep preview failed: {motion.BehaviorId}");
            var acceptedRequest = request!;
            Assert(acceptedRequest.ExecutionMode == BehaviorExecutionMode.DeveloperPreview, "sleep inspection bypassed isolated preview");
            runtime.CompleteMotion(acceptedRequest.RequestId, motion.BehaviorId, acceptedRequest.Motion.Phases.Last().Name);
            Assert(JsonSerializer.Serialize(runtime.AgentStateSnapshot) == before, "sleep preview changed production state");
        }
    }

    public static void MissingV11FramesFailClosedWithoutLegacyFallback()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var source = Path.Combine(output, "WukongAssets", "action-batches", SleepCandidateBehaviorIds.AssetBatch);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"wukong-sleep-v11-missing-{Guid.NewGuid():N}");
        var candidateRoot = Path.Combine(temporaryRoot, "WukongAssets", "action-batches", SleepCandidateBehaviorIds.AssetBatch);
        Directory.CreateDirectory(candidateRoot);
        try
        {
            File.Copy(Path.Combine(source, "manifest.json"), Path.Combine(candidateRoot, "manifest.json"));
            var catalog = DesktopMotionCatalog.Load(temporaryRoot);
            Assert(!catalog.Motions.Any(x => x.AssetBatch == SleepCandidateBehaviorIds.AssetBatch),
                "sleep v11 with missing frames entered the catalog");
            Assert(!catalog.Motions.Any(x => x.SourceRoot.Contains("WK-CORE-SLEEP-BREATH-v2", StringComparison.OrdinalIgnoreCase)),
                "missing sleep v11 silently fell back to legacy sleep pixels");
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
