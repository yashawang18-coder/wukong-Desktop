using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class FoodWaterCandidateTests
{
    private const string SourceManifestSha256 = "db81cb11a0dd4e3bc3b10dd6f013c4a0d9cbc99561a7fd3e75f6fb9c6e860449";

    public static void ManifestFramesAndRuntimeGateAreValid()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var batchRoot = Path.Combine(output, "WukongAssets", "action-batches", FoodWaterCandidateBehaviorIds.AssetBatch);
        var assetPath = Path.Combine(batchRoot, "asset.json");
        var manifestPath = Path.Combine(batchRoot, "manifest.json");
        Assert(File.Exists(assetPath), "food/water v5 candidate asset.json was not copied");
        Assert(File.Exists(manifestPath), "food/water v5 candidate manifest was not copied");

        using var assetDocument = JsonDocument.Parse(File.ReadAllText(assetPath));
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        foreach (var document in new[] { assetDocument.RootElement, manifestDocument.RootElement })
        {
            Assert(document.GetProperty("batch_id").GetString() == FoodWaterCandidateBehaviorIds.AssetBatch, "food/water v5 batch id changed");
            Assert(document.GetProperty("source_package").GetString() == "WK-FOOD-WATER-COAT-SEAM-v5", "source package identity changed");
            Assert(document.GetProperty("source_sha256_manifest_sha256").GetString() == SourceManifestSha256, "source SHA manifest changed");
            Assert(document.GetProperty("source_frame_count").GetInt32() == 48, "source unique frame count changed");
            Assert(document.GetProperty("runtime_frame_count").GetInt32() == 48, "runtime unique frame count changed");
            Assert(document.GetProperty("sequence_frame_reference_count").GetInt32() == 194, "timeline reference count changed");
            Assert(document.GetProperty("referenced_unique_frame_count").GetInt32() == 44, "timeline unique frame count changed");
            Assert(document.GetProperty("owner_preview_approved").GetBoolean(), "owner approval is missing");
            Assert(document.GetProperty("visual_approved").GetBoolean(), "visual approval is missing");
            Assert(document.GetProperty("runtime_validation").GetString() == "passed_windows_renderer_qa", "runtime validation state changed");
            Assert(document.GetProperty("runtime_approved").GetBoolean(), "runtime approval is missing");
            Assert(document.GetProperty("runtime_use").GetBoolean(), "runtime use is disabled");
            Assert(document.GetProperty("production_asset").GetBoolean(), "approved v5 asset is not marked production");
            Assert(!document.GetProperty("prototype_use").GetBoolean(), "approved owner action must not require prototype mode");
            Assert(document.GetProperty("developer_preview").GetBoolean(), "developer preview was disabled");
            Assert(!document.GetProperty("autonomous_binding_enabled").GetBoolean(), "owner action entered the autonomous pool");
            Assert(document.GetProperty("normal_runtime_available").GetBoolean(), "approved owner action is not available to Normal runtime");
            Assert(document.GetProperty("allowed_sources").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "OwnerContextMenu", "ControlPanel", "DeveloperPreview" }),
                "food/water v5 source policy changed");
        }

        var manifest = manifestDocument.RootElement;
        var inventory = manifest.GetProperty("frame_inventory").EnumerateArray().ToArray();
        Assert(inventory.Length == 48, "food/water v5 inventory must contain 48 unique runtime PNGs");
        Assert(inventory.Select(x => x.GetProperty("path").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 48,
            "food/water v5 inventory contains duplicate paths");
        Assert(Directory.GetFiles(Path.Combine(batchRoot, "frames"), "*.png", SearchOption.AllDirectories).Length == 48,
            "food/water v5 runtime frame directory count changed");

        foreach (var item in inventory)
        {
            var relative = item.GetProperty("path").GetString()!;
            var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"food/water v5 frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"food/water v5 byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"food/water v5 hash changed: {relative}");

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024, $"food/water v5 dimensions changed: {relative}");
            Assert(frame.Format.ToString().Contains("a", StringComparison.OrdinalIgnoreCase), $"food/water v5 alpha channel missing: {relative}");
        }

        var unreferenced = manifest.GetProperty("unreferenced_source_frames").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert(unreferenced.SequenceEqual(new[]
        {
            "frames/drink-pause/frame-05.png",
            "frames/drink-pause/frame-06.png",
            "frames/eat-pause/frame-05.png",
            "frames/eat-pause/frame-06.png"
        }), "food/water v5 unreferenced provenance frames changed");

        var actions = manifest.GetProperty("actions").EnumerateArray().ToArray();
        var expected = new Dictionary<string, (int Frames, int Duration, int[] Phases)>(StringComparer.Ordinal)
        {
            [FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5] = (91, 11_375, new[] { 6, 75, 10 }),
            [FoodWaterCandidateBehaviorIds.EatKibbleStandingV5] = (103, 12_875, new[] { 6, 87, 10 })
        };
        Assert(actions.Length == 2, "food/water v5 manifest must contain two actions");
        Assert(actions.Select(x => x.GetProperty("behavior_id").GetString()).ToHashSet(StringComparer.Ordinal).SetEquals(expected.Keys),
            "food/water v5 action ids changed");
        var timelinePaths = new List<string>();
        foreach (var action in actions)
        {
            var behaviorId = action.GetProperty("behavior_id").GetString()!;
            var contract = expected[behaviorId];
            Assert(action.GetProperty("frame_count").GetInt32() == contract.Frames, $"frame count changed: {behaviorId}");
            Assert(action.GetProperty("total_duration_ms").GetInt32() == contract.Duration, $"duration changed: {behaviorId}");
            Assert(action.GetProperty("frame_duration_ms").GetInt32() == 125, $"frame timing changed: {behaviorId}");
            Assert(action.GetProperty("from_pose").GetString() == "stand.neutral.left_front" &&
                   action.GetProperty("to_pose").GetString() == "stand.neutral.left_front", $"standing posture contract changed: {behaviorId}");
            Assert(!action.GetProperty("interruptible").GetBoolean(), $"feeding body loop became autonomously interruptible: {behaviorId}");
            var phases = action.GetProperty("phases").EnumerateArray().ToArray();
            Assert(phases.Length == 3, $"intro/action/exit phases changed: {behaviorId}");
            Assert(phases.Select(x => x.GetProperty("frame_count").GetInt32()).SequenceEqual(contract.Phases), $"phase counts changed: {behaviorId}");
            var frames = phases.SelectMany(x => x.GetProperty("frames").EnumerateArray()).ToArray();
            Assert(frames.Length == contract.Frames, $"phase frame total changed: {behaviorId}");
            Assert(frames.All(x => x.GetProperty("duration_ms").GetInt32() == 125), $"phase timing changed: {behaviorId}");
            timelinePaths.AddRange(frames.Select(x => x.GetProperty("path").GetString()!));
        }
        Assert(timelinePaths.Count == 194, "food/water v5 timeline reference count changed");
        Assert(timelinePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 44, "food/water v5 timeline unique reference count changed");
    }

    public static void OwnerRoutesUseNormalAndOtherSourcesStayClosed()
    {
        var runtime = new DesktopRuntimeHost();
        var motions = runtime.FoodWaterCandidateMotions;
        Assert(motions.Count == 2, "food/water v5 catalog did not expose both actions");
        Assert(motions.All(x => x.RuntimeEnabled && x.RuntimeApproved && !x.PrototypeUse && !x.AutonomousBindingEnabled),
            "food/water v5 owner gate changed");
        Assert(motions.All(x => x.StartPose == "stand.neutral.left_front" && x.EndPose == "stand.neutral.left_front"),
            "food/water v5 posture contract changed");

        runtime.UpdateBehaviorAgentMock(
            TemperamentProfile.Default,
            PetRuntimeState.Default with { CurrentPosture = StablePosture.Stand },
            RelationshipState.Default,
            81);
        var requests = new List<PetMotionRequest>();
        runtime.MotionRequested += (_, request) => requests.Add(request);

        var drink = runtime.SubmitFoodWaterAsync(FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
        Assert(drink == PetActionResult.Accepted, "owner menu drinking was not accepted");
        var foodRequests = requests.Where(x => FoodWaterCandidateBehaviorIds.All.Contains(x.Motion.BehaviorId)).ToArray();
        Assert(foodRequests.Length == 1 && foodRequests[0].Motion.BehaviorId == FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5,
            "owner menu requested the wrong drinking action");
        Assert(foodRequests[0].Source == BehaviorRequestSource.OwnerContextMenu && foodRequests[0].ExecutionMode == BehaviorExecutionMode.Normal,
            "owner menu drinking bypassed the Normal request path");
        runtime.CompleteMotion(FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5, "exit");

        var eat = runtime.SubmitFoodWaterAsync(FoodWaterCandidateBehaviorIds.EatKibbleStandingV5, BehaviorRequestSource.ControlPanel).GetAwaiter().GetResult();
        Assert(eat == PetActionResult.Accepted, "control panel eating was not accepted");
        foodRequests = requests.Where(x => FoodWaterCandidateBehaviorIds.All.Contains(x.Motion.BehaviorId)).ToArray();
        Assert(foodRequests.Length == 2 && foodRequests[1].Motion.BehaviorId == FoodWaterCandidateBehaviorIds.EatKibbleStandingV5,
            "control panel requested the wrong eating action");
        Assert(foodRequests[1].Source == BehaviorRequestSource.ControlPanel && foodRequests[1].ExecutionMode == BehaviorExecutionMode.Normal,
            "control panel eating bypassed the Normal request path");
        runtime.CompleteMotion(FoodWaterCandidateBehaviorIds.EatKibbleStandingV5, "exit");

        var dialogue = runtime.SubmitFoodWaterAsync(FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5, BehaviorRequestSource.Dialogue).GetAwaiter().GetResult();
        var autonomous = runtime.SubmitFoodWaterAsync(FoodWaterCandidateBehaviorIds.EatKibbleStandingV5, BehaviorRequestSource.AutonomousTick).GetAwaiter().GetResult();
        Assert(dialogue == PetActionResult.Deferred && autonomous == PetActionResult.Deferred,
            "non-owner source entered the food/water Normal route");
        Assert(requests.Count(x => FoodWaterCandidateBehaviorIds.All.Contains(x.Motion.BehaviorId)) == 2,
            "a forbidden food/water source emitted playback");
        Assert(!DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5) &&
               !DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(FoodWaterCandidateBehaviorIds.EatKibbleStandingV5),
            "food/water v5 entered autonomous runtime allowlist");

        var previewStateBefore = (runtime.CurrentStablePosture, runtime.Energy, runtime.Hunger, runtime.Mood, runtime.Stress, runtime.Comfort);
        var preview = runtime.SubmitDeveloperCandidateMotionAsync(FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5).GetAwaiter().GetResult();
        Assert(preview == PetActionResult.Accepted && requests.Last().ExecutionMode == BehaviorExecutionMode.DeveloperPreview,
            "developer preview no longer uses its isolated execution mode");
        runtime.CompleteMotion(FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5, "exit");
        var previewStateAfter = (runtime.CurrentStablePosture, runtime.Energy, runtime.Hunger, runtime.Mood, runtime.Stress, runtime.Comfort);
        Assert(previewStateAfter == previewStateBefore, "developer preview changed formal food/water state");

        runtime.UpdateBehaviorAgentMock(
            TemperamentProfile.Default,
            PetRuntimeState.Default with { CurrentPosture = StablePosture.Prone },
            RelationshipState.Default,
            82);
        var proneStart = requests.Count;
        var proneEat = runtime.SubmitFoodWaterAsync(FoodWaterCandidateBehaviorIds.EatKibbleStandingV5, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
        Assert(proneEat == PetActionResult.Accepted && requests[proneStart].Motion.BehaviorId == AutonomousDailyCandidateBehaviorIds.ProneToSit,
            "prone eating did not start with the approved prone-to-sit transition");
        runtime.CompleteMotion(AutonomousDailyCandidateBehaviorIds.ProneToSit, "exit");
        Assert(requests.Last().Motion.BehaviorId == AutonomousDailyCandidateBehaviorIds.SitToStand,
            "prone eating did not continue through the approved sit-to-stand transition");
        runtime.CompleteMotion(AutonomousDailyCandidateBehaviorIds.SitToStand, "exit");
        Assert(requests.Last().Motion.BehaviorId == FoodWaterCandidateBehaviorIds.EatKibbleStandingV5 &&
               requests.Last().ExecutionMode == BehaviorExecutionMode.Normal,
            "posture-safe eating sequence did not reach the approved food action");
        runtime.CompleteMotion(FoodWaterCandidateBehaviorIds.EatKibbleStandingV5, "exit");
    }

    public static void MissingFramesFailClosed()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var source = Path.Combine(output, "WukongAssets", "action-batches", FoodWaterCandidateBehaviorIds.AssetBatch);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"wukong-food-water-v5-missing-{Guid.NewGuid():N}");
        var candidateRoot = Path.Combine(temporaryRoot, "WukongAssets", "action-batches", FoodWaterCandidateBehaviorIds.AssetBatch);
        Directory.CreateDirectory(candidateRoot);
        try
        {
            File.Copy(Path.Combine(source, "manifest.json"), Path.Combine(candidateRoot, "manifest.json"));
            var catalog = DesktopMotionCatalog.Load(temporaryRoot);
            Assert(!catalog.Motions.Any(x => x.AssetBatch == FoodWaterCandidateBehaviorIds.AssetBatch),
                "food/water v5 candidate with missing frames entered the catalog");
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
