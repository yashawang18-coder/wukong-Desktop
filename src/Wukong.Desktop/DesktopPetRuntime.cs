using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using Wukong.Application;
using Wukong.Domain;
using Wukong.Infrastructure;

namespace Wukong.Desktop;

public enum PetGestureKind
{
    None,
    OwnerTouch,
    Stroke,
    Drag,
    RapidTap,
    DoubleClick
}

public enum PetActionResult
{
    Accepted,
    Rejected,
    Deferred,
    MissingAsset,
    Interrupted,
    Failed
}

public enum DesktopMotionEffect
{
    None,
    BroomFlight,
    Apparate,
    Petrify,
    PetrifyRelease,
    Scourgify,
    CarRide
}

public sealed record GestureSample(Point Down, Point Up, TimeSpan Duration, int ClickCount, bool HitVisibleBody);

public static class GestureInterpreter
{
    public static PetGestureKind Interpret(GestureSample sample)
    {
        if (!sample.HitVisibleBody)
            return PetGestureKind.None;
        if (sample.ClickCount >= 3)
            return PetGestureKind.RapidTap;
        if (sample.ClickCount >= 2)
            return PetGestureKind.DoubleClick;

        var distance = Distance(sample.Down, sample.Up);
        if (sample.Duration <= TimeSpan.FromMilliseconds(520) && distance <= 8)
            return PetGestureKind.None;
        if (sample.Duration <= TimeSpan.FromMilliseconds(900) && distance is > 8 and <= 72)
            return PetGestureKind.Stroke;
        if (sample.Duration > TimeSpan.FromMilliseconds(180) && distance > 72)
            return PetGestureKind.Drag;

        return PetGestureKind.None;
    }

    public static bool IsRapidTap(DateTimeOffset now, DateTimeOffset lastTap, int count) =>
        count >= 3 && now - lastTap <= TimeSpan.FromMilliseconds(900);

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public sealed record MotionPhase(
    string Name,
    IReadOnlyList<string> Frames,
    bool Loop,
    IReadOnlyList<int>? FrameDurationsMs = null,
    double? VisualScale = null)
{
    public int DurationForFrame(int frameIndex, int fallbackMs)
    {
        if (FrameDurationsMs is null || FrameDurationsMs.Count == 0)
            return fallbackMs;
        var index = Math.Clamp(frameIndex, 0, FrameDurationsMs.Count - 1);
        var value = FrameDurationsMs[index];
        return value > 0 ? value : fallbackMs;
    }

    public bool HasVariableDurations => FrameDurationsMs is not null && FrameDurationsMs.Count > 0;
    public int DurationTotalMs(int fallbackMs) => Frames.Select((_, index) => DurationForFrame(index, fallbackMs)).Sum();
}

public sealed record PlayableMotion(
    string BehaviorId,
    string DisplayName,
    string Category,
    string Direction,
    int FrameDurationMs,
    bool Interruptible,
    IReadOnlyList<MotionPhase> Phases,
    string SourceRoot,
    bool RuntimeEnabled = true,
    string Status = "Ready",
    string MissingContent = "None",
    string StartPose = "prone.awake.left_front",
    string EndPose = "prone.awake.left_front",
    string StyleGroup = "wukong-current-adult-v1",
    string Disposition = "Enabled",
    bool PrototypeUse = false,
    string AssetBatch = "built-in",
    DesktopMotionEffect Effect = DesktopMotionEffect.None,
    string Description = "",
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DirectionalFrames = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? NamedSequences = null,
    IReadOnlyDictionary<string, IReadOnlyList<int>>? NamedSequenceFrameDurations = null,
    string CandidateProfile = "",
    double VisualScale = 1.0,
    double? RenderScaleOverride = null,
    IReadOnlyList<string>? ScaleReferenceFrames = null,
    bool VisualApproved = false,
    bool RuntimeApproved = false,
    bool AutonomousBindingEnabled = false,
    bool Deprecated = false)
{
    public bool IsUsable => Phases.Any(x => x.Frames.Count > 0);
    public string FirstFrame => Phases.SelectMany(x => x.Frames).FirstOrDefault() ?? string.Empty;
    public string FirstFrameFileName => Path.GetFileName(FirstFrame);
    public int FrameCount => Phases.Sum(x => x.Frames.Count);
    public double Fps => FrameDurationMs <= 0 ? 0 : 1000.0 / FrameDurationMs;
    public string PreviewStatus => IsUsable ? "Preview ready" : "Missing frames";
    public string RuntimeStatus => RuntimeEnabled ? "Runtime enabled" : "Preview only / locked";
    public bool IsExpired => Deprecated || string.Equals(Disposition, "已过期", StringComparison.OrdinalIgnoreCase);
    public bool EffectiveVisualApproved => VisualApproved || RuntimeEnabled;
    public bool EffectiveRuntimeApproved => RuntimeApproved || RuntimeEnabled;
    public string StatusSummary => IsExpired
        ? "已过期"
        : EffectiveRuntimeApproved && RuntimeEnabled
            ? "已启用"
            : EffectiveRuntimeApproved
                ? "运行已批准 · 当前停用"
                : EffectiveVisualApproved
                    ? "视觉已通过 · 未启用"
                    : "待视觉验收";
    public string StatusDetails => string.Join(Environment.NewLine, new[]
    {
        $"视觉状态：{(EffectiveVisualApproved ? "已通过" : "待验收")}",
        $"运行批准：{(EffectiveRuntimeApproved ? "已批准" : "未批准")}",
        $"当前启用：{(RuntimeEnabled ? "是" : "否")}",
        $"自主行为池：{(AutonomousBindingEnabled ? "是" : "否")}",
        $"已过期：{(IsExpired ? "是" : "否")}",
        $"来源包：{AssetBatch}",
        $"action id：{BehaviorId}"
    });
    public string PhaseSummary => string.Join(" / ", Phases.Select(x => $"{x.Name}:{x.Frames.Count}"));
    public bool HasVariableFrameDurations => Phases.Any(x => x.HasVariableDurations);
    public MotionVisibleMetrics VisibleMetrics => MotionVisualSizer.Measure(FirstFrame);
    public int VisibleSubjectWidth => VisibleMetrics.VisibleWidth;
    public int VisibleSubjectHeight => VisibleMetrics.VisibleHeight;
    public double PreviewRenderSize => ScaleReferenceFrames is { Count: > 0 }
        ? Math.Clamp(150 * MotionVisualSizer.RenderScaleForMotion(this, DesktopMotionCatalog.ReferenceFramePath), 150 * 0.45, 150 * 2.6)
        : MotionVisualSizer.PreviewRenderSize(FirstFrame, DesktopMotionCatalog.ReferenceFramePath, VisualScale, 150);
}

public sealed record MotionVisibleMetrics(int CanvasWidth, int CanvasHeight, Int32Rect Bounds)
{
    public int VisibleWidth => Bounds.Width;
    public int VisibleHeight => Bounds.Height;
    public double VisibleHeightRatio => CanvasHeight <= 0 ? 1.0 : VisibleHeight / (double)CanvasHeight;
}

public static class MotionVisualSizer
{
    private static readonly Dictionary<string, MotionVisibleMetrics> Cache = new(StringComparer.OrdinalIgnoreCase);
    private const double DefaultVisibleRatio = 0.72;

    public static MotionVisibleMetrics Measure(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new MotionVisibleMetrics(1024, 1024, new Int32Rect(0, 0, 1024, 1024));
        if (Cache.TryGetValue(path, out var cached))
            return cached;

        var bitmap = BitmapFrame.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = Math.Max(1, width * 4);
        var pixels = new byte[stride * height];
        BitmapSource converted = bitmap.Format == System.Windows.Media.PixelFormats.Bgra32 || bitmap.Format == System.Windows.Media.PixelFormats.Pbgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);

        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + x * 4 + 3] <= 18)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        var bounds = maxX < minX || maxY < minY
            ? new Int32Rect(0, 0, width, height)
            : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        var result = new MotionVisibleMetrics(width, height, bounds);
        Cache[path] = result;
        return result;
    }

    public static double RenderScaleFor(string? framePath, string? referenceFramePath, double targetVisibleRatio)
    {
        var current = Measure(framePath);
        var reference = Measure(referenceFramePath);
        var referenceRatio = reference.VisibleHeightRatio > 0 ? reference.VisibleHeightRatio : DefaultVisibleRatio;
        var currentRatio = current.VisibleHeightRatio > 0 ? current.VisibleHeightRatio : DefaultVisibleRatio;
        return Math.Clamp(referenceRatio * targetVisibleRatio / currentRatio, 0.35, 3.0);
    }

    public static double RenderScaleForFrames(IEnumerable<string> framePaths, string? referenceFramePath, double targetVisibleRatio)
    {
        var frames = framePaths.Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x)).ToArray();
        if (frames.Length == 0)
            return RenderScaleFor(null, referenceFramePath, targetVisibleRatio);

        var reference = Measure(referenceFramePath);
        var referenceRatio = reference.VisibleHeightRatio > 0 ? reference.VisibleHeightRatio : DefaultVisibleRatio;
        var maxVisibleRatio = frames
            .Select(Measure)
            .Select(x => x.VisibleHeightRatio > 0 ? x.VisibleHeightRatio : DefaultVisibleRatio)
            .DefaultIfEmpty(DefaultVisibleRatio)
            .Max();
        return Math.Clamp(referenceRatio * targetVisibleRatio / maxVisibleRatio, 0.35, 3.0);
    }

    public static double RenderScaleForMotion(PlayableMotion motion, string? referenceFramePath)
    {
        if (motion.RenderScaleOverride is > 0)
            return motion.RenderScaleOverride.Value;

        var frames = motion.ScaleReferenceFrames is { Count: > 0 }
            ? motion.ScaleReferenceFrames
            : motion.Phases
                .SelectMany(x => x.Frames)
                .Concat(motion.DirectionalFrames?.Values.SelectMany(x => x) ?? Array.Empty<string>())
                .Concat(motion.NamedSequences?.Values.SelectMany(x => x) ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase);
        return RenderScaleForFrames(frames, referenceFramePath, motion.VisualScale);
    }

    public static double RenderScaleForPhase(PlayableMotion motion, MotionPhase phase, string? referenceFramePath)
    {
        if (motion.RenderScaleOverride is > 0)
            return motion.RenderScaleOverride.Value;
        return phase.VisualScale is > 0
            ? RenderScaleForFrames(phase.Frames, referenceFramePath, phase.VisualScale.Value)
            : RenderScaleForMotion(motion, referenceFramePath);
    }

    public static double PreviewRenderSize(string? framePath, string? referenceFramePath, double targetVisibleRatio, double stageSize)
        => Math.Clamp(stageSize * RenderScaleFor(framePath, referenceFramePath, targetVisibleRatio), stageSize * 0.45, stageSize * 2.6);
}

public sealed class DesktopMotionCatalog
{
    private const double ApprovedPetVisualScale = 0.92;
    public const string RoadGazeReviewMarkerFileName = "Wukong.RoadGazeReview.enabled";
    private readonly IReadOnlyList<PlayableMotion> _allMotions;
    private readonly Dictionary<string, PlayableMotion> _motions;

    private DesktopMotionCatalog(
        IEnumerable<PlayableMotion> motions,
        string loadSummary,
        bool carRideRoadGazeReviewEnabled)
    {
        _allMotions = motions.Where(x => x.IsUsable).ToArray();
        _motions = new Dictionary<string, PlayableMotion>(StringComparer.OrdinalIgnoreCase);
        foreach (var motion in _allMotions)
        {
            if (!_motions.ContainsKey(motion.BehaviorId))
                _motions.Add(motion.BehaviorId, motion);
        }
        LoadSummary = loadSummary;
        CarRideRoadGazeReviewEnabled = carRideRoadGazeReviewEnabled;
    }

    public IReadOnlyList<PlayableMotion> Motions => _allMotions.OrderBy(x => x.BehaviorId).ToList();
    public string LoadSummary { get; }
    public bool CarRideRoadGazeReviewEnabled { get; }
    public static string ReferenceFramePath { get; private set; } = string.Empty;

    public PlayableMotion? Find(string behaviorId) =>
        _motions.TryGetValue(behaviorId, out var motion) ? motion : null;

    public PlayableMotion RequiredIdle =>
        Find(LifecycleCandidateBehaviorIds.ProneIdleMicroloop) is { RuntimeEnabled: true } lifecycleProne
            ? lifecycleProne
            : Find(Phase15BehaviorIds.ProneIdle) ??
        _motions.Values.FirstOrDefault() ??
        throw new InvalidOperationException("No playable Wukong motion assets were found.");

    public static DesktopMotionCatalog Load(string baseDirectory)
    {
        var root = Path.Combine(baseDirectory, "WukongAssets");
        var carRideRoadGazeReviewEnabled = File.Exists(Path.Combine(baseDirectory, RoadGazeReviewMarkerFileName));
        var motions = new[]
        {
            Motion(
                Phase15BehaviorIds.ProneIdle,
                "Idle breathing",
                "Autonomous",
                "left_front_35deg",
                260,
                true,
                root,
                "actions/WK-CORE-PRONE-IDLE-LF-v1/approved-keyframes/v1",
                loop: true,
                pingPong: true,
                runtimeEnabled: false,
                status: "已过期：旧标准柴犬趴卧呼吸，仅保留动作参考",
                missing: "superseded runtime presentation",
                disposition: "已过期"),
            Motion(
                Phase15BehaviorIds.ProneIdleV3Candidate,
                "V3 idle candidate",
                "Autonomous",
                "left_front_35deg",
                125,
                true,
                root,
                "actions/WK-CORE-PRONE-IDLE-LF-v1/runtime-frames/v3",
                loop: true,
                runtimeEnabled: false,
                status: "已过期：旧标准柴犬趴卧候选，仅保留动作参考",
                missing: "superseded visual identity and runtime presentation",
                disposition: "已过期"),
            Motion(
                Phase15BehaviorIds.ProneBreath,
                "Prone breathing",
                "Autonomous",
                "left_front_35deg",
                260,
                true,
                root,
                "actions/WK-CORE-PRONE-IDLE-LF-v1/approved-keyframes/v1",
                loop: true,
                pingPong: true,
                runtimeEnabled: false,
                status: "已过期：旧标准柴犬静默趴卧呼吸，仅保留动作参考",
                missing: "superseded runtime presentation",
                disposition: "已过期"),
            Motion(
                Phase15BehaviorIds.LookAround,
                "Look around",
                "Autonomous",
                "left-front-to-right-front",
                170,
                true,
                root,
                "actions/WK-CORE-TURN-LF-TO-RF-v2/approved-keyframes/v1",
                loop: false,
                runtimeEnabled: false,
                status: "已过期：旧标准柴犬基础素材，仅保留动作参考",
                missing: "superseded visual identity / transition_in / transition_out / interrupt_exit",
                startPose: "stand.neutral.left_front",
                endPose: "stand.neutral.right_front",
                disposition: "已过期"),
            Motion(
                Phase15BehaviorIds.SafeStand,
                "Prone to stand",
                "Owner interaction",
                "left-front",
                180,
                true,
                root,
                "actions/WK-CORE-PRONE-TO-STAND-LF-v2/approved-keyframes/v1",
                loop: false,
                runtimeEnabled: false,
                status: "已过期：旧标准柴犬基础素材，仅保留动作参考",
                missing: "superseded visual identity / stand_to_prone interrupt_exit / renderer QA",
                startPose: "prone.awake.left_front",
                endPose: "stand.neutral.left_front",
                disposition: "已过期"),
            Motion(
                Phase15BehaviorIds.StrokeEnjoy,
                "Happy touch keyframes",
                "Owner interaction",
                "left-front",
                150,
                true,
                root,
                "actions/WK-INTERACT-HAPPY-TOUCH-v2/approved-keyframes/v1",
                loop: false,
                runtimeEnabled: false,
                status: "已过期：旧标准柴犬基础素材，仅保留动作参考",
                missing: "superseded visual identity / intro / loop / exit / interrupt_exit",
                disposition: "已过期"),
            TouchMotion(root),
            Motion(
                "wk.preview.prone_touch_nose_lick",
                "Nose lick reaction",
                "Owner interaction",
                "left-front",
                120,
                true,
                root,
                "action-batches/WK-INTERACTION-PRONE-TOUCH-v4-1/sequences/reaction_nose_lick",
                loop: false,
                runtimeEnabled: false,
                status: "Deprecated: owner rejected and removed from use",
                missing: "deprecated_reason=owner_rejected_and_removed_from_use_2026_08_26",
                disposition: "Deprecated") with
            {
                AssetBatch = "WK-INTERACTION-PRONE-TOUCH-v4-1",
                VisualApproved = false,
                RuntimeApproved = false,
                AutonomousBindingEnabled = false,
                Deprecated = true
            },
            Motion(
                "wk.preview.stand_to_prone",
                "Stand to prone",
                "Owner interaction",
                "left-front",
                180,
                true,
                root,
                "actions/WK-CORE-STAND-TO-PRONE-LF-v2/approved-keyframes/v1",
                loop: false,
                runtimeEnabled: false,
                status: "已过期：旧标准柴犬基础素材，仅保留动作参考",
                missing: "superseded visual identity / interrupt_exit",
                startPose: "stand.neutral.left_front",
                endPose: "prone.awake.left_front",
                disposition: "已过期"),
            Motion(
                "wk.preview.walk_left",
                "Walk left",
                "Other",
                "left",
                170,
                true,
                root,
                "actions/WK-CORE-WALK-LEFT-v2/approved-keyframes/v1",
                loop: true,
                runtimeEnabled: false,
                status: "已过期：旧标准柴犬基础素材，仅保留动作参考",
                missing: "superseded visual identity / intro / exit / interrupt_exit",
                startPose: "stand.neutral.left_front",
                endPose: "stand.neutral.left_front",
                disposition: "已过期")
        };

        var commandCandidates = LoadCommandCandidates(root).ToArray();
        var magicCandidates = LoadMagicCandidates(root).ToArray();
        var lifecycleCandidates = LoadLifecycleCandidates(root).ToArray();
        var lifecycleReviewCandidates = LoadLifecycleReviewCandidates(root).ToArray();
        var proneHeadCandidates = LoadProneHeadCandidates(root).ToArray();
        var sleepCandidates = LoadSleepCandidates(root).ToArray();
        var patrolWalkCandidates = LoadPatrolWalkCandidates(root).ToArray();
        var carRideCandidates = LoadCarRideCandidates(root, carRideRoadGazeReviewEnabled).ToArray();
        var commandMocks = LoadCommandMotionMocks(root).ToArray();
        var autonomousDailyCandidates = LoadAutonomousDailyCandidates(root, lifecycleCandidates.Concat(commandMocks)).ToArray();
        ReferenceFramePath = lifecycleCandidates
            .FirstOrDefault(x => x.BehaviorId == LifecycleCandidateBehaviorIds.ProneIdleMicroloop && x.RuntimeEnabled)?.FirstFrame
            ?? motions.FirstOrDefault(x => x.BehaviorId == Phase15BehaviorIds.ProneIdle)?.FirstFrame
            ?? string.Empty;
        var summary = $"asset_root=WukongAssets; built_in={motions.Length}; command_candidates={commandCandidates.Length}; magic_candidates={magicCandidates.Length}; lifecycle_candidates={lifecycleCandidates.Length}; lifecycle_review_candidates={lifecycleReviewCandidates.Length}; prone_head_candidates={proneHeadCandidates.Length}; sleep_candidates={sleepCandidates.Length}; patrol_walk_candidates={patrolWalkCandidates.Length}; autonomous_daily_candidates={autonomousDailyCandidates.Length}; car_ride_candidates={carRideCandidates.Length}; car_ride_road_gaze_review={carRideRoadGazeReviewEnabled}; command_mocks={commandMocks.Length}; manifests=action-batches/WK-COMMAND-ACTION-CANDIDATES-v3/manifest.json,action-batches/{MagicBehaviorIds.AssetBatch}/manifest.json,action-batches/{LifecycleCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch}/runtime-review-manifest.json,action-batches/{LifecycleReviewCandidateBehaviorIds.V4AssetBatch}/runtime-review-manifest.json,action-batches/{ProneHeadCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{SleepCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{PatrolWalkCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{SideProneFrontBehaviorIds.AssetBatch}/manifest.json,action-batches/{AutonomousDailyCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{CarRideBehaviorIds.AssetBatch}/manifest.json,action-batches/{CarRideBehaviorIds.RoadGazeAssetBatch}/manifest.json,action-mocks/{CommandMockBehaviorIds.AssetBatch}/manifest.json";
        BootstrapLog.WriteRaw($"asset_catalog_loaded {summary}");
        return new DesktopMotionCatalog(
            motions.Concat(commandCandidates).Concat(magicCandidates).Concat(lifecycleCandidates).Concat(lifecycleReviewCandidates).Concat(proneHeadCandidates).Concat(sleepCandidates).Concat(patrolWalkCandidates).Concat(autonomousDailyCandidates).Concat(carRideCandidates).Concat(commandMocks),
            summary,
            carRideRoadGazeReviewEnabled);
    }

