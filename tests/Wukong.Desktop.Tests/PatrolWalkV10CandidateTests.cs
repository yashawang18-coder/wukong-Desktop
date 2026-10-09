using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Contracts;
using Wukong.Desktop;
using Wukong.Domain;

internal static class PatrolWalkV10CandidateTests
{
    private const string ExpectedFrameSha256 = "373b4dae88823d4e86616385ad1ded5d1d9845c8a421812167149106e1f5f453";

    public static void CandidateReplacesOnlyTheThirdCycleFrame()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var v8 = Path.Combine(output, "WukongAssets", "action-batches", PatrolWalkCandidateBehaviorIds.PreviousAssetBatch);
        var v10 = Path.Combine(output, "WukongAssets", "action-batches", PatrolWalkV10CandidateBehaviorIds.AssetBatch);
        var manifestPath = Path.Combine(v10, "manifest.json");
        Assert(File.Exists(manifestPath), "walk v10 manifest was not copied");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        Assert(root.GetProperty("owner_preview_approved").GetBoolean() &&
               root.GetProperty("visual_approved").GetBoolean() &&
               root.GetProperty("runtime_approved").GetBoolean() &&
               root.GetProperty("runtime_use").GetBoolean() &&
               root.GetProperty("production_asset").GetBoolean() &&
               !root.GetProperty("prototype_use").GetBoolean() &&
               root.GetProperty("developer_preview").GetBoolean() &&
               root.GetProperty("autonomous_binding_enabled").GetBoolean(),
            "walk v10 production gate is inconsistent");
        Assert(root.GetProperty("allowed_sources").EnumerateArray().Select(item => item.GetString())
            .SequenceEqual(new[] { "AutonomousTick", "DeveloperPreview", "OwnerDialogue" }), "walk v10 source gate changed");

        var inventory = root.GetProperty("frame_inventory").EnumerateArray().ToArray();
        Assert(inventory.Length == 13, "walk v10 inventory count changed");
        foreach (var item in inventory)
        {
            var relative = item.GetProperty("path").GetString()!;
            var path = Path.Combine(v10, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(path), $"walk v10 frame missing: {relative}");
            Assert(new FileInfo(path).Length == item.GetProperty("bytes").GetInt64(), $"walk v10 byte count changed: {relative}");
            Assert(Sha256(path) == item.GetProperty("sha256").GetString(), $"walk v10 hash changed: {relative}");
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
            Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024 && frame.Format.ToString().Contains('a', StringComparison.OrdinalIgnoreCase),
                $"walk v10 frame contract changed: {relative}");
        }

        var replacement = Path.Combine(v10, "frames", "cycle-003.png");
        Assert(Sha256(replacement) == ExpectedFrameSha256, "walk v10 replacement frame changed");
        foreach (var file in Directory.GetFiles(Path.Combine(v10, "frames"), "*.png"))
        {
            if (Path.GetFileName(file).Equals("cycle-003.png", StringComparison.OrdinalIgnoreCase))
                continue;
            var old = Path.Combine(v8, "frames", Path.GetFileName(file));
            Assert(File.Exists(old) && Sha256(file) == Sha256(old), $"walk v10 unexpectedly changed {Path.GetFileName(file)}");
        }
        Assert(Sha256(replacement) != Sha256(Path.Combine(v8, "frames", "cycle-003.png")), "walk v10 did not replace the defective frame");
    }

    public static void ReviewMarkerIsolatesV10FromFormalRuntime()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        var marker = Path.Combine(output, DesktopMotionCatalog.PatrolWalkV10ReviewMarkerFileName);
        File.Delete(marker);
        var normal = DesktopMotionCatalog.Load(output);
        Assert(PatrolWalkCandidateBehaviorIds.All.All(id => normal.Find(id) is
            { AssetBatch: PatrolWalkCandidateBehaviorIds.AssetBatch, RuntimeEnabled: true, RuntimeApproved: true, AutonomousBindingEnabled: true }),
            "normal catalog did not use approved walk v10");

        File.WriteAllText(marker, "local walk v10 review only");
        try
        {
            var review = DesktopMotionCatalog.Load(output);
            var motions = PatrolWalkCandidateBehaviorIds.All.Select(id => review.Find(id)).ToArray();
            Assert(motions.All(motion => motion?.AssetBatch == PatrolWalkV10CandidateBehaviorIds.AssetBatch),
                "legacy review marker changed the approved walk batch");
            Assert(motions.All(motion => motion is { RuntimeEnabled: true, RuntimeApproved: true, VisualApproved: true, AutonomousBindingEnabled: true }),
                "legacy review marker changed walk v10 production eligibility");
            Assert(motions.All(motion => motion!.WindowMotionEnabled && motion.SupportsHorizontalMirror),
                "walk v10 lost real translation or directional mirror support");

            var runtime = new DesktopRuntimeHost();
            var before = runtime.AgentStateSnapshot;
            PetMotionRequest? request = null;
            runtime.MotionRequested += (_, value) => request = value;
            var result = runtime.SubmitDeveloperCandidateMotionAsync(PatrolWalkCandidateBehaviorIds.WalkRight).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted && request is not null, "walk v10 developer preview was rejected");
            Assert(request!.ExecutionMode == BehaviorExecutionMode.DeveloperPreview && request.Motion.WindowMotionEnabled && request.MirrorHorizontally,
                "walk v10 preview bypassed isolated directional playback");
            runtime.CompleteMotion(request.RequestId, request.Motion.BehaviorId, "exit");
            Assert(runtime.AgentStateSnapshot.Runtime == before.Runtime &&
                   runtime.AgentStateSnapshot.Relationship == before.Relationship &&
                   runtime.AgentStateSnapshot.RecentExperience.SequenceEqual(before.RecentExperience),
                "walk v10 preview wrote formal state or memory");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
