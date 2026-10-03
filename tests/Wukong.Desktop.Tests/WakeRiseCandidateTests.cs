using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class WakeRiseCandidateTests
{
    public static void RuntimeFramesAndDeveloperPreviewStayIsolated()
    {
        var output = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;
        AssertBatch(
            output,
            WakeRiseCandidateBehaviorIds.AssetBatch,
            WakeRiseCandidateBehaviorIds.All,
            expectedFrameCount: 27,
            expectedActionCount: 7,
            expectWindowMotion: false);
        AssertBatch(
            output,
            PatrolWalkV9CandidateBehaviorIds.AssetBatch,
            PatrolWalkV9CandidateBehaviorIds.All,
            expectedFrameCount: 26,
            expectedActionCount: 2,
            expectWindowMotion: true);

        var runtime = new DesktopRuntimeHost();
        var before = runtime.AgentStateSnapshot;
        var motions = runtime.Motions
            .Where(x => x.AssetBatch is WakeRiseCandidateBehaviorIds.AssetBatch or PatrolWalkV9CandidateBehaviorIds.AssetBatch)
            .ToArray();
        PatrolWalkCandidateTests.Assert(motions.Length == 9, "v9 catalog count invalid");
        var wakeRise = motions.Where(x => x.AssetBatch == WakeRiseCandidateBehaviorIds.AssetBatch).ToArray();
        PatrolWalkCandidateTests.Assert(wakeRise.Length == 7 && wakeRise.All(x =>
                x.VisualApproved && x.RuntimeApproved && x.RuntimeEnabled && x.AutonomousBindingEnabled),
            "approved wake/rise runtime boundary invalid");
        PatrolWalkCandidateTests.Assert(runtime.Motions.Any(x => x.AssetBatch == PatrolWalkCandidateBehaviorIds.AssetBatch && x.RuntimeEnabled),
            "candidate registration displaced approved v8 walk");
        PatrolWalkCandidateTests.Assert(
            motions.Where(x => x.AssetBatch == PatrolWalkV9CandidateBehaviorIds.AssetBatch)
                .All(x => x.WindowMotionEnabled && x.SupportsHorizontalMirror),
            "v9 directional walk is missing its horizontal mirror contract");

        var capabilityCatalog = DesktopBehaviorCapabilityCatalog.Create(runtime.Motions);
        foreach (var behaviorId in WakeRiseCandidateBehaviorIds.All)
        {
            var capability = capabilityCatalog.Find(behaviorId);
            PatrolWalkCandidateTests.Assert(capability is { ProductionApproved: true, RuntimeUse: true, ProductionAsset: true, AutonomousBindingEnabled: true },
                $"wake/rise capability was not promoted: {behaviorId}");
            PatrolWalkCandidateTests.Assert(DesktopBehaviorOutcomeProfiles.Find(behaviorId) is not null,
                $"wake/rise reducer profile is missing: {behaviorId}");
        }

        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        foreach (var motion in motions)
        {
            request = null;
            var result = runtime.SubmitDeveloperCandidateMotionAsync(motion.BehaviorId).GetAwaiter().GetResult();
            PatrolWalkCandidateTests.Assert(result == PetActionResult.Accepted && request is not null, "candidate preview request failed");
            PatrolWalkCandidateTests.Assert(request!.ExecutionMode == BehaviorExecutionMode.DeveloperPreview, "developer preview bypassed isolated mode");
            PatrolWalkCandidateTests.Assert(request.Motion.BehaviorId == motion.BehaviorId, "candidate preview resolved a different motion");
            runtime.CompleteMotion(request.RequestId, motion.BehaviorId, "sequence");
            PatrolWalkCandidateTests.Assert(runtime.AgentStateSnapshot.Relationship == before.Relationship &&
                                           runtime.AgentStateSnapshot.RecentExperience.SequenceEqual(before.RecentExperience),
                "developer preview wrote production state");
        }
    }

    private static void AssertBatch(
        string output,
        string batchId,
        IReadOnlySet<string> expectedBehaviorIds,
        int expectedFrameCount,
        int expectedActionCount,
        bool expectWindowMotion)
    {
        var root = Path.Combine(output, "WukongAssets", "action-batches", batchId);
        using var asset = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "asset.json")));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        PatrolWalkCandidateTests.Assert(asset.RootElement.GetRawText() == document.RootElement.GetRawText(), "asset/manifest drift");
        var manifest = document.RootElement;
        PatrolWalkCandidateTests.Assert(manifest.GetProperty("owner_preview_approved").GetBoolean(), "owner visual approval missing");
        PatrolWalkCandidateTests.Assert(manifest.GetProperty("visual_approved").GetBoolean(), "visual approval missing");
        var isWakeRise = batchId == WakeRiseCandidateBehaviorIds.AssetBatch;
        PatrolWalkCandidateTests.Assert(
            isWakeRise
                ? manifest.GetProperty("runtime_approved").GetBoolean() && manifest.GetProperty("runtime_use").GetBoolean() && manifest.GetProperty("production_asset").GetBoolean()
                : !manifest.GetProperty("runtime_approved").GetBoolean() && !manifest.GetProperty("runtime_use").GetBoolean() && !manifest.GetProperty("production_asset").GetBoolean(),
            "batch runtime boundary invalid");
        PatrolWalkCandidateTests.Assert(manifest.GetProperty("developer_preview").GetBoolean() &&
                                       manifest.GetProperty("autonomous_binding_enabled").GetBoolean() == isWakeRise,
            "batch autonomous policy invalid");
        PatrolWalkCandidateTests.Assert(manifest.GetProperty("runtime_validation").GetString() ==
                                       (isWakeRise ? "passed_windows_renderer_qa" : "pending_windows_renderer_qa"),
            "batch QA state invalid");
        PatrolWalkCandidateTests.Assert(manifest.GetProperty("window_motion_enabled").GetBoolean() == expectWindowMotion, "window motion contract invalid");

        var inventory = manifest.GetProperty("frame_inventory").EnumerateArray().ToArray();
        PatrolWalkCandidateTests.Assert(inventory.Length == expectedFrameCount &&
                                       inventory.Select(x => x.GetProperty("path").GetString()).Distinct().Count() == expectedFrameCount,
            "frame inventory count invalid");
        foreach (var item in inventory)
        {
            var framePath = Path.Combine(root, item.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(framePath);
            PatrolWalkCandidateTests.Assert(bytes.Length == item.GetProperty("bytes").GetInt64(), "frame byte count invalid");
            PatrolWalkCandidateTests.Assert(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == item.GetProperty("sha256").GetString(), "frame SHA invalid");
            using var stream = File.OpenRead(framePath);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
            PatrolWalkCandidateTests.Assert(frame.PixelWidth == 1024 && frame.PixelHeight == 1024 &&
                                           frame.Format.ToString().Contains('a', StringComparison.OrdinalIgnoreCase), "frame RGBA contract invalid");
        }

        var actions = manifest.GetProperty("actions").EnumerateArray().ToArray();
        PatrolWalkCandidateTests.Assert(actions.Length == expectedActionCount, "action count invalid");
        PatrolWalkCandidateTests.Assert(actions.Select(x => x.GetProperty("behavior_id").GetString()).ToHashSet(StringComparer.Ordinal)
            .SetEquals(expectedBehaviorIds), "behavior IDs invalid");
        foreach (var action in actions)
        {
            PatrolWalkCandidateTests.Assert(
                isWakeRise
                    ? action.GetProperty("runtime_approved").GetBoolean() && action.GetProperty("runtime_use").GetBoolean() && action.GetProperty("autonomous_binding_enabled").GetBoolean()
                    : !action.GetProperty("runtime_approved").GetBoolean() && !action.GetProperty("runtime_use").GetBoolean() && !action.GetProperty("autonomous_binding_enabled").GetBoolean(),
                "action runtime gate invalid");
            var frameCount = action.GetProperty("phases").EnumerateArray()
                .Sum(phase => phase.GetProperty("frames").GetArrayLength());
            PatrolWalkCandidateTests.Assert(frameCount == action.GetProperty("frame_count").GetInt32(), "action frame count invalid");
        }

        if (!isWakeRise) return;
        var byBehavior = actions.ToDictionary(action => action.GetProperty("behavior_id").GetString()!, StringComparer.Ordinal);
        AssertRemovedFrame(byBehavior[WakeRiseCandidateBehaviorIds.FrontWake], "frames/front-wake/awake.png", 4, 1880);
        AssertRemovedFrame(byBehavior[WakeRiseCandidateBehaviorIds.FrontRiseSit], "frames/front-rise/near-sit.png", 5, 2230);
        AssertRemovedFrame(byBehavior[WakeRiseCandidateBehaviorIds.FrontRiseFull], "frames/front-rise/stand-half.png", 8, 3370);
        AssertRemovedFrame(byBehavior[WakeRiseCandidateBehaviorIds.FrontSitStand], "frames/front-rise/stand-half.png", 3, 1680);
    }

    private static void AssertRemovedFrame(JsonElement action, string removedPath, int expectedFrameCount, int expectedDurationMs)
    {
        var frames = action.GetProperty("phases").EnumerateArray()
            .SelectMany(phase => phase.GetProperty("frames").EnumerateArray())
            .ToArray();
        PatrolWalkCandidateTests.Assert(frames.All(frame => frame.GetProperty("path").GetString() != removedPath),
            $"removed wake/rise frame remains active: {removedPath}");
        PatrolWalkCandidateTests.Assert(action.GetProperty("frame_count").GetInt32() == expectedFrameCount &&
                                       action.GetProperty("total_duration_ms").GetInt32() == expectedDurationMs,
            $"wake/rise timeline metadata was not adjusted for: {removedPath}");
    }
}