    private static PlayableMotion Motion(
        string behaviorId,
        string displayName,
        string category,
        string direction,
        int frameDurationMs,
        bool interruptible,
        string root,
        string relativeDirectory,
        bool loop,
        bool pingPong = false,
        bool runtimeEnabled = true,
        string status = "Ready",
        string missing = "None",
        string startPose = "prone.awake.left_front",
        string endPose = "prone.awake.left_front",
        string disposition = "Enabled")
    {
        var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        var frames = pingPong ? ReadPingPongFrames(directory) : ReadFrames(directory);
        return new PlayableMotion(
            behaviorId,
            displayName,
            category,
            direction,
            frameDurationMs,
            interruptible,
            new[] { new MotionPhase(loop ? "loop" : "intro", frames, loop) },
            directory,
            runtimeEnabled,
            status,
            missing,
            startPose,
            endPose,
            "wukong-current-adult-v1",
            disposition);
    }

    private static PlayableMotion TouchMotion(string root)
    {
        var touchRoot = Path.Combine(root, "action-batches", "WK-INTERACTION-PRONE-TOUCH-v4-1", "sequences");
        return new PlayableMotion(
            Phase15BehaviorIds.ProneTouch,
            "摸摸回应",
            "主人互动",
            "left-front",
            95,
            true,
            new[]
            {
                new MotionPhase("intro", ReadFrames(Path.Combine(touchRoot, "intro")), Loop: false),
                new MotionPhase("loop", ReadFrames(Path.Combine(touchRoot, "loop")), Loop: true),
                new MotionPhase("exit", ReadFrames(Path.Combine(touchRoot, "exit")), Loop: false),
                new MotionPhase("interrupt_exit", ReadFrames(Path.Combine(touchRoot, "interrupt_exit")), Loop: false)
            },
            touchRoot,
            RuntimeEnabled: false,
            Status: "已过期：主人已拒绝并移出使用范围",
            MissingContent: "Deprecated by owner on 2026-08-26",
            StartPose: "prone.awake.left_front",
            EndPose: "prone.awake.left_front",
            StyleGroup: "wukong-light-malt-gold-v4",
            Disposition: "已过期",
            PrototypeUse: false,
            AssetBatch: "WK-INTERACTION-PRONE-TOUCH-v4-1",
            Description: "deprecated_reason=owner_rejected_and_removed_from_use_2026_08_26; archived preview only",
            VisualApproved: false,
            RuntimeApproved: false,
            AutonomousBindingEnabled: false,
            Deprecated: true);
    }

    private static IReadOnlyList<string> ReadFrames(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<string>();

    private static IReadOnlyList<string> ReadPingPongFrames(string directory)
    {
        var frames = ReadFrames(directory).ToList();
        if (frames.Count <= 2)
            return frames;
        frames.AddRange(frames.Skip(1).Take(frames.Count - 2).Reverse());
        return frames;
    }

    private static IEnumerable<PlayableMotion> LoadCommandCandidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", "WK-COMMAND-ACTION-CANDIDATES-v3", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("command_candidate_manifest_missing");
            yield break;
        }

        CommandActionBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CommandActionBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Command candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest?.Actions is null)
            yield break;

        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        foreach (var action in manifest.Actions)
        {
            var frames = new List<string>();
            var errors = new List<string>();
            foreach (var frame in action.Frames ?? Array.Empty<CommandActionFrameManifest>())
            {
                var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    errors.Add($"missing:{frame.Path}");
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length != frame.Bytes)
                    errors.Add($"bytes:{frame.Path}");
                if (!string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"sha256:{frame.Path}");
                frames.Add(path);
            }

            if (frames.Count != action.FrameCount)
                errors.Add($"frame_count:{frames.Count}/{action.FrameCount}");

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"command_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            var validationStatus = string.Equals(action.RuntimeValidation, "failed", StringComparison.OrdinalIgnoreCase)
                ? "已过期：旧口令素材验收失败，仅保留为动作参考"
                : "已过期：旧口令素材，仅保留为动作参考";

            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "口令动作",
                action.Direction,
                action.FrameDurationMs,
                action.Interruptible,
                new[] { new MotionPhase("intro", frames, Loop: false) },
                Path.Combine(batchRoot, action.SourceFolder.Replace('/', Path.DirectorySeparatorChar)),
                RuntimeEnabled: false,
                Status: validationStatus,
                MissingContent: "runtime approval / production registry binding",
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: "wukong-current-adult-v1",
                Disposition: "已过期",
                AssetBatch: "WK-COMMAND-ACTION-CANDIDATES-v3");
        }
    }

    private static IEnumerable<PlayableMotion> LoadCommandMotionMocks(string root)
    {
        var manifestPath = Path.Combine(root, "action-mocks", CommandMockBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("command_production_candidate_owner_qa_pending_manifest_missing");
            yield break;
        }

        CommandMotionMockBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CommandMotionMockBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Command motion mock manifest parse failed", ex);
            yield break;
        }

        if (manifest?.Actions is null)
            yield break;

        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        var sharedScaleFrame = manifest.Actions
            .Where(x => string.Equals(x.BehaviorId, MockCommandActionIds.Spin, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Frames ?? Array.Empty<CommandActionFrameManifest>())
            .Select(x => Path.Combine(batchRoot, x.Path.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(File.Exists)
            ?? manifest.Actions
                .SelectMany(x => x.Frames ?? Array.Empty<CommandActionFrameManifest>())
                .Select(x => Path.Combine(batchRoot, x.Path.Replace('/', Path.DirectorySeparatorChar)))
                .FirstOrDefault(File.Exists);
        var sharedScaleReference = string.IsNullOrWhiteSpace(sharedScaleFrame)
            ? Array.Empty<string>()
            : new[] { sharedScaleFrame };
        foreach (var action in manifest.Actions)
        {
            var frames = new List<string>();
            var durations = new List<int>();
            var errors = new List<string>();
            foreach (var frame in action.Frames ?? Array.Empty<CommandActionFrameManifest>())
            {
                var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    errors.Add($"missing:{frame.Path}");
                    continue;
                }
                if (new FileInfo(path).Length != frame.Bytes)
                    errors.Add($"bytes:{frame.Path}");
                if (!string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"sha256:{frame.Path}");
                frames.Add(path);
                durations.Add(frame.DurationMs.GetValueOrDefault(action.FrameDurationMs));
            }

            if (frames.Count != action.FrameCount)
                errors.Add($"frame_count:{frames.Count}/{action.FrameCount}");
            var approvedOwnerCommand = action.RuntimeApproved &&
                action.RuntimeUse &&
                action.ProductionAsset &&
                !action.PrototypeUse &&
                string.Equals(action.AssetStage, "runtime_approved_owner_command", StringComparison.OrdinalIgnoreCase);
            var prototypeOwnerCommand = !action.RuntimeApproved &&
                !action.RuntimeUse &&
                !action.ProductionAsset &&
                action.PrototypeUse &&
                string.Equals(action.AssetStage, "production_candidate_owner_qa_pending", StringComparison.OrdinalIgnoreCase);
            if (!approvedOwnerCommand && !prototypeOwnerCommand)
                errors.Add("command_owner_gate_not_declared");

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"command_production_candidate_owner_qa_pending_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "口令动作",
                action.ToPosture,
                action.FrameDurationMs,
                Interruptible: false,
                new[] { new MotionPhase("mock", frames, Loop: false, durations) },
                Path.Combine(batchRoot, action.SourceFolder.Replace('/', Path.DirectorySeparatorChar)),
                RuntimeEnabled: approvedOwnerCommand,
                Status: approvedOwnerCommand
                    ? "已批准：主人手动口令可用"
                    : "候选：主人预览待验收",
                MissingContent: approvedOwnerCommand ? "None" : "owner QA / runtime approval",
                StartPose: action.FromPosture,
                EndPose: action.ToPosture,
                StyleGroup: "wukong-command-production-candidates-v4",
                Disposition: approvedOwnerCommand ? "已启用" : "候选预览",
                PrototypeUse: action.PrototypeUse,
                AssetBatch: manifest.BatchId,
                Description: approvedOwnerCommand
                    ? "Approved owner command motion. Manual context-menu and control-panel command paths only."
                    : "Real command production candidate for deterministic personality/state/command behavior wiring.",
                CandidateProfile: manifest.AssetStage,
                VisualScale: approvedOwnerCommand ? 0.92 : 1.0,
                ScaleReferenceFrames: sharedScaleReference,
                VisualApproved: approvedOwnerCommand,
                RuntimeApproved: action.RuntimeApproved,
                AutonomousBindingEnabled: false);
        }
    }


    private static IEnumerable<PlayableMotion> LoadLifecycleCandidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", LifecycleCandidateBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("lifecycle_candidate_manifest_missing");
            yield break;
        }

        LifecycleCandidateBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<LifecycleCandidateBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Lifecycle candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest?.Actions is null)
            yield break;

        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        foreach (var action in manifest.Actions)
        {
            if (action.BehaviorId.StartsWith("wk.command.", StringComparison.OrdinalIgnoreCase))
            {
                BootstrapLog.WriteRaw($"lifecycle_candidate_invalid behavior={action.BehaviorId} errors=command_namespace_forbidden");
                continue;
            }

            var phases = new List<MotionPhase>();
            var errors = new List<string>();
            foreach (var phase in action.Phases ?? Array.Empty<LifecycleCandidatePhaseManifest>())
            {
                var frames = new List<string>();
                var durations = new List<int>();
                foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
                {
                    var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path))
                    {
                        errors.Add($"missing:{frame.Path}");
                        continue;
                    }

                    var info = new FileInfo(path);
                    if (info.Length != frame.Bytes)
                        errors.Add($"bytes:{frame.Path}");
                    if (!string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"sha256:{frame.Path}");
                    frames.Add(path);
                    durations.Add(frame.DurationMs.GetValueOrDefault(action.FrameDurationMs));
                }

                if (frames.Count != phase.FrameCount)
                    errors.Add($"phase_frame_count:{phase.Name}:{frames.Count}/{phase.FrameCount}");
                phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
            }

            if (!action.RuntimeApproved || !action.RuntimeUse)
                errors.Add("runtime_gate_not_enabled_after_windows_qa");

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"lifecycle_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "基础动作",
                action.Direction,
                action.FrameDurationMs,
                action.Interruptible,
                phases,
                Path.Combine(batchRoot, action.SourceFolder.Replace('/', Path.DirectorySeparatorChar)),
                RuntimeEnabled: action.RuntimeApproved && action.RuntimeUse,
                Status: action.RuntimeApproved && action.RuntimeUse
                    ? "Runtime approved?Windows renderer QA passed"
                    : "Developer candidate?? Windows renderer QA",
                MissingContent: action.RuntimeApproved && action.RuntimeUse ? "None" : "runtime approval / production profile binding",
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: "wukong-standard-shiba-reference-v23-candidate",
                Disposition: action.RuntimeApproved && action.RuntimeUse ? "已启用" : "候选预览",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: action.Description,
                CandidateProfile: action.CandidateProfile ?? manifest.CandidateProfile,
                VisualScale: ApprovedPetVisualScale,
                VisualApproved: action.RuntimeApproved,
                RuntimeApproved: action.RuntimeApproved,
                AutonomousBindingEnabled: !string.IsNullOrWhiteSpace(action.AutonomousMapping));
        }
    }

    private static IEnumerable<PlayableMotion> LoadProneHeadCandidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", ProneHeadCandidateBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("prone_head_candidate_manifest_missing");
            yield break;
        }

        ProneHeadCandidateBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ProneHeadCandidateBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Prone head candidate manifest parse failed", ex);
            yield break;
        }

        var batchErrors = new List<string>();
        if (manifest is null ||
            !string.Equals(manifest.BatchId, ProneHeadCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal) ||
            !string.Equals(manifest.AssetId, ProneHeadCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal))
        {
            BootstrapLog.WriteRaw("prone_head_candidate_identity_invalid");
            yield break;
        }

        if (!manifest.VisualApproved ||
            !manifest.RuntimeApproved ||
            !manifest.RuntimeUse ||
            !manifest.ProductionAsset ||
            manifest.PrototypeUse ||
            !manifest.DeveloperPreview ||
            !manifest.AutonomousBindingEnabled ||
            !string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
            batchErrors.Add("approved_gate_invalid");
        if (manifest.CurrentRuntimeProneAnchorExact)
            batchErrors.Add("unverified_runtime_anchor_claim");
        if (!string.Equals(manifest.ApprovedRuntimeProfile, "non_front_prone_owner_validated", StringComparison.Ordinal))
            batchErrors.Add("approved_runtime_profile_invalid");
        if (manifest.AllowedSources is null ||
            manifest.AllowedSources.Count != 2 ||
            !manifest.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
            !manifest.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
            batchErrors.Add("source_policy_invalid");
        if (manifest.Actions is null || manifest.Actions.Count != 1)
            batchErrors.Add("approved_action_count_invalid");

        var inventory = new Dictionary<string, ProneHeadCandidateInventoryFrame>(StringComparer.OrdinalIgnoreCase);
        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        foreach (var item in manifest.FrameInventory ?? Array.Empty<ProneHeadCandidateInventoryFrame>())
        {
            if (string.IsNullOrWhiteSpace(item.Path) ||
                Path.IsPathRooted(item.Path) ||
                item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
            {
                batchErrors.Add($"unsafe_path:{item.Path}");
                continue;
            }
            if (!inventory.TryAdd(item.Path, item))
            {
                batchErrors.Add($"duplicate_inventory:{item.Path}");
                continue;
            }
            if (item.Width != 1024 || item.Height != 1024 || !string.Equals(item.Mode, "RGBA", StringComparison.Ordinal))
                batchErrors.Add($"frame_contract:{item.Path}");

            var path = Path.Combine(batchRoot, item.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                batchErrors.Add($"missing:{item.Path}");
                continue;
            }
            if (new FileInfo(path).Length != item.Bytes)
                batchErrors.Add($"bytes:{item.Path}");
            if (!string.Equals(Sha256(path), item.Sha256, StringComparison.OrdinalIgnoreCase))
                batchErrors.Add($"sha256:{item.Path}");
        }

        if (inventory.Count != 24)
            batchErrors.Add($"inventory_count:{inventory.Count}/24");
        if (!inventory.TryGetValue("frames/head-lower/frame-011.png", out var lowerHandoff) ||
            !inventory.TryGetValue("frames/head-turn/frame-001.png", out var turnHandoff) ||
            !string.Equals(lowerHandoff.Sha256, turnHandoff.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(lowerHandoff.Sha256, manifest.InternalHandoffSha256, StringComparison.OrdinalIgnoreCase))
            batchErrors.Add("internal_handoff_mismatch");

        if (batchErrors.Count > 0)
        {
            BootstrapLog.WriteRaw($"prone_head_candidate_invalid batch={manifest.BatchId} errors={string.Join(",", batchErrors)}");
            yield break;
        }

        foreach (var action in manifest.Actions ?? Array.Empty<ProneHeadCandidateActionManifest>())
        {
            var errors = new List<string>();
            if (!string.Equals(action.BehaviorId, ProneHeadCandidateBehaviorIds.HeadLowerTurnV4, StringComparison.Ordinal))
                errors.Add("behavior_id_invalid");
            if (!action.VisualApproved ||
                !action.RuntimeApproved ||
                !action.RuntimeUse ||
                !action.ProductionAsset ||
                action.PrototypeUse ||
                !action.DeveloperPreview ||
                !action.AutonomousBindingEnabled ||
                !string.Equals(action.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
                errors.Add("action_gate_invalid");
            if (action.AllowedSources is null ||
                action.AllowedSources.Count != 2 ||
                !action.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
                !action.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
                errors.Add("action_source_policy_invalid");

            var phases = new List<MotionPhase>();
            foreach (var phase in action.Phases ?? Array.Empty<ProneHeadCandidatePhaseManifest>())
            {
                var frames = new List<string>();
                var durations = new List<int>();
                foreach (var frame in phase.Frames ?? Array.Empty<ProneHeadCandidatePhaseFrameManifest>())
                {
                    if (!inventory.ContainsKey(frame.Path))
                    {
                        errors.Add($"unregistered_frame:{frame.Path}");
                        continue;
                    }
                    var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                    frames.Add(path);
                    durations.Add(frame.DurationMs);
                }
                if (frames.Count == 0 || durations.Any(x => x <= 0))
                    errors.Add($"phase_invalid:{phase.Name}");
                phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
            }

            if (phases.Sum(x => x.Frames.Count) != action.FrameCount)
                errors.Add($"action_frame_count:{phases.Sum(x => x.Frames.Count)}/{action.FrameCount}");
            var first = phases.SelectMany(x => x.Frames).FirstOrDefault();
            var last = phases.SelectMany(x => x.Frames).LastOrDefault();
            if (first is null || last is null ||
                !string.Equals(Sha256(first), Sha256(last), StringComparison.OrdinalIgnoreCase))
                errors.Add("closed_sequence_anchor_mismatch");

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"prone_head_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "自主日常",
                action.Direction,
                action.FrameDurationMs,
                action.Interruptible,
                phases,
                batchRoot,
                RuntimeEnabled: true,
                Status: "Windows 渲染验收通过：兼容趴姿自主动作已启用",
                MissingContent: "Byte-exact current runtime prone anchor is not claimed; forward-prone profile remains incompatible.",
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: "wukong-prone-head-microevent-v4-candidate",
                Disposition: "已启用",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: $"{action.Description} Internal low-head handoff is exact; owner Windows renderer QA permits autonomous use from the compatible non-front prone profile only.",
                CandidateProfile: manifest.CandidateProfile,
                VisualScale: ApprovedPetVisualScale,
                VisualApproved: true,
                RuntimeApproved: true,
                AutonomousBindingEnabled: true);
        }
    }

    private static IEnumerable<PlayableMotion> LoadSleepCandidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", SleepCandidateBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("sleep_candidate_manifest_missing");
            yield break;
        }

        SleepCandidateBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SleepCandidateBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Sleep candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest is null ||
            !string.Equals(manifest.BatchId, SleepCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal) ||
            !string.Equals(manifest.AssetId, SleepCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal))
        {
            BootstrapLog.WriteRaw("sleep_candidate_identity_invalid");
            yield break;
        }

        var batchErrors = new List<string>();
        if (manifest.OwnerPreviewApproved ||
            manifest.VisualApproved ||
            manifest.RuntimeApproved ||
            manifest.RuntimeUse ||
            manifest.ProductionAsset ||
            manifest.PrototypeUse ||
            !manifest.DeveloperPreview ||
            manifest.AutonomousBindingEnabled ||
            !string.Equals(manifest.RuntimeValidation, "pending_owner_windows_renderer_qa", StringComparison.Ordinal))
            batchErrors.Add("candidate_gate_invalid");
        if (manifest.SourceFrameCount != 48 || manifest.RuntimeFrameCount != 48 || manifest.SequenceCount != 8)
            batchErrors.Add("candidate_inventory_contract_invalid");
        if (manifest.RuntimeRenderScale <= 0)
            batchErrors.Add("runtime_render_scale_invalid");
        if (manifest.AllowedSources is null ||
            manifest.AllowedSources.Count != 1 ||
            !manifest.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
            batchErrors.Add("source_policy_invalid");

        var inventory = new Dictionary<string, ProneHeadCandidateInventoryFrame>(StringComparer.OrdinalIgnoreCase);
        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        foreach (var item in manifest.FrameInventory ?? Array.Empty<ProneHeadCandidateInventoryFrame>())
        {
            if (string.IsNullOrWhiteSpace(item.Path) ||
                Path.IsPathRooted(item.Path) ||
                item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
            {
                batchErrors.Add($"unsafe_path:{item.Path}");
                continue;
            }
            if (!inventory.TryAdd(item.Path, item))
            {
                batchErrors.Add($"duplicate_inventory:{item.Path}");
                continue;
            }
            if (item.Width != 1024 || item.Height != 1024 || !string.Equals(item.Mode, "RGBA", StringComparison.Ordinal))
                batchErrors.Add($"frame_contract:{item.Path}");

            var path = Path.Combine(batchRoot, item.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                batchErrors.Add($"missing:{item.Path}");
                continue;
            }
            if (new FileInfo(path).Length != item.Bytes)
                batchErrors.Add($"bytes:{item.Path}");
            if (!string.Equals(Sha256(path), item.Sha256, StringComparison.OrdinalIgnoreCase))
                batchErrors.Add($"sha256:{item.Path}");
        }

        if (inventory.Count != 48)
            batchErrors.Add($"inventory_count:{inventory.Count}/48");
        var actions = manifest.Actions ?? Array.Empty<SleepCandidateActionManifest>();
        if (actions.Count != 8)
            batchErrors.Add($"action_count:{actions.Count}/8");
        if (batchErrors.Count > 0)
        {
            BootstrapLog.WriteRaw($"sleep_candidate_invalid batch={manifest.BatchId} errors={string.Join(",", batchErrors)}");
            yield break;
        }

        foreach (var action in actions)
        {
            var errors = new List<string>();
            if (!SleepCandidateBehaviorIds.All.Contains(action.BehaviorId))
                errors.Add("behavior_id_invalid");
            if (action.OwnerPreviewApproved ||
                action.VisualApproved ||
                action.RuntimeApproved ||
                action.RuntimeUse ||
                action.ProductionAsset ||
                action.PrototypeUse ||
                !action.DeveloperPreview ||
                action.AutonomousBindingEnabled ||
                !string.Equals(action.RuntimeValidation, "pending_owner_windows_renderer_qa", StringComparison.Ordinal))
                errors.Add("action_gate_invalid");
            if (action.AllowedSources is null ||
                action.AllowedSources.Count != 1 ||
                !action.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
                errors.Add("action_source_policy_invalid");

            var phases = new List<MotionPhase>();
            foreach (var phase in action.Phases ?? Array.Empty<SleepCandidatePhaseManifest>())
            {
                var frames = new List<string>();
                var durations = new List<int>();
                foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
                {
                    if (!inventory.TryGetValue(frame.Path, out var inventoryFrame))
                    {
                        errors.Add($"unregistered_frame:{frame.Path}");
                        continue;
                    }
                    if (frame.Bytes != inventoryFrame.Bytes ||
                        !string.Equals(frame.Sha256, inventoryFrame.Sha256, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"frame_metadata_mismatch:{frame.Path}");
                    var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                    frames.Add(path);
                    durations.Add(frame.DurationMs.GetValueOrDefault(action.FrameDurationMs));
                }
                if (frames.Count != phase.FrameCount || frames.Count == 0 || durations.Any(x => x <= 0))
                    errors.Add($"phase_invalid:{phase.Name}:{frames.Count}/{phase.FrameCount}");
                if (phase.Loop != action.Loop)
                    errors.Add($"loop_contract_invalid:{phase.Name}");
                phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
            }

            if (phases.Count != 1 || phases.Sum(x => x.Frames.Count) != action.FrameCount)
                errors.Add($"action_frame_count:{phases.Sum(x => x.Frames.Count)}/{action.FrameCount}");
            if (phases.Sum(x => x.DurationTotalMs(action.FrameDurationMs)) != action.TotalDurationMs)
                errors.Add("action_duration_total_invalid");
            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"sleep_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "自主睡眠候选",
                action.Direction,
                action.FrameDurationMs,
                action.Interruptible,
                phases,
                batchRoot,
                RuntimeEnabled: false,
                Status: "v10 待主人 Windows 渲染验收",
                MissingContent: "Windows transparent-renderer QA / compatible runtime bridge / approved wake and interrupt-exit / runtime approval",
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: "wukong-sleep-runtime-final-v10-candidate",
                Disposition: "仅开发者候审预览",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: $"{action.Description} entry_policy={action.EntryPolicy}; source={manifest.SourceZip}; owner_preview_approved=false; visual_approved=false; runtime_use=false.",
                CandidateProfile: manifest.CandidateProfile,
                VisualScale: ApprovedPetVisualScale,
                RenderScaleOverride: manifest.RuntimeRenderScale,
                VisualApproved: false,
                RuntimeApproved: false,
                AutonomousBindingEnabled: false);
        }
    }

    private static IEnumerable<PlayableMotion> LoadPatrolWalkCandidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", PatrolWalkCandidateBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("patrol_walk_candidate_manifest_missing");
            yield break;
        }

        PatrolWalkCandidateBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PatrolWalkCandidateBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Patrol walk candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest is null ||
            !string.Equals(manifest.BatchId, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal) ||
            !string.Equals(manifest.AssetId, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal))
        {
            BootstrapLog.WriteRaw("patrol_walk_candidate_identity_invalid");
            yield break;
        }

        var batchErrors = new List<string>();
        if (!manifest.OwnerPreviewApproved ||
            !manifest.VisualApproved ||
            !manifest.RuntimeApproved ||
            !manifest.RuntimeUse ||
            !manifest.ProductionAsset ||
            manifest.PrototypeUse ||
            !manifest.DeveloperPreview ||
            !manifest.AutonomousBindingEnabled ||
            manifest.WindowMotionEnabled ||
            !string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
            batchErrors.Add("approved_gate_invalid");
        if (manifest.SourceFrameCount != 24 || manifest.RuntimeFrameCount != 24 || manifest.SequenceCount != 2)
            batchErrors.Add("candidate_inventory_contract_invalid");
        if (manifest.RuntimeRenderScale <= 0)
            batchErrors.Add("runtime_render_scale_invalid");
        if (manifest.AllowedSources is null ||
            manifest.AllowedSources.Count != 2 ||
            !manifest.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
            !manifest.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
            batchErrors.Add("source_policy_invalid");

        var inventory = new Dictionary<string, ProneHeadCandidateInventoryFrame>(StringComparer.OrdinalIgnoreCase);
        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        foreach (var item in manifest.FrameInventory ?? Array.Empty<ProneHeadCandidateInventoryFrame>())
        {
            if (string.IsNullOrWhiteSpace(item.Path) ||
                Path.IsPathRooted(item.Path) ||
                item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
            {
                batchErrors.Add($"unsafe_path:{item.Path}");
                continue;
            }
            if (!inventory.TryAdd(item.Path, item))
            {
                batchErrors.Add($"duplicate_inventory:{item.Path}");
                continue;
            }
            if (item.Width != 1024 || item.Height != 1024 || !string.Equals(item.Mode, "RGBA", StringComparison.Ordinal))
                batchErrors.Add($"frame_contract:{item.Path}");

            var path = Path.Combine(batchRoot, item.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                batchErrors.Add($"missing:{item.Path}");
                continue;
            }
            if (new FileInfo(path).Length != item.Bytes)
                batchErrors.Add($"bytes:{item.Path}");
            if (!string.Equals(Sha256(path), item.Sha256, StringComparison.OrdinalIgnoreCase))
                batchErrors.Add($"sha256:{item.Path}");
        }

        if (inventory.Count != 24)
            batchErrors.Add($"inventory_count:{inventory.Count}/24");
        var actions = manifest.Actions ?? Array.Empty<PatrolWalkCandidateActionManifest>();
        if (actions.Count != 2)
            batchErrors.Add($"action_count:{actions.Count}/2");
        if (batchErrors.Count > 0)
        {
            BootstrapLog.WriteRaw($"patrol_walk_candidate_invalid batch={manifest.BatchId} errors={string.Join(",", batchErrors)}");
            yield break;
        }

        foreach (var action in actions)
        {
            var errors = new List<string>();
            if (!PatrolWalkCandidateBehaviorIds.All.Contains(action.BehaviorId))
                errors.Add("behavior_id_invalid");
            if (!action.VisualApproved ||
                !action.RuntimeApproved ||
                !action.RuntimeUse ||
                !action.ProductionAsset ||
                action.PrototypeUse ||
                !action.DeveloperPreview ||
                !action.AutonomousBindingEnabled ||
                !string.Equals(action.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
                errors.Add("action_gate_invalid");
            if (action.AllowedSources is null ||
                action.AllowedSources.Count != 2 ||
                !action.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
                !action.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
                errors.Add("action_source_policy_invalid");

            var phases = new List<MotionPhase>();
            foreach (var phase in action.Phases ?? Array.Empty<SleepCandidatePhaseManifest>())
            {
                var frames = new List<string>();
                var durations = new List<int>();
                foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
                {
                    if (!inventory.TryGetValue(frame.Path, out var inventoryFrame))
                    {
                        errors.Add($"unregistered_frame:{frame.Path}");
                        continue;
                    }
                    if (frame.Bytes != inventoryFrame.Bytes ||
                        !string.Equals(frame.Sha256, inventoryFrame.Sha256, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"frame_metadata_mismatch:{frame.Path}");
                    frames.Add(Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar)));
                    durations.Add(frame.DurationMs.GetValueOrDefault(action.FrameDurationMs));
                }
                if (!phase.Loop || frames.Count != phase.FrameCount || frames.Count == 0 || durations.Any(x => x <= 0))
                    errors.Add($"phase_invalid:{phase.Name}:{frames.Count}/{phase.FrameCount}");
                phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
            }

            if (!action.Loop || phases.Count != 1 || phases.Sum(x => x.Frames.Count) != action.FrameCount)
                errors.Add($"action_frame_count:{phases.Sum(x => x.Frames.Count)}/{action.FrameCount}");
            if (phases.Sum(x => x.DurationTotalMs(action.FrameDurationMs)) != action.TotalDurationMs)
                errors.Add("action_duration_total_invalid");
            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"patrol_walk_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "Autonomous review candidate",
                action.Direction,
                action.FrameDurationMs,
                action.Interruptible,
                phases,
                batchRoot,
                RuntimeEnabled: true,
                Status: "Windows renderer QA passed; low-frequency in-place autonomous gait enabled",
                MissingContent: "Window translation remains disabled and requires separate motion QA.",
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: "wukong-autonomous-patrol-walk-v1-candidate",
                Disposition: "已启用",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: $"{action.Description} source={manifest.SourcePackage}; runtime_use=true; window_motion=false.",
                CandidateProfile: manifest.CandidateProfile,
                VisualScale: ApprovedPetVisualScale,
                RenderScaleOverride: manifest.RuntimeRenderScale,
                VisualApproved: true,
                RuntimeApproved: true,
                AutonomousBindingEnabled: true);
        }
    }

    private static IEnumerable<PlayableMotion> LoadLifecycleReviewCandidates(string root)
    {
        foreach (var assetBatch in LifecycleReviewCandidateBehaviorIds.AssetBatches)
        {
            var manifestPath = Path.Combine(root, "action-batches", assetBatch, "runtime-review-manifest.json");
            if (!File.Exists(manifestPath))
            {
                BootstrapLog.WriteRaw($"lifecycle_review_manifest_missing batch={assetBatch}");
                continue;
            }

            LifecycleReviewBatchManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<LifecycleReviewBatchManifest>(
                    File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                BootstrapLog.Write($"Lifecycle review manifest parse failed: {assetBatch}", ex);
                continue;
            }

            if (manifest?.Actions is null ||
                !string.Equals(manifest.BatchId, assetBatch, StringComparison.Ordinal) ||
                !manifest.RuntimeApproved || !manifest.RuntimeUse || !manifest.ProductionAsset || !manifest.VisualApproved ||
                !string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
            {
                BootstrapLog.WriteRaw($"lifecycle_approved_manifest_gate_invalid batch={assetBatch}");
                continue;
            }

            var batchRoot = Path.GetDirectoryName(manifestPath)!;
            var loaded = new List<PlayableMotion>();
            foreach (var action in manifest.Actions)
            {
                var errors = new List<string>();
                if (!action.BehaviorId.StartsWith("wk.candidate.", StringComparison.Ordinal))
                    errors.Add("candidate_namespace_required");
                if (!action.RuntimeApproved || !action.RuntimeUse || !action.ProductionAsset || action.PrototypeUse || !action.AutonomousBindingEnabled)
                    errors.Add("approved_runtime_gate_closed");
                if (!action.VisualApproved)
                    errors.Add("owner_visual_approval_required");
                if (action.AllowedSources is null ||
                    !action.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
                    !action.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
                    errors.Add("approved_source_policy_invalid");
                if (!string.Equals(action.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
                    errors.Add("runtime_renderer_qa_not_passed");
                if (string.Equals(assetBatch, LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch, StringComparison.Ordinal) &&
                    (action.FromPose.Contains("front", StringComparison.OrdinalIgnoreCase) || action.ToPose.Contains("front", StringComparison.OrdinalIgnoreCase)) &&
                    action.LegacySideProne)
                    errors.Add("legacy_side_declared_as_front");
                if (string.Equals(assetBatch, LifecycleReviewCandidateBehaviorIds.V4AssetBatch, StringComparison.Ordinal) &&
                    (!action.FromPose.Contains("front", StringComparison.OrdinalIgnoreCase) || !action.ToPose.Contains("front", StringComparison.OrdinalIgnoreCase) || action.LegacySideProne))
                    errors.Add("front_prone_identity_invalid");

                var phases = new List<MotionPhase>();
                foreach (var phase in action.Phases ?? Array.Empty<LifecycleCandidatePhaseManifest>())
                {
                    var frames = new List<string>();
                    var durations = new List<int>();
                    foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
                    {
                        var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(path))
                        {
                            errors.Add($"missing:{frame.Path}");
                            continue;
                        }

                        if (new FileInfo(path).Length != frame.Bytes)
                            errors.Add($"bytes:{frame.Path}");
                        if (!string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                            errors.Add($"sha256:{frame.Path}");
                        frames.Add(path);
                        durations.Add(frame.DurationMs.GetValueOrDefault(action.FrameDurationMs));
                    }

                    if (frames.Count != phase.FrameCount)
                        errors.Add($"phase_frame_count:{phase.Name}:{frames.Count}/{phase.FrameCount}");
                    phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
                }

                if (phases.Sum(x => x.Frames.Count) != action.FrameCount)
                    errors.Add($"action_frame_count:{phases.Sum(x => x.Frames.Count)}/{action.FrameCount}");
                if (errors.Count > 0)
                {
                    BootstrapLog.WriteRaw($"lifecycle_review_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                    continue;
                }

                var warning = action.LegacySideProne
                    ? "Approved side-prone continuity; never splice to the V4 forward-prone profile."
                    : "Owner Windows review passed; approved only for the explicit autonomous lifecycle mapping.";
                loaded.Add(new PlayableMotion(
                    action.BehaviorId,
                    action.DisplayName,
                    "基础动作",
                    action.Direction,
                    action.FrameDurationMs,
                    action.Interruptible,
                    phases,
                    Path.Combine(batchRoot, action.SourceFolder.Replace('/', Path.DirectorySeparatorChar)),
                    RuntimeEnabled: action.RuntimeApproved && action.RuntimeUse,
                    Status: "Windows renderer QA passed; runtime enabled",
                    MissingContent: "None",
                    StartPose: action.FromPose,
                    EndPose: action.ToPose,
                    StyleGroup: string.Equals(assetBatch, LifecycleReviewCandidateBehaviorIds.V4AssetBatch, StringComparison.Ordinal)
                        ? "wukong-light-malt-gold-front-prone-v4"
                        : "wukong-lifecycle-v3r1-recovered",
                    Disposition: "已启用",
                    PrototypeUse: false,
                    AssetBatch: manifest.BatchId,
                    Description: $"{action.Description} source={manifest.BatchId}; expired_pixel_contribution=false; {warning}",
                    CandidateProfile: manifest.CandidateProfile,
                    VisualScale: ApprovedPetVisualScale,
                    VisualApproved: action.VisualApproved,
                    RuntimeApproved: action.RuntimeApproved,
                    AutonomousBindingEnabled: action.AutonomousBindingEnabled));
            }

            if (string.Equals(assetBatch, LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch, StringComparison.Ordinal))
            {
                var introIndex = loaded.FindIndex(x => string.Equals(x.BehaviorId, LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1, StringComparison.Ordinal));
                var proneLoop = loaded.FirstOrDefault(x => string.Equals(x.BehaviorId, LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1, StringComparison.Ordinal));
                var exit = loaded.FirstOrDefault(x => string.Equals(x.BehaviorId, LifecycleReviewCandidateBehaviorIds.LivelyDailyExitV3R1, StringComparison.Ordinal));
                if (introIndex < 0 || proneLoop is null || exit is null)
                {
                    BootstrapLog.WriteRaw("lifecycle_v3r1_composition_missing");
                    continue;
                }

                var intro = loaded[introIndex];
                var introPhase = intro.Phases.Single(x => string.Equals(x.Name, "intro", StringComparison.OrdinalIgnoreCase));
                var exitPhase = exit.Phases.Single(x => string.Equals(x.Name, "exit", StringComparison.OrdinalIgnoreCase));
                var sideProneFront = LoadSideProneFrontProductionPhases(root);
                loaded[introIndex] = sideProneFront is { Count: 3 }
                    ? intro with
                    {
                        Phases = new[] { introPhase }
                            .Concat(sideProneFront)
                            .Append(exitPhase)
                            .ToArray(),
                        Description = $"{intro.Description} Runtime composition is V3R1 intro -> v5 turn-to-front bridge -> forward-observe calm loop -> v5 return bridge -> V3R1 exit. Frozen V3R1 source bytes are unchanged."
                    }
                    : intro with
                    {
                        Phases = new[]
                        {
                            introPhase,
                            proneLoop.Phases.Single(x => string.Equals(x.Name, "loop", StringComparison.OrdinalIgnoreCase)),
                            exitPhase
                        },
                        Description = $"{intro.Description} Runtime composition is intro -> legacy-side-prone loop -> exit; the v5 bridge extension remains fail closed."
                    };
            }

            foreach (var motion in loaded)
                yield return motion;
        }
    }

    private static IReadOnlyList<MotionPhase>? LoadSideProneFrontProductionPhases(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", SideProneFrontBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("side_prone_front_v5_manifest_missing");
            return null;
        }

        SideProneFrontProductionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SideProneFrontProductionManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Side-prone front v5 manifest parse failed", ex);
            return null;
        }

        if (manifest is null ||
            !string.Equals(manifest.BatchId, SideProneFrontBehaviorIds.AssetBatch, StringComparison.Ordinal) ||
            !string.Equals(manifest.BehaviorId, SideProneFrontBehaviorIds.ObserveV5, StringComparison.Ordinal))
        {
            BootstrapLog.WriteRaw("side_prone_front_v5_identity_invalid");
            return null;
        }

        if (!manifest.VisualApproved || !manifest.RuntimeApproved || !manifest.RuntimeUse ||
            !manifest.ProductionAsset || manifest.PrototypeUse || !manifest.AutonomousBindingEnabled ||
            !string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal) ||
            manifest.AllowedSources is null ||
            !manifest.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
            !manifest.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
        {
            BootstrapLog.WriteRaw("side_prone_front_v5_gate_closed");
            return null;
        }

        var expected = new[]
        {
            (Name: "bridge-to-front", Loop: false),
            (Name: "side-prone-front-calm", Loop: true),
            (Name: "bridge-to-legacy", Loop: false)
        };
        if (manifest.Phases is null || manifest.Phases.Count != expected.Length)
        {
            BootstrapLog.WriteRaw("side_prone_front_v5_phase_contract_invalid");
            return null;
        }

        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        var phases = new List<MotionPhase>(expected.Length);
        var errors = new List<string>();
        for (var index = 0; index < expected.Length; index++)
        {
            var phase = manifest.Phases[index];
            var contract = expected[index];
            if (!string.Equals(phase.Name, contract.Name, StringComparison.Ordinal) ||
                phase.Loop != contract.Loop || phase.FrameCount != 12)
                errors.Add($"phase_contract:{phase.Name}");

            var frames = new List<string>();
            var durations = new List<int>();
            foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
            {
                var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    errors.Add($"missing:{frame.Path}");
                    continue;
                }
                if (new FileInfo(path).Length != frame.Bytes)
                    errors.Add($"bytes:{frame.Path}");
                if (!string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"sha256:{frame.Path}");
                frames.Add(path);
                durations.Add(frame.DurationMs.GetValueOrDefault(manifest.FrameDurationMs));
            }
            if (frames.Count != phase.FrameCount)
                errors.Add($"phase_frame_count:{phase.Name}:{frames.Count}/{phase.FrameCount}");
            phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
        }

        if (errors.Count > 0)
        {
            BootstrapLog.WriteRaw($"side_prone_front_v5_invalid errors={string.Join(",", errors)}");
            return null;
        }

        BootstrapLog.WriteRaw("side_prone_front_v5_runtime_ready frames=36 composition=v3r1_bidirectional_bridge");
        return phases;
    }

    private static IEnumerable<PlayableMotion> LoadAutonomousDailyCandidates(
        string root,
        IEnumerable<PlayableMotion> approvedSourceMotions)
    {
        var manifestPath = Path.Combine(root, "action-batches", AutonomousDailyCandidateBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("autonomous_daily_candidate_manifest_missing");
            yield break;
        }

        AutonomousDailyCandidateBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AutonomousDailyCandidateBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Autonomous daily candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest?.Actions is null)
            yield break;

        var batchGateOpen =
            string.Equals(manifest.BatchId, AutonomousDailyCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal) &&
            string.Equals(manifest.AssetStage, "runtime-approved", StringComparison.Ordinal) &&
            manifest.AutonomousSemanticsOwnerApproved &&
            manifest.ProductionAsset &&
            manifest.VisualApproved &&
            string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal) &&
            manifest.RuntimeApproved &&
            manifest.RuntimeUse &&
            !manifest.PrototypeUse &&
            manifest.DeveloperPreview &&
            manifest.AutonomousBindingEnabled &&
            manifest.MayEnterAutonomousPoolByDefault &&
            manifest.AllowedSources is { Count: 2 } &&
            manifest.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) &&
            manifest.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal);
        if (!batchGateOpen)
        {
            BootstrapLog.WriteRaw("autonomous_daily_candidate_invalid errors=batch_gate_not_approved");
            yield break;
        }

        var sourceMotions = approvedSourceMotions.ToArray();
        foreach (var action in manifest.Actions)
        {
            var frames = new List<string>();
            var durations = new List<int>();
            var errors = new List<string>();
            if (!action.BehaviorId.StartsWith("wk.daily.", StringComparison.Ordinal))
                errors.Add("daily_namespace_required");
            if (!action.SourceMotionDesignApproved)
                errors.Add("source_motion_design_not_approved");
            if (!action.AutonomousSemanticsOwnerApproved ||
                !action.VisualApproved ||
                !string.Equals(action.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal) ||
                !action.RuntimeApproved ||
                !action.RuntimeUse ||
                !action.ProductionAsset ||
                action.PrototypeUse ||
                !action.DeveloperPreview ||
                !action.AutonomousBindingEnabled ||
                action.AllowedSources is not { Count: 2 } ||
                !action.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
                !action.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
                errors.Add("action_gate_not_approved");

            var binding = action.SourceBinding;
            if (binding is null)
            {
                errors.Add("source_binding_required");
            }
            else
            {
                var allowedSourceBatch =
                    string.Equals(binding.AssetBatch, CommandMockBehaviorIds.AssetBatch, StringComparison.Ordinal) ||
                    string.Equals(binding.AssetBatch, LifecycleCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal);
                if (!allowedSourceBatch)
                    errors.Add($"source_batch_not_allowed:{binding.AssetBatch}");

                var matchingSources = sourceMotions
                    .Where(x =>
                        string.Equals(x.AssetBatch, binding.AssetBatch, StringComparison.Ordinal) &&
                        string.Equals(x.BehaviorId, binding.BehaviorId, StringComparison.Ordinal))
                    .ToArray();
                if (matchingSources.Length != 1)
                {
                    errors.Add($"source_motion_count:{matchingSources.Length}");
                }
                else
                {
                    var source = matchingSources[0];
                    if (!source.RuntimeEnabled || source.IsExpired)
                        errors.Add("source_motion_must_be_runtime_approved_and_current");

                    var sourcePhase = source.Phases.SingleOrDefault(x =>
                        string.Equals(x.Name, binding.Phase, StringComparison.OrdinalIgnoreCase));
                    if (sourcePhase is null)
                    {
                        errors.Add($"source_phase_missing:{binding.Phase}");
                    }
                    else if (binding.StartFrame < 1 || binding.FrameCount != action.FrameCount)
                    {
                        errors.Add("source_range_invalid");
                    }
                    else
                    {
                        var startIndex = binding.StartFrame - 1;
                        frames.AddRange(sourcePhase.Frames.Skip(startIndex).Take(binding.FrameCount));
                        durations.AddRange(
                            Enumerable.Range(startIndex, frames.Count)
                                .Select(index => sourcePhase.DurationForFrame(index, source.FrameDurationMs)));
                        if (frames.Count != binding.FrameCount)
                            errors.Add($"source_range_count:{frames.Count}/{binding.FrameCount}");
                        else if (!string.Equals(SequenceSha256(frames), binding.SequenceSha256, StringComparison.OrdinalIgnoreCase))
                            errors.Add("source_sequence_sha256");
                    }
                }
            }

            if (frames.Count != action.FrameCount)
                errors.Add($"frame_count:{frames.Count}/{action.FrameCount}");
            if (durations.Any(x => x <= 0))
                errors.Add("duration_must_be_positive");

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"autonomous_daily_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            var sourceRoot = frames.Count > 0 ? Path.GetDirectoryName(frames[0])! : root;
            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "自主日常",
                $"{action.FromPosture} -> {action.ToPosture}",
                durations.Count > 0 ? durations[0] : 120,
                Interruptible: true,
                new[] { new MotionPhase("review", frames, action.Loop, durations) },
                sourceRoot,
                RuntimeEnabled: true,
                Status: "Windows 渲染验收通过：低频自主姿态过渡已启用",
                MissingContent: string.Empty,
                StartPose: action.FromPosture,
                EndPose: action.ToPosture,
                StyleGroup: "wukong-light-malt-gold-autonomous-daily-v1",
                Disposition: "已启用",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: $"{action.DailyRole}; shared immutable reference to approved light-malt-gold source frames; no duplicate PNG; enabled for low-frequency autonomous posture transitions.",
                CandidateProfile: manifest.AssetStage,
                VisualScale: ApprovedPetVisualScale,
                VisualApproved: true,
                RuntimeApproved: true,
                AutonomousBindingEnabled: true);
        }
    }

    private static IEnumerable<PlayableMotion> LoadMagicCandidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", MagicBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("magic_candidate_manifest_missing");
            yield break;
        }

        MagicMockBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<MagicMockBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Magic candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest?.Actions is null)
            yield break;

        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        var directionalFrames = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var directionalErrors = new List<string>();
        foreach (var direction in manifest.BroomDirectionalFlight ?? new Dictionary<string, IReadOnlyList<CommandActionFrameManifest>>())
        {
            var frames = new List<string>();
            foreach (var frame in direction.Value)
            {
                var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    directionalErrors.Add($"missing:{frame.Path}");
                    continue;
                }
                if (new FileInfo(path).Length != frame.Bytes)
                    directionalErrors.Add($"bytes:{frame.Path}");
                if (!string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                    directionalErrors.Add($"sha256:{frame.Path}");
                frames.Add(path);
            }
            if (frames.Count != 8)
                directionalErrors.Add($"direction_frame_count:{direction.Key}:{frames.Count}/8");
            directionalFrames[direction.Key] = frames;
        }

        foreach (var action in manifest.Actions)
        {
            var phases = new List<MotionPhase>();
            var errors = new List<string>();
            if (string.Equals(action.BehaviorId, MagicBehaviorIds.AccioBroom, StringComparison.OrdinalIgnoreCase))
            {
                errors.AddRange(directionalErrors);
                if (directionalFrames.Count != 8)
                    errors.Add($"direction_count:{directionalFrames.Count}/8");
            }
            foreach (var phase in action.Phases ?? Array.Empty<MagicMockPhaseManifest>())
            {
                var frames = new List<string>();
                foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
                {
                    var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path))
                    {
                        errors.Add($"missing:{frame.Path}");
                        continue;
                    }

                    var info = new FileInfo(path);
                    if (info.Length != frame.Bytes)
                        errors.Add($"bytes:{frame.Path}");
                    if (!string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                        errors.Add($"sha256:{frame.Path}");
                    frames.Add(path);
                }

                if (frames.Count != phase.FrameCount)
                    errors.Add($"phase_frame_count:{phase.Name}:{frames.Count}/{phase.FrameCount}");
                phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, VisualScale: phase.VisualScale));
            }

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"magic_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "宠物魔法",
                action.Direction,
                MagicFrameDurationFor(action.BehaviorId, action.FrameDurationMs),
                action.Interruptible,
                phases,
                Path.Combine(batchRoot, action.SourceFolder.Replace('/', Path.DirectorySeparatorChar)),
                RuntimeEnabled: false,
                Status: action.PrototypeUse
                    ? "Candidate / Prototype：允许主人原型展示"
                    : "Candidate / Prototype：原型展示关闭",
                MissingContent: "Windows transparent-renderer approval",
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: manifest.IdentityProfile,
                Disposition: "Prototype preview only",
                PrototypeUse: action.PrototypeUse,
                AssetBatch: manifest.BatchId,
                Effect: ParseMagicEffect(action.Effect),
                Description: action.Description,
                DirectionalFrames: string.Equals(action.BehaviorId, MagicBehaviorIds.AccioBroom, StringComparison.OrdinalIgnoreCase)
                    ? directionalFrames
                    : null,
                VisualScale: MagicVisualScaleFor(action.BehaviorId));
        }
    }

    private static double MagicVisualScaleFor(string behaviorId) =>
        behaviorId is MagicBehaviorIds.PetrificusTotalus or MagicBehaviorIds.PetrificusRelease
            ? ApprovedPetVisualScale
            : 1.35;

    private static int MagicFrameDurationFor(string behaviorId, int declaredDurationMs) =>
        string.Equals(behaviorId, MagicBehaviorIds.PetrificusTotalus, StringComparison.OrdinalIgnoreCase)
            ? Math.Max(170, declaredDurationMs)
            : declaredDurationMs;

    private static DesktopMotionEffect ParseMagicEffect(string? value) =>
        Enum.TryParse<DesktopMotionEffect>(value, ignoreCase: true, out var effect)
            ? effect
            : DesktopMotionEffect.None;


    private static IEnumerable<PlayableMotion> LoadCarRideCandidates(string root, bool allowPendingRoadGazeReview)
    {
        var loadStarted = Stopwatch.GetTimestamp();
        var manifestPath = Path.Combine(root, "action-batches", CarRideBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("car_ride_candidate_manifest_missing");
            yield break;
        }

        CarRideCandidateManifest? manifest;
        var parseStarted = Stopwatch.GetTimestamp();
        try
        {
            manifest = JsonSerializer.Deserialize<CarRideCandidateManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Car ride candidate manifest parse failed", ex);
            yield break;
        }
        var manifestParseMs = Stopwatch.GetElapsedTime(parseStarted).TotalMilliseconds;

        if (manifest?.Phases is null || !string.Equals(manifest.BehaviorId, CarRideBehaviorIds.CarRide, StringComparison.OrdinalIgnoreCase))
            yield break;

        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        var phases = new List<MotionPhase>();
        var errors = new List<string>();
        foreach (var phase in manifest.Phases)
        {
            var frames = new List<string>();
            var durations = new List<int>();
            foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
            {
                var path = Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    errors.Add($"missing:{frame.Path}");
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length != frame.Bytes)
                    errors.Add($"bytes:{frame.Path}");
                if (!string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"sha256:{frame.Path}");
                frames.Add(path);
                durations.Add(frame.DurationMs.GetValueOrDefault(manifest.FrameDurationMs));
            }

            if (frames.Count != phase.FrameCount)
                errors.Add($"phase_frame_count:{phase.Name}:{frames.Count}/{phase.FrameCount}");
            phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
        }

        if (!manifest.RuntimeApproved)
            errors.Add("runtime_approved_false");
        if (!manifest.RuntimeUse)
            errors.Add("runtime_use_false");
        if (manifest.PrototypeUse)
            errors.Add("prototype_use_still_enabled");
        if (!string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.OrdinalIgnoreCase))
            errors.Add("runtime_validation_not_passed_windows_renderer_qa");

        if (errors.Count > 0)
        {
            BootstrapLog.WriteRaw($"car_ride_candidate_invalid behavior={manifest.BehaviorId} errors={string.Join(",", errors)}");
            yield break;
        }

        BootstrapLog.WriteRaw(
            $"car_ride_index_ready manifest_parse_ms={manifestParseMs:0.0} validation_index_ms={Stopwatch.GetElapsedTime(loadStarted).TotalMilliseconds:0.0} frame_refs={manifest.AllSequences?.Values.Sum(x => x.Count) ?? 0}");

        var namedSequences = BuildCarRideNamedSequences(manifest, batchRoot, root, allowPendingRoadGazeReview);
        yield return new PlayableMotion(
            manifest.BehaviorId,
            manifest.DisplayName,
            "主人互动",
            "right",
            manifest.FrameDurationMs,
            Interruptible: true,
            phases,
            batchRoot,
            RuntimeEnabled: manifest.RuntimeApproved && manifest.RuntimeUse,
            Status: "正式运行：主人手动兜风",
            MissingContent: "None",
            StartPose: "stand.neutral.right",
            EndPose: "stand.neutral.right",
            StyleGroup: "wukong-current-adult-v1",
            Disposition: "Owner manual runtime only",
            PrototypeUse: manifest.PrototypeUse,
            AssetBatch: manifest.AssetId,
            Effect: DesktopMotionEffect.CarRide,
            DirectionalFrames: BuildCarRideDirectionalFrames(manifest, batchRoot),
            NamedSequences: namedSequences.Frames,
            NamedSequenceFrameDurations: namedSequences.FrameDurations,
            Description: "Owner-only car ride v8 runtime approved interaction",
            CandidateProfile: manifest.AssetId,
            VisualScale: 1.18,
            ScaleReferenceFrames: phases.SelectMany(x => x.Frames).Take(6).ToArray(),
            VisualApproved: true,
            RuntimeApproved: manifest.RuntimeApproved,
            AutonomousBindingEnabled: false);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildCarRideDirectionalFrames(CarRideCandidateManifest manifest, string batchRoot)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (manifest.AllSequences is null)
            return result;

        foreach (var entry in manifest.AllSequences)
        {
            const string prefix = "directions/";
            if (!entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var direction = entry.Key[prefix.Length..];
            var frames = entry.Value
                .Select(relative => Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar)))
                .Where(File.Exists)
                .ToArray();
            if (frames.Length > 0)
                result[direction] = frames;
        }

        return result;
    }

    private sealed record CarRideNamedSequenceCatalog(
        IReadOnlyDictionary<string, IReadOnlyList<string>> Frames,
        IReadOnlyDictionary<string, IReadOnlyList<int>> FrameDurations);

    private static CarRideNamedSequenceCatalog BuildCarRideNamedSequences(
        CarRideCandidateManifest manifest,
        string batchRoot,
        string assetRoot,
        bool allowPendingRoadGazeReview)
    {
        if (manifest.AllSequences is null)
            return new CarRideNamedSequenceCatalog(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase));

        var result = manifest.AllSequences.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value
                .Select(relative => Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar)))
                .Where(File.Exists)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var durations = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

        var extensionPath = Path.Combine(
            assetRoot,
            "action-batches",
            CarRideBehaviorIds.RoadGazeAssetBatch,
            "manifest.json");
        if (!File.Exists(extensionPath))
            return new CarRideNamedSequenceCatalog(result, durations);

        try
        {
            var extension = JsonSerializer.Deserialize<CarRideRoadGazeManifest>(
                File.ReadAllText(extensionPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (extension is null || extension.Sequences is null)
            {
                BootstrapLog.WriteRaw("car_ride_road_gaze_extension_gate_closed");
                return new CarRideNamedSequenceCatalog(result, durations);
            }

            var runtimeApproved = extension.RuntimeApproved &&
                                  extension.RuntimeUse &&
                                  extension.ProductionAsset &&
                                  extension.VisualApproved &&
                                  !extension.PrototypeUse &&
                                  string.Equals(extension.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.OrdinalIgnoreCase);
            var localReviewAllowed = allowPendingRoadGazeReview &&
                                     string.Equals(extension.AssetId, CarRideBehaviorIds.RoadGazeAssetBatch, StringComparison.Ordinal) &&
                                     string.Equals(extension.Status, "runtime_candidate_owner_visual_qa_pending", StringComparison.Ordinal) &&
                                     extension.PrototypeUse &&
                                     !extension.RuntimeApproved &&
                                     !extension.RuntimeUse &&
                                     !extension.ProductionAsset &&
                                     string.Equals(extension.RuntimeValidation, "pending_owner_windows_renderer_qa", StringComparison.OrdinalIgnoreCase);
            if (!runtimeApproved && !localReviewAllowed)
            {
                BootstrapLog.WriteRaw("car_ride_road_gaze_extension_gate_closed");
                return new CarRideNamedSequenceCatalog(result, durations);
            }
            if (localReviewAllowed)
                BootstrapLog.WriteRaw("car_ride_road_gaze_local_review_enabled");

            var extensionRoot = Path.GetDirectoryName(extensionPath)!;
            foreach (var entry in extension.Sequences)
            {
                if (entry.Key is not ("road-gaze/left" or "road-gaze/right") ||
                    entry.Value.Count < 6 ||
                    entry.Value.Count % 6 != 0)
                    continue;

                var frames = new List<string>(entry.Value.Count);
                var frameDurations = new List<int>(entry.Value.Count);
                var valid = true;
                foreach (var frame in entry.Value)
                {
                    var path = Path.Combine(extensionRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path) ||
                        new FileInfo(path).Length != frame.Bytes ||
                        !string.Equals(Sha256(path), frame.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        valid = false;
                        break;
                    }
                    frames.Add(path);
                    frameDurations.Add(frame.DurationMs.GetValueOrDefault(manifest.FrameDurationMs));
                }
                if (valid)
                {
                    result[entry.Key] = frames;
                    durations[entry.Key] = frameDurations;
                }
            }
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Car ride road-gaze extension parse failed", ex);
        }

        return new CarRideNamedSequenceCatalog(result, durations);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string SequenceSha256(IEnumerable<string> paths)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths)
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[64 * 1024];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                digest.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }
}

