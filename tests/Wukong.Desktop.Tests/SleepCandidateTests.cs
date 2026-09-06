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
        Assert(File.Exists(assetPath), "sleep candidate asset.json was not copied");
        Assert(File.Exists(manifestPath), "sleep candidate manifest was not copied");

        using var assetDocument = JsonDocument.Parse(File.ReadAllText(assetPath));
        var asset = assetDocument.RootElement;
        Assert(asset.GetProperty("owner_preview_approved").GetBoolean(), "v10 owner approval was not recorded");
        Assert(asset.GetProperty("visual_approved").GetBoolean(), "v10 visual approval was not recorded");
        Assert(asset.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "sleep runtime validation was not promoted");
        Assert(asset.GetProperty("runtime_approved").GetBoolean(), "sleep runtime approval was not recorded");
        Assert(asset.GetProperty("runtime_use").GetBoolean(), "sleep runtime use was not enabled");
        Assert(asset.GetProperty("production_asset").GetBoolean(), "sleep production status was not recorded");
        Assert(!asset.GetProperty("prototype_use").GetBoolean(), "sleep candidate incorrectly enabled owner prototype use");
        Assert(asset.GetProperty("developer_preview").GetBoolean(), "sleep developer review was disabled");
        Assert(asset.GetProperty("autonomous_binding_enabled").GetBoolean(), "compatible sleep actions were not enabled for autonomous use");
        Assert(asset.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "AutonomousTick", "DeveloperPreview" }),
            "sleep batch source policy changed");
        Assert(asset.GetProperty("runtime_render_scale").GetDouble() == 0.61, "sleep batch reference scale changed");
        Assert(asset.GetProperty("deprecated_action_count").GetInt32() == 4, "sleep deprecated action count changed");
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
        var expectedScales = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [SleepCandidateBehaviorIds.MainLifecycle] = 0.61,
            [SleepCandidateBehaviorIds.ProneToSideRoll] = 0.64,
            [SleepCandidateBehaviorIds.SprawledFrontBreath] = 0.63,
            [SleepCandidateBehaviorIds.SprawledLeftSideBreath] = 0.78,
            [SleepCandidateBehaviorIds.SprawledRightSideBreath] = 0.80,
            [SleepCandidateBehaviorIds.CompactProneBreath] = 1.01,
            [SleepCandidateBehaviorIds.CurledSideBreath] = 0.92,
            [SleepCandidateBehaviorIds.TopDownProneBreath] = 1.04
        };
        var deprecatedIds = new HashSet<string>(StringComparer.Ordinal)
        {
            SleepCandidateBehaviorIds.SprawledRightSideBreath,
            SleepCandidateBehaviorIds.CompactProneBreath,
            SleepCandidateBehaviorIds.CurledSideBreath,
            SleepCandidateBehaviorIds.TopDownProneBreath
        };
        var autonomousIds = new HashSet<string>(StringComparer.Ordinal)
        {
            SleepCandidateBehaviorIds.MainLifecycle,
            SleepCandidateBehaviorIds.SprawledFrontBreath
        };
        foreach (var action in actions)
        {
            var behaviorId = action.GetProperty("behavior_id").GetString()!;
            var deprecated = deprecatedIds.Contains(behaviorId);
            Assert(SleepCandidateBehaviorIds.All.Contains(behaviorId), "unknown sleep candidate behavior id");
            Assert(action.GetProperty("runtime_render_scale").GetDouble() == expectedScales[behaviorId],
                $"sleep action scale changed: {behaviorId}");
            Assert(action.GetProperty("deprecated").GetBoolean() == deprecated, $"sleep action deprecation changed: {behaviorId}");
            if (deprecated)
            {
                Assert(!action.GetProperty("owner_preview_approved").GetBoolean() &&
                       !action.GetProperty("visual_approved").GetBoolean() &&
                       !action.GetProperty("runtime_approved").GetBoolean() &&
                       !action.GetProperty("runtime_use").GetBoolean() &&
                       !action.GetProperty("production_asset").GetBoolean() &&
                       !action.GetProperty("prototype_use").GetBoolean() &&
                       !action.GetProperty("autonomous_binding_enabled").GetBoolean(),
                    "deprecated sleep action escaped its closed gate");
                Assert(action.GetProperty("runtime_validation").GetString() == "failed_owner_visual_qa", "deprecated sleep action validation changed");
                Assert(!action.GetProperty("developer_preview").GetBoolean(), "deprecated sleep action remains playable");
                Assert(action.GetProperty("allowed_sources").GetArrayLength() == 0, "deprecated sleep action retains a request source");
                Assert(action.GetProperty("deprecated_reason").GetString() == "owner_rejected_color_and_fur_texture_2026_09_05",
                    "deprecated sleep action reason changed");
            }
            else
            {
                var autonomous = autonomousIds.Contains(behaviorId);
                Assert(action.GetProperty("owner_preview_approved").GetBoolean() &&
                       action.GetProperty("visual_approved").GetBoolean() &&
                       action.GetProperty("runtime_approved").GetBoolean() &&
                       action.GetProperty("runtime_use").GetBoolean() &&
                       action.GetProperty("production_asset").GetBoolean() &&
                       !action.GetProperty("prototype_use").GetBoolean(),
                    "approved sleep action gate changed");
                Assert(action.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "approved sleep validation changed");
                Assert(action.GetProperty("developer_preview").GetBoolean(), "active sleep developer preview was disabled");
                Assert(action.GetProperty("autonomous_binding_enabled").GetBoolean() == autonomous,
                    "sleep autonomous compatibility gate changed");
                var expectedSources = autonomous ? new[] { "AutonomousTick", "DeveloperPreview" } : new[] { "DeveloperPreview" };
                Assert(action.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(expectedSources),
                    "approved sleep source policy changed");
            }
        }

        var rules = manifest.GetProperty("sequence_rules");
        Assert(!rules.GetProperty("append_prone_to_side_roll_after_main").GetBoolean(), "standalone roll was appended to the main lifecycle");
        Assert(!rules.GetProperty("hard_cut_between_incompatible_views").GetBoolean(), "incompatible sleep views can hard cut");
        Assert(!rules.GetProperty("reverse_main_as_wake").GetBoolean(), "sleep entry was reversed as an unapproved wake action");
        Assert(!rules.GetProperty("legacy_sleep_visual_fallback_allowed").GetBoolean(), "legacy sleep visuals can be used as fallback");
    }

    public static void ApprovedSleepUsesCompatibleAutonomousRoutesAndIsolatedPreview()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var catalog = DesktopMotionCatalog.Load(output);
        var motions = catalog.Motions.Where(x => x.AssetBatch == SleepCandidateBehaviorIds.AssetBatch).ToArray();
        Assert(motions.Length == 8, "catalog did not expose all eight v10 sleep actions");
        Assert(!catalog.Motions.Any(x => x.SourceRoot.Contains("WK-CORE-SLEEP-BREATH-v2", StringComparison.OrdinalIgnoreCase)),
            "legacy sleep pixels remain discoverable through the desktop catalog");
        Assert(motions.Where(x => !x.IsExpired).All(x => x.VisualApproved && x.RuntimeEnabled && x.RuntimeApproved && !x.PrototypeUse),
            "approved sleep catalog gate changed");
        Assert(motions.Where(x => x.IsExpired).All(x => !x.VisualApproved && !x.RuntimeEnabled && !x.RuntimeApproved && !x.AutonomousBindingEnabled),
            "deprecated sleep catalog gate changed");
        Assert(motions.Count(x => x.IsExpired) == 4, "sleep candidate must retain exactly four deprecated review records");
        Assert(motions.Count(x => !x.IsExpired) == 4, "sleep candidate must retain exactly four active review records");
        Assert(motions.Where(x => x.AutonomousBindingEnabled).Select(x => x.BehaviorId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(SleepCandidateBehaviorIds.AutonomousAllowed), "sleep autonomous posture allowlist changed");
        Assert(SleepCandidateBehaviorIds.AutonomousAllowed.All(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed),
            "compatible sleep action is missing from the autonomous runtime allowlist");
        Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(SleepCandidateBehaviorIds.ProneToSideRoll) &&
               !DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(SleepCandidateBehaviorIds.SprawledLeftSideBreath),
            "sleep action without a compatible runtime pose entered the autonomous allowlist");
        Assert(DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(SleepCandidateBehaviorIds.MainLifecycle, StablePosture.Prone, frontProneProfileActive: false),
            "main sleep lifecycle cannot start from its compatible prone profile");
        Assert(!DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(SleepCandidateBehaviorIds.MainLifecycle, StablePosture.Prone, frontProneProfileActive: true),
            "main sleep lifecycle can hard-cut from the front-prone profile");
        Assert(DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(SleepCandidateBehaviorIds.SprawledFrontBreath, StablePosture.Prone, frontProneProfileActive: true),
            "front breathing cannot start from its compatible front-prone profile");
        Assert(!DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(SleepCandidateBehaviorIds.SprawledFrontBreath, StablePosture.Prone, frontProneProfileActive: false),
            "front breathing can hard-cut from a non-front prone profile");
        Assert(!DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(SleepCandidateBehaviorIds.ProneToSideRoll, StablePosture.Prone, frontProneProfileActive: false) &&
               !DesktopRuntimeHost.IsSleepAutonomousProfileAllowed(SleepCandidateBehaviorIds.SprawledLeftSideBreath, StablePosture.Prone, frontProneProfileActive: false),
            "sleep action without a represented bridge passed posture eligibility");

        var runtime = new DesktopRuntimeHost();
        Assert(runtime.AutonomousDailyCandidateMotions.Count(x => x.AssetBatch == SleepCandidateBehaviorIds.AssetBatch) == 8,
            "sleep candidates are missing from the developer autonomous review page");
        var postureBefore = runtime.CurrentStablePosture;
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        foreach (var motion in motions.Where(x => !x.IsExpired))
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
            Assert(request.Motion.BehaviorId == LifecycleCandidateBehaviorIds.ProneIdleMicroloop, $"sleep completion selected an unrelated posture idle: {motion.BehaviorId}");
            Assert(request.Motion.RenderScaleOverride == 0.68, $"sleep completion returned to the oversized prone idle scale: {motion.BehaviorId}");
        }

        foreach (var motion in motions.Where(x => x.IsExpired))
        {
            request = null;
            var result = runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Deferred, $"deprecated sleep action was playable: {motion.BehaviorId}");
            Assert(request is null, $"deprecated sleep action emitted a motion request: {motion.BehaviorId}");
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