public static class Phase15BehaviorIds
{
    public const string ProneIdle = "wk.phase15.prone_idle";
    public const string ProneBreath = "wk.phase15.prone_breath";
    public const string ProneIdleV3Candidate = "wk.phase15.prone_idle_v3_candidate";
    public const string LookAround = "wk.phase15.look_around";
    public const string SafeStand = "wk.phase15.safe_stand";
    public const string StrokeEnjoy = "wk.phase15.stroke_enjoy";
    public const string ProneTouch = "wk.interaction.prone_touch";
}

public static class LifecycleCandidateBehaviorIds
{
    public const string AssetBatch = "WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2";
    public const string LivelyDailyP2 = "wk.candidate.lifecycle.lively_daily_p2";
    public const string StandIdleMicroloop = "wk.candidate.lifecycle.stand_idle_microloop";
    public const string SitIdleMicroloop = "wk.candidate.lifecycle.sit_idle_microloop";
    public const string ProneIdleMicroloop = "wk.candidate.lifecycle.prone_idle_microloop";
}

public static class RuntimeVisualScale
{
    public const double MinimumUserScale = 0.5;
    public const double MaximumUserScale = 2.5;

    public static double ClampUserScale(double userScale) =>
        Math.Clamp(userScale, MinimumUserScale, MaximumUserScale);

    public static double EffectiveScale(double userScale, double actionLocalScale) =>
        ClampUserScale(userScale) * Math.Max(0.01, actionLocalScale);
}

public static class LifecycleReviewCandidateBehaviorIds
{
    public const string V3R1AssetBatch = "WK-RUNTIME-LIFECYCLE-MICROLOOPS-PRODUCTION-CANDIDATE-v3R1-RECOVERED";
    public const string V4AssetBatch = "WK-AUTONOMOUS-PRONE-IDLE-FRONT-CANDIDATE-v4";
    public const string LivelyDailyV3R1 = "wk.candidate.lifecycle.lively_daily_v3r1";
    public const string LivelyDailyExitV3R1 = "wk.candidate.review.lifecycle.lively_daily_v3r1_exit";
    public const string StandIdleV3R1 = "wk.candidate.lifecycle.stand_idle_microloop_v3r1";
    public const string SitIdleV3R1 = "wk.candidate.lifecycle.sit_idle_microloop_v3r1";
    public const string LegacySideProneIdleV3R1 = "wk.candidate.lifecycle.prone_idle_legacy_side_v3r1";
    public const string FrontProneIdleV4 = "wk.candidate.lifecycle.prone_idle_front_microloop_v4";
    public const string FrontProneLickV4 = "wk.candidate.daily.prone_front_lick_microevent_v4";

    public static readonly IReadOnlySet<string> AssetBatches =
        new HashSet<string>(StringComparer.Ordinal)
        {
            V3R1AssetBatch,
            V4AssetBatch
        };
}

public static class SideProneFrontBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-SIDE-PRONE-FRONT-PRODUCTION-v5";
    public const string ObserveV5 = "wk.candidate.lifecycle.side_prone_front_observe_v5";
}

public static class ProneHeadCandidateBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-PRONE-HEAD-MICROEVENT-CANDIDATE-v4";
    public const string HeadLowerTurnV4 = "wk.candidate.daily.prone_head_lower_turn_v4";
}

public static class SleepCandidateBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-SLEEP-RUNTIME-FINAL-CANDIDATE-v10";
    public const string MainLifecycle = "wk.candidate.sleep.main_lifecycle_v2";
    public const string ProneToSideRoll = "wk.candidate.sleep.prone_to_side_roll_v2";
    public const string SprawledFrontBreath = "wk.candidate.sleep.sprawled_front_breath_v2";
    public const string SprawledLeftSideBreath = "wk.candidate.sleep.sprawled_left_side_breath_v2";
    public const string SprawledRightSideBreath = "wk.candidate.sleep.sprawled_right_side_breath_v2";
    public const string CompactProneBreath = "wk.candidate.sleep.compact_prone_breath_v2";
    public const string CurledSideBreath = "wk.candidate.sleep.curled_side_breath_v2";
    public const string TopDownProneBreath = "wk.candidate.sleep.top_down_prone_breath_v2";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        MainLifecycle,
        ProneToSideRoll,
        SprawledFrontBreath,
        SprawledLeftSideBreath,
        SprawledRightSideBreath,
        CompactProneBreath,
        CurledSideBreath,
        TopDownProneBreath
    };
}

public static class PatrolWalkCandidateBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-PATROL-WALK-v1-candidate";
    public const string WalkLeft = "wk.candidate.autonomous.patrol_walk_left_v1";
    public const string WalkRight = "wk.candidate.autonomous.patrol_walk_right_v1";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        WalkLeft,
        WalkRight
    };
}

public static class AutonomousDailyCandidateBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-DAILY-BEHAVIORS-v1";
    public const string StandToSit = "wk.daily.stand_to_sit";
    public const string SitToProne = "wk.daily.sit_to_prone";
    public const string ProneToSit = "wk.daily.prone_to_sit";
    public const string SitToStand = "wk.daily.sit_to_stand";
}

public static class CommandBehaviorIds
{
    public const string Sit = "wk.command.sit";
    public const string LieDown = "wk.command.lie_down";
    public const string PawRise = "wk.command.paw_rise";
    public const string Jump = "wk.command.jump";
    public const string SpinApproachStopSit = "wk.command.spin_approach_stop_sit";
    public const string PawEat = "wk.command.paw_eat";
}

public static class CommandMockBehaviorIds
{
    public const string AssetBatch = "WK-COMMAND-PRODUCTION-CANDIDATES-v4";
}

public static class InteractionBehaviorIds
{
    public const string EatOnce = "wk.interaction.eat_once";
    public const string PlayOnce = "wk.interaction.play_once";
}

public static class CarRideBehaviorIds
{
    public const string AssetBatch = "WK-INTERACTION-CAR-RIDE-CANDIDATE-v8";
    public const string RoadGazeAssetBatch = "WK-INTERACTION-CAR-RIDE-ROAD-GAZE-CANDIDATE-v13";
    public const string CarRide = "wk.interaction.car_ride";

    public static readonly IReadOnlySet<string> PrototypeWhitelist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CarRide };
}

public static class MagicBehaviorIds{
    public const string AssetBatch = "WK-MAGIC-SPECIALS-CANDIDATE-v1";
    public const string AccioBroom = "wk.magic.accio_broom";
    public const string Apparate = "wk.magic.apparate";
    public const string PetrificusTotalus = "wk.magic.petrificus_totalus";
    public const string PetrificusRelease = "wk.magic.petrificus_release";
    public const string PetrifiedCoin = "wk.magic.petrificus_coin";
    public const string Scourgify = "wk.magic.scourgify";

    public static readonly IReadOnlySet<string> PrototypeWhitelist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AccioBroom,
            Apparate,
            PetrificusTotalus,
            PetrificusRelease,
            Scourgify
        };
}

public sealed record CommandActionBatchManifest(
    [property: JsonPropertyName("actions")] IReadOnlyList<CommandActionManifest> Actions);

public sealed record CommandActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("source_folder")] string SourceFolder,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("from_pose")] string FromPose,
    [property: JsonPropertyName("to_pose")] string ToPose,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("interruptible")] bool Interruptible,
    [property: JsonPropertyName("frames")] IReadOnlyList<CommandActionFrameManifest> Frames);

public sealed record CommandActionFrameManifest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("duration_ms")] int? DurationMs = null);

public sealed record CommandMotionMockBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("asset_stage")] string AssetStage,
    [property: JsonPropertyName("actions")] IReadOnlyList<CommandMotionMockActionManifest> Actions);

public sealed record CommandMotionMockActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("source_folder")] string SourceFolder,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("from_posture")] string FromPosture,
    [property: JsonPropertyName("to_posture")] string ToPosture,
    [property: JsonPropertyName("interruptible")] bool Interruptible,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("asset_stage")] string AssetStage,
    [property: JsonPropertyName("frames")] IReadOnlyList<CommandActionFrameManifest> Frames);


public sealed record LifecycleCandidateBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("candidate_profile")] string CandidateProfile,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("actions")] IReadOnlyList<LifecycleCandidateActionManifest> Actions);

public sealed record LifecycleCandidateActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("source_folder")] string SourceFolder,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("candidate_profile")] string? CandidateProfile,
    [property: JsonPropertyName("autonomous_mapping")] string? AutonomousMapping,
    [property: JsonPropertyName("from_pose")] string FromPose,
    [property: JsonPropertyName("to_pose")] string ToPose,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("interruptible")] bool Interruptible,
    [property: JsonPropertyName("phases")] IReadOnlyList<LifecycleCandidatePhaseManifest> Phases);

public sealed record LifecycleCandidatePhaseManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("loop")] bool Loop,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("frames")] IReadOnlyList<CommandActionFrameManifest> Frames);

public sealed record LifecycleReviewBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("candidate_profile")] string CandidateProfile,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("actions")] IReadOnlyList<LifecycleReviewActionManifest> Actions);

public sealed record LifecycleReviewActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("source_folder")] string SourceFolder,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("from_pose")] string FromPose,
    [property: JsonPropertyName("to_pose")] string ToPose,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("interruptible")] bool Interruptible,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("legacy_side_prone")] bool LegacySideProne,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("phases")] IReadOnlyList<LifecycleCandidatePhaseManifest> Phases);

public sealed record SideProneFrontProductionManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("phases")] IReadOnlyList<LifecycleCandidatePhaseManifest> Phases);

public sealed record ProneHeadCandidateBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("candidate_profile")] string CandidateProfile,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("internal_handoff_sha256")] string InternalHandoffSha256,
    [property: JsonPropertyName("current_runtime_prone_anchor_exact")] bool CurrentRuntimeProneAnchorExact,
    [property: JsonPropertyName("approved_runtime_profile")] string ApprovedRuntimeProfile,
    [property: JsonPropertyName("frame_inventory")] IReadOnlyList<ProneHeadCandidateInventoryFrame> FrameInventory,
    [property: JsonPropertyName("actions")] IReadOnlyList<ProneHeadCandidateActionManifest> Actions);

public sealed record ProneHeadCandidateInventoryFrame(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record ProneHeadCandidateActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("from_pose")] string FromPose,
    [property: JsonPropertyName("to_pose")] string ToPose,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("interruptible")] bool Interruptible,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("phases")] IReadOnlyList<ProneHeadCandidatePhaseManifest> Phases);

public sealed record ProneHeadCandidatePhaseManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("loop")] bool Loop,
    [property: JsonPropertyName("frames")] IReadOnlyList<ProneHeadCandidatePhaseFrameManifest> Frames);

public sealed record ProneHeadCandidatePhaseFrameManifest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("duration_ms")] int DurationMs);

public sealed record SleepCandidateBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("candidate_profile")] string CandidateProfile,
    [property: JsonPropertyName("source_zip")] string SourceZip,
    [property: JsonPropertyName("source_frame_count")] int SourceFrameCount,
    [property: JsonPropertyName("runtime_frame_count")] int RuntimeFrameCount,
    [property: JsonPropertyName("sequence_count")] int SequenceCount,
    [property: JsonPropertyName("owner_preview_approved")] bool OwnerPreviewApproved,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("runtime_render_scale")] double RuntimeRenderScale,
    [property: JsonPropertyName("frame_inventory")] IReadOnlyList<ProneHeadCandidateInventoryFrame> FrameInventory,
    [property: JsonPropertyName("actions")] IReadOnlyList<SleepCandidateActionManifest> Actions);

public sealed record SleepCandidateActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("from_pose")] string FromPose,
    [property: JsonPropertyName("to_pose")] string ToPose,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("entry_policy")] string EntryPolicy,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("total_duration_ms")] int TotalDurationMs,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("interruptible")] bool Interruptible,
    [property: JsonPropertyName("loop")] bool Loop,
    [property: JsonPropertyName("owner_preview_approved")] bool OwnerPreviewApproved,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("phases")] IReadOnlyList<SleepCandidatePhaseManifest> Phases);

public sealed record SleepCandidatePhaseManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("loop")] bool Loop,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("frames")] IReadOnlyList<CommandActionFrameManifest> Frames);

public sealed record PatrolWalkCandidateBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("candidate_profile")] string CandidateProfile,
    [property: JsonPropertyName("source_package")] string SourcePackage,
    [property: JsonPropertyName("source_frame_count")] int SourceFrameCount,
    [property: JsonPropertyName("runtime_frame_count")] int RuntimeFrameCount,
    [property: JsonPropertyName("sequence_count")] int SequenceCount,
    [property: JsonPropertyName("owner_preview_approved")] bool OwnerPreviewApproved,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("runtime_render_scale")] double RuntimeRenderScale,
    [property: JsonPropertyName("window_motion_enabled")] bool WindowMotionEnabled,
    [property: JsonPropertyName("frame_inventory")] IReadOnlyList<ProneHeadCandidateInventoryFrame> FrameInventory,
    [property: JsonPropertyName("actions")] IReadOnlyList<PatrolWalkCandidateActionManifest> Actions);

public sealed record PatrolWalkCandidateActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("from_pose")] string FromPose,
    [property: JsonPropertyName("to_pose")] string ToPose,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("total_duration_ms")] int TotalDurationMs,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("interruptible")] bool Interruptible,
    [property: JsonPropertyName("loop")] bool Loop,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("phases")] IReadOnlyList<SleepCandidatePhaseManifest> Phases);

public sealed record AutonomousDailyCandidateBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("asset_stage")] string AssetStage,
    [property: JsonPropertyName("autonomous_semantics_owner_approved")] bool AutonomousSemanticsOwnerApproved,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("may_enter_autonomous_pool_by_default")] bool MayEnterAutonomousPoolByDefault,
    [property: JsonPropertyName("actions")] IReadOnlyList<AutonomousDailyCandidateActionManifest> Actions);

public sealed record AutonomousDailyCandidateActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("daily_role")] string DailyRole,
    [property: JsonPropertyName("from_posture")] string FromPosture,
    [property: JsonPropertyName("to_posture")] string ToPosture,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("loop")] bool Loop,
    [property: JsonPropertyName("source_motion_design_approved")] bool SourceMotionDesignApproved,
    [property: JsonPropertyName("autonomous_semantics_owner_approved")] bool AutonomousSemanticsOwnerApproved,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("source_binding")] AutonomousDailySourceBindingManifest? SourceBinding);

public sealed record AutonomousDailySourceBindingManifest(
    [property: JsonPropertyName("asset_batch")] string AssetBatch,
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("start_frame")] int StartFrame,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("sequence_sha256")] string SequenceSha256);

public sealed record CarRideCandidateManifest(
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("all_sequences")] IReadOnlyDictionary<string, IReadOnlyList<string>>? AllSequences,
    [property: JsonPropertyName("phases")] IReadOnlyList<LifecycleCandidatePhaseManifest> Phases);

public sealed record CarRideRoadGazeManifest(
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("owner_runtime_enable_requested")] bool OwnerRuntimeEnableRequested,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("sequences")] IReadOnlyDictionary<string, IReadOnlyList<CarRideRoadGazeFrameManifest>> Sequences);

public sealed record CarRideRoadGazeFrameManifest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("duration_ms")] int? DurationMs,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("bytes")] long Bytes);

public sealed record MagicMockBatchManifest(    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("identity_profile")] string IdentityProfile,
    [property: JsonPropertyName("broom_directional_flight")] IReadOnlyDictionary<string, IReadOnlyList<CommandActionFrameManifest>>? BroomDirectionalFlight,
    [property: JsonPropertyName("actions")] IReadOnlyList<MagicMockActionManifest> Actions);

public sealed record MagicMockActionManifest(
    [property: JsonPropertyName("behavior_id")] string BehaviorId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("source_folder")] string SourceFolder,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs,
    [property: JsonPropertyName("from_pose")] string FromPose,
    [property: JsonPropertyName("to_pose")] string ToPose,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("interruptible")] bool Interruptible,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("effect")] string Effect,
    [property: JsonPropertyName("phases")] IReadOnlyList<MagicMockPhaseManifest> Phases);

public sealed record MagicMockPhaseManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("loop")] bool Loop,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("frames")] IReadOnlyList<CommandActionFrameManifest> Frames,
    [property: JsonPropertyName("visual_scale")] double? VisualScale = null);

public enum PetrifiedCoinState
{
    Vivid,
    Flat,
    Faded,
    Exhausted
}

public enum PetrifiedCoinSide
{
    Front,
    Back
}

public sealed record PetrifiedCoinOptions(
    TimeSpan SettleToFlat,
    TimeSpan FadeAfter,
    TimeSpan ExhaustedAfter)
{
    public static PetrifiedCoinOptions Default { get; } = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(20));
}

public sealed record PetrifiedCoinManifest(
    [property: JsonPropertyName("states")] IReadOnlyList<PetrifiedCoinStateManifest> States,
    [property: JsonPropertyName("timing")] PetrifiedCoinTimingManifest Timing,
    [property: JsonPropertyName("flip")] PetrifiedCoinFlipManifest Flip);

public sealed record PetrifiedCoinStateManifest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("front")] string Front,
    [property: JsonPropertyName("back")] string Back);

public sealed record PetrifiedCoinTimingManifest(
    [property: JsonPropertyName("settle_to_flat_ms")] int SettleToFlatMs,
    [property: JsonPropertyName("fade_step_ms")] int FadeStepMs,
    [property: JsonPropertyName("exhausted_after_ms")] int ExhaustedAfterMs);

public sealed record PetrifiedCoinFlipManifest(
    [property: JsonPropertyName("front_to_back")] PetrifiedCoinFlipDirectionManifest FrontToBack);

public sealed record PetrifiedCoinFlipDirectionManifest(
    [property: JsonPropertyName("directories_by_state")] IReadOnlyDictionary<string, string> DirectoriesByState,
    [property: JsonPropertyName("frames")] int Frames,
    [property: JsonPropertyName("frame_duration_ms")] int FrameDurationMs);

public sealed class PetrifiedCoinAssets
{
    private readonly IReadOnlyDictionary<PetrifiedCoinState, (string Front, string Back)> _states;
    private readonly IReadOnlyDictionary<PetrifiedCoinState, IReadOnlyList<string>> _frontToBack;

    private PetrifiedCoinAssets(
        string root,
        PetrifiedCoinOptions defaults,
        IReadOnlyDictionary<PetrifiedCoinState, (string Front, string Back)> states,
        IReadOnlyDictionary<PetrifiedCoinState, IReadOnlyList<string>> frontToBack,
        int frameDurationMs)
    {
        Root = root;
        Defaults = defaults;
        _states = states;
        _frontToBack = frontToBack;
        FrameDurationMs = frameDurationMs;
    }

    public string Root { get; }
    public PetrifiedCoinOptions Defaults { get; }
    public int FrameDurationMs { get; }

    public static PetrifiedCoinAssets Load(string baseDirectory)
    {
        var root = Path.Combine(baseDirectory, "WukongAssets", "action-batches", MagicBehaviorIds.AssetBatch);
        var manifestPath = Path.Combine(root, "coin-manifest.json");
        var manifest = JsonSerializer.Deserialize<PetrifiedCoinManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Coin manifest is empty.");

        var states = new Dictionary<PetrifiedCoinState, (string Front, string Back)>();
        foreach (var item in manifest.States)
        {
            var state = ParseState(item.Id);
            var front = ResolveExisting(root, item.Front);
            var back = ResolveExisting(root, item.Back);
            states[state] = (front, back);
        }

        var flip = new Dictionary<PetrifiedCoinState, IReadOnlyList<string>>();
        foreach (var item in manifest.Flip.FrontToBack.DirectoriesByState)
        {
            var state = ParseState(item.Key);
            var directory = Path.Combine(root, item.Value.Replace('/', Path.DirectorySeparatorChar));
            var frames = Directory.GetFiles(directory, "*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            if (frames.Length != manifest.Flip.FrontToBack.Frames)
                throw new InvalidDataException($"Coin flip frame count mismatch for {item.Key}.");
            flip[state] = frames;
        }

        if (states.Count != 4 || flip.Count != 4)
            throw new InvalidDataException("Coin assets must contain four states and four flip sequences.");

        return new PetrifiedCoinAssets(
            root,
            new PetrifiedCoinOptions(
                TimeSpan.FromMilliseconds(manifest.Timing.SettleToFlatMs),
                TimeSpan.FromMilliseconds(manifest.Timing.FadeStepMs),
                TimeSpan.FromMilliseconds(manifest.Timing.ExhaustedAfterMs)),
            states,
            flip,
            manifest.Flip.FrontToBack.FrameDurationMs);
    }

    public PlayableMotion Static(PetrifiedCoinState state, PetrifiedCoinSide side)
    {
        var pair = _states[state];
        var frame = side == PetrifiedCoinSide.Front ? pair.Front : pair.Back;
        return Motion($"Coin {side} {state}", new[] { frame }, loop: true);
    }

    public PlayableMotion Flip(PetrifiedCoinState state, PetrifiedCoinSide from, bool resetToVivid)
    {
        IReadOnlyList<string> frames;
        if (from == PetrifiedCoinSide.Front)
        {
            frames = _frontToBack[state];
        }
        else if (resetToVivid)
        {
            var currentReverse = _frontToBack[state].Reverse().Take(5);
            var vividReverse = _frontToBack[PetrifiedCoinState.Vivid].Reverse().Skip(5);
            frames = currentReverse.Concat(vividReverse).ToArray();
        }
        else
        {
            frames = _frontToBack[state].Reverse().ToArray();
        }

        return Motion(from == PetrifiedCoinSide.Front ? "Coin flip to back" : "Coin flip to front", frames, loop: false);
    }

    private PlayableMotion Motion(string displayName, IReadOnlyList<string> frames, bool loop) => new(
        MagicBehaviorIds.PetrifiedCoin,
        displayName,
        "宠物魔法",
        "front",
        FrameDurationMs,
        false,
        new[] { new MotionPhase(loop ? "coin_hold" : "coin_flip", frames, loop) },
        Root,
        RuntimeEnabled: false,
        Status: "Candidate / Prototype：石化金币互动",
        MissingContent: "Windows renderer approval",
        PrototypeUse: true,
        AssetBatch: MagicBehaviorIds.AssetBatch,
        Description: "Owner-only interactive petrification coin candidate",
        VisualScale: 2.0 / 3.0);

    private static PetrifiedCoinState ParseState(string value) => value.ToLowerInvariant() switch
    {
        "vivid" => PetrifiedCoinState.Vivid,
        "flat" => PetrifiedCoinState.Flat,
        "faded" => PetrifiedCoinState.Faded,
        "exhausted" => PetrifiedCoinState.Exhausted,
        _ => throw new InvalidDataException($"Unknown coin state: {value}")
    };

    private static string ResolveExisting(string root, string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : throw new FileNotFoundException("Coin asset is missing.", path);
    }
}

public sealed record PetMotionRequest(
    PlayableMotion Motion,
    string Trigger,
    bool ReturnToIdle,
    int LoopCycles,
    BehaviorRequestSource Source = BehaviorRequestSource.OwnerUi,
    BehaviorExecutionMode ExecutionMode = BehaviorExecutionMode.Normal,
    long RequestedAtTimestamp = 0);

public sealed class DesktopRuntimeHost : INotifyPropertyChanged
{
    private const string StableHoldPrefix = "wk.runtime.posture_hold.";
    private static readonly IReadOnlySet<string> AutonomousRuntimeAllowlist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            LifecycleCandidateBehaviorIds.StandIdleMicroloop,
            LifecycleCandidateBehaviorIds.SitIdleMicroloop,
            LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
            LifecycleCandidateBehaviorIds.LivelyDailyP2,
            LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1,
            LifecycleReviewCandidateBehaviorIds.StandIdleV3R1,
            LifecycleReviewCandidateBehaviorIds.SitIdleV3R1,
            LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
            LifecycleReviewCandidateBehaviorIds.FrontProneLickV4,
            AutonomousDailyCandidateBehaviorIds.StandToSit,
            AutonomousDailyCandidateBehaviorIds.SitToProne,
            AutonomousDailyCandidateBehaviorIds.ProneToSit,
            AutonomousDailyCandidateBehaviorIds.SitToStand,
            ProneHeadCandidateBehaviorIds.HeadLowerTurnV4,
            PatrolWalkCandidateBehaviorIds.WalkLeft,
            PatrolWalkCandidateBehaviorIds.WalkRight
        };
    private readonly DesktopMotionCatalog _catalog;
    private readonly PetrifiedCoinAssets? _coinAssets;
    private readonly PetrifiedCoinOptions _coinOptions;
    private readonly Func<DateTimeOffset> _now;
    private readonly Random _random = new(1508);
    private PlayableMotion? _currentMotion;
    private BehaviorExecutionMode _currentExecutionMode = BehaviorExecutionMode.Normal;
    private readonly BehaviorAgentMockEngine _behaviorAgent = new();
    private readonly InteractionDecisionService _interactionDecisions = new();
    private readonly InitiativeSpeechDecisionService _initiativeSpeechDecisions = new();
    private readonly Dictionary<string, DateTimeOffset> _lastAccepted = new(StringComparer.OrdinalIgnoreCase);
    private readonly RollingFileLogStore _logs = RollingFileLogStore.CreateDefault();
    private PetRuntimeState _agentState = PetRuntimeState.Default;
    private RelationshipState _relationshipState = RelationshipState.Default;
    private TemperamentProfile _temperament = TemperamentProfile.Default;
    private PetDecision? _lastAgentDecision;
    private PetDecision? _pendingAgentDecision;
    private int _decisionSeed = 1508;
    private int _autonomousDecisionCount;
    private long _pendingRequestTimestamp;
    private DateTimeOffset _lastTapAt = DateTimeOffset.MinValue;
    private int _tapBurst;
    private DateTimeOffset _currentStartedAt = DateTimeOffset.MinValue;
    private string _currentBehaviorId = Phase15BehaviorIds.ProneIdle;
    private bool _currentInterruptible = true;
    private DateTimeOffset _nextAutonomousDecisionAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _coinActivityAt;
    private DateTimeOffset? _lastInitiativeSpeechAt;
    private BehaviorRequestSource _coinPreviewSource = BehaviorRequestSource.OwnerContextMenu;
    private bool _frontProneProfileActive;

    public DesktopRuntimeHost(PetrifiedCoinOptions? coinOptions = null, Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
        _catalog = DesktopMotionCatalog.Load(AppContext.BaseDirectory);
        try
        {
            _coinAssets = PetrifiedCoinAssets.Load(AppContext.BaseDirectory);
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Petrified coin assets failed to load", ex);
        }
        _coinOptions = coinOptions ?? _coinAssets?.Defaults ?? PetrifiedCoinOptions.Default;
        CurrentAsset = _catalog.RequiredIdle.FirstFrame;
        CurrentAction = _catalog.RequiredIdle.DisplayName;
        CurrentBehaviorId = _catalog.RequiredIdle.BehaviorId;
        _currentBehaviorId = _catalog.RequiredIdle.BehaviorId;
        _currentStartedAt = _now();
        _nextAutonomousDecisionAt = _currentStartedAt + ChooseAutonomousIdleDelay(_agentState.CurrentPosture, _random);
        Trace("asset_catalog_loaded", $"{_catalog.LoadSummary}; motions={_catalog.Motions.Count}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PetMotionRequest>? MotionRequested;
    public event EventHandler<int>? PetPixelSizeRequested;
    public event EventHandler<double>? PetScaleRequested;

    public ObservableCollection<string> TraceLines { get; } = new();
    public IReadOnlyList<PlayableMotion> Motions => _catalog.Motions;
    public string ReferenceVisualFramePath => _catalog.RequiredIdle.FirstFrame;
    public IReadOnlyList<PlayableMotion> MagicMotions => _catalog.Motions
        .Where(x => string.Equals(x.Category, "宠物魔法", StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.DisplayName)
        .ToArray();
    public IReadOnlyList<PlayableMotion> LifecycleCandidateMotions => _catalog.Motions
        .Where(x => string.Equals(x.AssetBatch, LifecycleCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.BehaviorId)
        .ToArray();
    public IReadOnlyList<PlayableMotion> LifecycleReviewCandidateMotions => _catalog.Motions
        .Where(x => LifecycleReviewCandidateBehaviorIds.AssetBatches.Contains(x.AssetBatch))
        .OrderBy(x => x.BehaviorId)
        .ToArray();
    public IReadOnlyList<PlayableMotion> AutonomousDailyCandidateMotions => _catalog.Motions
        .Where(x =>
            string.Equals(x.AssetBatch, AutonomousDailyCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.AssetBatch, ProneHeadCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.BehaviorId)
        .ToArray();
    public IReadOnlyList<PlayableMotion> CarRideCandidateMotions => _catalog.Motions
        .Where(x => string.Equals(x.BehaviorId, CarRideBehaviorIds.CarRide, StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.BehaviorId)
        .ToArray();
    public IReadOnlyList<PlayableMotion> CommandMotionMockMotions => _catalog.Motions
        .Where(x => string.Equals(x.AssetBatch, CommandMockBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.BehaviorId)
        .ToArray();
    public bool IsPetrified { get; private set; }
    public bool IsCoinAssetsReady => _coinAssets is not null;
    public PetrifiedCoinState? CurrentCoinState { get; private set; }
    public PetrifiedCoinSide? CurrentCoinSide { get; private set; }
    public bool EnableBehaviorAgentMock { get; private set; }
    public StablePosture CurrentStablePosture => _agentState.CurrentPosture;
    public string BehaviorAgentSnapshot => BuildBehaviorAgentSnapshot();
    public string BroomFlightMetrics { get; private set; } = "Not measured";

    public string CurrentAction { get; private set; } = "安静趴卧";
    public string CurrentBehaviorId { get; private set; } = Phase15BehaviorIds.ProneIdle;
    public string CurrentPhase { get; private set; } = "loop";
    public string CurrentAsset { get; private set; } = string.Empty;
    public string CurrentDisposition { get; private set; } = "Accepted";
    public string CurrentReason { get; private set; } = "启动后进入安静趴卧";
    public string LastSource { get; private set; } = "Startup";
    public string LastTrigger { get; private set; } = "startup";
    public string LastError { get; private set; } = "无";
    public string AgentStatus { get; private set; } = "本地 fallback runtime";
    public string Willingness { get; private set; } = "悟空现在很平静，愿意听你说话，但不一定想起来";
    public string Reply { get; private set; } = "让我再趴一会儿";
    public double Energy => _agentState.Energy;
    public double Hunger => _agentState.Hunger;
    public double Mood => _agentState.MoodValence;
    public double Curiosity => _agentState.Curiosity;
    public double Social => _agentState.SocialNeed;
    public double Stress => _agentState.Stress;
    public double Focus => _agentState.Focus;
    public double Comfort => _agentState.Comfort;

    public void SetBehaviorAgentMockEnabled(bool enabled)
    {
        EnableBehaviorAgentMock = enabled;
        Trace("behavior_agent_mock", enabled ? "enabled" : "disabled");
        OnPropertyChanged(nameof(EnableBehaviorAgentMock));
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
    }

    public void UpdateBehaviorAgentMock(TemperamentProfile temperament, PetRuntimeState state, RelationshipState relationship, int seed)
    {
        _temperament = temperament;
        _agentState = state.Clamp();
        _relationshipState = relationship;
        _decisionSeed = seed;
        _autonomousDecisionCount = 0;
        Trace("behavior_agent_state", $"seed={seed} posture={_agentState.CurrentPosture} energy={_agentState.Energy:0.00} hunger={_agentState.Hunger:0.00} social={_agentState.SocialNeed:0.00} boredom={_agentState.Boredom:0.00} stress={_agentState.Stress:0.00}");
        OnPropertyChanged(nameof(CurrentStablePosture));
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
    }

    public PetActionResult StartIdle(string source = "Startup")
    {
        var posture = string.Equals(source, "Startup", StringComparison.OrdinalIgnoreCase)
            ? PreferredStartupPosture()
            : _agentState.CurrentPosture;
        StartStablePostureIdle(posture, source);
        return PetActionResult.Accepted;
    }

    private StablePosture PreferredStartupPosture()
    {
        if (_agentState.Stress >= 0.68 || _agentState.Energy < 0.34)
            return StablePosture.Prone;
        if (_agentState.Energy < 0.52 || _agentState.Arousal < 0.34)
            return StablePosture.Sit;
        return StablePosture.Stand;
    }

    public Task RecordInputAsync(InputEvent inputEvent)
    {
        Trace("input", $"{inputEvent.Kind} source={inputEvent.Source}");
        return Task.CompletedTask;
    }

    public Task<PetActionResult> SubmitGestureAsync(PetGestureKind gesture, BehaviorRequestSource source)
    {
        var now = _now();
        Trace("gesture", gesture.ToString());
        if (gesture == PetGestureKind.OwnerTouch && _catalog.Find(Phase15BehaviorIds.ProneTouch) is { Deprecated: true })
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "asset_deprecated_owner_rejected", "摸摸回应已由主人移出使用范围");
            return Task.FromResult(PetActionResult.Deferred);
        }
        if (gesture == PetGestureKind.OwnerTouch)
        {
            var previousTapAt = _lastTapAt;
            _tapBurst = now - previousTapAt <= TimeSpan.FromMilliseconds(900) ? _tapBurst + 1 : 1;
            _lastTapAt = now;
        }
        else if (gesture == PetGestureKind.RapidTap)
        {
            _tapBurst = Math.Max(3, _tapBurst);
            _lastTapAt = now;
        }

        var enabled = _catalog.Motions
            .Where(motion => motion.RuntimeEnabled)
            .Select(motion => motion.BehaviorId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var decision = _interactionDecisions.Decide(new InteractionDecisionContext(
            gesture,
            _tapBurst,
            _agentState,
            _temperament,
            _relationshipState,
            now,
            IsStableIdleBehavior(_currentBehaviorId),
            _currentInterruptible,
            IsPetrified,
            enabled));
        _agentState = decision.UpdatedState;
        RaiseMetrics();
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        Trace("interaction_decision", $"gesture={decision.EffectiveGesture} disposition={decision.Disposition} behavior={decision.BehaviorId ?? "none"} reason={decision.ReasonCode}");
        if (decision.BehaviorId is not null)
            return Task.FromResult(SubmitBehavior(source, decision.BehaviorId, $"gesture:{decision.EffectiveGesture}", priority: 6));

        UpdateDecision(decision.Disposition, source.ToString(), decision.ReasonCode, decision.UserFacingReason);
        return Task.FromResult(decision.Disposition);
    }

    public InitiativeSpeechDecision DecideInitiativeSpeech(bool isChatExpanded)
    {
        var now = _now();
        var decision = _initiativeSpeechDecisions.Decide(new InitiativeSpeechContext(
            _agentState,
            _temperament,
            _relationshipState,
            now,
            _lastInitiativeSpeechAt,
            IsStableIdleBehavior(_currentBehaviorId),
            IsPetrified,
            isChatExpanded,
            now.Hour is >= 23 or < 7,
            _decisionSeed + _autonomousDecisionCount + (int)(now.Ticks % int.MaxValue)));
        Trace("initiative_speech_decision", $"speak={decision.ShouldSpeak} topic={decision.Topic} reason={decision.ReasonCode}");
        return decision;
    }

    public void RecordInitiativeSpeech(InitiativeSpeechTopic topic)
    {
        _lastInitiativeSpeechAt = _now();
        _agentState = _agentState with
        {
            SocialNeed = Clamp01(_agentState.SocialNeed - (topic == InitiativeSpeechTopic.Companionship ? 0.025 : 0.008)),
            LastInteractionAt = _lastInitiativeSpeechAt
        };
        RaiseMetrics();
        Trace("initiative_speech_shown", $"topic={topic}");
    }

    public Task<PetActionResult> SubmitContextMenuIntentAsync(SemanticIntent intent)
    {
        var behaviorId = intent.Kind switch
        {
            SemanticIntentKind.Touch => Phase15BehaviorIds.ProneTouch,
            SemanticIntentKind.Quiet or SemanticIntentKind.Stop => _catalog.RequiredIdle.BehaviorId,
            _ => Phase15BehaviorIds.LookAround
        };
        return Task.FromResult(SubmitBehavior(BehaviorRequestSource.OwnerContextMenu, behaviorId, $"menu:{intent.Kind}", priority: 5));
    }

    public Task<PetActionResult> SubmitOwnerCommandAsync(string command)
    {
        var ownerCommand = ParseOwnerCommand(command);
        if (ownerCommand != OwnerCommandKind.None && CommandMotionMockMotions.Count > 0)
            return Task.FromResult(SubmitBehaviorAgentCommand(ownerCommand, BehaviorRequestSource.OwnerContextMenu, $"owner_command:{command}"));
        if (ownerCommand != OwnerCommandKind.None)
        {
            UpdateDecision(PetActionResult.Deferred, BehaviorRequestSource.OwnerContextMenu.ToString(), "command_candidate_assets_missing", "Command candidate assets are unavailable; formal command assets are not runtime-approved.");
            return Task.FromResult(PetActionResult.Deferred);
        }

        var behaviorId = ResolveOwnerCommandBehavior(command);
        if (command is "停下" or "停")
            return StopAsync("owner_command:stop");
        return Task.FromResult(SubmitBehavior(BehaviorRequestSource.OwnerContextMenu, behaviorId, $"owner_command:{command}", priority: 8));
    }

    public Task<PetActionResult> SubmitDeveloperMotionAsync(string behaviorId) =>
        Task.FromResult(SubmitBehavior(BehaviorRequestSource.DeveloperForced, behaviorId, $"developer_force:{behaviorId}", priority: 100, executionMode: BehaviorExecutionMode.DeveloperPreview, bypassRuntimeGate: true));

    public Task<PetActionResult> SubmitBehaviorAgentCommandAsync(OwnerCommandKind command, BehaviorRequestSource source = BehaviorRequestSource.ControlPanel) =>
        Task.FromResult(SubmitBehaviorAgentCommand(command, source, $"agent_mock:{command}"));

    public PetDecision PreviewBehaviorAgentDecision(OwnerCommandKind command, int seed)
    {
        var decision = _behaviorAgent.Decide(CreateDecisionContext(command, seed, allowInitiative: command == OwnerCommandKind.None));
        _lastAgentDecision = decision;
        TraceDecision(decision, "preview");
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        return decision;
    }

    public Task<PetActionResult> SubmitDeveloperCandidateMotionAsync(string behaviorId) =>
        Task.FromResult(SubmitBehavior(BehaviorRequestSource.DeveloperForced, behaviorId, $"developer_candidate:{behaviorId}", priority: 100, executionMode: BehaviorExecutionMode.DeveloperPreview, bypassRuntimeGate: true));

    public void RequestPetPixelSize(int pixels)
    {
        var clamped = Math.Clamp(pixels, 128, 256);
        PetPixelSizeRequested?.Invoke(this, clamped);
        Trace("developer_size", $"candidate_profile={LifecycleCandidateBehaviorIds.AssetBatch} pixels={clamped}");
    }

    public void RequestPetScale(double scale)
    {
        var clamped = RuntimeVisualScale.ClampUserScale(scale);
        PetScaleRequested?.Invoke(this, clamped);
        Trace("user_scale", $"scale={clamped:0.00}");
    }

    public void ReportPerformance(string detail) => Trace("performance", detail);

    public void ReportBroomFlightMetrics(double horizontalPixels, double verticalPixels, Rect workArea)
    {
        BroomFlightMetrics = $"horizontal={horizontalPixels:0}px ({horizontalPixels / Math.Max(1, workArea.Width):P0}), vertical={verticalPixels:0}px ({verticalPixels / Math.Max(1, workArea.Height):P0})";
        Trace("broom_route", BroomFlightMetrics);
        OnPropertyChanged(nameof(BroomFlightMetrics));
    }

    public Task<PetActionResult> SubmitMagicAsync(string behaviorId, BehaviorRequestSource source)
    {
        if (behaviorId == MagicBehaviorIds.PetrificusTotalus && IsPetrified)
            behaviorId = MagicBehaviorIds.PetrificusRelease;
        return Task.FromResult(SubmitBehavior(source, behaviorId, $"magic:{behaviorId}", priority: 20, executionMode: BehaviorExecutionMode.PrototypePreview));
    }

    public Task<PetActionResult> SubmitCarRideAsync(BehaviorRequestSource source)
    {
        _pendingRequestTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var roadGazeReview = _catalog.CarRideRoadGazeReviewEnabled;
            return Task.FromResult(SubmitBehavior(
                source,
                CarRideBehaviorIds.CarRide,
                roadGazeReview ? "owner:car_ride_v8_road_gaze_review" : "owner:car_ride_v8",
                priority: 18,
                executionMode: roadGazeReview ? BehaviorExecutionMode.DeveloperPreview : BehaviorExecutionMode.Normal,
                bypassRuntimeGate: roadGazeReview));
        }
        finally
        {
            _pendingRequestTimestamp = 0;
        }
    }

    private PetActionResult SubmitBehaviorAgentCommand(OwnerCommandKind command, BehaviorRequestSource source, string trigger)
    {
        if (command == OwnerCommandKind.None)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "command_unresolved", "Unknown owner command.");
            return PetActionResult.Deferred;
        }

        var decision = _behaviorAgent.Decide(CreateDecisionContext(command, seed: 2408, allowInitiative: false));
        _lastAgentDecision = decision;
        TraceDecision(decision, trigger);
        return SubmitMockDecision(decision, source, trigger, allowAutonomous: false);
    }

    private PetActionResult SubmitMockDecision(PetDecision decision, BehaviorRequestSource source, string trigger, bool allowAutonomous)
    {
        if (decision.ReasonCodes.Contains("busy_non_interruptible", StringComparer.OrdinalIgnoreCase))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "busy_non_interruptible", "Current action is not safely interruptible.");
            return PetActionResult.Deferred;
        }

        if (source == BehaviorRequestSource.AutonomousTick && !allowAutonomous)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "mock_autonomous_forbidden", "Mock owner command cannot be started by autonomous tick.");
            return PetActionResult.Deferred;
        }

        var motion = BuildMotionForDecision(decision);
        if (motion is null)
        {
            UpdateDecision(PetActionResult.MissingAsset, source.ToString(), "mock_asset_missing", $"Missing mock asset: {decision.SelectedActionId}");
            return PetActionResult.MissingAsset;
        }

        var executionMode = motion.RuntimeEnabled ? BehaviorExecutionMode.Normal : BehaviorExecutionMode.PrototypePreview;
        var gate = EvaluateGate(source, executionMode, motion);
        if (!gate.Allowed)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), gate.ReasonCode, gate.UserFacingReason);
            return PetActionResult.Deferred;
        }

        var now = _now();
        if (source != BehaviorRequestSource.OwnerContextMenu &&
            source != BehaviorRequestSource.ControlPanel &&
            now - _currentStartedAt < TimeSpan.FromSeconds(3) &&
            _currentBehaviorId != Phase15BehaviorIds.ProneIdle)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "minimum_dwell", "Current behavior is in minimum dwell.");
            return PetActionResult.Deferred;
        }

        _agentState = _agentState.Clamp() with
        {
            IsBusy = true,
            ActiveActionId = decision.SelectedActionId
        };
        _pendingAgentDecision = decision;
        Accept(motion, source, executionMode, trigger, returnToIdle: true, loopCycles: 1);
        Trace("behavior_agent_started", $"decision={decision.DecisionId} action={decision.SelectedActionId}");
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        return PetActionResult.Accepted;
    }

    private PlayableMotion? BuildMotionForDecision(PetDecision decision)
    {
        var steps = decision.TransitionPlan.Count > 0
            ? decision.TransitionPlan
            : new[] { new TransitionStep(decision.SelectedActionId, decision.StartPosture, decision.EndPosture, true, "single_step") };
        var phases = new List<MotionPhase>();
        foreach (var step in steps)
        {
            var motion = CommandMotionMockMotions.FirstOrDefault(x => string.Equals(x.BehaviorId, step.ActionId, StringComparison.OrdinalIgnoreCase)) ?? _catalog.Find(step.ActionId);
            if (motion is null)
            {
                Trace("behavior_agent_transition_gap", $"missing={step.ActionId} reason={step.Reason}");
                continue;
            }
            foreach (var phase in motion.Phases)
                phases.Add(phase with { Name = $"{step.ActionId}:{phase.Name}" });
        }

        var selectedMotion = CommandMotionMockMotions.FirstOrDefault(x => string.Equals(x.BehaviorId, decision.SelectedActionId, StringComparison.OrdinalIgnoreCase)) ?? _catalog.Find(decision.SelectedActionId) ?? CommandMotionMockMotions.FirstOrDefault(x => string.Equals(x.BehaviorId, steps.Last().ActionId, StringComparison.OrdinalIgnoreCase)) ?? _catalog.Find(steps.Last().ActionId);
        if (selectedMotion is null)
            return null;
        if (phases.Count == 0)
            foreach (var phase in selectedMotion.Phases)
                phases.Add(phase with { Name = $"{selectedMotion.BehaviorId}:{phase.Name}" });

        return selectedMotion with
        {
            DisplayName = selectedMotion.RuntimeEnabled
                ? $"口令 - {selectedMotion.DisplayName}"
                : $"Agent Mock - {selectedMotion.DisplayName}",
            Phases = phases,
            StartPose = decision.StartPosture.ToString(),
            EndPose = decision.EndPosture.ToString(),
            Interruptible = false,
            RuntimeEnabled = selectedMotion.RuntimeEnabled,
            PrototypeUse = selectedMotion.PrototypeUse,
            AssetBatch = CommandMockBehaviorIds.AssetBatch,
            Status = selectedMotion.RuntimeEnabled
                ? "Approved owner command running through Normal"
                : "Behavior Agent Mock running through PrototypePreview",
            Description = string.Join("; ", decision.ReasonCodes)
        };
    }

    private BehaviorDecisionContext CreateDecisionContext(OwnerCommandKind command, int seed, bool allowInitiative) =>
        new(
            _temperament,
            _agentState.Clamp() with
            {
                IsBusy = !IsStableIdleBehavior(_currentBehaviorId) && _currentBehaviorId != _agentState.ActiveActionId,
                ActiveActionId = _currentBehaviorId
            },
            _relationshipState,
            command,
            _lastAccepted.Keys.TakeLast(8).ToArray(),
            _now(),
            _lastAccepted,
            seed,
            allowInitiative,
            IsNonInterruptible: !_currentInterruptible && !IsStableIdleBehavior(_currentBehaviorId));

    private static bool IsStableIdleBehavior(string behaviorId) =>
        string.Equals(behaviorId, Phase15BehaviorIds.ProneIdle, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(behaviorId, LifecycleCandidateBehaviorIds.StandIdleMicroloop, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(behaviorId, LifecycleCandidateBehaviorIds.SitIdleMicroloop, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(behaviorId, LifecycleCandidateBehaviorIds.ProneIdleMicroloop, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(behaviorId, LifecycleReviewCandidateBehaviorIds.StandIdleV3R1, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(behaviorId, LifecycleReviewCandidateBehaviorIds.SitIdleV3R1, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(behaviorId, LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4, StringComparison.OrdinalIgnoreCase) ||
        behaviorId.StartsWith(StableHoldPrefix, StringComparison.OrdinalIgnoreCase);

    private void TraceDecision(PetDecision decision, string trigger)
    {
        var scores = string.Join(", ", decision.CandidateScores
            .OrderByDescending(x => x.FinalScore)
            .Take(4)
            .Select(x => $"{x.ActionId}={x.FinalScore:0.00}{(x.Eliminated ? ":blocked" : string.Empty)}"));
        Trace("behavior_agent_decision", $"trigger={trigger} selected={decision.SelectedActionId} start={decision.StartPosture} end={decision.EndPosture} mood={decision.MoodExpression} style={decision.DialogueStyle} reasons={string.Join('|', decision.ReasonCodes)} scores=[{scores}]");
    }

    public Task<PetActionResult> SubmitPetrifiedCoinClickAsync(BehaviorRequestSource source = BehaviorRequestSource.OwnerUi)
    {
        if (!CanInteractWithCoin(source))
            return Task.FromResult(PetActionResult.Deferred);

        SetCoin(PetrifiedCoinState.Vivid, PetrifiedCoinSide.Front, _now());
        _coinPreviewSource = source;
        RequestCoinMotion(_coinAssets!.Static(PetrifiedCoinState.Vivid, PetrifiedCoinSide.Front), "coin:single_click_reset", int.MaxValue, source);
        return Task.FromResult(PetActionResult.Accepted);
    }

    public Task<PetActionResult> SubmitPetrifiedCoinDoubleClickAsync(BehaviorRequestSource source = BehaviorRequestSource.OwnerUi)
    {
        if (!CanInteractWithCoin(source) || CurrentCoinState is null || CurrentCoinSide is null)
            return Task.FromResult(PetActionResult.Deferred);

        var state = CurrentCoinState.Value;
        var side = CurrentCoinSide.Value;
        if (side == PetrifiedCoinSide.Front)
        {
            SetCoin(state, PetrifiedCoinSide.Back, activityAt: null);
            _coinPreviewSource = source;
            RequestCoinMotion(_coinAssets!.Flip(state, side, resetToVivid: false), "coin:double_click_back", 1, source);
        }
        else
        {
            SetCoin(PetrifiedCoinState.Vivid, PetrifiedCoinSide.Front, _now());
            _coinPreviewSource = source;
            RequestCoinMotion(_coinAssets!.Flip(state, side, resetToVivid: true), "coin:double_click_front_reset", 1, source);
        }
        return Task.FromResult(PetActionResult.Accepted);
    }

    public bool RefreshPetrifiedCoinState(DateTimeOffset? at = null)
    {
        if (!IsPetrified || _coinAssets is null || _coinActivityAt is null || CurrentCoinSide is null)
            return false;

        var elapsed = (at ?? _now()) - _coinActivityAt.Value;
        var next = elapsed >= _coinOptions.ExhaustedAfter
            ? PetrifiedCoinState.Exhausted
            : elapsed >= _coinOptions.FadeAfter
                ? PetrifiedCoinState.Faded
                : elapsed >= _coinOptions.SettleToFlat
                    ? PetrifiedCoinState.Flat
                    : PetrifiedCoinState.Vivid;
        if (next == CurrentCoinState)
            return false;

        SetCoin(next, CurrentCoinSide.Value, activityAt: null);
        RequestCoinMotion(_coinAssets.Static(next, CurrentCoinSide.Value), $"coin:inactivity:{next}", int.MaxValue, _coinPreviewSource);
        return true;
    }

    public Task<PetActionResult> StopAsync(string reason = "stop")
    {
        CompletePendingAgentDecision(completed: false, "owner_stop");
        IsPetrified = false;
        ClearCoin();
        OnPropertyChanged(nameof(IsPetrified));
        Trace("stop_requested", reason);
        StartIdle("stop");
        UpdateDecision(PetActionResult.Interrupted, BehaviorRequestSource.OwnerContextMenu.ToString(), "stopped", "已停止并恢复安静趴卧");
        return Task.FromResult(PetActionResult.Interrupted);
    }

    public Task SubmitAutonomousTickAsync()
    {
        AdvanceRuntimeStateForAutonomousTick();
        RaiseMetrics();

        var now = _now();
        if (now < _nextAutonomousDecisionAt || !IsStableIdleBehavior(_currentBehaviorId))
            return Task.CompletedTask;

        var choice = ChooseAutonomousBehavior();
        var result = SubmitBehavior(BehaviorRequestSource.AutonomousTick, choice.BehaviorId, choice.Reason, priority: -5);
        _nextAutonomousDecisionAt = now + (result == PetActionResult.Accepted
            ? ChooseAutonomousIdleDelay(_agentState.CurrentPosture, _random)
            : TimeSpan.FromSeconds(_random.Next(14, 25)));
        return Task.CompletedTask;
    }

    private void AdvanceRuntimeStateForAutonomousTick()
    {
        _agentState = _agentState.Clamp() with
        {
            Energy = Clamp01(_agentState.Energy - 0.015),
            Hunger = Clamp01(_agentState.Hunger + 0.006),
            SocialNeed = Clamp01(_agentState.SocialNeed + 0.004),
            Boredom = Clamp01(_agentState.Boredom + 0.012),
            Curiosity = Clamp01(_agentState.Curiosity + 0.018),
            Comfort = Clamp01(_agentState.Comfort + 0.004)
        };
    }

    public async Task SubmitFakeModelMessageAsync(string text)
    {
        var redacted = SensitiveDataRedactor.Redact(text);
        Reply = string.IsNullOrWhiteSpace(text) ? "让我再趴一会儿" : $"我听见了：{redacted}。让我再趴一会儿";
        Trace("model_reply", Reply);
        _logs.Append(RuntimeMode.Production, "model_response", new { Reply });
        OnPropertyChanged(nameof(Reply));
        SubmitBehavior(BehaviorRequestSource.Dialogue, Phase15BehaviorIds.LookAround, $"model:{SemanticIntentKind.ModelSuggested}", priority: 1);
        await Task.CompletedTask;
    }

    public void MarkPhase(string phase, string framePath)
    {
        CurrentPhase = phase;
        CurrentAsset = framePath;
        OnPropertyChanged(nameof(CurrentPhase));
        OnPropertyChanged(nameof(CurrentAsset));
        Trace("frame", $"{CurrentBehaviorId} phase={phase} asset={Path.GetFileName(framePath)}");
    }

    public void ReportError(string message)
    {
        LastError = string.IsNullOrWhiteSpace(message) ? "无" : message;
        OnPropertyChanged(nameof(LastError));
        Trace("runtime_error", LastError);
    }

    public void CompleteMotion(string behaviorId, string phase)
    {
        if (behaviorId == Phase15BehaviorIds.ProneIdle)
            return;

        if (behaviorId.StartsWith(StableHoldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Trace("command_terminal_settled", $"{behaviorId} phase={phase} posture={_agentState.CurrentPosture}");
            StartStablePostureIdle(
                _agentState.CurrentPosture,
                $"command_terminal_settled:{behaviorId}",
                _currentMotion?.RenderScaleOverride);
            return;
        }

        if (MockCommandActionIds.PrototypeWhitelist.Contains(behaviorId))
        {
            var completedRequestMotion = _currentMotion;
            _currentInterruptible = true;
            CompletePendingAgentDecision(completed: true, $"motion_complete:{phase}");
            _frontProneProfileActive = string.Equals(behaviorId, MockCommandActionIds.EatProne, StringComparison.OrdinalIgnoreCase) &&
                _agentState.CurrentPosture == StablePosture.Prone;
            Trace("mock_motion_completed", $"{behaviorId} phase={phase} posture={_agentState.CurrentPosture}");
            OnPropertyChanged(nameof(BehaviorAgentSnapshot));
            StartCommandEndPoseHold(behaviorId, _agentState.CurrentPosture, $"command_complete:{behaviorId}", completedRequestMotion);
            return;
        }

        var completedMotion = _catalog.Find(behaviorId);
        if (completedMotion is not null &&
            string.Equals(completedMotion.AssetBatch, LifecycleCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        {
            var completedFullLifecycle = string.Equals(
                completedMotion.BehaviorId,
                LifecycleCandidateBehaviorIds.LivelyDailyP2,
                StringComparison.OrdinalIgnoreCase);
            var posture = completedFullLifecycle && string.Equals(phase, "exit", StringComparison.OrdinalIgnoreCase)
                ? StablePosture.Stand
                : StablePostureFromPose(completedMotion.EndPose, _agentState.CurrentPosture);
            var repeated = string.Equals(_agentState.LastActionId, behaviorId, StringComparison.OrdinalIgnoreCase)
                ? _agentState.RepeatedActionCount + 1
                : 0;
            _agentState = _agentState with
            {
                CurrentPosture = posture,
                LastActionId = behaviorId,
                RepeatedActionCount = repeated,
                IsBusy = false,
                ActiveActionId = null,
                Energy = Clamp01(_agentState.Energy - (completedFullLifecycle ? 0.06 : 0.005)),
                Hunger = Clamp01(_agentState.Hunger + (completedFullLifecycle ? 0.012 : 0.002)),
                Boredom = Clamp01(_agentState.Boredom - (completedFullLifecycle ? 0.14 : 0.025)),
                MoodValence = Clamp01(_agentState.MoodValence + (completedFullLifecycle ? 0.015 : 0.003))
            };
            Trace("lifecycle_motion_completed", $"{behaviorId} phase={phase} posture={posture}");
            RaiseMetrics();
            StartStablePostureIdle(posture, $"lifecycle_complete:{behaviorId}");
            return;
        }

        if (completedMotion is not null &&
            LifecycleReviewCandidateBehaviorIds.AssetBatches.Contains(completedMotion.AssetBatch))
        {
            var completedFullLifecycle = string.Equals(
                completedMotion.BehaviorId,
                LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1,
                StringComparison.OrdinalIgnoreCase);
            var posture = completedFullLifecycle
                ? StablePosture.Stand
                : StablePostureFromPose(completedMotion.EndPose, _agentState.CurrentPosture);
            _frontProneProfileActive = posture == StablePosture.Prone &&
                (string.Equals(completedMotion.BehaviorId, LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(completedMotion.BehaviorId, LifecycleReviewCandidateBehaviorIds.FrontProneLickV4, StringComparison.OrdinalIgnoreCase));
            _agentState = _agentState with
            {
                CurrentPosture = posture,
                LastActionId = behaviorId,
                RepeatedActionCount = string.Equals(_agentState.LastActionId, behaviorId, StringComparison.OrdinalIgnoreCase)
                    ? _agentState.RepeatedActionCount + 1
                    : 0,
                IsBusy = false,
                ActiveActionId = null,
                Energy = Clamp01(_agentState.Energy - (completedFullLifecycle ? 0.055 : 0.004)),
                Boredom = Clamp01(_agentState.Boredom - (completedFullLifecycle ? 0.13 : 0.02)),
                MoodValence = Clamp01(_agentState.MoodValence + (completedFullLifecycle ? 0.015 : 0.002))
            };
            Trace("approved_lifecycle_motion_completed", $"{behaviorId} phase={phase} posture={posture} front_prone={_frontProneProfileActive}");
            RaiseMetrics();
            StartStablePostureIdle(posture, $"approved_lifecycle_complete:{behaviorId}");
            return;
        }

        if (completedMotion is not null &&
            string.Equals(completedMotion.AssetBatch, AutonomousDailyCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        {
            if (_currentExecutionMode != BehaviorExecutionMode.Normal)
            {
                Trace("autonomous_daily_preview_completed", $"{behaviorId} phase={phase} state_write=false");
                StartStablePostureIdle(_agentState.CurrentPosture, $"autonomous_daily_preview_complete:{behaviorId}");
                return;
            }
            var posture = StablePostureFromPose(completedMotion.EndPose, _agentState.CurrentPosture);
            _agentState = _agentState with
            {
                CurrentPosture = posture,
                LastActionId = behaviorId,
                IsBusy = false,
                ActiveActionId = null
            };
            Trace("autonomous_daily_completed", $"{behaviorId} phase={phase} posture={posture} runtime_approved=true");
            RaiseMetrics();
            StartStablePostureIdle(posture, $"autonomous_daily_candidate_complete:{behaviorId}");
            return;
        }

        if (completedMotion is not null &&
            string.Equals(completedMotion.AssetBatch, ProneHeadCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        {
            if (_currentExecutionMode != BehaviorExecutionMode.Normal)
            {
                Trace("prone_head_preview_completed", $"{behaviorId} phase={phase} state_write=false");
                StartStablePostureIdle(_agentState.CurrentPosture, $"prone_head_preview_complete:{behaviorId}");
                return;
            }
            _agentState = _agentState with
            {
                CurrentPosture = StablePosture.Prone,
                LastActionId = behaviorId,
                IsBusy = false,
                ActiveActionId = null
            };
            Trace("prone_head_microevent_completed", $"{behaviorId} phase={phase} posture=Prone runtime_approved=true profile=non_front_prone");
            RaiseMetrics();
            StartStablePostureIdle(StablePosture.Prone, $"prone_head_microevent_complete:{behaviorId}");
            return;
        }

        if (completedMotion is not null &&
            string.Equals(completedMotion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        {
            Trace("sleep_candidate_completed", $"{behaviorId} phase={phase} review_only=true state_write=false memory_write=false");
            StartStablePostureIdle(_agentState.CurrentPosture, $"sleep_candidate_complete:{behaviorId}");
            return;
        }

        if (completedMotion is not null &&
            string.Equals(completedMotion.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        {
            if (_currentExecutionMode != BehaviorExecutionMode.Normal)
            {
                Trace("patrol_walk_preview_completed", $"{behaviorId} phase={phase} state_write=false window_motion=false");
                StartStablePostureIdle(_agentState.CurrentPosture, $"patrol_walk_preview_complete:{behaviorId}");
                return;
            }
            _agentState = _agentState with
            {
                CurrentPosture = StablePosture.Stand,
                LastActionId = behaviorId,
                IsBusy = false,
                ActiveActionId = null
            };
            Trace("patrol_walk_completed", $"{behaviorId} phase={phase} posture=Stand runtime_approved=true window_motion=false");
            RaiseMetrics();
            StartStablePostureIdle(StablePosture.Stand, $"patrol_walk_complete:{behaviorId}");
            return;
        }

        _agentState = _agentState with
        {
            MoodValence = Clamp01(_agentState.MoodValence + 0.01),
            Comfort = Clamp01(_agentState.Comfort + 0.01),
            SocialNeed = behaviorId == Phase15BehaviorIds.ProneTouch || behaviorId == Phase15BehaviorIds.StrokeEnjoy
                ? Clamp01(_agentState.SocialNeed - 0.04)
                : _agentState.SocialNeed
        };
        Trace("motion_completed", $"{behaviorId} phase={phase}");
        StartIdle("safe_return");
        RaiseMetrics();
    }

    private void StartStablePostureIdle(StablePosture posture, string source, double? renderScaleOverride = null)
    {
        var preferred = posture switch
        {
            StablePosture.Stand => LifecycleCandidateBehaviorIds.StandIdleMicroloop,
            StablePosture.Sit => LifecycleCandidateBehaviorIds.SitIdleMicroloop,
            StablePosture.Prone when _frontProneProfileActive => LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
            StablePosture.Prone => LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
            _ => Phase15BehaviorIds.ProneIdle
        };
        var motion = _catalog.Find(preferred);
        if (motion is { RuntimeEnabled: true })
        {
            var playbackMotion = renderScaleOverride is > 0
                ? motion with { RenderScaleOverride = renderScaleOverride }
                : motion;
            _agentState = _agentState with { CurrentPosture = posture, IsBusy = false, ActiveActionId = null };
            _nextAutonomousDecisionAt = _now() + ChooseAutonomousIdleDelay(posture, _random);
            Accept(playbackMotion, BehaviorRequestSource.OwnerUi, BehaviorExecutionMode.Normal, source, returnToIdle: false, loopCycles: int.MaxValue);
            return;
        }

        var fallback = _catalog.RequiredIdle;
        var fallbackPosture = StablePostureFromPose(fallback.EndPose, StablePosture.Prone);
        _agentState = _agentState with { CurrentPosture = fallbackPosture, IsBusy = false, ActiveActionId = null };
        _nextAutonomousDecisionAt = _now() + ChooseAutonomousIdleDelay(fallbackPosture, _random);
        Accept(fallback, BehaviorRequestSource.OwnerUi, BehaviorExecutionMode.Normal, source, returnToIdle: false, loopCycles: int.MaxValue);
    }

    private void StartCommandEndPoseHold(string behaviorId, StablePosture posture, string source, PlayableMotion? completedRequestMotion)
    {
        var completedMotion = completedRequestMotion is not null &&
                              string.Equals(completedRequestMotion.BehaviorId, behaviorId, StringComparison.OrdinalIgnoreCase)
            ? completedRequestMotion
            : CommandMotionMockMotions.FirstOrDefault(x =>
                string.Equals(x.BehaviorId, behaviorId, StringComparison.OrdinalIgnoreCase));
        var terminalFrame = completedMotion?.Phases.SelectMany(x => x.Frames).LastOrDefault();
        if (completedMotion is null || string.IsNullOrWhiteSpace(terminalFrame))
        {
            StartStablePostureIdle(posture, source);
            return;
        }

        var postureName = posture.ToString().ToLowerInvariant();
        var hold = new PlayableMotion(
            $"{StableHoldPrefix}{postureName}",
            $"Command end hold ({postureName})",
            "基础动作",
            completedMotion.Direction,
            450,
            Interruptible: true,
            new[] { new MotionPhase("hold", new[] { terminalFrame }, Loop: true, new[] { 450 }) },
            completedMotion.SourceRoot,
            RuntimeEnabled: true,
            Status: "Stable command end posture",
            MissingContent: "None",
            StartPose: posture.ToString(),
            EndPose: posture.ToString(),
            StyleGroup: completedMotion.StyleGroup,
            Disposition: "Runtime hold",
            PrototypeUse: false,
            AssetBatch: completedMotion.AssetBatch,
            Description: "Briefly settles on the approved command terminal frame, then enters the matching posture microloop.",
            CandidateProfile: completedMotion.CandidateProfile,
            VisualScale: completedMotion.VisualScale,
            RenderScaleOverride: MotionVisualSizer.RenderScaleForMotion(completedMotion, DesktopMotionCatalog.ReferenceFramePath));

        Accept(hold, BehaviorRequestSource.OwnerUi, BehaviorExecutionMode.Normal, source, returnToIdle: true, loopCycles: 2);
    }

    private static StablePosture StablePostureFromPose(string? pose, StablePosture fallback)
    {
        if (string.IsNullOrWhiteSpace(pose))
            return fallback;
        if (pose.Contains("prone", StringComparison.OrdinalIgnoreCase))
            return StablePosture.Prone;
        if (pose.Contains("sit", StringComparison.OrdinalIgnoreCase))
            return StablePosture.Sit;
        if (pose.Contains("stand", StringComparison.OrdinalIgnoreCase))
            return StablePosture.Stand;
        return fallback;
    }

    private PetActionResult SubmitBehavior(
        BehaviorRequestSource source,
        string behaviorId,
        string trigger,
        int priority,
        BehaviorExecutionMode executionMode = BehaviorExecutionMode.Normal,
        bool bypassRuntimeGate = false)
    {
        var motion = _catalog.Find(behaviorId);
        if (motion is null)
        {
            UpdateDecision(PetActionResult.MissingAsset, source.ToString(), "missing_asset", $"缺少素材：{behaviorId}");
            return PetActionResult.MissingAsset;
        }
        var gate = EvaluateGate(source, executionMode, motion);
        if (!gate.Allowed)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), gate.ReasonCode, gate.UserFacingReason);
            return PetActionResult.Deferred;
        }

        var now = _now();
        var ownerExplicit = source is BehaviorRequestSource.OwnerUi or BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel;
        if (behaviorId == CarRideBehaviorIds.CarRide && _currentBehaviorId == behaviorId)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "car_ride_already_running", "兜风已经在进行中");
            return PetActionResult.Deferred;
        }

        if (!ownerExplicit && !bypassRuntimeGate && !_currentInterruptible && _currentBehaviorId != behaviorId)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "current_not_interruptible", "当前动作不能安全中断");
            return PetActionResult.Deferred;
        }

        if (!ownerExplicit && !bypassRuntimeGate && now - _currentStartedAt < TimeSpan.FromSeconds(3) && _currentBehaviorId != Phase15BehaviorIds.ProneIdle)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "minimum_dwell", "当前动作还在最短驻留时间内");
            return PetActionResult.Deferred;
        }

        if (!ownerExplicit &&
            !bypassRuntimeGate &&
            _lastAccepted.TryGetValue(behaviorId, out var last) &&
            now - last < TimeSpan.FromSeconds(source == BehaviorRequestSource.AutonomousTick ? 25 : 6))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "cooldown", "动作冷却中，已延后");
            return PetActionResult.Deferred;
        }

        var keepPetrified = motion.Effect == DesktopMotionEffect.Petrify;
        var longRunningEffect = motion.Effect is DesktopMotionEffect.BroomFlight or DesktopMotionEffect.CarRide;
        var stableIdle = IsStableIdleBehavior(behaviorId);
        var loopCycles = stableIdle || keepPetrified || longRunningEffect
            ? int.MaxValue
            : 2;
        if (source == BehaviorRequestSource.AutonomousTick &&
            behaviorId is LifecycleCandidateBehaviorIds.LivelyDailyP2 or LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1)
            loopCycles = ChooseAutonomousProneLoopCycles(_random);
        if (source == BehaviorRequestSource.AutonomousTick &&
            motion.AssetBatch is AutonomousDailyCandidateBehaviorIds.AssetBatch or ProneHeadCandidateBehaviorIds.AssetBatch)
            loopCycles = 1;
        if (source == BehaviorRequestSource.AutonomousTick &&
            string.Equals(motion.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            loopCycles = 2;
        if (source == BehaviorRequestSource.AutonomousTick && !stableIdle)
        {
            _agentState = _agentState with
            {
                IsBusy = true,
                ActiveActionId = behaviorId
            };
        }
        Accept(motion, source, executionMode, trigger, returnToIdle: !stableIdle && !keepPetrified, loopCycles: loopCycles);
        return PetActionResult.Accepted;
    }

    private static (bool Allowed, string ReasonCode, string UserFacingReason) EvaluateGate(
        BehaviorRequestSource source,
        BehaviorExecutionMode executionMode,
        PlayableMotion motion)
    {
        if (motion.Deprecated &&
            string.Equals(motion.AssetBatch, "WK-INTERACTION-PRONE-TOUCH-v4-1", StringComparison.OrdinalIgnoreCase))
            return (false, "asset_deprecated_owner_rejected", $"{motion.DisplayName} 已由主人明确移出使用范围");

        if (executionMode == BehaviorExecutionMode.DeveloperPreview)
            return (true, "developer_preview", "开发者预览已允许");

        if (motion.IsExpired)
            return (false, "asset_deprecated", $"{motion.DisplayName} 已过期，只能作为动作参考预览");

        if (executionMode == BehaviorExecutionMode.PrototypePreview)
        {
            var sourceAllowed = source is BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel;
            if (!sourceAllowed)
                return (false, "prototype_source_forbidden", "该入口不允许原型展示");
            if (!MagicBehaviorIds.PrototypeWhitelist.Contains(motion.BehaviorId) &&
                !CarRideBehaviorIds.PrototypeWhitelist.Contains(motion.BehaviorId) &&
                !MockCommandActionIds.PrototypeWhitelist.Contains(motion.BehaviorId))
                return (false, "prototype_not_whitelisted", "该行为不在原型白名单中");
            if (!motion.PrototypeUse)
                return (false, "prototype_use_disabled", "该素材未开启原型展示");
            return (true, "prototype_preview_allowed", "原型展示已允许");
        }

        if (string.Equals(motion.BehaviorId, CarRideBehaviorIds.CarRide, StringComparison.OrdinalIgnoreCase) &&
            executionMode == BehaviorExecutionMode.Normal &&
            source is not (BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel))
            return (false, "car_ride_source_forbidden", "兜风只允许主人从玩一下菜单或面板手动触发");

        if (MockCommandActionIds.PrototypeWhitelist.Contains(motion.BehaviorId) &&
            executionMode == BehaviorExecutionMode.Normal &&
            source is not (BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel))
            return (false, "command_source_forbidden", "口令动作只允许主人从右键菜单或面板手动触发");

        if (!motion.RuntimeEnabled)
            return (false, "runtime_locked", $"{motion.DisplayName} 素材正在返工，暂时不能正式播放。");

        return (true, "runtime_allowed", "正式素材已允许");
    }

    private static string ResolveOwnerCommandBehavior(string command) => command.Trim() switch
    {
        "叫过来" => Phase15BehaviorIds.LookAround,
        "吃一下" => InteractionBehaviorIds.EatOnce,
        "玩一下" => InteractionBehaviorIds.PlayOnce,
        "坐" => CommandBehaviorIds.Sit,
        "卧" => CommandBehaviorIds.LieDown,
        "伸爪" or "抬爪" or "握手" or "手" => CommandBehaviorIds.PawRise,
        "摸摸" => Phase15BehaviorIds.ProneTouch,
        "跳" or "跳跃" => CommandBehaviorIds.Jump,
        "转圈" or "靠近" or "停止坐下" or "转圈靠近停止坐下" => CommandBehaviorIds.SpinApproachStopSit,
        "喂食" or "吃东西" or "舔爪" or "吃" => CommandBehaviorIds.PawEat,
        "玩耍" => Phase15BehaviorIds.LookAround,
        "邀请外出" => Phase15BehaviorIds.SafeStand,
        "停下" or "停" => Phase15BehaviorIds.ProneIdle,
        _ => Phase15BehaviorIds.ProneIdle
    };

    private static OwnerCommandKind ParseOwnerCommand(string command)
    {
        var value = command.Trim();
        if (value is "Sit" or "\u5750")
            return OwnerCommandKind.Sit;
        if (value is "Down" or "\u5367" or "\u81e5")
            return OwnerCommandKind.Down;
        if (value is "Paw" or "\u624b" or "\u4f38\u722a" or "\u62ac\u722a" or "\u63e1\u624b")
            return OwnerCommandKind.Paw;
        if (value is "Jump" or "\u8df3" or "\u8df3\u8dc3")
            return OwnerCommandKind.Jump;
        if (value is "Spin" or "\u8f6c\u5708" or "\u8f49\u5708")
            return OwnerCommandKind.Spin;
        if (value is "Eat" or "\u5403" or "\u5582\u98df" or "\u5403\u4e1c\u897f")
            return OwnerCommandKind.Eat;
        return OwnerCommandKind.None;
    }

    private void Accept(
        PlayableMotion motion,
        BehaviorRequestSource source,
        BehaviorExecutionMode executionMode,
        string reason,
        bool returnToIdle,
        int loopCycles)
    {
        _currentBehaviorId = motion.BehaviorId;
        _currentMotion = motion;
        _currentExecutionMode = executionMode;
        _currentStartedAt = _now();
        _currentInterruptible = motion.Interruptible;
        _lastAccepted[motion.BehaviorId] = _currentStartedAt;
        CurrentBehaviorId = motion.BehaviorId;
        CurrentAction = motion.DisplayName;
        LastTrigger = reason;
        LastError = "无";
        if (motion.Effect == DesktopMotionEffect.Petrify)
        {
            IsPetrified = true;
            _coinPreviewSource = source;
            var transitionFrames = motion.Phases.TakeWhile(x => !x.Loop).Sum(x => x.Frames.Count);
            var coinVisibleAt = _currentStartedAt + TimeSpan.FromMilliseconds(transitionFrames * motion.FrameDurationMs);
            SetCoin(PetrifiedCoinState.Vivid, PetrifiedCoinSide.Front, coinVisibleAt);
        }
        else if (motion.Effect == DesktopMotionEffect.PetrifyRelease)
        {
            IsPetrified = false;
            ClearCoin();
        }
        OnPropertyChanged(nameof(IsPetrified));
        UpdateDecision(PetActionResult.Accepted, source.ToString(), reason, executionMode == BehaviorExecutionMode.PrototypePreview ? "正在展示原型魔法" : "接受");
        MotionRequested?.Invoke(this, new PetMotionRequest(motion, reason, returnToIdle, loopCycles, source, executionMode, _pendingRequestTimestamp));
        Trace("motion_requested", $"{motion.BehaviorId} source={source} mode={executionMode} asset_batch={motion.AssetBatch} reason={reason}");
        OnPropertyChanged(nameof(CurrentBehaviorId));
        OnPropertyChanged(nameof(CurrentAction));
        OnPropertyChanged(nameof(LastTrigger));
        OnPropertyChanged(nameof(LastError));
    }

    private bool CanInteractWithCoin(BehaviorRequestSource source)
    {
        var sourceAllowed = source is BehaviorRequestSource.OwnerUi or BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel;
        if (IsPetrified && sourceAllowed && _coinAssets is not null)
            return true;
        UpdateDecision(PetActionResult.Deferred, source.ToString(), "coin_interaction_forbidden", "石化金币当前不可互动");
        return false;
    }

    private void RequestCoinMotion(PlayableMotion motion, string trigger, int loopCycles, BehaviorRequestSource source)
    {
        _currentBehaviorId = motion.BehaviorId;
        _currentStartedAt = _now();
        _currentInterruptible = false;
        CurrentBehaviorId = motion.BehaviorId;
        CurrentAction = motion.DisplayName;
        LastTrigger = trigger;
        UpdateDecision(PetActionResult.Accepted, source.ToString(), trigger, "正在展示石化金币原型互动");
        MotionRequested?.Invoke(this, new PetMotionRequest(
            motion,
            trigger,
            ReturnToIdle: false,
            loopCycles,
            source,
            BehaviorExecutionMode.PrototypePreview));
        Trace("coin_motion_requested", $"state={CurrentCoinState} side={CurrentCoinSide} trigger={trigger}");
        OnPropertyChanged(nameof(CurrentBehaviorId));
        OnPropertyChanged(nameof(CurrentAction));
        OnPropertyChanged(nameof(LastTrigger));
    }

    private void SetCoin(PetrifiedCoinState state, PetrifiedCoinSide side, DateTimeOffset? activityAt)
    {
        CurrentCoinState = state;
        CurrentCoinSide = side;
        if (activityAt is not null)
            _coinActivityAt = activityAt;
        OnPropertyChanged(nameof(CurrentCoinState));
        OnPropertyChanged(nameof(CurrentCoinSide));
    }

    private void ClearCoin()
    {
        _coinActivityAt = null;
        CurrentCoinState = null;
        CurrentCoinSide = null;
        OnPropertyChanged(nameof(CurrentCoinState));
        OnPropertyChanged(nameof(CurrentCoinSide));
    }

    private (string BehaviorId, string Reason) ChooseAutonomousBehavior()
    {
        var hour = _now().Hour;
        var workQuiet = hour is >= 9 and <= 18;
        var elapsed = _now() - _currentStartedAt;
        var candidates = new List<(string BehaviorId, double Score, string Reason)>();
        switch (_agentState.CurrentPosture)
        {
            case StablePosture.Stand:
                AddIfEnabled(candidates, LifecycleCandidateBehaviorIds.StandIdleMicroloop,
                    0.16 + Comfort * 0.08 + (1 - _agentState.Arousal) * 0.05 + (workQuiet ? 0.04 : 0.01), "autonomous:brief_stable_stand_microloop");
                AddIfEnabled(candidates, LifecycleReviewCandidateBehaviorIds.StandIdleV3R1,
                    0.12 + Comfort * 0.07 + (1 - _agentState.Arousal) * 0.04 + (workQuiet ? 0.03 : 0.01), "autonomous:brief_approved_v3r1_stand_microloop");
                if (elapsed >= TimeSpan.FromSeconds(8) && Energy >= 0.18 && Stress < 0.85)
                {
                    AddIfEnabled(candidates, AutonomousDailyCandidateBehaviorIds.StandToSit,
                        0.42 + Comfort * 0.18 + (1 - Energy) * 0.14 + (workQuiet ? 0.08 : 0.02),
                        "autonomous:approved_stand_to_sit_transition");
                    AddIfEnabled(candidates, LifecycleCandidateBehaviorIds.LivelyDailyP2,
                        0.94 + Comfort * 0.38 + (1 - Energy) * 0.24 + Curiosity * 0.16 + _agentState.Boredom * 0.12 + Mood * 0.08 - Stress * 0.18 + (workQuiet ? 0.12 : 0.04),
                        "autonomous:prefer_long_prone_rest_lifecycle");
                    AddIfEnabled(candidates, LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1,
                        0.78 + Comfort * 0.32 + (1 - Energy) * 0.20 + Curiosity * 0.12 + _agentState.Boredom * 0.10 + Mood * 0.06 - Stress * 0.16 + (workQuiet ? 0.10 : 0.03),
                        "autonomous:prefer_approved_v3r1_long_prone_rest");
                    if (Energy >= 0.35 && Stress < 0.65)
                    {
                        AddIfEnabled(candidates, PatrolWalkCandidateBehaviorIds.WalkLeft,
                            0.08 + _agentState.Boredom * 0.08 + Curiosity * 0.05,
                            "autonomous:low_frequency_in_place_patrol_left");
                        AddIfEnabled(candidates, PatrolWalkCandidateBehaviorIds.WalkRight,
                            0.08 + _agentState.Boredom * 0.08 + Curiosity * 0.05,
                            "autonomous:low_frequency_in_place_patrol_right");
                    }
                }
                break;
            case StablePosture.Sit:
                AddIfEnabled(candidates, LifecycleCandidateBehaviorIds.SitIdleMicroloop,
                    0.82 + Comfort * 0.12 + (workQuiet ? 0.14 : 0.03), "autonomous:stable_sit_microloop");
                AddIfEnabled(candidates, LifecycleReviewCandidateBehaviorIds.SitIdleV3R1,
                    0.72 + Comfort * 0.10 + (workQuiet ? 0.12 : 0.03), "autonomous:approved_v3r1_sit_microloop");
                if (elapsed >= TimeSpan.FromSeconds(10))
                {
                    AddIfEnabled(candidates, AutonomousDailyCandidateBehaviorIds.SitToProne,
                        0.58 + Comfort * 0.18 + (1 - Energy) * 0.16 + (workQuiet ? 0.10 : 0.03),
                        "autonomous:approved_sit_to_prone_transition");
                    AddIfEnabled(candidates, AutonomousDailyCandidateBehaviorIds.SitToStand,
                        0.10 + Energy * 0.08 + _agentState.Arousal * 0.04,
                        "autonomous:low_frequency_sit_to_stand_transition");
                }
                break;
            default:
                if (_frontProneProfileActive)
                {
                    AddIfEnabled(candidates, LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
                        0.90 + Comfort * 0.14 + (workQuiet ? 0.16 : 0.04), "autonomous:approved_v4_front_prone_calm");
                    var lickReady = !_lastAccepted.TryGetValue(LifecycleReviewCandidateBehaviorIds.FrontProneLickV4, out var lastLick) ||
                        _now() - lastLick >= TimeSpan.FromSeconds(45);
                    if (lickReady && string.Equals(_currentBehaviorId, LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4, StringComparison.OrdinalIgnoreCase))
                        AddIfEnabled(candidates, LifecycleReviewCandidateBehaviorIds.FrontProneLickV4,
                            0.08 + Curiosity * 0.06 + Mood * 0.04, "autonomous:approved_v4_single_lick_microevent");
                }
                else
                {
                    AddIfEnabled(candidates, LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
                        0.86 + Comfort * 0.14 + (workQuiet ? 0.16 : 0.04), "autonomous:stable_prone_microloop");
                    if (elapsed >= TimeSpan.FromSeconds(12) &&
                        IsProneHeadAutonomousProfileAllowed(_agentState.CurrentPosture, _frontProneProfileActive))
                    {
                        AddIfEnabled(candidates, ProneHeadCandidateBehaviorIds.HeadLowerTurnV4,
                            0.12 + Curiosity * 0.08 + Mood * 0.03,
                            "autonomous:approved_prone_head_lower_turn_microevent");
                        AddIfEnabled(candidates, AutonomousDailyCandidateBehaviorIds.ProneToSit,
                            0.07 + Energy * 0.05 + _agentState.Arousal * 0.03,
                            "autonomous:low_frequency_prone_to_sit_transition");
                    }
                }
                break;
        }

        if (candidates.Count == 0)
            return (_catalog.RequiredIdle.BehaviorId, "autonomous:fallback_runtime_idle");

        var adjusted = candidates.Select(candidate =>
        {
            var repeated = string.Equals(candidate.BehaviorId, _currentBehaviorId, StringComparison.OrdinalIgnoreCase);
            var recent = _lastAccepted.TryGetValue(candidate.BehaviorId, out var last) && _now() - last < TimeSpan.FromSeconds(70);
            var penalty = repeated ? 0.62 : recent ? 0.78 : 1.0;
            return candidate with { Score = Math.Max(0.05, candidate.Score * penalty) };
        }).ToArray();
        var total = adjusted.Sum(x => x.Score);
        var decisionRandom = new Random(HashCode.Combine(_decisionSeed, _autonomousDecisionCount++, (int)_agentState.CurrentPosture));
        var draw = decisionRandom.NextDouble() * total;
        foreach (var candidate in adjusted)
        {
            draw -= candidate.Score;
            if (draw <= 0)
                return (candidate.BehaviorId, candidate.Reason);
        }
        var fallback = adjusted[^1];
        return (fallback.BehaviorId, fallback.Reason);
    }

    public static TimeSpan ChooseAutonomousIdleDelay(StablePosture posture, Random random) => posture switch
    {
        StablePosture.Stand => TimeSpan.FromSeconds(random.Next(8, 16)),
        StablePosture.Sit => TimeSpan.FromSeconds(random.Next(26, 45)),
        _ => TimeSpan.FromSeconds(random.Next(48, 77))
    };

    public static int ChooseAutonomousProneLoopCycles(Random random) => random.Next(4, 8);

    private void CompletePendingAgentDecision(bool completed, string reason)
    {
        var decision = _pendingAgentDecision;
        _pendingAgentDecision = null;
        if (decision is null)
        {
            _agentState = _agentState with { IsBusy = false, ActiveActionId = null };
            return;
        }

        var update = _behaviorAgent.ApplyOutcome(_agentState, _relationshipState, decision, completed, _now());
        _agentState = update.State;
        _relationshipState = update.Relationship;
        Trace("behavior_agent_outcome", $"{reason},{string.Join(",", update.Events)}");
        OnPropertyChanged(nameof(CurrentStablePosture));
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        RaiseMetrics();
    }

    private void AddIfEnabled(List<(string BehaviorId, double Score, string Reason)> candidates, string behaviorId, double score, string reason)
    {
        if (AutonomousRuntimeAllowlist.Contains(behaviorId) &&
            _catalog.Find(behaviorId) is { RuntimeEnabled: true, AutonomousBindingEnabled: true })
            candidates.Add((behaviorId, Math.Max(0.05, score), reason));
    }

    public static bool IsAutonomousRuntimeBehaviorAllowed(string behaviorId) =>
        AutonomousRuntimeAllowlist.Contains(behaviorId);

    public static bool IsProneHeadAutonomousProfileAllowed(StablePosture posture, bool frontProneProfileActive) =>
        posture == StablePosture.Prone && !frontProneProfileActive;

    private void UpdateDecision(PetActionResult result, string source, string reasonCode, string userFacing)
    {
        CurrentDisposition = result.ToString();
        CurrentReason = $"{reasonCode} · {userFacing}";
        LastSource = source;
        LastTrigger = reasonCode;
        LastError = result == PetActionResult.MissingAsset ? userFacing : LastError;
        Willingness = result == PetActionResult.Accepted
            ? "悟空愿意回应，但动作结束后会回到安静趴卧"
            : "悟空暂时不想动，或者缺少合适素材";
        OnPropertyChanged(nameof(CurrentDisposition));
        OnPropertyChanged(nameof(CurrentReason));
        OnPropertyChanged(nameof(LastSource));
        OnPropertyChanged(nameof(LastTrigger));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(Willingness));
        Trace("decision", $"{result} source={source} reason={reasonCode}");
    }

    private void Trace(string kind, string detail)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss} {kind}: {SensitiveDataRedactor.Redact(detail)}";
        TraceLines.Add(line);
        while (TraceLines.Count > 240)
            TraceLines.RemoveAt(0);
        _logs.Append(RuntimeMode.Production, kind, new { detail });
    }

    private void RaiseMetrics()
    {
        OnPropertyChanged(nameof(Energy));
        OnPropertyChanged(nameof(Hunger));
        OnPropertyChanged(nameof(Mood));
        OnPropertyChanged(nameof(Curiosity));
        OnPropertyChanged(nameof(Social));
        OnPropertyChanged(nameof(Stress));
        OnPropertyChanged(nameof(Focus));
        OnPropertyChanged(nameof(Comfort));
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private string BuildBehaviorAgentSnapshot()
    {
        var decision = _lastAgentDecision is null
            ? "none"
            : $"{_lastAgentDecision.SelectedActionId} {_lastAgentDecision.StartPosture}->{_lastAgentDecision.EndPosture} mood={_lastAgentDecision.MoodExpression} style={_lastAgentDecision.DialogueStyle}";
        return $"enabled={EnableBehaviorAgentMock}; posture={_agentState.CurrentPosture}; energy={_agentState.Energy:0.00}; hunger={_agentState.Hunger:0.00}; social={_agentState.SocialNeed:0.00}; boredom={_agentState.Boredom:0.00}; stress={_agentState.Stress:0.00}; mood={_agentState.MoodValence:0.00}; arousal={_agentState.Arousal:0.00}; temperament=({_temperament.Activity},{_temperament.Attachment},{_temperament.Sensitivity},{_temperament.Independence},{_temperament.Mischief}); last_decision={decision}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
