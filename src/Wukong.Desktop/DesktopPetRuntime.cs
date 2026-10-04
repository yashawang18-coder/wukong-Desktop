using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
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

public sealed record DesktopDialogueBehaviorResult(
    bool Recognized,
    NormalizedOwnerIntent Intent,
    PetActionResult Result,
    DialogueAct? Act,
    Guid? RequestId);

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
    bool WindowMotionEnabled = false,
    bool Deprecated = false,
    bool SupportsHorizontalMirror = false)
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
        $"左右镜像：{(SupportsHorizontalMirror ? "可用" : "不适用")}",
        $"已过期：{(IsExpired ? "是" : "否")}",
        $"来源包：{AssetBatch}",
        $"action id：{BehaviorId}"
    });
    public string PhaseSummary => string.Join(" / ", Phases.Select(x => $"{x.Name}:{x.Frames.Count}"));
    public bool HasVariableFrameDurations => Phases.Any(x => x.HasVariableDurations);
    public MotionVisibleMetrics VisibleMetrics => MotionVisualSizer.Measure(FirstFrame);
    public int VisibleSubjectWidth => VisibleMetrics.VisibleWidth;
    public int VisibleSubjectHeight => VisibleMetrics.VisibleHeight;
    public double PreviewRenderSize => Math.Clamp(
        150 * MotionVisualSizer.RenderScaleForMotion(this, DesktopMotionCatalog.ReferenceFramePath),
        150 * 0.45,
        150 * 2.6);
}

public sealed record MotionVisibleMetrics(int CanvasWidth, int CanvasHeight, Int32Rect Bounds)
{
    public int VisibleWidth => Bounds.Width;
    public int VisibleHeight => Bounds.Height;
    public double VisibleHeightRatio => CanvasHeight <= 0 ? 1.0 : VisibleHeight / (double)CanvasHeight;
}

public static class MotionHorizontalMirrorPolicy
{
    private static readonly IReadOnlySet<string> EligibleAssetBatches =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            LifecycleCandidateBehaviorIds.AssetBatch,
            LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch,
            LifecycleReviewCandidateBehaviorIds.V4AssetBatch,
            ProneHeadCandidateBehaviorIds.AssetBatch,
            SleepCandidateBehaviorIds.AssetBatch,
            FoodWaterCandidateBehaviorIds.AssetBatch,
            AutonomousDailyCandidateBehaviorIds.AssetBatch,
            CommandMockBehaviorIds.AssetBatch,
            PatrolWalkCandidateBehaviorIds.AssetBatch,
            PatrolWalkV9CandidateBehaviorIds.AssetBatch
        };

    public static bool Supports(PlayableMotion motion) =>
        !motion.IsExpired &&
        motion.Effect == DesktopMotionEffect.None &&
        (!motion.WindowMotionEnabled || motion.AssetBatch is PatrolWalkCandidateBehaviorIds.AssetBatch or PatrolWalkV9CandidateBehaviorIds.AssetBatch) &&
        motion.DirectionalFrames is not { Count: > 1 } &&
        EligibleAssetBatches.Contains(motion.AssetBatch);

    public static bool Resolve(PlayableMotion motion, bool facesRight) =>
        motion.SupportsHorizontalMirror &&
        (motion.AssetBatch == PatrolWalkCandidateBehaviorIds.AssetBatch
            ? string.Equals(motion.Direction, "right", StringComparison.OrdinalIgnoreCase)
            : facesRight);
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

public static class MotionDisplayNameCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Phase15BehaviorIds.ProneIdle] = "趴着休息",
            [Phase15BehaviorIds.ProneBreath] = "趴着呼吸",
            [Phase15BehaviorIds.ProneIdleV3Candidate] = "趴着休息（旧版）",
            [Phase15BehaviorIds.LookAround] = "回头看看",
            [Phase15BehaviorIds.SafeStand] = "安稳站立",
            [Phase15BehaviorIds.StrokeEnjoy] = "享受摸摸",
            [Phase15BehaviorIds.ProneTouch] = "趴着回应摸摸",
            [LifecycleCandidateBehaviorIds.LivelyDailyP2] = "走几步后趴下",
            [LifecycleCandidateBehaviorIds.StandIdleMicroloop] = "站着放松",
            [LifecycleCandidateBehaviorIds.SitIdleMicroloop] = "坐着休息",
            [LifecycleCandidateBehaviorIds.ProneIdleMicroloop] = "趴着休息",
            [LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1] = "散步后趴下",
            [LifecycleReviewCandidateBehaviorIds.LivelyDailyExitV3R1] = "从趴下起身",
            [LifecycleReviewCandidateBehaviorIds.StandIdleV3R1] = "站着呼吸",
            [LifecycleReviewCandidateBehaviorIds.SitIdleV3R1] = "坐着呼吸",
            [LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1] = "侧身趴着呼吸",
            [LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4] = "正面趴着呼吸",
            [LifecycleReviewCandidateBehaviorIds.FrontProneLickV4] = "趴着舔嘴",
            [FrontProneExpressionBehaviorIds.SatisfiedSmile] = "满足地笑一笑",
            [FrontProneExpressionBehaviorIds.CuriousObserve] = "好奇地看看",
            [FrontProneExpressionBehaviorIds.KnowingLook] = "若有所思地看着",
            [ProneHappyHotPantingBehaviorIds.HappyHotPanting] = "开心地趴着吐舌",
            [StandingHappyExpectantBehaviorIds.HappyExpectant] = "开心地期待",
            [SideProneFrontBehaviorIds.ObserveV5] = "侧趴回头看看",
            [ProneHeadCandidateBehaviorIds.HeadLowerTurnV4] = "趴着低头回头",
            [SleepCandidateBehaviorIds.MainLifecycle] = "慢慢入睡",
            [SleepCandidateBehaviorIds.ProneToSideRoll] = "翻身侧睡",
            [SleepCandidateBehaviorIds.SprawledFrontBreath] = "摊开趴睡",
            [SleepCandidateBehaviorIds.SprawledLeftSideBreath] = "左侧睡呼吸",
            [SleepCandidateBehaviorIds.SprawledRightSideBreath] = "右侧睡呼吸",
            [SleepCandidateBehaviorIds.CompactProneBreath] = "蜷身趴睡",
            [SleepCandidateBehaviorIds.CurledSideBreath] = "蜷着侧睡",
            [FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5] = "喝水",
            [FoodWaterCandidateBehaviorIds.EatKibbleStandingV5] = "吃饭",
            [PatrolWalkCandidateBehaviorIds.WalkLeft] = "向左走走",
            [PatrolWalkCandidateBehaviorIds.WalkRight] = "向右走走",
            [PatrolWalkV9CandidateBehaviorIds.WalkLeft] = "向左走走（候审）",
            [PatrolWalkV9CandidateBehaviorIds.WalkRight] = "向右走走（候审）",
            [WakeRiseCandidateBehaviorIds.SideWake] = "侧睡醒来（候审）",
            [WakeRiseCandidateBehaviorIds.SideInterrupt] = "侧睡被打断（候审）",
            [WakeRiseCandidateBehaviorIds.LowInterrupt] = "低头浅睡被打断（候审）",
            [WakeRiseCandidateBehaviorIds.FrontWake] = "正趴睡醒（候审）",
            [WakeRiseCandidateBehaviorIds.FrontRiseSit] = "正趴坐起（候审）",
            [WakeRiseCandidateBehaviorIds.FrontSitStand] = "正面坐姿站起（候审）",
            [WakeRiseCandidateBehaviorIds.FrontRiseFull] = "正趴完整起身（候审）",
            [AutonomousDailyCandidateBehaviorIds.StandToSit] = "从站立坐下",
            [AutonomousDailyCandidateBehaviorIds.SitToProne] = "从坐姿趴下",
            [AutonomousDailyCandidateBehaviorIds.ProneToSit] = "从趴姿坐起",
            [AutonomousDailyCandidateBehaviorIds.SitToStand] = "从坐姿站起",
            [CommandBehaviorIds.Sit] = "坐下",
            [CommandBehaviorIds.LieDown] = "卧下",
            [CommandBehaviorIds.PawRise] = "抬手",
            [CommandBehaviorIds.Jump] = "跳一下",
            [CommandBehaviorIds.SpinApproachStopSit] = "转一圈",
            [CommandBehaviorIds.PawEat] = "吃东西",
            [MockCommandActionIds.PawSit] = "坐着抬手",
            [MockCommandActionIds.PawProne] = "趴着抬手",
            [MockCommandActionIds.EatSit] = "坐着吃",
            [MockCommandActionIds.EatProne] = "趴着吃",
            [MockCommandActionIds.MockStandToSit] = "从站立坐下",
            [MockCommandActionIds.MockSitToProne] = "从坐姿趴下",
            [MockCommandActionIds.MockProneToSit] = "从趴姿坐起",
            [MockCommandActionIds.MockSitToStand] = "从坐姿站起",
            [MockCommandActionIds.MaintainCurrentIdle] = "保持当前姿态",
            [MockCommandActionIds.OrientToOwner] = "看向主人",
            [MockCommandActionIds.QuietSit] = "安静坐着",
            [MockCommandActionIds.QuietProne] = "安静趴着",
            [MockCommandActionIds.RequestAttention] = "想要陪伴",
            [MockCommandActionIds.PlayfulJump] = "想玩一下",
            [MockCommandActionIds.PlayfulSpin] = "想转一圈",
            [MockCommandActionIds.AskForFood] = "想吃东西",
            [MockCommandActionIds.Rest] = "休息",
            [MockCommandActionIds.Observe] = "观察周围",
            [InteractionBehaviorIds.EatOnce] = "吃一下",
            [InteractionBehaviorIds.PlayOnce] = "玩一下",
            [CarRideBehaviorIds.CarRide] = "开车兜风",
            [MagicBehaviorIds.AccioBroom] = "骑扫把飞行",
            [MagicBehaviorIds.Apparate] = "消失再出现",
            [MagicBehaviorIds.PetrificusTotalus] = "变成魔法金币",
            [MagicBehaviorIds.PetrificusRelease] = "解除金币魔法",
            [MagicBehaviorIds.PetrifiedCoin] = "魔法金币",
            [MagicBehaviorIds.Scourgify] = "清扫魔法（已停用）"
        };

    public static string Resolve(string behaviorId, string currentName) =>
        Names.TryGetValue(behaviorId, out var name)
            ? name
            : string.IsNullOrWhiteSpace(currentName) ? "动作预览" : currentName.Trim();
}

public sealed class DesktopMotionCatalog
{
    private const double ApprovedPetVisualScale = 0.92;
    private const double ApprovedProneIdleRenderScale = 0.68;
    public const string RoadGazeReviewMarkerFileName = "Wukong.RoadGazeReview.enabled";
    public const string FrontProneExpressionsReviewMarkerFileName = "Wukong.FrontProneExpressionsReview.enabled";
    public const string ProneHappyHotPantingReviewMarkerFileName = "Wukong.ProneHappyHotPantingReview.enabled";
    public const string StandingHappyExpectantReviewMarkerFileName = "Wukong.StandingHappyExpectantReview.enabled";
    private readonly IReadOnlyList<PlayableMotion> _allMotions;
    private readonly Dictionary<string, PlayableMotion> _motions;

    private DesktopMotionCatalog(
        IEnumerable<PlayableMotion> motions,
        string loadSummary,
        bool carRideRoadGazeReviewEnabled)
    {
        _allMotions = motions
            .Where(x => x.IsUsable)
            .Select(x => x with
            {
                DisplayName = MotionDisplayNameCatalog.Resolve(x.BehaviorId, x.DisplayName),
                SupportsHorizontalMirror = MotionHorizontalMirrorPolicy.Supports(x)
            })
            .ToArray();
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
        var frontProneExpressionsReviewEnabled = File.Exists(Path.Combine(baseDirectory, FrontProneExpressionsReviewMarkerFileName));
        var proneHappyHotPantingReviewEnabled = File.Exists(Path.Combine(baseDirectory, ProneHappyHotPantingReviewMarkerFileName));
        var standingHappyExpectantReviewEnabled = File.Exists(Path.Combine(baseDirectory, StandingHappyExpectantReviewMarkerFileName));
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
        var frontProneExpressionCandidates = LoadFrontProneExpressionCandidates(root, frontProneExpressionsReviewEnabled).ToArray();
        var proneHappyHotPantingCandidates = LoadProneHappyHotPantingCandidates(root, proneHappyHotPantingReviewEnabled).ToArray();
        var standingHappyExpectantCandidates = LoadStandingHappyExpectantCandidates(root, standingHappyExpectantReviewEnabled).ToArray();
        var sleepCandidates = LoadSleepCandidates(root).ToArray();
        var foodWaterCandidates = LoadFoodWaterCandidates(root).ToArray();
        var patrolWalkCandidates = LoadPatrolWalkCandidates(root).ToArray();
        var patrolWalkV9Candidates = LoadPatrolWalkV9Candidates(root).ToArray();
        var wakeRiseCandidates = LoadWakeRiseCandidates(root).ToArray();
        var carRideCandidates = LoadCarRideCandidates(root, carRideRoadGazeReviewEnabled).ToArray();
        var commandMocks = LoadCommandMotionMocks(root).ToArray();
        var autonomousDailyCandidates = LoadAutonomousDailyCandidates(root, lifecycleCandidates.Concat(commandMocks)).ToArray();
        ReferenceFramePath = lifecycleCandidates
            .FirstOrDefault(x => x.BehaviorId == LifecycleCandidateBehaviorIds.ProneIdleMicroloop && x.RuntimeEnabled)?.FirstFrame
            ?? motions.FirstOrDefault(x => x.BehaviorId == Phase15BehaviorIds.ProneIdle)?.FirstFrame
            ?? string.Empty;
        var summary = $"asset_root=WukongAssets; built_in={motions.Length}; command_candidates={commandCandidates.Length}; magic_candidates={magicCandidates.Length}; lifecycle_candidates={lifecycleCandidates.Length}; lifecycle_review_candidates={lifecycleReviewCandidates.Length}; prone_head_candidates={proneHeadCandidates.Length}; front_prone_expression_candidates={frontProneExpressionCandidates.Length}; front_prone_expression_review={frontProneExpressionsReviewEnabled}; prone_happy_hot_panting_candidates={proneHappyHotPantingCandidates.Length}; prone_happy_hot_panting_review={proneHappyHotPantingReviewEnabled}; standing_happy_expectant_candidates={standingHappyExpectantCandidates.Length}; standing_happy_expectant_review={standingHappyExpectantReviewEnabled}; sleep_candidates={sleepCandidates.Length}; food_water_candidates={foodWaterCandidates.Length}; patrol_walk_candidates={patrolWalkCandidates.Length}; patrol_walk_v9_candidates={patrolWalkV9Candidates.Length}; wake_rise_candidates={wakeRiseCandidates.Length}; autonomous_daily_candidates={autonomousDailyCandidates.Length}; car_ride_candidates={carRideCandidates.Length}; car_ride_road_gaze_review={carRideRoadGazeReviewEnabled}; command_mocks={commandMocks.Length}; manifests=action-batches/WK-COMMAND-ACTION-CANDIDATES-v3/manifest.json,action-batches/{MagicBehaviorIds.AssetBatch}/manifest.json,action-batches/{LifecycleCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{LifecycleReviewCandidateBehaviorIds.V3R1AssetBatch}/runtime-review-manifest.json,action-batches/{LifecycleReviewCandidateBehaviorIds.V4AssetBatch}/runtime-review-manifest.json,action-batches/{ProneHeadCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{FrontProneExpressionBehaviorIds.AssetBatch}/manifest.json,action-batches/{ProneHappyHotPantingBehaviorIds.AssetBatch}/manifest.json,action-batches/{StandingHappyExpectantBehaviorIds.AssetBatch}/manifest.json,action-batches/{SleepCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{FoodWaterCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{PatrolWalkCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{PatrolWalkV9CandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{WakeRiseCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{AutonomousDailyCandidateBehaviorIds.AssetBatch}/manifest.json,action-batches/{CarRideBehaviorIds.AssetBatch}/manifest.json,action-batches/{CarRideBehaviorIds.RoadGazeAssetBatch}/manifest.json,action-mocks/{CommandMockBehaviorIds.AssetBatch}/manifest.json";
        BootstrapLog.WriteRaw($"asset_catalog_loaded {summary}");
        return new DesktopMotionCatalog(
            motions.Concat(commandCandidates).Concat(magicCandidates).Concat(lifecycleCandidates).Concat(lifecycleReviewCandidates).Concat(proneHeadCandidates).Concat(frontProneExpressionCandidates).Concat(proneHappyHotPantingCandidates).Concat(standingHappyExpectantCandidates).Concat(sleepCandidates).Concat(foodWaterCandidates).Concat(patrolWalkCandidates).Concat(patrolWalkV9Candidates).Concat(wakeRiseCandidates).Concat(autonomousDailyCandidates).Concat(carRideCandidates).Concat(commandMocks),
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
                RenderScaleOverride: string.Equals(
                    action.BehaviorId,
                    LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
                    StringComparison.OrdinalIgnoreCase)
                        ? ApprovedProneIdleRenderScale
                        : action.RuntimeRenderScale,
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
        if (manifest.RuntimeRenderScale <= 0)
            batchErrors.Add("runtime_render_scale_invalid");
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
                RenderScaleOverride: manifest.RuntimeRenderScale,
                VisualApproved: true,
                RuntimeApproved: true,
                AutonomousBindingEnabled: true);
        }
    }

    private static IEnumerable<PlayableMotion> LoadFrontProneExpressionCandidates(string root, bool reviewEnabled)
    {
        var manifestPath = Path.Combine(root, "action-batches", FrontProneExpressionBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("front_prone_expression_manifest_missing");
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Front prone expression manifest parse failed", ex);
            yield break;
        }

        using (document)
        {
            var manifest = document.RootElement;
            var errors = new List<string>();
            if (manifest.GetProperty("batch_id").GetString() != FrontProneExpressionBehaviorIds.AssetBatch)
                errors.Add("batch_id_invalid");
            if (!manifest.GetProperty("owner_preview_approved").GetBoolean() ||
                !manifest.GetProperty("visual_approved").GetBoolean() ||
                !ValidFrontProneApproval(manifest, "pending_windows_expression_strength_qa") ||
                manifest.GetProperty("prototype_use").GetBoolean() ||
                !manifest.GetProperty("developer_preview").GetBoolean())
                errors.Add("candidate_gate_invalid");

            var batchRoot = Path.GetDirectoryName(manifestPath)!;
            var inventory = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifest.GetProperty("frame_inventory").EnumerateArray())
            {
                var relative = item.GetProperty("path").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                    relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
                {
                    errors.Add($"unsafe_path:{relative}");
                    continue;
                }
                if (!inventory.TryAdd(relative, item.Clone()))
                {
                    errors.Add($"duplicate_inventory:{relative}");
                    continue;
                }
                var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    errors.Add($"missing:{relative}");
                else if (new FileInfo(path).Length != item.GetProperty("bytes").GetInt64())
                    errors.Add($"bytes:{relative}");
                else if (!string.Equals(Sha256(path), item.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    errors.Add($"sha256:{relative}");
                if (item.GetProperty("width").GetInt32() != 1024 ||
                    item.GetProperty("height").GetInt32() != 1024 ||
                    item.GetProperty("mode").GetString() != "RGBA")
                    errors.Add($"frame_contract:{relative}");
            }
            if (inventory.Count != 36)
                errors.Add($"inventory_count:{inventory.Count}/36");

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"front_prone_expression_manifest_invalid errors={string.Join(',', errors)}");
                yield break;
            }

            var approved = manifest.GetProperty("runtime_approved").GetBoolean();
            foreach (var action in manifest.GetProperty("actions").EnumerateArray())
            {
                var behaviorId = action.GetProperty("behavior_id").GetString() ?? string.Empty;
                if (!FrontProneExpressionBehaviorIds.All.Contains(behaviorId))
                    continue;
                var frames = new List<string>();
                var durations = new List<int>();
                foreach (var frame in action.GetProperty("frames").EnumerateArray())
                {
                    var relative = frame.GetProperty("path").GetString() ?? string.Empty;
                    if (!inventory.ContainsKey(relative))
                    {
                        errors.Add($"unregistered_frame:{relative}");
                        continue;
                    }
                    frames.Add(Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                    durations.Add(frame.GetProperty("duration_ms").GetInt32());
                }
                if (frames.Count != 12 || durations.Count != 12 || durations.Any(x => x <= 0) ||
                    !string.Equals(Sha256(frames[0]), Sha256(frames[^1]), StringComparison.OrdinalIgnoreCase))
                {
                    BootstrapLog.WriteRaw($"front_prone_expression_action_invalid behavior={behaviorId}");
                    continue;
                }

                yield return new PlayableMotion(
                    behaviorId,
                    action.GetProperty("display_name").GetString() ?? behaviorId,
                    "自主日常",
                    "front",
                    durations[0],
                    Interruptible: true,
                    new[] { new MotionPhase("microexpression", frames, Loop: false, durations) },
                    batchRoot,
                    RuntimeEnabled: approved || reviewEnabled,
                    Status: approved ? "已启用" : "视觉已通过，等待 Windows 实际尺寸表情强度验收",
                    MissingContent: approved ? "None" : "Windows renderer QA",
                    StartPose: "prone.awake.front",
                    EndPose: "prone.awake.front",
                    StyleGroup: "wukong-front-prone-microexpressions-v1",
                    Disposition: approved ? "已启用" : "视觉已通过 · 未启用",
                    PrototypeUse: false,
                    AssetBatch: FrontProneExpressionBehaviorIds.AssetBatch,
                    Description: action.GetProperty("description").GetString() ?? string.Empty,
                    CandidateProfile: approved ? "runtime-approved" : "front_prone_expression_review_v1",
                    VisualScale: ApprovedPetVisualScale,
                    VisualApproved: true,
                    RuntimeApproved: approved,
                    AutonomousBindingEnabled: approved || reviewEnabled,
                    SupportsHorizontalMirror: false);
            }
        }
    }

    private static IEnumerable<PlayableMotion> LoadStandingHappyExpectantCandidates(string root, bool reviewEnabled)
    {
        var manifestPath = Path.Combine(root, "action-batches", StandingHappyExpectantBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("standing_happy_expectant_manifest_missing");
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Standing happy-expectant manifest parse failed", ex);
            yield break;
        }

        using (document)
        {
            var manifest = document.RootElement;
            var errors = new List<string>();
            if (manifest.GetProperty("batch_id").GetString() != StandingHappyExpectantBehaviorIds.AssetBatch)
                errors.Add("batch_id_invalid");
            if (!manifest.GetProperty("owner_preview_approved").GetBoolean() ||
                !manifest.GetProperty("visual_approved").GetBoolean() ||
                manifest.GetProperty("runtime_approved").GetBoolean() ||
                manifest.GetProperty("runtime_use").GetBoolean() ||
                manifest.GetProperty("production_asset").GetBoolean() ||
                manifest.GetProperty("prototype_use").GetBoolean() ||
                !manifest.GetProperty("developer_preview").GetBoolean() ||
                manifest.GetProperty("autonomous_binding_enabled").GetBoolean() ||
                manifest.GetProperty("runtime_validation").GetString() != "pending_windows_renderer_qa")
                errors.Add("candidate_gate_invalid");

            var action = manifest.GetProperty("action");
            if (action.GetProperty("behavior_id").GetString() != StandingHappyExpectantBehaviorIds.HappyExpectant ||
                action.GetProperty("from_pose").GetString() != "stand.neutral.left_front" ||
                action.GetProperty("to_pose").GetString() != "stand.neutral.left_front" ||
                action.GetProperty("loop").GetBoolean() ||
                action.GetProperty("total_duration_ms").GetInt32() != 6170)
                errors.Add("action_contract_invalid");

            var batchRoot = Path.GetDirectoryName(manifestPath)!;
            var frames = new List<string>();
            var durations = new List<int>();
            foreach (var frame in action.GetProperty("frames").EnumerateArray())
            {
                var relative = frame.GetProperty("path").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                    relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
                {
                    errors.Add($"unsafe_path:{relative}");
                    continue;
                }

                var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    errors.Add($"missing:{relative}");
                else if (new FileInfo(path).Length != frame.GetProperty("bytes").GetInt64())
                    errors.Add($"bytes:{relative}");
                else if (!string.Equals(Sha256(path), frame.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    errors.Add($"sha256:{relative}");
                frames.Add(path);
                durations.Add(frame.GetProperty("duration_ms").GetInt32());
            }

            if (frames.Count != 16 || durations.Count != 16 || durations.Any(x => x <= 0) || durations.Sum() != 6170 ||
                frames.Count > 0 && !string.Equals(Sha256(frames[0]), Sha256(frames[^1]), StringComparison.OrdinalIgnoreCase))
                errors.Add("frame_contract_invalid");

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"standing_happy_expectant_manifest_invalid errors={string.Join(',', errors)}");
                yield break;
            }

            yield return new PlayableMotion(
                StandingHappyExpectantBehaviorIds.HappyExpectant,
                action.GetProperty("display_name").GetString() ?? StandingHappyExpectantBehaviorIds.HappyExpectant,
                "自主日常",
                "left-front",
                durations[0],
                Interruptible: true,
                new[] { new MotionPhase("microexpression", frames, Loop: false, durations) },
                batchRoot,
                RuntimeEnabled: reviewEnabled,
                Status: reviewEnabled ? "本地候选 EXE 审阅已启用" : "视觉已通过，等待 Windows 渲染验收",
                MissingContent: "Formal runtime approval remains pending until owner reviews the candidate EXE.",
                StartPose: "stand.neutral.left_front",
                EndPose: "stand.neutral.left_front",
                StyleGroup: "wukong-standing-happy-expectant-v1",
                Disposition: reviewEnabled ? "候选 EXE 审阅" : "视觉已通过 · 未启用",
                PrototypeUse: false,
                AssetBatch: StandingHappyExpectantBehaviorIds.AssetBatch,
                Description: action.GetProperty("description").GetString() ?? string.Empty,
                CandidateProfile: "standing_happy_expectant_review_v1",
                VisualScale: ApprovedPetVisualScale,
                VisualApproved: true,
                RuntimeApproved: false,
                AutonomousBindingEnabled: reviewEnabled,
                SupportsHorizontalMirror: false);
        }
    }

    private static IEnumerable<PlayableMotion> LoadProneHappyHotPantingCandidates(string root, bool reviewEnabled)
    {
        var manifestPath = Path.Combine(root, "action-batches", ProneHappyHotPantingBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("prone_happy_hot_panting_manifest_missing");
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Prone happy hot-panting manifest parse failed", ex);
            yield break;
        }

        using (document)
        {
            var manifest = document.RootElement;
            var errors = new List<string>();
            if (manifest.GetProperty("batch_id").GetString() != ProneHappyHotPantingBehaviorIds.AssetBatch)
                errors.Add("batch_id_invalid");
            if (!manifest.GetProperty("owner_preview_approved").GetBoolean() ||
                !manifest.GetProperty("visual_approved").GetBoolean() ||
                !ValidFrontProneApproval(manifest, "pending_windows_renderer_qa") ||
                manifest.GetProperty("prototype_use").GetBoolean() ||
                !manifest.GetProperty("developer_preview").GetBoolean())
                errors.Add("candidate_gate_invalid");

            var action = manifest.GetProperty("action");
            if (action.GetProperty("behavior_id").GetString() != ProneHappyHotPantingBehaviorIds.HappyHotPanting ||
                action.GetProperty("from_pose").GetString() != "prone.awake.front" ||
                action.GetProperty("to_pose").GetString() != "prone.awake.front" ||
                action.GetProperty("loop").GetBoolean() ||
                action.GetProperty("total_duration_ms").GetInt32() != 5680 ||
                action.GetProperty("full_tongue_duration_ms").GetInt32() != 4100)
                errors.Add("action_contract_invalid");

            var batchRoot = Path.GetDirectoryName(manifestPath)!;
            var frames = new List<string>();
            var durations = new List<int>();
            foreach (var frame in action.GetProperty("frames").EnumerateArray())
            {
                var relative = frame.GetProperty("path").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                    relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
                {
                    errors.Add($"unsafe_path:{relative}");
                    continue;
                }

                var path = Path.Combine(batchRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    errors.Add($"missing:{relative}");
                else if (new FileInfo(path).Length != frame.GetProperty("bytes").GetInt64())
                    errors.Add($"bytes:{relative}");
                else if (!string.Equals(Sha256(path), frame.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    errors.Add($"sha256:{relative}");
                frames.Add(path);
                durations.Add(frame.GetProperty("duration_ms").GetInt32());
            }

            if (frames.Count != 17 || durations.Count != 17 || durations.Any(x => x <= 0) ||
                durations.Sum() != 5680 || durations.Skip(3).Take(10).Sum() != 4100 ||
                frames.Count > 0 && !string.Equals(Sha256(frames[0]), Sha256(frames[^1]), StringComparison.OrdinalIgnoreCase))
                errors.Add("frame_contract_invalid");

            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"prone_happy_hot_panting_manifest_invalid errors={string.Join(',', errors)}");
                yield break;
            }

            var approved = manifest.GetProperty("runtime_approved").GetBoolean();
            yield return new PlayableMotion(
                ProneHappyHotPantingBehaviorIds.HappyHotPanting,
                action.GetProperty("display_name").GetString() ?? ProneHappyHotPantingBehaviorIds.HappyHotPanting,
                "自主日常",
                "front",
                durations[0],
                Interruptible: true,
                new[] { new MotionPhase("microexpression", frames, Loop: false, durations) },
                batchRoot,
                RuntimeEnabled: approved || reviewEnabled,
                Status: approved ? "已启用" : "视觉已通过，等待 Windows 渲染验收",
                MissingContent: approved ? "None" : "Windows renderer QA",
                StartPose: "prone.awake.front",
                EndPose: "prone.awake.front",
                StyleGroup: "wukong-prone-happy-hot-panting-v6",
                Disposition: approved ? "已启用" : "视觉已通过 · 未启用",
                PrototypeUse: false,
                AssetBatch: ProneHappyHotPantingBehaviorIds.AssetBatch,
                Description: action.GetProperty("description").GetString() ?? string.Empty,
                CandidateProfile: approved ? "runtime-approved" : "prone_happy_hot_panting_review_v6",
                VisualScale: ApprovedPetVisualScale,
                VisualApproved: true,
                RuntimeApproved: approved,
                AutonomousBindingEnabled: approved || reviewEnabled,
                SupportsHorizontalMirror: false);
        }
    }

    private static bool ValidFrontProneApproval(JsonElement manifest, string pendingValidation)
    {
        if (manifest.TryGetProperty("deprecated", out var deprecated) && deprecated.GetBoolean()) return false;
        var approved = manifest.GetProperty("runtime_approved").GetBoolean();
        return manifest.GetProperty("runtime_use").GetBoolean() == approved &&
            manifest.GetProperty("production_asset").GetBoolean() == approved &&
            manifest.GetProperty("autonomous_binding_enabled").GetBoolean() == approved &&
            manifest.GetProperty("runtime_validation").GetString() ==
                (approved ? "passed_windows_renderer_qa" : pendingValidation);
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
        if (!manifest.OwnerPreviewApproved ||
            !manifest.VisualApproved ||
            !manifest.RuntimeApproved ||
            !manifest.RuntimeUse ||
            !manifest.ProductionAsset ||
            manifest.PrototypeUse ||
            !manifest.DeveloperPreview ||
            !manifest.AutonomousBindingEnabled ||
            !string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
            batchErrors.Add("approved_batch_gate_invalid");
        if (manifest.SourceFrameCount != 29 || manifest.RuntimeFrameCount != 29 || manifest.SequenceCount != 7)
            batchErrors.Add("runtime_inventory_contract_invalid");
        if (manifest.RuntimeRenderScale <= 0)
            batchErrors.Add("runtime_render_scale_invalid");
        var expectedBatchSources = new[] { "AutonomousTick", "DeveloperPreview", "OwnerDialogue" };
        if (manifest.AllowedSources is null ||
            !manifest.AllowedSources.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(expectedBatchSources.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
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

        if (inventory.Count != 29)
            batchErrors.Add($"inventory_count:{inventory.Count}/29");
        var actions = manifest.Actions ?? Array.Empty<SleepCandidateActionManifest>();
        if (actions.Count != 7)
            batchErrors.Add($"action_count:{actions.Count}/7");
        if (batchErrors.Count > 0)
        {
            BootstrapLog.WriteRaw($"sleep_candidate_invalid batch={manifest.BatchId} errors={string.Join(",", batchErrors)}");
            yield break;
        }

        foreach (var action in actions)
        {
            var errors = new List<string>();
            var deprecated = action.Deprecated;
            if (!SleepCandidateBehaviorIds.All.Contains(action.BehaviorId))
                errors.Add("behavior_id_invalid");
            if (action.RuntimeRenderScale <= 0)
                errors.Add("action_runtime_render_scale_invalid");
            if (deprecated)
            {
                if (action.OwnerPreviewApproved ||
                    action.VisualApproved ||
                    action.RuntimeApproved ||
                    action.RuntimeUse ||
                    action.ProductionAsset ||
                    action.PrototypeUse ||
                    action.DeveloperPreview ||
                    action.AutonomousBindingEnabled ||
                    action.AllowedSources is null ||
                    action.AllowedSources.Count != 0 ||
                    !string.Equals(action.RuntimeValidation, "failed_owner_visual_qa", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(action.DeprecatedReason))
                    errors.Add("deprecated_action_gate_invalid");
            }
            else
            {
                var autonomous = SleepCandidateBehaviorIds.AutonomousAllowed.Contains(action.BehaviorId);
                if (!SleepCandidateBehaviorIds.RuntimeApproved.Contains(action.BehaviorId) ||
                    !action.OwnerPreviewApproved ||
                    !action.VisualApproved ||
                    !action.RuntimeApproved ||
                    !action.RuntimeUse ||
                    !action.ProductionAsset ||
                    action.PrototypeUse ||
                    !action.DeveloperPreview ||
                    action.AutonomousBindingEnabled != autonomous ||
                    !string.Equals(action.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
                    errors.Add("approved_action_gate_invalid");
                var expectedSources = autonomous
                    ? new[] { "AutonomousTick", "DeveloperPreview", "OwnerDialogue" }
                    : new[] { "DeveloperPreview" };
                if (action.AllowedSources is null ||
                    !action.AllowedSources.OrderBy(x => x, StringComparer.Ordinal)
                        .SequenceEqual(expectedSources.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
                    errors.Add("approved_action_source_policy_invalid");
            }

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
                "自主睡眠",
                action.Direction,
                action.FrameDurationMs,
                action.Interruptible,
                phases,
                batchRoot,
                RuntimeEnabled: action.RuntimeUse && action.RuntimeApproved && !deprecated,
                Status: deprecated ? "已过期" : "已通过 Windows 渲染验收",
                MissingContent: deprecated
                    ? action.DeprecatedReason ?? "owner_rejected_visual_quality"
                    : "Approved wake and interrupt-exit remain unavailable; incompatible camera-view entries stay disabled.",
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: "wukong-sleep-continuity-v11-production",
                Disposition: deprecated ? "已过期" : action.AutonomousBindingEnabled ? "已启用：兼容趴姿低频自主" : "运行已批准：仅兼容入口预览",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: $"{action.Description} entry_policy={action.EntryPolicy}; source={manifest.SourceZip}; owner_preview_approved={action.OwnerPreviewApproved}; visual_approved={action.VisualApproved}; runtime_use={action.RuntimeUse}; deprecated={deprecated}.",
                CandidateProfile: manifest.CandidateProfile,
                VisualScale: ApprovedPetVisualScale,
                RenderScaleOverride: action.RuntimeRenderScale,
                VisualApproved: action.VisualApproved,
                RuntimeApproved: action.RuntimeApproved,
                AutonomousBindingEnabled: action.AutonomousBindingEnabled,
                Deprecated: deprecated);
        }
    }

    private static IEnumerable<PlayableMotion> LoadFoodWaterCandidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", FoodWaterCandidateBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("food_water_candidate_manifest_missing");
            yield break;
        }

        FoodWaterCandidateBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<FoodWaterCandidateBatchManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Food/water candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest is null ||
            !string.Equals(manifest.BatchId, FoodWaterCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal) ||
            !string.Equals(manifest.AssetId, FoodWaterCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal))
        {
            BootstrapLog.WriteRaw("food_water_candidate_identity_invalid");
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
            manifest.AutonomousBindingEnabled ||
            !manifest.NormalRuntimeAvailable ||
            !string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
            batchErrors.Add("runtime_batch_gate_invalid");
        if (manifest.SourceFrameCount != 48 ||
            manifest.RuntimeFrameCount != 48 ||
            manifest.SequenceFrameReferenceCount != 194 ||
            manifest.ReferencedUniqueFrameCount != 44 ||
            manifest.SequenceCount != 2)
            batchErrors.Add("candidate_inventory_contract_invalid");
        if (manifest.AllowedSources is null ||
            manifest.AllowedSources.Count != 3 ||
            !manifest.AllowedSources.Contains("OwnerContextMenu", StringComparer.Ordinal) ||
            !manifest.AllowedSources.Contains("ControlPanel", StringComparer.Ordinal) ||
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
        var actions = manifest.Actions ?? Array.Empty<FoodWaterCandidateActionManifest>();
        if (actions.Count != 2)
            batchErrors.Add($"action_count:{actions.Count}/2");
        if (batchErrors.Count > 0)
        {
            BootstrapLog.WriteRaw($"food_water_candidate_invalid batch={manifest.BatchId} errors={string.Join(",", batchErrors)}");
            yield break;
        }

        foreach (var action in actions)
        {
            var errors = new List<string>();
            var expectedFrames = action.BehaviorId switch
            {
                FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5 => 91,
                FoodWaterCandidateBehaviorIds.EatKibbleStandingV5 => 103,
                _ => 0
            };
            if (expectedFrames == 0)
                errors.Add("behavior_id_invalid");
            if (!action.OwnerPreviewApproved ||
                !action.VisualApproved ||
                !action.RuntimeApproved ||
                !action.RuntimeUse ||
                !action.ProductionAsset ||
                action.PrototypeUse ||
                !action.DeveloperPreview ||
                action.AutonomousBindingEnabled ||
                !action.NormalRuntimeAvailable ||
                !string.Equals(action.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal))
                errors.Add("runtime_action_gate_invalid");
            if (action.AllowedSources is null ||
                action.AllowedSources.Count != 3 ||
                !action.AllowedSources.Contains("OwnerContextMenu", StringComparer.Ordinal) ||
                !action.AllowedSources.Contains("ControlPanel", StringComparer.Ordinal) ||
                !action.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal))
                errors.Add("runtime_action_source_policy_invalid");
            if (!string.Equals(action.FromPose, "stand.neutral.left_front", StringComparison.Ordinal) ||
                !string.Equals(action.ToPose, "stand.neutral.left_front", StringComparison.Ordinal))
                errors.Add("standing_pose_contract_invalid");
            if (action.Interruptible || action.Loop || action.FrameDurationMs != 125)
                errors.Add("playback_contract_invalid");

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
                if (phase.Loop || frames.Count != phase.FrameCount || frames.Count == 0 || durations.Any(x => x != 125))
                    errors.Add($"phase_invalid:{phase.Name}:{frames.Count}/{phase.FrameCount}");
                phases.Add(new MotionPhase(phase.Name, frames, Loop: false, durations));
            }

            if (phases.Count != 3 || phases.Sum(x => x.Frames.Count) != expectedFrames || action.FrameCount != expectedFrames)
                errors.Add($"action_frame_count:{phases.Sum(x => x.Frames.Count)}/{expectedFrames}");
            if (phases.Sum(x => x.DurationTotalMs(action.FrameDurationMs)) != action.TotalDurationMs ||
                action.TotalDurationMs != expectedFrames * 125)
                errors.Add("action_duration_total_invalid");
            if (errors.Count > 0)
            {
                BootstrapLog.WriteRaw($"food_water_candidate_invalid behavior={action.BehaviorId} errors={string.Join(",", errors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId,
                action.DisplayName,
                "吃一下",
                action.Direction,
                action.FrameDurationMs,
                action.Interruptible,
                phases,
                batchRoot,
                RuntimeEnabled: true,
                Status: "已启用：仅允许主人手动触发",
                MissingContent: string.Empty,
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: "wukong-food-water-coat-seam-v5",
                Disposition: "已启用",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: $"{action.Description} OwnerContextMenu and ControlPanel use Normal; autonomous, dialogue and model routes remain disabled.",
                CandidateProfile: manifest.CandidateProfile,
                VisualScale: ApprovedPetVisualScale,
                VisualApproved: true,
                RuntimeApproved: true,
                AutonomousBindingEnabled: false);
        }
    }

    private static IEnumerable<PlayableMotion> LoadWakeRiseCandidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", WakeRiseCandidateBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("wake_rise_candidate_manifest_missing");
            yield break;
        }

        SleepCandidateBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SleepCandidateBatchManifest>(
                File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Wake/rise candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest is null ||
            !string.Equals(manifest.BatchId, WakeRiseCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal) ||
            !string.Equals(manifest.AssetId, WakeRiseCandidateBehaviorIds.AssetBatch, StringComparison.Ordinal))
        {
            BootstrapLog.WriteRaw("wake_rise_candidate_identity_invalid");
            yield break;
        }

        var errors = new List<string>();
        var runtimeGate = manifest.OwnerPreviewApproved && manifest.VisualApproved &&
            manifest.RuntimeApproved && manifest.RuntimeUse && manifest.ProductionAsset &&
            !manifest.PrototypeUse && manifest.DeveloperPreview && manifest.AutonomousBindingEnabled &&
            string.Equals(manifest.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal);
        if (!runtimeGate) errors.Add("runtime_gate_invalid");
        if (manifest.SourceFrameCount != 27 || manifest.RuntimeFrameCount != 27 || manifest.SequenceCount != 7)
            errors.Add("candidate_inventory_contract_invalid");
        if (manifest.AllowedSources is null || !manifest.AllowedSources.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(new[] { "AutonomousTick", "DeveloperPreview" }.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            errors.Add("runtime_source_policy_invalid");
        if (manifest.RuntimeRenderScale <= 0) errors.Add("runtime_render_scale_invalid");

        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        var inventory = new Dictionary<string, ProneHeadCandidateInventoryFrame>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.FrameInventory ?? Array.Empty<ProneHeadCandidateInventoryFrame>())
        {
            if (string.IsNullOrWhiteSpace(item.Path) || Path.IsPathRooted(item.Path) ||
                item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal) || !inventory.TryAdd(item.Path, item))
            {
                errors.Add($"unsafe_or_duplicate_inventory:{item.Path}");
                continue;
            }
            var framePath = Path.Combine(batchRoot, item.Path.Replace('/', Path.DirectorySeparatorChar));
            if (item.Width != 1024 || item.Height != 1024 || !string.Equals(item.Mode, "RGBA", StringComparison.Ordinal) ||
                !File.Exists(framePath) || new FileInfo(framePath).Length != item.Bytes ||
                !string.Equals(Sha256(framePath), item.Sha256, StringComparison.OrdinalIgnoreCase))
                errors.Add($"frame_contract:{item.Path}");
        }
        if (inventory.Count != 27) errors.Add($"inventory_count:{inventory.Count}/27");

        var actions = manifest.Actions ?? Array.Empty<SleepCandidateActionManifest>();
        if (actions.Count != WakeRiseCandidateBehaviorIds.All.Count) errors.Add("action_count_invalid");
        if (errors.Count > 0)
        {
            BootstrapLog.WriteRaw($"wake_rise_candidate_invalid batch={manifest.BatchId} errors={string.Join(',', errors)}");
            yield break;
        }

        foreach (var action in actions)
        {
            var actionErrors = new List<string>();
            if (!WakeRiseCandidateBehaviorIds.All.Contains(action.BehaviorId)) actionErrors.Add("behavior_id_invalid");
            if (!action.OwnerPreviewApproved || !action.VisualApproved || !action.RuntimeApproved || !action.RuntimeUse ||
                !action.ProductionAsset || action.PrototypeUse || !action.DeveloperPreview || !action.AutonomousBindingEnabled ||
                !string.Equals(action.RuntimeValidation, "passed_windows_renderer_qa", StringComparison.Ordinal) ||
                action.AllowedSources is null || !action.AllowedSources.OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(new[] { "AutonomousTick", "DeveloperPreview" }.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
                actionErrors.Add("action_runtime_gate_invalid");

            var phases = new List<MotionPhase>();
            foreach (var phase in action.Phases ?? Array.Empty<SleepCandidatePhaseManifest>())
            {
                var frames = new List<string>();
                var durations = new List<int>();
                foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
                {
                    if (!inventory.TryGetValue(frame.Path, out var inventoryFrame) || frame.Bytes != inventoryFrame.Bytes ||
                        !string.Equals(frame.Sha256, inventoryFrame.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        actionErrors.Add($"frame_metadata_invalid:{frame.Path}");
                        continue;
                    }
                    frames.Add(Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar)));
                    durations.Add(frame.DurationMs.GetValueOrDefault(action.FrameDurationMs));
                }
                if (phase.Loop || phase.Name != "sequence" || frames.Count != phase.FrameCount || frames.Count == 0 || durations.Any(x => x <= 0))
                    actionErrors.Add($"phase_invalid:{phase.Name}");
                phases.Add(new MotionPhase(phase.Name, frames, false, durations));
            }
            if (phases.Count != 1 || phases.Single().Frames.Count != action.FrameCount ||
                phases.Single().DurationTotalMs(action.FrameDurationMs) != action.TotalDurationMs || action.Loop)
                actionErrors.Add("action_timeline_invalid");
            if (actionErrors.Count > 0)
            {
                BootstrapLog.WriteRaw($"wake_rise_candidate_invalid behavior={action.BehaviorId} errors={string.Join(',', actionErrors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId, action.DisplayName, "基础动作", action.Direction, action.FrameDurationMs,
                action.Interruptible, phases, batchRoot, RuntimeEnabled: true,
                Status: "已启用：仅匹配姿态路径", MissingContent: "跨视角或不存在的睡眠中间姿态不会作为 fallback。",
                StartPose: action.FromPose, EndPose: action.ToPose, StyleGroup: "wukong-wake-rise-v9-candidate",
                Disposition: "已启用：兼容姿态与睡眠退出", PrototypeUse: false, AssetBatch: manifest.BatchId,
                Description: $"{action.Description} Uses the Normal lifecycle only when the current pose family is compatible.",
                CandidateProfile: manifest.CandidateProfile, VisualScale: ApprovedPetVisualScale,
                RenderScaleOverride: manifest.RuntimeRenderScale, VisualApproved: true, RuntimeApproved: true,
                AutonomousBindingEnabled: true, WindowMotionEnabled: false, SupportsHorizontalMirror: false);
        }
    }

    private static IEnumerable<PlayableMotion> LoadPatrolWalkV9Candidates(string root)
    {
        var manifestPath = Path.Combine(root, "action-batches", PatrolWalkV9CandidateBehaviorIds.AssetBatch, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            BootstrapLog.WriteRaw("patrol_walk_v9_candidate_manifest_missing");
            yield break;
        }

        PatrolWalkCandidateBatchManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PatrolWalkCandidateBatchManifest>(
                File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Patrol walk v9 candidate manifest parse failed", ex);
            yield break;
        }

        if (manifest is null ||
            !string.Equals(manifest.BatchId, PatrolWalkV9CandidateBehaviorIds.AssetBatch, StringComparison.Ordinal) ||
            !string.Equals(manifest.AssetId, PatrolWalkV9CandidateBehaviorIds.AssetBatch, StringComparison.Ordinal))
        {
            BootstrapLog.WriteRaw("patrol_walk_v9_candidate_identity_invalid");
            yield break;
        }

        var errors = new List<string>();
        var candidateGate = manifest.OwnerPreviewApproved && manifest.VisualApproved &&
            !manifest.RuntimeApproved && !manifest.RuntimeUse && !manifest.ProductionAsset &&
            !manifest.PrototypeUse && manifest.DeveloperPreview && !manifest.AutonomousBindingEnabled &&
            manifest.WindowMotionEnabled &&
            string.Equals(manifest.RuntimeValidation, "pending_windows_renderer_qa", StringComparison.Ordinal) &&
            string.Equals(manifest.WindowMotionValidation, "pending_windows_renderer_qa", StringComparison.Ordinal);
        if (!candidateGate) errors.Add("candidate_gate_invalid");
        if (manifest.SourceFrameCount != 26 || manifest.RuntimeFrameCount != 26 || manifest.SequenceCount != 2)
            errors.Add("candidate_inventory_contract_invalid");
        if (manifest.AllowedSources is null || !manifest.AllowedSources.SequenceEqual(new[] { "DeveloperPreview" }, StringComparer.Ordinal))
            errors.Add("candidate_source_policy_invalid");
        if (manifest.RuntimeRenderScale <= 0) errors.Add("runtime_render_scale_invalid");

        var batchRoot = Path.GetDirectoryName(manifestPath)!;
        var inventory = new Dictionary<string, ProneHeadCandidateInventoryFrame>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.FrameInventory ?? Array.Empty<ProneHeadCandidateInventoryFrame>())
        {
            if (string.IsNullOrWhiteSpace(item.Path) || Path.IsPathRooted(item.Path) ||
                item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal) || !inventory.TryAdd(item.Path, item))
            {
                errors.Add($"unsafe_or_duplicate_inventory:{item.Path}");
                continue;
            }
            var framePath = Path.Combine(batchRoot, item.Path.Replace('/', Path.DirectorySeparatorChar));
            if (item.Width != 1024 || item.Height != 1024 || !string.Equals(item.Mode, "RGBA", StringComparison.Ordinal) ||
                !File.Exists(framePath) || new FileInfo(framePath).Length != item.Bytes ||
                !string.Equals(Sha256(framePath), item.Sha256, StringComparison.OrdinalIgnoreCase))
                errors.Add($"frame_contract:{item.Path}");
        }
        if (inventory.Count != 26) errors.Add($"inventory_count:{inventory.Count}/26");

        var actions = manifest.Actions ?? Array.Empty<PatrolWalkCandidateActionManifest>();
        if (actions.Count != PatrolWalkV9CandidateBehaviorIds.All.Count) errors.Add("action_count_invalid");
        if (errors.Count > 0)
        {
            BootstrapLog.WriteRaw($"patrol_walk_v9_candidate_invalid batch={manifest.BatchId} errors={string.Join(',', errors)}");
            yield break;
        }

        foreach (var action in actions)
        {
            var actionErrors = new List<string>();
            if (!PatrolWalkV9CandidateBehaviorIds.All.Contains(action.BehaviorId)) actionErrors.Add("behavior_id_invalid");
            if (!action.OwnerPreviewApproved || !action.VisualApproved || action.RuntimeApproved || action.RuntimeUse ||
                action.ProductionAsset || action.PrototypeUse || !action.DeveloperPreview || action.AutonomousBindingEnabled ||
                !string.Equals(action.RuntimeValidation, "pending_windows_renderer_qa", StringComparison.Ordinal) ||
                action.AllowedSources is null || !action.AllowedSources.SequenceEqual(new[] { "DeveloperPreview" }, StringComparer.Ordinal))
                actionErrors.Add("action_gate_invalid");

            var phases = new List<MotionPhase>();
            foreach (var phase in action.Phases ?? Array.Empty<SleepCandidatePhaseManifest>())
            {
                var frames = new List<string>();
                var durations = new List<int>();
                foreach (var frame in phase.Frames ?? Array.Empty<CommandActionFrameManifest>())
                {
                    if (!inventory.TryGetValue(frame.Path, out var inventoryFrame) || frame.Bytes != inventoryFrame.Bytes ||
                        !string.Equals(frame.Sha256, inventoryFrame.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        actionErrors.Add($"frame_metadata_invalid:{frame.Path}");
                        continue;
                    }
                    frames.Add(Path.Combine(batchRoot, frame.Path.Replace('/', Path.DirectorySeparatorChar)));
                    durations.Add(frame.DurationMs.GetValueOrDefault(action.FrameDurationMs));
                }
                if (phase.Loop != (phase.Name == "loop") || frames.Count != phase.FrameCount || frames.Count == 0 || durations.Any(x => x <= 0))
                    actionErrors.Add($"phase_invalid:{phase.Name}");
                phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
            }
            if (action.Loop || !phases.Select(x => x.Name).SequenceEqual(new[] { "intro", "loop", "exit" }) ||
                !phases.Select(x => x.Frames.Count).SequenceEqual(new[] { 4, 8, 4 }) ||
                phases.Sum(x => x.Frames.Count) != action.FrameCount ||
                phases.Sum(x => x.DurationTotalMs(action.FrameDurationMs)) != action.TotalDurationMs)
                actionErrors.Add("action_timeline_invalid");
            if (actionErrors.Count > 0)
            {
                BootstrapLog.WriteRaw($"patrol_walk_v9_candidate_invalid behavior={action.BehaviorId} errors={string.Join(',', actionErrors)}");
                continue;
            }

            yield return new PlayableMotion(
                action.BehaviorId, action.DisplayName, "基础动作", action.Direction, action.FrameDurationMs,
                action.Interruptible, phases, batchRoot, RuntimeEnabled: false,
                Status: "候选：待 Windows 动画验收", MissingContent: "v8 remains the only automatic patrol route until v9 passes Windows QA.",
                StartPose: action.FromPose, EndPose: action.ToPose, StyleGroup: "wukong-autonomous-patrol-walk-v9-candidate",
                Disposition: "视觉已通过 · 未启用", PrototypeUse: false, AssetBatch: manifest.BatchId,
                Description: $"{action.Description} DeveloperPreview only; source={manifest.SourcePackage}; no v8 replacement yet.",
                CandidateProfile: manifest.CandidateProfile, VisualScale: ApprovedPetVisualScale,
                RenderScaleOverride: manifest.RuntimeRenderScale, VisualApproved: true, RuntimeApproved: false,
                AutonomousBindingEnabled: false, WindowMotionEnabled: true);
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
        var runtimeEnabled = manifest.RuntimeApproved && manifest.RuntimeUse && manifest.ProductionAsset &&
            manifest.AutonomousBindingEnabled && manifest.RuntimeValidation == "passed_windows_renderer_qa" &&
            manifest.WindowMotionValidation == "passed_windows_renderer_qa";
        var pendingPreview = !manifest.RuntimeApproved && !manifest.RuntimeUse && !manifest.ProductionAsset &&
            !manifest.AutonomousBindingEnabled && manifest.RuntimeValidation == "pending_windows_renderer_qa";
        if (!manifest.OwnerPreviewApproved || !manifest.VisualApproved || (!runtimeEnabled && !pendingPreview) ||
            manifest.PrototypeUse ||
            !manifest.DeveloperPreview ||
            !manifest.WindowMotionEnabled)
            batchErrors.Add("approved_gate_invalid");
        if (manifest.SourceFrameCount != 13 || manifest.RuntimeFrameCount != 13 || manifest.SequenceCount != 2)
            batchErrors.Add("candidate_inventory_contract_invalid");
        if (manifest.RuntimeRenderScale <= 0)
            batchErrors.Add("runtime_render_scale_invalid");
        if (manifest.AllowedSources is null ||
            manifest.AllowedSources.Count != 3 ||
            !manifest.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
            !manifest.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal) ||
            !manifest.AllowedSources.Contains("OwnerDialogue", StringComparer.Ordinal))
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

        if (inventory.Count != 13)
            batchErrors.Add($"inventory_count:{inventory.Count}/13");
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
                action.RuntimeApproved != manifest.RuntimeApproved ||
                action.RuntimeUse != manifest.RuntimeUse ||
                action.ProductionAsset != manifest.ProductionAsset ||
                action.PrototypeUse ||
                !action.DeveloperPreview ||
                action.AutonomousBindingEnabled != manifest.AutonomousBindingEnabled ||
                action.RuntimeValidation != manifest.RuntimeValidation)
                errors.Add("action_gate_invalid");
            if (action.AllowedSources is null ||
                action.AllowedSources.Count != 3 ||
                !action.AllowedSources.Contains("AutonomousTick", StringComparer.Ordinal) ||
                !action.AllowedSources.Contains("DeveloperPreview", StringComparer.Ordinal) ||
                !action.AllowedSources.Contains("OwnerDialogue", StringComparer.Ordinal))
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
                if (phase.Loop != (phase.Name == "loop") || frames.Count != phase.FrameCount || frames.Count == 0 || durations.Any(x => x <= 0))
                    errors.Add($"phase_invalid:{phase.Name}:{frames.Count}/{phase.FrameCount}");
                phases.Add(new MotionPhase(phase.Name, frames, phase.Loop, durations));
            }

            if (action.Loop || !phases.Select(x => x.Name).SequenceEqual(new[] { "intro", "loop", "exit" }) ||
                !phases.Select(x => x.Frames.Count).SequenceEqual(new[] { 4, 8, 4 }) ||
                phases.Sum(x => x.Frames.Count) != action.FrameCount)
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
                "基础动作",
                action.Direction,
                action.FrameDurationMs,
                action.Interruptible,
                phases,
                batchRoot,
                RuntimeEnabled: runtimeEnabled,
                Status: runtimeEnabled ? "走路与左右镜像已启用" : "视觉已通过，等待运行验证",
                MissingContent: "None",
                StartPose: action.FromPose,
                EndPose: action.ToPose,
                StyleGroup: "wukong-autonomous-patrol-walk-v8",
                Disposition: runtimeEnabled ? "已启用" : "视觉已通过 · 未启用",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: $"{action.Description} source={manifest.SourcePackage}; runtime_use={runtimeEnabled}; window_motion=true; window_motion_validation={manifest.WindowMotionValidation}.",
                CandidateProfile: manifest.CandidateProfile,
                VisualScale: ApprovedPetVisualScale,
                RenderScaleOverride: manifest.RuntimeRenderScale,
                VisualApproved: true,
                RuntimeApproved: runtimeEnabled,
                AutonomousBindingEnabled: runtimeEnabled,
                WindowMotionEnabled: true);
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
                loaded[introIndex] = intro with
                    {
                        Phases = new[]
                        {
                            introPhase,
                            proneLoop.Phases.Single(x => string.Equals(x.Name, "loop", StringComparison.OrdinalIgnoreCase)),
                            exitPhase
                        },
                        Description = $"{intro.Description} Runtime composition is intro -> legacy-side-prone loop -> exit; the rejected head-composite v5 extension has been removed."
                    };
            }

            foreach (var motion in loaded)
                yield return motion;
        }
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
            if (string.Equals(action.BehaviorId, MagicBehaviorIds.Scourgify, StringComparison.OrdinalIgnoreCase))
            {
                BootstrapLog.WriteRaw("magic_action_excluded behavior=wk.magic.scourgify reason=owner_removed_mock");
                continue;
            }

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

public static class FrontProneExpressionBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-PRONE-MICROEXPRESSIONS-v1";
    public const string SatisfiedSmile = "prone-satisfied-smile";
    public const string CuriousObserve = "prone-curious-observe";
    public const string KnowingLook = "prone-knowing-look";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SatisfiedSmile,
        CuriousObserve,
        KnowingLook
    };
}

public static class ProneHappyHotPantingBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-PRONE-HAPPY-HOT-PANTING-SEQUENCE-v6";
    public const string HappyHotPanting = "prone_happy_hot_panting";
}

public static class StandingHappyExpectantBehaviorIds
{
    public const string AssetBatch = "WK-STANDING-HAPPY-EXPECTANT-PRODUCTION-v1";
    public const string HappyExpectant = "wk.expression.stand_happy_expectant";
}

public static class SleepCandidateBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-SLEEP-CONTINUITY-PRODUCTION-v11";
    public const string MainLifecycle = "wk.candidate.sleep.main_lifecycle_v2";
    public const string ProneToSideRoll = "wk.candidate.sleep.prone_to_side_roll_v2";
    public const string SprawledFrontBreath = "wk.candidate.sleep.sprawled_front_breath_v2";
    public const string SprawledLeftSideBreath = "wk.candidate.sleep.sprawled_left_side_breath_v2";
    public const string SprawledRightSideBreath = "wk.candidate.sleep.sprawled_right_side_breath_v2";
    public const string CompactProneBreath = "wk.candidate.sleep.compact_prone_breath_v2";
    public const string CurledSideBreath = "wk.candidate.sleep.curled_side_breath_v2";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        MainLifecycle,
        ProneToSideRoll,
        SprawledFrontBreath,
        SprawledLeftSideBreath,
        SprawledRightSideBreath,
        CompactProneBreath,
        CurledSideBreath
    };

    public static readonly IReadOnlySet<string> RuntimeApproved = new HashSet<string>(All, StringComparer.Ordinal);
    public static readonly IReadOnlySet<string> AutonomousAllowed = new HashSet<string>(StringComparer.Ordinal)
    {
        MainLifecycle,
        SprawledFrontBreath
    };

}

public static class FoodWaterCandidateBehaviorIds
{
    public const string AssetBatch = "WK-INTERACTION-FOOD-WATER-COAT-SEAM-CANDIDATE-v5";
    public const string DrinkWaterStandingV5 = "wk.interaction.drink_water";
    public const string EatKibbleStandingV5 = "wk.interaction.eat_kibble";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        DrinkWaterStandingV5,
        EatKibbleStandingV5
    };
}

public static class PatrolWalkCandidateBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-PATROL-WALK-v8";
    public const string WalkLeft = "wk.candidate.autonomous.patrol_walk_left_v1";
    public const string WalkRight = "wk.candidate.autonomous.patrol_walk_right_v1";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        WalkLeft,
        WalkRight
    };
}

public static class PatrolWalkV9CandidateBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-PATROL-WALK-v9";
    public const string WalkLeft = "wk.candidate.autonomous.patrol_walk_left_v9";
    public const string WalkRight = "wk.candidate.autonomous.patrol_walk_right_v9";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { WalkLeft, WalkRight };
}

public static class WakeRiseCandidateBehaviorIds
{
    public const string AssetBatch = "WK-AUTONOMOUS-WAKE-RISE-CANDIDATE-v1";
    public const string SideWake = "wk.candidate.autonomous.side_sleep_wake_v1";
    public const string SideInterrupt = "wk.candidate.autonomous.side_sleep_interrupt_exit_v1";
    public const string LowInterrupt = "wk.candidate.autonomous.low_prone_interrupt_exit_v1";
    public const string FrontWake = "wk.candidate.autonomous.front_prone_wake_v1";
    public const string FrontRiseSit = "wk.candidate.autonomous.front_prone_to_sit_v1";
    public const string FrontSitStand = "wk.candidate.autonomous.front_sit_to_stand_v1";
    public const string FrontRiseFull = "wk.candidate.autonomous.front_prone_rise_v1";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SideWake, SideInterrupt, LowInterrupt, FrontWake, FrontRiseSit, FrontSitStand, FrontRiseFull
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
            PetrificusRelease
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
    [property: JsonPropertyName("phases")] IReadOnlyList<LifecycleCandidatePhaseManifest> Phases,
    [property: JsonPropertyName("runtime_render_scale")] double? RuntimeRenderScale = null);

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
    [property: JsonPropertyName("runtime_render_scale")] double RuntimeRenderScale,
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
    [property: JsonPropertyName("runtime_render_scale")] double RuntimeRenderScale,
    [property: JsonPropertyName("deprecated")] bool Deprecated,
    [property: JsonPropertyName("deprecated_reason")] string? DeprecatedReason,
    [property: JsonPropertyName("phases")] IReadOnlyList<SleepCandidatePhaseManifest> Phases);

public sealed record SleepCandidatePhaseManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("loop")] bool Loop,
    [property: JsonPropertyName("frame_count")] int FrameCount,
    [property: JsonPropertyName("frames")] IReadOnlyList<CommandActionFrameManifest> Frames);

public sealed record FoodWaterCandidateBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("candidate_profile")] string CandidateProfile,
    [property: JsonPropertyName("source_frame_count")] int SourceFrameCount,
    [property: JsonPropertyName("runtime_frame_count")] int RuntimeFrameCount,
    [property: JsonPropertyName("sequence_frame_reference_count")] int SequenceFrameReferenceCount,
    [property: JsonPropertyName("referenced_unique_frame_count")] int ReferencedUniqueFrameCount,
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
    [property: JsonPropertyName("normal_runtime_available")] bool NormalRuntimeAvailable,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("frame_inventory")] IReadOnlyList<ProneHeadCandidateInventoryFrame> FrameInventory,
    [property: JsonPropertyName("actions")] IReadOnlyList<FoodWaterCandidateActionManifest> Actions);

public sealed record FoodWaterCandidateActionManifest(
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
    [property: JsonPropertyName("owner_preview_approved")] bool OwnerPreviewApproved,
    [property: JsonPropertyName("visual_approved")] bool VisualApproved,
    [property: JsonPropertyName("runtime_validation")] string RuntimeValidation,
    [property: JsonPropertyName("runtime_approved")] bool RuntimeApproved,
    [property: JsonPropertyName("runtime_use")] bool RuntimeUse,
    [property: JsonPropertyName("production_asset")] bool ProductionAsset,
    [property: JsonPropertyName("prototype_use")] bool PrototypeUse,
    [property: JsonPropertyName("developer_preview")] bool DeveloperPreview,
    [property: JsonPropertyName("autonomous_binding_enabled")] bool AutonomousBindingEnabled,
    [property: JsonPropertyName("normal_runtime_available")] bool NormalRuntimeAvailable,
    [property: JsonPropertyName("allowed_sources")] IReadOnlyList<string> AllowedSources,
    [property: JsonPropertyName("phases")] IReadOnlyList<SleepCandidatePhaseManifest> Phases);

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
    [property: JsonPropertyName("window_motion_validation")] string WindowMotionValidation,
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
    long RequestedAtTimestamp = 0,
    bool MirrorHorizontally = false,
    Guid RequestId = default,
    Guid CorrelationId = default,
    bool TracksAgentLifecycle = false);

/// <summary>
/// Session-only diagnostic projection. It describes the active runtime contract and
/// never participates in behavior selection or persistence.
/// </summary>
public sealed record BehaviorMechanismSnapshot(
    string DisplayName,
    string BehaviorId,
    string CategoryKey,
    string CategoryName,
    string CategoryAccent,
    string TriggerMechanism,
    string WeightAndFrequency,
    string GateSummary,
    int SessionTriggerCount,
    string AverageInterval,
    string LastTriggered,
    string TriggerSources);

/// <summary>
/// Session-only group statistics for the developer diagnostics panel. These are
/// deliberately descriptive: the values never feed decision scoring or memory.
/// </summary>
public sealed record BehaviorMechanismCategorySnapshot(
    string Key,
    string Name,
    string Accent,
    int AvailableMotionCount,
    int SessionTriggerCount,
    string SessionShare,
    double BarWidth,
    string AverageInterval,
    string TriggerSources);

public sealed record BehaviorMechanismTrendBucket(
    string Label,
    int TriggerCount,
    double BarHeight,
    string Tooltip);

public sealed record BehaviorMechanismDashboardSnapshot(
    int TotalTriggerCount,
    int AutonomousTriggerCount,
    int OwnerTriggerCount,
    int DistinctActionCount,
    string Summary,
    IReadOnlyList<BehaviorMechanismCategorySnapshot> Categories,
    IReadOnlyList<BehaviorMechanismTrendBucket> TrendBuckets,
    PointCollection TrendLinePoints);

public sealed class DesktopRuntimeHost : INotifyPropertyChanged
{
    private sealed record MechanismCategoryDescriptor(string Key, string Name, string Accent, int Order);
    private sealed record BehaviorTriggerEvent(
        DateTimeOffset Timestamp,
        string BehaviorId,
        string CategoryKey,
        BehaviorRequestSource Source);

    private sealed record ActiveBehaviorExecution(
        BehaviorRequest Request,
        BehaviorOutcomeProfile Profile,
        TimeSpan EstimatedDuration);

    private sealed class BehaviorTriggerAggregate
    {
        private readonly Dictionary<BehaviorRequestSource, int> _sources = new();
        private DateTimeOffset? _lastTriggeredAt;
        private TimeSpan _intervalTotal;
        private int _intervalCount;

        public int Count { get; private set; }
        public DateTimeOffset? LastTriggeredAt => _lastTriggeredAt;

        public void Record(DateTimeOffset timestamp, BehaviorRequestSource source)
        {
            if (_lastTriggeredAt is { } previous && timestamp >= previous)
            {
                _intervalTotal += timestamp - previous;
                _intervalCount++;
            }

            Count++;
            _lastTriggeredAt = timestamp;
            _sources[source] = _sources.GetValueOrDefault(source) + 1;
        }

        public string AverageInterval => _intervalCount == 0
            ? "尚无重复触发"
            : $"{(_intervalTotal.TotalSeconds / _intervalCount):0.#} 秒";

        public string SourceSummary => _sources.Count == 0
            ? "--"
            : string.Join(" / ", _sources
                .OrderBy(item => item.Key.ToString(), StringComparer.Ordinal)
                .Select(item => $"{DisplaySource(item.Key)} {item.Value}"));
    }

    private const string StableHoldPrefix = "wk.runtime.posture_hold.";
    private const int BehaviorStatisticsEventLimit = 256;
    private const int BehaviorTrendBucketCount = 12;
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
            LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1,
            LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
            LifecycleReviewCandidateBehaviorIds.FrontProneLickV4,
            AutonomousDailyCandidateBehaviorIds.StandToSit,
            AutonomousDailyCandidateBehaviorIds.SitToProne,
            AutonomousDailyCandidateBehaviorIds.ProneToSit,
            AutonomousDailyCandidateBehaviorIds.SitToStand,
            ProneHeadCandidateBehaviorIds.HeadLowerTurnV4,
            FrontProneExpressionBehaviorIds.SatisfiedSmile,
            FrontProneExpressionBehaviorIds.CuriousObserve,
            FrontProneExpressionBehaviorIds.KnowingLook,
            ProneHappyHotPantingBehaviorIds.HappyHotPanting,
             StandingHappyExpectantBehaviorIds.HappyExpectant,
            SleepCandidateBehaviorIds.MainLifecycle,
            SleepCandidateBehaviorIds.SprawledFrontBreath,
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
    private readonly BehaviorDecisionEngine _agentDecisionEngine = new();
    private readonly PetStateReducer _stateReducer = new();
    private readonly BehaviorParticipationPolicy _participationPolicy = new();
    private readonly PetEpisodePolicy _episodePolicy = new();
    private readonly DialogueStateProjector _dialogueStateProjector = new();
    private readonly OwnerIntentNormalizer _ownerIntentNormalizer = new();
    private readonly DialogueStateClaimValidator _dialogueClaimValidator = new();
    private readonly BehaviorCapabilityCatalog _behaviorCapabilities;
    private readonly InteractionDecisionService _interactionDecisions = new();
    private readonly InitiativeSpeechDecisionService _initiativeSpeechDecisions = new();
    private readonly AutonomousAgentRolloutOptions _rolloutOptions;
    private AutonomousBehaviorPreferences _autonomousPreferences = AutonomousBehaviorPreferences.Default;
    private readonly Dictionary<string, DateTimeOffset> _lastAccepted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BehaviorTriggerAggregate> _behaviorTriggerStatistics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<BehaviorTriggerEvent> _behaviorTriggerEvents = new();
    private readonly RollingFileLogStore _logs = RollingFileLogStore.CreateDefault();
    private readonly object _agentStatePersistenceLock = new();
    private IPetAgentStateStore? _agentStateStore;
    private IPetDecisionMemorySource? _decisionMemorySource;
    private readonly SemaphoreSlim _decisionMemoryRefreshGate = new(1, 1);
    private Task _agentStatePersistence = Task.CompletedTask;
    private PetAgentState _petAgentState = PetAgentState.CreateDefault(DateTimeOffset.UnixEpoch);
    private PetRuntimeState _agentState
    {
        get => _petAgentState.Runtime;
        set => _petAgentState = (_petAgentState with { Runtime = value.Clamp() }).Clamp();
    }
    private RelationshipState _relationshipState
    {
        get => _petAgentState.Relationship;
        set => _petAgentState = (_petAgentState with { Relationship = value }).Clamp();
    }
    private TemperamentProfile _temperament
    {
        get => _petAgentState.Temperament;
        set => _petAgentState = (_petAgentState with { Temperament = value }).Clamp();
    }
    private PetDecision? _lastAgentDecision;
    private PetDecision? _pendingAgentDecision;
    private BehaviorAgentDecision? _lastShadowDecision;
    private string _lastShadowComparison = "not_evaluated";
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
    private DateTimeOffset _nextFrontProneExpressionAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextStandingHappyExpectantAt = DateTimeOffset.MinValue;
    private BehaviorRequestSource _coinPreviewSource = BehaviorRequestSource.OwnerContextMenu;
    private bool _frontProneProfileActive;
    private bool _patrolCanMoveLeft;
    private bool _patrolCanMoveRight;
    private bool _petFacesRight;
    private readonly Queue<string> _recentFrontProneExpressions = new();
    private readonly Queue<string> _pendingFoodWaterSequence = new();
    private BehaviorRequestSource _pendingFoodWaterSource = BehaviorRequestSource.OwnerContextMenu;
    private string _pendingFoodWaterTarget = string.Empty;
    private readonly Queue<string> _pendingDialogueEpisodeSequence = new();
    private string _pendingDialogueEpisodeTarget = string.Empty;
    private ActiveBehaviorExecution? _activeReducerExecution;
    private PetAgentState? _previewAgentStateSnapshot;
    private BehaviorRequest? _lastBehaviorRequest;

    public DesktopRuntimeHost(
        PetrifiedCoinOptions? coinOptions = null,
        Func<DateTimeOffset>? now = null,
        AutonomousAgentRolloutOptions? rolloutOptions = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
        _rolloutOptions = rolloutOptions ?? AutonomousAgentRolloutOptions.ContinuityV1;
        _catalog = DesktopMotionCatalog.Load(AppContext.BaseDirectory);
        _behaviorCapabilities = DesktopBehaviorCapabilityCatalog.Create(_catalog.Motions);
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
        _petAgentState = PetAgentState.CreateDefault(_currentStartedAt) with
        {
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePostureFromPose(_catalog.RequiredIdle.EndPose, StablePosture.Prone),
                CurrentPoseId = _catalog.RequiredIdle.EndPose,
                LastActionId = _catalog.RequiredIdle.BehaviorId,
                CurrentPhase = "idle"
            }
        };
        _nextAutonomousDecisionAt = _currentStartedAt + ChooseAutonomousIdleDelay(_agentState.CurrentPosture, _random);
        Trace("asset_catalog_loaded", $"{_catalog.LoadSummary}; motions={_catalog.Motions.Count}; capabilities={_behaviorCapabilities.Capabilities.Count}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PetMotionRequest>? MotionRequested;
    public event EventHandler<int>? PetPixelSizeRequested;
    public event EventHandler<double>? PetScaleRequested;

    public ObservableCollection<string> TraceLines { get; } = new();
    public bool PetFacesRight => _petFacesRight;
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
    public IReadOnlyList<PlayableMotion> StandingExpressionCandidateMotions => _catalog.Motions
        .Where(x => string.Equals(x.AssetBatch, StandingHappyExpectantBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
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
            string.Equals(x.AssetBatch, FrontProneExpressionBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.AssetBatch, ProneHappyHotPantingBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.AssetBatch, WakeRiseCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.BehaviorId)
        .ToArray();
    public IReadOnlyList<PlayableMotion> FoodWaterCandidateMotions => _catalog.Motions
        .Where(x => string.Equals(x.AssetBatch, FoodWaterCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
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
    public bool EnableBehaviorAgentShadow => _rolloutOptions.ShadowEnabled;
    public IReadOnlySet<PetEpisodeKind> AuthoritativeAgentEpisodes => _rolloutOptions.AuthoritativeEpisodes;
    public StablePosture CurrentStablePosture => _agentState.CurrentPosture;
    public PetAgentState AgentStateSnapshot => _petAgentState.Clamp();
    public BehaviorAgentDecision? LastShadowDecision => _lastShadowDecision;
    public string BehaviorAgentSnapshot => BuildBehaviorAgentSnapshot();
    public AutonomousBehaviorPreferences AutonomousPreferences => _autonomousPreferences;
    public string BroomFlightMetrics { get; private set; } = "Not measured";

    public string CurrentAction { get; private set; } = "安静趴卧";
    public string CurrentBehaviorId { get; private set; } = Phase15BehaviorIds.ProneIdle;
    public string CurrentPhase { get; private set; } = "loop";
    public string CurrentAsset { get; private set; } = string.Empty;
    public string CurrentDisposition { get; private set; } = "愿意";
    public string CurrentReason { get; private set; } = "启动后进入安静趴卧";
    public string CurrentDecisionDetail { get; private set; } = "等待下一次互动";
    public string LastSource { get; private set; } = "启动";
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
    public double Thirst => _agentState.Thirst;
    public int TemperamentActivity => _temperament.Activity;
    public int TemperamentAttachment => _temperament.Attachment;
    public int TemperamentSensitivity => _temperament.Sensitivity;
    public int TemperamentIndependence => _temperament.Independence;
    public int TemperamentMischief => _temperament.Mischief;
    public double RelationshipTrust => _relationshipState.Trust01;
    public double RelationshipFamiliarity => _relationshipState.Familiarity01;
    public double RelationshipTouchAcceptance => _relationshipState.TouchAcceptance01;
    public double RelationshipInitiativeAcceptance => _relationshipState.InitiativeAcceptance01;
    public int RecentPositiveInteractions => _relationshipState.RecentPositiveInteractions;
    public int RecentNegativeInteractions => _relationshipState.RecentNegativeInteractions;
    public string AgentMoodProjection => $"{_agentState.CurrentPosture} · 心情 {_agentState.MoodValence:P0} · 唤醒度 {_agentState.Arousal:P0}";

    public IReadOnlyList<BehaviorMechanismSnapshot> BehaviorMechanisms => _catalog.Motions
        .Where(motion => !motion.IsExpired &&
                         (motion.RuntimeEnabled || motion.PrototypeUse) &&
                         !motion.BehaviorId.StartsWith(StableHoldPrefix, StringComparison.OrdinalIgnoreCase))
        .OrderBy(motion => MotionDisplayNameCatalog.Resolve(motion.BehaviorId, motion.DisplayName), StringComparer.CurrentCulture)
        .Select(BuildBehaviorMechanismSnapshot)
        .ToArray();

    public BehaviorMechanismDashboardSnapshot BehaviorMechanismDashboard => BuildBehaviorMechanismDashboard();

    public async Task<bool> AttachAgentStateStoreAsync(
        IPetAgentStateStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        _agentStateStore = store;
        try
        {
            var loaded = await store.LoadAsync(cancellationToken);
            if (loaded is null)
                return false;
            var now = _now();
            var recoveredRuntime = EnsurePoseMatchesPosture(loaded.Runtime with
            {
                ActiveExecutionId = null,
                ActiveActionId = null,
                ActiveActionStartedAt = null,
                CurrentPhase = "idle",
                IsBusy = false,
                IsInterruptible = true
            });
            var recovered = loaded with { Runtime = recoveredRuntime };
            recovered = _stateReducer.Reduce(recovered, new PetTimeAdvanced(now));
            _petAgentState = (recovered with
            {
                Episode = BehaviorEpisodeCatalog.Start(PetEpisodeKind.Resting, now, "process_restart"),
                Clock = recovered.Clock with { LastUpdatedAt = now }
            }).Clamp();
            _frontProneProfileActive = string.Equals(
                PetPoseCompatibility.FamilyFor(_agentState.CurrentPoseId, _agentState.CurrentPosture),
                "prone.front",
                StringComparison.OrdinalIgnoreCase);
            Trace("agent_state_loaded", $"schema={_petAgentState.SchemaVersion} experiences={_petAgentState.RecentExperience.Count} preferences={_petAgentState.Preferences.Count}");
            return true;
        }
        catch (Exception ex)
        {
            Trace("agent_state_load_failed", ex.GetType().Name);
            return false;
        }
    }

    public void AttachDecisionMemorySource(IPetDecisionMemorySource source)
    {
        _decisionMemorySource = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task RefreshDecisionMemoryAsync(string reason = "manual_refresh", CancellationToken cancellationToken = default)
    {
        var source = _decisionMemorySource;
        if (source is null)
            return;
        await _decisionMemoryRefreshGate.WaitAsync(cancellationToken);
        try
        {
            var profile = await source.LoadAsync(cancellationToken);
            _petAgentState = (_petAgentState with { DecisionMemory = profile }).Clamp();
            QueueAgentStatePersistence($"decision_memory:{reason}");
            Trace("decision_memory_refreshed",
                $"reason={reason} fingerprint={profile.Fingerprint[..Math.Min(12, profile.Fingerprint.Length)]} conversation={profile.EvidenceCounts.GetValueOrDefault("confirmed_conversation")} album={profile.EvidenceCounts.GetValueOrDefault("album_description")}");
            OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Trace("decision_memory_refresh_failed", $"reason={reason} type={ex.GetType().Name}");
        }
        finally
        {
            _decisionMemoryRefreshGate.Release();
        }
    }

    public Task FlushAgentStateAsync()
    {
        lock (_agentStatePersistenceLock)
            return _agentStatePersistence;
    }

    public PetAgentDialogueProjection BuildDialogueProjection()
    {
        var active = IsStableIdleBehavior(_currentBehaviorId) ? null : _currentBehaviorId;
        var projectionState = _petAgentState with
        {
            Runtime = _agentState with
            {
                ActiveActionId = active,
                CurrentPhase = CurrentPhase,
                IsBusy = active is not null,
                IsInterruptible = _currentInterruptible,
                ActiveActionStartedAt = active is null ? null : _currentStartedAt
            }
        };
        return _dialogueStateProjector.Project(projectionState);
    }

    public void SetBehaviorAgentMockEnabled(bool enabled)
    {
        EnableBehaviorAgentMock = enabled;
        Trace("behavior_agent_mock", enabled ? "enabled" : "disabled");
        OnPropertyChanged(nameof(EnableBehaviorAgentMock));
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
    }

    public void UpdateBehaviorAgentMock(TemperamentProfile temperament, PetRuntimeState state, RelationshipState relationship, int seed)
    {
        var now = _now();
        var normalizedState = EnsurePoseMatchesPosture(state.Clamp());
        _petAgentState = (_petAgentState with
        {
            Temperament = temperament.Clamp(),
            Runtime = normalizedState,
            Relationship = relationship,
            Clock = _petAgentState.Clock with { LastUpdatedAt = now }
        }).Clamp();
        _frontProneProfileActive = string.Equals(
            PetPoseCompatibility.FamilyFor(_agentState.CurrentPoseId, _agentState.CurrentPosture),
            "prone.front",
            StringComparison.OrdinalIgnoreCase);
        _decisionSeed = seed;
        _autonomousDecisionCount = 0;
        Trace("behavior_agent_state", $"seed={seed} posture={_agentState.CurrentPosture} energy={_agentState.Energy:0.00} hunger={_agentState.Hunger:0.00} social={_agentState.SocialNeed:0.00} boredom={_agentState.Boredom:0.00} stress={_agentState.Stress:0.00}");
        OnPropertyChanged(nameof(CurrentStablePosture));
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        RaiseAgentProfileProjection();
    }

    private static PetRuntimeState EnsurePoseMatchesPosture(PetRuntimeState state)
    {
        var family = PetPoseCompatibility.FamilyFor(state.CurrentPoseId, state.CurrentPosture);
        var compatible = state.CurrentPosture switch
        {
            StablePosture.Stand => family == "stand",
            StablePosture.Sit => family == "sit",
            _ => family.StartsWith("prone", StringComparison.Ordinal) || family == "sleep"
        };
        return compatible ? state : state with { CurrentPoseId = PetRuntimeState.DefaultPoseFor(state.CurrentPosture) };
    }

    public void UpdateTemperament(TemperamentProfile temperament)
    {
        _temperament = temperament.Clamp();
        QueueAgentStatePersistence("temperament_updated");
        Trace("temperament_updated", $"activity={_temperament.Activity} attachment={_temperament.Attachment} sensitivity={_temperament.Sensitivity} independence={_temperament.Independence} mischief={_temperament.Mischief}");
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        RaiseAgentProfileProjection();
    }

    public void UpdateAutonomousBehaviorPreferences(AutonomousBehaviorPreferences preferences)
    {
        _autonomousPreferences = preferences.Clamp();
        Trace("autonomous_preferences_updated",
            $"walking={_autonomousPreferences.WalkingWeight:0.00} prone={_autonomousPreferences.ProneRestWeight:0.00} sleeping={_autonomousPreferences.SleepingWeight:0.00} standing={_autonomousPreferences.StandingIdleWeight:0.00}");
        OnPropertyChanged(nameof(AutonomousPreferences));
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
    }

    public PetActionResult StartIdle(string source = "Startup")
    {
        var posture = string.Equals(source, "Startup", StringComparison.OrdinalIgnoreCase)
            ? PreferredStartupPosture()
            : _agentState.CurrentPosture;
        // Initial placement has no visible predecessor to bridge from.
        if (source == "Startup" && posture == StablePosture.Prone &&
            _catalog.Find(LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4) is { RuntimeEnabled: true })
            _frontProneProfileActive = true;
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
        ReduceAgentState(new PetOwnerInteractionObserved(
            now,
            decision.EffectiveGesture.ToString(),
            Math.Max(1, _tapBurst),
            decision.Disposition == PetActionResult.Accepted), "owner_interaction");
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
            _agentState.LastInitiativeSpeechAt,
            IsStableIdleBehavior(_currentBehaviorId),
            IsPetrified,
            isChatExpanded,
            now.Hour is >= 23 or < 7,
            _decisionSeed + _autonomousDecisionCount + (int)(now.Ticks % int.MaxValue))
        {
            Episode = _petAgentState.Episode.Kind,
            RecentExperience = _petAgentState.RecentExperience,
            DecisionMemory = _petAgentState.DecisionMemory,
            Feedback = _petAgentState.InitiativeSpeechFeedback
        });
        var scores = string.Join(",", decision.Candidates
            .OrderByDescending(candidate => candidate.Score)
            .Take(3)
            .Select(candidate => $"{candidate.Topic}:{candidate.Score:0.00}"));
        Trace("initiative_speech_decision", $"speak={decision.ShouldSpeak} topic={decision.Topic} reason={decision.ReasonCode} episode={_petAgentState.Episode.Kind} next_seconds={decision.NextCheck.TotalSeconds:0} scores=[{scores}]");
        return decision;
    }

    public void RecordInitiativeSpeech(InitiativeSpeechTopic topic, string generationReason = "local_rule")
    {
        var spokenAt = _now();
        ReduceAgentState(new PetInitiativeSpeechOccurred(spokenAt, topic.ToString()), "initiative_speech");
        RaiseMetrics();
        Trace("initiative_speech_shown", $"topic={topic} generation={generationReason}");
    }

    public void RecordOwnerDialogueResponse(bool positive = true)
    {
        ReduceAgentState(new PetOwnerDialogueObserved(_now(), positive), "owner_dialogue_response");
        RaiseMetrics();
        Trace("owner_dialogue_response", $"positive={positive} pending={_petAgentState.InitiativeSpeechFeedback.PendingTopic ?? "none"}");
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

    public Task<PetActionResult> SubmitExpressionIntentAsync(
        SemanticIntent intent,
        BehaviorRequestSource source = BehaviorRequestSource.OwnerDialogue)
    {
        if (intent.Kind != SemanticIntentKind.PositiveExpression ||
            !string.Equals(intent.CanonicalBehaviorId, StandingHappyExpectantBehaviorIds.HappyExpectant, StringComparison.OrdinalIgnoreCase))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "expression_intent_unresolved", "当前没有可执行的表情意图");
            return Task.FromResult(PetActionResult.Deferred);
        }

        if (!IsStandingHappyExpectantProfileAllowed(_agentState.CurrentPosture, _agentState.CurrentPoseId, _agentState.IsBusy))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "expression_pose_or_busy_mismatch", "当前姿态不适合这个表情");
            return Task.FromResult(PetActionResult.Deferred);
        }

        if (_agentState.Stress >= 0.72 || _agentState.Energy < 0.24)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "expression_state_mismatch", "悟空现在更想安静休息");
            return Task.FromResult(PetActionResult.Deferred);
        }

        return Task.FromResult(SubmitBehavior(
            source,
            StandingHappyExpectantBehaviorIds.HappyExpectant,
            "semantic:positive_expression:happy_expectant",
            priority: 4));
    }

    public Task<PetActionResult> SubmitOwnerCommandAsync(
        string command,
        BehaviorRequestSource source = BehaviorRequestSource.OwnerContextMenu)
    {
        var ownerCommand = ParseOwnerCommand(command);
        if (ownerCommand != OwnerCommandKind.None && CommandMotionMockMotions.Count > 0)
            return Task.FromResult(SubmitBehaviorAgentCommand(ownerCommand, source, $"owner_command:{command}"));
        if (ownerCommand != OwnerCommandKind.None)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "command_candidate_assets_missing", "Command candidate assets are unavailable; formal command assets are not runtime-approved.");
            return Task.FromResult(PetActionResult.Deferred);
        }

        var behaviorId = ResolveOwnerCommandBehavior(command);
        if (command is "停下" or "停")
            return StopAsync("owner_command:stop");
        return Task.FromResult(SubmitBehavior(source, behaviorId, $"owner_command:{command}", priority: 8));
    }

    public async Task<DesktopDialogueBehaviorResult> SubmitDialogueIntentAsync(string text)
    {
        var intent = _ownerIntentNormalizer.Normalize(text);
        if (!intent.IsBehaviorIntent)
            return new DesktopDialogueBehaviorResult(false, intent, PetActionResult.Deferred, null, null);

        var before = _lastBehaviorRequest?.RequestId;
        var result = intent.Kind switch
        {
            NormalizedOwnerIntentKind.Stop => await StopAsync("dialogue:stop"),
            NormalizedOwnerIntentKind.Sit => await SubmitOwnerCommandAsync("坐", BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Down => await SubmitOwnerCommandAsync("卧", BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Paw => await SubmitOwnerCommandAsync("手", BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Jump => await SubmitOwnerCommandAsync("跳", BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Spin => await SubmitOwnerCommandAsync("转圈", BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Eat => await SubmitFoodWaterAsync(FoodWaterCandidateBehaviorIds.EatKibbleStandingV5, BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Drink => await SubmitFoodWaterAsync(FoodWaterCandidateBehaviorIds.DrinkWaterStandingV5, BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Sleep => SubmitDialogueSleep(),
            NormalizedOwnerIntentKind.Walk => SubmitDialogueWalk(),
            NormalizedOwnerIntentKind.CarRide => await SubmitCarRideAsync(BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.BroomFlight => await SubmitMagicAsync(MagicBehaviorIds.AccioBroom, BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Apparate => await SubmitMagicAsync(MagicBehaviorIds.Apparate, BehaviorRequestSource.OwnerDialogue),
            NormalizedOwnerIntentKind.Petrify => await SubmitMagicAsync(MagicBehaviorIds.PetrificusTotalus, BehaviorRequestSource.OwnerDialogue),
            _ => PetActionResult.Deferred
        };
        var requestId = _lastBehaviorRequest?.RequestId != before ? _lastBehaviorRequest?.RequestId : null;
        var act = BuildBehaviorDialogueAct(intent, result, requestId);
        Trace("dialogue_behavior_fact", $"intent={intent.IntentId} result={result} request={requestId?.ToString() ?? "none"} action={_agentState.ActiveActionId ?? _currentBehaviorId} episode={_petAgentState.Episode.Kind}");
        return new DesktopDialogueBehaviorResult(true, intent, result, act, requestId);
    }

    public DialogueValidationResult ValidateDialogueReply(string text) =>
        _dialogueClaimValidator.Validate(
            _dialogueClaimValidator.InferClaims(text),
            BuildDialogueTruthSnapshot());

    public DialogueTruthSnapshot BuildDialogueTruthSnapshot() => new(
        _petAgentState.Episode.Kind,
        _agentState.CurrentPosture,
        _agentState.ActiveActionId ?? _currentBehaviorId,
        _agentState.ActiveExecutionId ?? (!IsStableIdleBehavior(_currentBehaviorId) ? _lastBehaviorRequest?.RequestId : null),
        _agentState.IsBusy,
        _petAgentState.RecentExperience);

    private PetActionResult SubmitDialogueSleep()
    {
        if (_agentState.IsBusy)
        {
            UpdateDecision(PetActionResult.Deferred, BehaviorRequestSource.OwnerDialogue.ToString(), "sleep_wait_for_current_action", "我先把现在的动作做完，再去睡觉");
            return PetActionResult.Deferred;
        }

        var staysInFrontProneProfile = _agentState.CurrentPosture == StablePosture.Prone && _frontProneProfileActive;
        var behaviorId = staysInFrontProneProfile
            ? SleepCandidateBehaviorIds.SprawledFrontBreath
            : SleepCandidateBehaviorIds.MainLifecycle;
        var steps = _agentState.CurrentPosture switch
        {
            StablePosture.Stand => new[]
            {
                AutonomousDailyCandidateBehaviorIds.StandToSit,
                AutonomousDailyCandidateBehaviorIds.SitToProne,
                behaviorId
            },
            StablePosture.Sit => new[]
            {
                AutonomousDailyCandidateBehaviorIds.SitToProne,
                behaviorId
            },
            _ => new[] { behaviorId }
        };
        return StartDialogueEpisodeSequence("sleep", behaviorId, steps, "我先慢慢趴好，再睡觉");
    }

    private PetActionResult SubmitDialogueWalk()
    {
        if (_agentState.IsBusy)
        {
            UpdateDecision(PetActionResult.Deferred, BehaviorRequestSource.OwnerDialogue.ToString(), "walk_wait_for_current_action", "我先把现在的动作做完，再去走走");
            return PetActionResult.Deferred;
        }
        if (!_patrolCanMoveLeft && !_patrolCanMoveRight)
        {
            UpdateDecision(PetActionResult.Deferred, BehaviorRequestSource.OwnerDialogue.ToString(), "walk_workspace_unavailable", "这里没有足够空间让我走走");
            return PetActionResult.Deferred;
        }

        var behaviorId = _patrolCanMoveRight && (!_patrolCanMoveLeft || _petFacesRight)
            ? PatrolWalkCandidateBehaviorIds.WalkRight
            : PatrolWalkCandidateBehaviorIds.WalkLeft;
        var steps = _agentState.CurrentPosture switch
        {
            StablePosture.Prone => new[]
            {
                AutonomousDailyCandidateBehaviorIds.ProneToSit,
                AutonomousDailyCandidateBehaviorIds.SitToStand,
                behaviorId
            },
            StablePosture.Sit => new[]
            {
                AutonomousDailyCandidateBehaviorIds.SitToStand,
                behaviorId
            },
            _ => new[] { behaviorId }
        };
        return StartDialogueEpisodeSequence("walk", behaviorId, steps, "我先自然站起来，再去走走");
    }

    private DialogueAct BuildBehaviorDialogueAct(NormalizedOwnerIntent intent, PetActionResult result, Guid? requestId)
    {
        var (type, text, temporal) = result switch
        {
            PetActionResult.Accepted => (DialogueActType.BehaviorAccepted, AcceptedText(intent.Kind), DialogueTemporalClaim.Preparing),
            PetActionResult.Rejected => (DialogueActType.BehaviorRejected, "我现在不太想呀。", DialogueTemporalClaim.Desired),
            PetActionResult.Deferred => (DialogueActType.BehaviorDeferred, "等一下再做呀。", DialogueTemporalClaim.Desired),
            PetActionResult.Interrupted => (DialogueActType.BehaviorInterrupted, "我停下来啦。", DialogueTemporalClaim.Completed),
            _ => (DialogueActType.BehaviorFailed, "刚才没成功呀。", DialogueTemporalClaim.Desired)
        };
        var claim = ClaimFor(intent.Kind);
        var claims = claim is null ? Array.Empty<DialogueClaim>() : new[] { new DialogueClaim(claim.Value, temporal) };
        var act = new DialogueAct(type, text, requestId, claims);
        var validated = _dialogueClaimValidator.Validate(act, BuildDialogueTruthSnapshot());
        return validated.IsValid ? act : act with { Text = validated.Text, Claims = Array.Empty<DialogueClaim>() };
    }

    private static string AcceptedText(NormalizedOwnerIntentKind kind) => kind switch
    {
        NormalizedOwnerIntentKind.Sleep => "好呀，我去睡觉。",
        NormalizedOwnerIntentKind.Walk => "好呀，我去走走。",
        NormalizedOwnerIntentKind.Eat => "好呀，我去吃饭饭。",
        NormalizedOwnerIntentKind.Drink => "好呀，我去喝水。",
        NormalizedOwnerIntentKind.CarRide => "好呀，我去兜风。",
        NormalizedOwnerIntentKind.BroomFlight => "好呀，我去飞一圈。",
        _ => "好呀，我这就做。"
    };

    private static DialogueStateClaim? ClaimFor(NormalizedOwnerIntentKind kind) => kind switch
    {
        NormalizedOwnerIntentKind.Sleep => DialogueStateClaim.Sleeping,
        NormalizedOwnerIntentKind.Walk => DialogueStateClaim.Walking,
        NormalizedOwnerIntentKind.Eat => DialogueStateClaim.Eating,
        NormalizedOwnerIntentKind.Drink => DialogueStateClaim.Drinking,
        NormalizedOwnerIntentKind.CarRide => DialogueStateClaim.Driving,
        NormalizedOwnerIntentKind.BroomFlight => DialogueStateClaim.Flying,
        NormalizedOwnerIntentKind.Sit => DialogueStateClaim.Sitting,
        NormalizedOwnerIntentKind.Down => DialogueStateClaim.Prone,
        _ => null
    };

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

    public void SetPetFacingRight(bool faceRight, string reason = "runtime")
    {
        if (_petFacesRight == faceRight)
            return;
        _petFacesRight = faceRight;
        Trace("pet_facing_changed", $"direction={(faceRight ? "right" : "left")} reason={reason}");
    }

    public void UpdatePatrolTravelSpace(double availableLeftPixels, double availableRightPixels, double minimumTravelPixels)
    {
        var minimum = Math.Max(1, minimumTravelPixels);
        _patrolCanMoveLeft = availableLeftPixels >= minimum;
        _patrolCanMoveRight = availableRightPixels >= minimum;
    }

    public void ReportPerformance(string detail) => Trace("performance", detail);

    public void ReportDialogueValidationFailure(IEnumerable<string> reasons) =>
        Trace("dialogue_claim_rejected", string.Join(",", reasons));

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

    public Task<PetActionResult> SubmitFoodWaterAsync(string behaviorId, BehaviorRequestSource source)
    {
        if (source is not (BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel or BehaviorRequestSource.OwnerDialogue))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "food_water_source_forbidden", "吃饭和喝水只允许主人从右键菜单或素材面板手动触发");
            return Task.FromResult(PetActionResult.Deferred);
        }
        if (!FoodWaterCandidateBehaviorIds.All.Contains(behaviorId))
        {
            UpdateDecision(PetActionResult.MissingAsset, source.ToString(), "food_water_behavior_unknown", $"缺少餐饮动作：{behaviorId}");
            return Task.FromResult(PetActionResult.MissingAsset);
        }
        if (_pendingFoodWaterSequence.Count > 0 || FoodWaterCandidateBehaviorIds.All.Contains(_currentBehaviorId))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "food_water_already_running", "悟空正在吃饭或喝水");
            return Task.FromResult(PetActionResult.Deferred);
        }

        var steps = _agentState.CurrentPosture switch
        {
            StablePosture.Prone => new[]
            {
                AutonomousDailyCandidateBehaviorIds.ProneToSit,
                AutonomousDailyCandidateBehaviorIds.SitToStand,
                behaviorId
            },
            StablePosture.Sit => new[]
            {
                AutonomousDailyCandidateBehaviorIds.SitToStand,
                behaviorId
            },
            _ => new[] { behaviorId }
        };
        foreach (var step in steps)
        {
            if (_catalog.Find(step) is not { RuntimeEnabled: true })
            {
                UpdateDecision(PetActionResult.Deferred, source.ToString(), "food_water_posture_path_missing", "当前姿态缺少已批准的站立过渡，已延后餐饮动作");
                return Task.FromResult(PetActionResult.Deferred);
            }
        }

        _pendingFoodWaterSequence.Clear();
        foreach (var step in steps)
            _pendingFoodWaterSequence.Enqueue(step);
        _pendingFoodWaterSource = source;
        _pendingFoodWaterTarget = behaviorId;
        Trace("food_water_sequence_planned", $"target={behaviorId} posture={_agentState.CurrentPosture} steps={string.Join(",", steps)} source={source}");
        return Task.FromResult(SubmitNextFoodWaterStep());
    }

    public Task<PetActionResult> SubmitBaseMotionAsync(string behaviorId, BehaviorRequestSource source)
    {
        if (source != BehaviorRequestSource.ControlPanel)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "base_motion_source_forbidden", "基础动作只允许从素材面板手动触发");
            return Task.FromResult(PetActionResult.Deferred);
        }

        var motion = _catalog.Find(behaviorId);
        if (motion is null)
        {
            UpdateDecision(PetActionResult.MissingAsset, source.ToString(), "base_motion_missing", $"缺少基础动作素材：{behaviorId}");
            return Task.FromResult(PetActionResult.MissingAsset);
        }
        if (!IsBaseAssetMotion(motion))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "base_motion_category_forbidden", "该动作不属于基础动作，请使用对应的主人入口");
            return Task.FromResult(PetActionResult.Deferred);
        }

        if (motion.IsExpired || motion.Deprecated)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "base_motion_deprecated", "该素材已过期，只保留静态预览");
            return Task.FromResult(PetActionResult.Deferred);
        }
        if (!motion.VisualApproved && !motion.RuntimeApproved)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "base_motion_visual_approval_missing", "该素材尚未通过主人视觉验收，只能在预览框查看");
            return Task.FromResult(PetActionResult.Deferred);
        }

        var capability = _behaviorCapabilities.Find(behaviorId);
        if (capability is null)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "base_motion_capability_missing", "该基础动作尚未注册运行能力");
            return Task.FromResult(PetActionResult.Deferred);
        }
        if (!PetPoseCompatibility.IsCompatible(capability.StartPoseFamily, _agentState))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "base_motion_pose_incompatible", "当前姿态不能自然衔接这个动作");
            return Task.FromResult(PetActionResult.Deferred);
        }
        if (_agentState.IsBusy && !_currentInterruptible)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "base_motion_wait_for_safe_point", "当前动作正在安全结束，请稍后再试");
            return Task.FromResult(PetActionResult.Deferred);
        }

        return Task.FromResult(SubmitBehavior(
            source,
            behaviorId,
            $"owner_base_motion:{behaviorId}",
            priority: 7,
            executionMode: motion.RuntimeEnabled && motion.EffectiveRuntimeApproved
                ? BehaviorExecutionMode.Normal
                : BehaviorExecutionMode.DeveloperPreview));
    }

    public static bool IsBaseAssetMotion(PlayableMotion motion) =>
        !string.Equals(motion.Category, "口令动作", StringComparison.Ordinal) &&
        !string.Equals(motion.Category, "宠物魔法", StringComparison.Ordinal) &&
        !string.Equals(motion.BehaviorId, CarRideBehaviorIds.CarRide, StringComparison.OrdinalIgnoreCase);

    private PetActionResult SubmitNextFoodWaterStep()
    {
        if (_pendingFoodWaterSequence.Count == 0)
            return PetActionResult.Accepted;

        var step = _pendingFoodWaterSequence.Dequeue();
        var result = SubmitBehavior(
            _pendingFoodWaterSource,
            step,
            $"owner_food_water:{_pendingFoodWaterTarget}:step:{step}",
            priority: 12);
        if (result != PetActionResult.Accepted)
        {
            Trace("food_water_sequence_stopped", $"target={_pendingFoodWaterTarget} step={step} result={result}");
            _pendingFoodWaterSequence.Clear();
            _pendingFoodWaterTarget = string.Empty;
        }
        return result;
    }

    private bool ContinueFoodWaterSequenceAfterTransition()
    {
        if (_pendingFoodWaterSequence.Count == 0)
            return false;
        return SubmitNextFoodWaterStep() == PetActionResult.Accepted;
    }

    private PetActionResult StartDialogueEpisodeSequence(
        string episode,
        string targetBehaviorId,
        IReadOnlyList<string> steps,
        string preparationText)
    {
        if (_pendingDialogueEpisodeSequence.Count > 0)
        {
            UpdateDecision(PetActionResult.Deferred, BehaviorRequestSource.OwnerDialogue.ToString(),
                $"{episode}_sequence_already_planned", "我已经在准备这个动作啦");
            return PetActionResult.Deferred;
        }

        foreach (var step in steps)
        {
            if (_catalog.Find(step) is not { RuntimeEnabled: true, IsExpired: false })
            {
                UpdateDecision(PetActionResult.Deferred, BehaviorRequestSource.OwnerDialogue.ToString(),
                    $"{episode}_posture_path_missing", "现在缺少自然衔接的姿态动作，先不硬切过去");
                return PetActionResult.Deferred;
            }
        }

        _pendingDialogueEpisodeSequence.Clear();
        foreach (var step in steps)
            _pendingDialogueEpisodeSequence.Enqueue(step);
        _pendingDialogueEpisodeTarget = targetBehaviorId;
        Trace("dialogue_episode_sequence_planned",
            $"episode={episode} target={targetBehaviorId} posture={_agentState.CurrentPosture} steps={string.Join(',', steps)}");
        var result = SubmitNextDialogueEpisodeStep(episode);
        if (result == PetActionResult.Accepted && steps.Count > 1)
            UpdateDecision(PetActionResult.Accepted, BehaviorRequestSource.OwnerDialogue.ToString(),
                $"dialogue_{episode}_preparing", preparationText);
        return result;
    }

    private PetActionResult SubmitNextDialogueEpisodeStep(string episode)
    {
        if (_pendingDialogueEpisodeSequence.Count == 0)
            return PetActionResult.Accepted;

        var step = _pendingDialogueEpisodeSequence.Dequeue();
        var result = SubmitBehavior(
            BehaviorRequestSource.OwnerDialogue,
            step,
            $"dialogue_{episode}:{_pendingDialogueEpisodeTarget}:step:{step}",
            priority: 9);
        if (result != PetActionResult.Accepted)
        {
            Trace("dialogue_episode_sequence_stopped",
                $"episode={episode} target={_pendingDialogueEpisodeTarget} step={step} result={result}");
            _pendingDialogueEpisodeSequence.Clear();
            _pendingDialogueEpisodeTarget = string.Empty;
        }
        return result;
    }

    private bool ContinueDialogueEpisodeSequenceAfterTransition()
    {
        if (_pendingDialogueEpisodeSequence.Count == 0)
            return false;

        var episode = _pendingDialogueEpisodeTarget == SleepCandidateBehaviorIds.MainLifecycle ||
                      _pendingDialogueEpisodeTarget == SleepCandidateBehaviorIds.SprawledFrontBreath
            ? "sleep"
            : "walk";
        return SubmitNextDialogueEpisodeStep(episode) == PetActionResult.Accepted;
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
        var capability = _behaviorCapabilities.Find(decision.SelectedActionId);
        if (capability is null)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "command_capability_missing", "这个口令暂时没有可用能力");
            return PetActionResult.Deferred;
        }
        var participationCapability = capability;
        if (!capability.StartPostures.Contains(_agentState.CurrentPosture) &&
            decision.TransitionPlan.Count > 0 &&
            decision.TransitionPlan[0].FromPosture == _agentState.CurrentPosture)
        {
            // The command planner already proved a posture path for this explicit
            // owner request. Willingness applies to the complete plan, while the
            // animation pipeline still validates and plays each available step.
            participationCapability = capability with
            {
                StartPostures = new HashSet<StablePosture> { _agentState.CurrentPosture },
                StartPoseFamily = IsPostureAcknowledgement(decision.TransitionPlan[0])
                    ? PetPoseCompatibility.FamilyFor(_agentState.CurrentPoseId, _agentState.CurrentPosture)
                    : ResolveCommandStep(decision.TransitionPlan[0]) is { RuntimeEnabled: true } entry
                        ? PetPoseCompatibility.FamilyFor(entry.StartPose, _agentState.CurrentPosture)
                        : capability.StartPoseFamily
            };
        }
        var participation = _participationPolicy.Evaluate(participationCapability, _petAgentState, source, _now());
        var components = string.Join(",", participation.Components.Select(item => $"{item.Key}={item.Value:0.000}"));
        Trace("command_participation",
            $"command={command} action={decision.SelectedActionId} disposition={participation.Disposition} willingness={participation.WillingnessScore:0.000} reason={participation.ReasonCode} components=[{components}]");
        if (participation.Disposition != RequestDisposition.Accepted)
        {
            var result = participation.Disposition == RequestDisposition.Rejected
                ? PetActionResult.Rejected
                : PetActionResult.Deferred;
            UpdateCommandDecision(result, source, participation, decision.SelectedActionId);
            return result;
        }
        var submitted = SubmitMockDecision(decision, source, trigger, allowAutonomous: false);
        if (submitted == PetActionResult.Accepted)
            UpdateCommandDecision(submitted, source, participation, decision.SelectedActionId);
        return submitted;
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
            source != BehaviorRequestSource.OwnerDialogue &&
            now - _currentStartedAt < TimeSpan.FromSeconds(3) &&
            _currentBehaviorId != Phase15BehaviorIds.ProneIdle)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "minimum_dwell", "Current behavior is in minimum dwell.");
            return PetActionResult.Deferred;
        }

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
            // A same-pose acknowledgement is not a second stand-to-sit/down clip.
            if (IsPostureAcknowledgement(step))
                continue;
            var motion = ResolveCommandStep(step);
            if (motion is null)
            {
                Trace("behavior_agent_transition_gap", $"missing={step.ActionId} reason={step.Reason}");
                return null;
            }
            foreach (var phase in motion.Phases)
                phases.Add(phase with { Name = $"{step.ActionId}:{phase.Name}" });
        }

        var selectedMotion = CommandMotionMockMotions.FirstOrDefault(x => string.Equals(x.BehaviorId, decision.SelectedActionId, StringComparison.OrdinalIgnoreCase)) ?? _catalog.Find(decision.SelectedActionId) ?? CommandMotionMockMotions.FirstOrDefault(x => string.Equals(x.BehaviorId, steps.Last().ActionId, StringComparison.OrdinalIgnoreCase)) ?? _catalog.Find(steps.Last().ActionId);
        if (selectedMotion is null)
            return null;
        if (phases.Count == 0)
        {
            var heldFrame = File.Exists(CurrentAsset) ? CurrentAsset : _currentMotion?.FirstFrame;
            if (heldFrame is null || !File.Exists(heldFrame)) return null;
            phases.Add(new MotionPhase("stable_posture_acknowledgement", new[] { heldFrame }, false, new[] { 320 }));
        }

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

    private PlayableMotion? ResolveCommandStep(TransitionStep step)
    {
        var approvedTransitionId = step.ActionId switch
        {
            MockCommandActionIds.MockProneToSit => AutonomousDailyCandidateBehaviorIds.ProneToSit,
            MockCommandActionIds.MockSitToStand => AutonomousDailyCandidateBehaviorIds.SitToStand,
            _ => null
        };
        if (approvedTransitionId is not null)
            return _catalog.Find(approvedTransitionId) is { RuntimeEnabled: true, IsExpired: false } transition
                ? transition
                : null;
        return CommandMotionMockMotions.FirstOrDefault(x => string.Equals(x.BehaviorId, step.ActionId, StringComparison.OrdinalIgnoreCase))
            ?? _catalog.Find(step.ActionId);
    }

    private static bool IsPostureAcknowledgement(TransitionStep step) =>
        step.FromPosture == step.ToPosture &&
        (step.Reason.Contains("light_response", StringComparison.Ordinal) ||
         step.Reason.Contains("already_prone", StringComparison.Ordinal));

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
        string.Equals(behaviorId, LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1, StringComparison.OrdinalIgnoreCase) ||
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
        _pendingFoodWaterSequence.Clear();
        _pendingFoodWaterTarget = string.Empty;
        _pendingDialogueEpisodeSequence.Clear();
        _pendingDialogueEpisodeTarget = string.Empty;
        var restoredPreview = RestorePreviewAgentState("stop");
        FinishReducerOwnedExecution(ExecutionStatus.Interrupted, EstimateCurrentCompletionRatio(), reason);
        if (!restoredPreview && _pendingAgentDecision is not null)
        {
            Trace("behavior_agent_outcome", "owner_stop,reducer_owned=true");
            _pendingAgentDecision = null;
        }
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

        var decisionOrdinal = _autonomousDecisionCount++;
        var decisionSeed = CombineDecisionSeed(_decisionSeed, decisionOrdinal, (int)_agentState.CurrentPosture);
        var authoritative = _rolloutOptions.IsAuthoritative(_petAgentState.Episode.Kind);
        var episodeBindings = BuildAutonomousEpisodeBindings(_petAgentState.Episode.Kind);
        Exception? decisionFailure = null;
        try
        {
            _lastShadowDecision = _agentDecisionEngine.Decide(
                _petAgentState,
                _behaviorCapabilities,
                new BehaviorDecisionInput(
                    BehaviorRequestSource.AutonomousTick,
                    now,
                    _currentBehaviorId,
                    _currentStartedAt,
                    _currentInterruptible,
                    _lastAccepted,
                    _petAgentState.RecentExperience.Select(item => item.BehaviorId).ToArray(),
                    decisionSeed,
                    AllowInitiative: true,
                    WindowMotionAvailable: _patrolCanMoveLeft || _patrolCanMoveRight,
                    CandidateBehaviorIds: episodeBindings,
                    BehaviorWeightMultipliers: BuildAutonomousBehaviorWeightMultipliers()));
        }
        catch (Exception ex)
        {
            decisionFailure = ex;
            _lastShadowDecision = null;
            Trace("behavior_agent_decision_failed", $"episode={_petAgentState.Episode.Kind} error={ex.GetType().Name}");
        }

        // Legacy remains a side-effect-free comparison source during rollout.
        var legacyChoice = ChooseAutonomousBehavior(decisionOrdinal);
        if (_lastShadowDecision is not null)
        {
            _lastShadowComparison = string.Equals(
                legacyChoice.BehaviorId,
                _lastShadowDecision.SelectedBehaviorId,
                StringComparison.OrdinalIgnoreCase)
                ? "same"
                : "different";
            var selected = _lastShadowDecision.Candidates.FirstOrDefault(item => item.Selected);
            Trace("behavior_agent_shadow_decision",
                $"legacy={legacyChoice.BehaviorId} agent={_lastShadowDecision.SelectedBehaviorId ?? "none"} comparison={_lastShadowComparison} episode={_lastShadowDecision.Episode} score={(selected?.FinalScore.ToString("0.000") ?? "n/a")} authoritative={(authoritative ? "agent" : "legacy")}");
        }

        var choice = authoritative && decisionFailure is null &&
                     _lastShadowDecision is { Disposition: RequestDisposition.Accepted, SelectedBehaviorId: not null }
            ? (_lastShadowDecision.SelectedBehaviorId, $"agent:{_petAgentState.Episode.Kind}:{_lastShadowDecision.ReasonCode}")
            : legacyChoice;

        if (authoritative && (decisionFailure is not null || _lastShadowDecision?.SelectedBehaviorId is null))
        {
            _nextAutonomousDecisionAt = now + TimeSpan.FromSeconds(_random.Next(14, 25));
            Trace("behavior_agent_authoritative_idle",
                $"episode={_petAgentState.Episode.Kind} reason={_lastShadowDecision?.ReasonCode ?? "infrastructure_failure"} legacy_fallback=false");
            return Task.CompletedTask;
        }

        if (ShouldKeepCurrentStableIdle(_currentBehaviorId, choice.Item1))
        {
            _nextAutonomousDecisionAt = now + ChooseAutonomousIdleDelay(_agentState.CurrentPosture, _random);
            Trace("autonomous_idle_held", $"behavior={choice.Item1} reason={choice.Item2}");
            return Task.CompletedTask;
        }
        var result = SubmitBehavior(BehaviorRequestSource.AutonomousTick, choice.Item1, choice.Item2, priority: -5);
        _nextAutonomousDecisionAt = now + (result == PetActionResult.Accepted
            ? ChooseAutonomousIdleDelay(_agentState.CurrentPosture, _random)
            : TimeSpan.FromSeconds(_random.Next(14, 25)));
        return Task.CompletedTask;
    }

    private void AdvanceRuntimeStateForAutonomousTick()
    {
        var now = _now();
        ReduceAgentState(new PetTimeAdvanced(now), "elapsed_time");
        var episode = _episodePolicy.Evaluate(_petAgentState, now,
            DesktopAutonomousEpisodeBindings.AvailableEpisodes(_behaviorCapabilities, _agentState));
        if (episode.Changed)
        {
            ReduceAgentState(new PetEpisodeChanged(
                now,
                episode.Episode.Kind,
                episode.Episode.MinimumDwell,
                episode.Episode.ReasonCode,
                episode.Episode.CorrelationId), "episode_policy");
            Trace("behavior_agent_episode", string.Join(",", episode.ReasonCodes));
            OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        }
    }

    private IReadOnlySet<string>? BuildAutonomousEpisodeBindings(PetEpisodeKind episode)
    {
        var configured = DesktopAutonomousEpisodeBindings.For(episode);
        if (episode is PetEpisodeKind.Resting or PetEpisodeKind.Observing)
        {
            var prone = new HashSet<string>(configured ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var id in FrontProneExpressionBehaviorIds.All.Append(ProneHappyHotPantingBehaviorIds.HappyHotPanting))
                if (CanScheduleProneMicroevent(id)) prone.Add(id);
                else prone.Remove(id);
            if (episode == PetEpisodeKind.Resting)
            {
                foreach (var id in new[]
                         {
                             WakeRiseCandidateBehaviorIds.FrontRiseSit,
                             WakeRiseCandidateBehaviorIds.FrontRiseFull,
                             WakeRiseCandidateBehaviorIds.FrontSitStand
                         })
                    if (CanScheduleWakeRiseTransition(id)) prone.Add(id);
                    else prone.Remove(id);
                return prone;
            }
            configured = prone;
        }
        if (episode == PetEpisodeKind.Exploring && configured is not null)
            return new HashSet<string>(configured.Where(id =>
                !PatrolWalkCandidateBehaviorIds.All.Contains(id) || CanSchedulePatrol(id)), StringComparer.OrdinalIgnoreCase);
        if (episode is not (PetEpisodeKind.Observing or PetEpisodeKind.Socializing))
            return configured;

        var result = configured is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase);
        if (_catalog.Find(StandingHappyExpectantBehaviorIds.HappyExpectant) is
            { RuntimeEnabled: true, AutonomousBindingEnabled: true })
            result.Add(StandingHappyExpectantBehaviorIds.HappyExpectant);
        return result;
    }

    private bool CanScheduleProneMicroevent(string id) =>
        _catalog.Find(id) is { RuntimeEnabled: true, AutonomousBindingEnabled: true } &&
        IsFrontProneExpressionProfileAllowed(_agentState.CurrentPosture, _agentState.CurrentPoseId,
            _frontProneProfileActive, _agentState.IsBusy) &&
        Energy >= 0.25 && Stress < 0.7 &&
        CanScheduleFrontProneExpression(_now(), _nextFrontProneExpressionAt, id, _recentFrontProneExpressions) &&
        (id != ProneHappyHotPantingBehaviorIds.HappyHotPanting ||
            IsProneHappyHotPantingProfileAllowed(_agentState.CurrentPosture, _agentState.CurrentPoseId,
                _frontProneProfileActive, _agentState.IsBusy, Mood, Stress));

    private bool CanScheduleWakeRiseTransition(string behaviorId)
    {
        if (_agentState.IsBusy ||
            _catalog.Find(behaviorId) is not { RuntimeEnabled: true, RuntimeApproved: true, AutonomousBindingEnabled: true })
            return false;

        return behaviorId switch
        {
            WakeRiseCandidateBehaviorIds.FrontRiseSit or WakeRiseCandidateBehaviorIds.FrontRiseFull =>
                _agentState.CurrentPosture == StablePosture.Prone &&
                _frontProneProfileActive &&
                string.Equals(PetPoseCompatibility.FamilyFor(_agentState.CurrentPoseId, _agentState.CurrentPosture), "prone.front", StringComparison.OrdinalIgnoreCase) &&
                _now() - _currentStartedAt >= TimeSpan.FromSeconds(45),
            WakeRiseCandidateBehaviorIds.FrontSitStand =>
                _agentState.CurrentPosture == StablePosture.Sit &&
                string.Equals(_agentState.LastActionId, WakeRiseCandidateBehaviorIds.FrontRiseSit, StringComparison.OrdinalIgnoreCase),
            _ => false
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

    public void CompleteMotion(string behaviorId, string phase) =>
        CompleteMotion(_lastBehaviorRequest?.RequestId ?? Guid.Empty, behaviorId, phase);

    public void CompleteMotion(Guid executionId, string behaviorId, string phase)
    {
        // Presentation callbacks are correlated too: an old hold or preview
        // must never replace a newer action, even after its reducer has settled.
        if (!IsCurrentMotionCallback(executionId, behaviorId))
        {
            Trace("behavior_completion_ignored", $"behavior={behaviorId} execution={executionId} reason=duplicate_or_stale");
            return;
        }

        if (_currentExecutionMode != BehaviorExecutionMode.Normal)
        {
            RestorePreviewAgentState($"complete:{behaviorId}");
            _pendingAgentDecision = null;
            Trace("behavior_preview_completed", $"behavior={behaviorId} execution={executionId} state_write=false");
            StartStablePostureIdle(_agentState.CurrentPosture, $"preview_complete:{behaviorId}");
            return;
        }

        if (behaviorId.StartsWith(StableHoldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Trace("command_terminal_settled", $"{behaviorId} phase={phase} posture={_agentState.CurrentPosture}");
            StartStablePostureIdle(
                _agentState.CurrentPosture,
                $"command_terminal_settled:{behaviorId}",
                _currentMotion?.RenderScaleOverride);
            return;
        }

        if (IsStableIdleBehavior(behaviorId))
            return;

        if (_activeReducerExecution is null ||
            _activeReducerExecution.Request.RequestId != executionId ||
            !string.Equals(_activeReducerExecution.Profile.BehaviorId, behaviorId, StringComparison.OrdinalIgnoreCase))
        {
            Trace("behavior_completion_ignored", $"behavior={behaviorId} execution={executionId} reason=no_active_execution");
            return;
        }

        var completedPlaybackMotion = _currentMotion;
        FinishReducerOwnedExecution(ExecutionStatus.Completed, 1, $"motion_complete:{phase}");
        _frontProneProfileActive =
            string.Equals(behaviorId, MockCommandActionIds.EatProne, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                PetPoseCompatibility.FamilyFor(_agentState.CurrentPoseId, _agentState.CurrentPosture),
                "prone.front",
                StringComparison.OrdinalIgnoreCase);
        Trace("behavior_reducer_completed", $"behavior={behaviorId} execution={executionId} posture={_agentState.CurrentPosture} pose={_agentState.CurrentPoseId}");
        RaiseMetrics();
        if (ContinueFoodWaterSequenceAfterTransition())
            return;
        if (ContinueDialogueEpisodeSequenceAfterTransition())
            return;
        if (completedPlaybackMotion is not null &&
            string.Equals(completedPlaybackMotion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        {
            if (TryStartApprovedWakeExit(completedPlaybackMotion))
            {
                _pendingAgentDecision = null;
                return;
            }
            StartTerminalPoseHold(behaviorId, _agentState.CurrentPosture, $"sleep_hold:{behaviorId}", completedPlaybackMotion);
        }
        else if (completedPlaybackMotion is not null &&
                 string.Equals(completedPlaybackMotion.AssetBatch, WakeRiseCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            StartTerminalPoseHold(behaviorId, _agentState.CurrentPosture, $"wake_rise_hold:{behaviorId}", completedPlaybackMotion);
        else if (completedPlaybackMotion is not null && MockCommandActionIds.PrototypeWhitelist.Contains(behaviorId))
            StartTerminalPoseHold(behaviorId, _agentState.CurrentPosture, $"command_complete:{behaviorId}", completedPlaybackMotion);
        else
            StartStablePostureIdle(_agentState.CurrentPosture, $"reducer_complete:{behaviorId}");
        if (_pendingAgentDecision is not null)
        {
            Trace("behavior_agent_outcome", $"motion_complete:{phase},reducer_owned=true");
            _pendingAgentDecision = null;
        }
    }

    private bool IsCurrentMotionCallback(Guid executionId, string behaviorId) =>
        executionId != Guid.Empty &&
        _lastBehaviorRequest?.RequestId == executionId &&
        string.Equals(_currentMotion?.BehaviorId, behaviorId, StringComparison.OrdinalIgnoreCase);

    public void FailMotion(Guid executionId, string behaviorId, string reason)
    {
        if (!IsCurrentMotionCallback(executionId, behaviorId))
        {
            Trace("behavior_failure_ignored", $"behavior={behaviorId} execution={executionId} reason=stale_or_inactive");
            return;
        }
        if (_currentExecutionMode != BehaviorExecutionMode.Normal)
        {
            RestorePreviewAgentState($"failed:{behaviorId}");
            _pendingAgentDecision = null;
            Trace("behavior_preview_failed", $"behavior={behaviorId} execution={executionId} reason={reason} state_write=false");
            StartStablePostureIdle(_agentState.CurrentPosture, $"preview_failed:{behaviorId}");
            return;
        }
        if (_activeReducerExecution is null ||
            _activeReducerExecution.Request.RequestId != executionId ||
            !string.Equals(_activeReducerExecution.Profile.BehaviorId, behaviorId, StringComparison.OrdinalIgnoreCase))
        {
            Trace("behavior_failure_ignored", $"behavior={behaviorId} execution={executionId} reason=stale_or_inactive");
            return;
        }

        FinishReducerOwnedExecution(ExecutionStatus.Failed, EstimateCurrentCompletionRatio(), reason);
        Trace("behavior_reducer_failed", $"behavior={behaviorId} execution={executionId} reason={reason}");
        RaiseMetrics();
        StartStablePostureIdle(_agentState.CurrentPosture, $"reducer_failed:{behaviorId}");
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
            _agentState = _agentState with
            {
                CurrentPosture = posture,
                CurrentPoseId = playbackMotion.EndPose,
                ActiveExecutionId = null,
                IsBusy = false,
                ActiveActionId = null
            };
            _nextAutonomousDecisionAt = _now() + ChooseAutonomousIdleDelay(posture, _random);
            Accept(playbackMotion, BehaviorRequestSource.OwnerUi, BehaviorExecutionMode.Normal, source, returnToIdle: false, loopCycles: int.MaxValue);
            return;
        }

        var fallback = _catalog.RequiredIdle;
        var fallbackPosture = StablePostureFromPose(fallback.EndPose, StablePosture.Prone);
        _agentState = _agentState with
        {
            CurrentPosture = fallbackPosture,
            CurrentPoseId = fallback.EndPose,
            ActiveExecutionId = null,
            IsBusy = false,
            ActiveActionId = null
        };
        _nextAutonomousDecisionAt = _now() + ChooseAutonomousIdleDelay(fallbackPosture, _random);
        Accept(fallback, BehaviorRequestSource.OwnerUi, BehaviorExecutionMode.Normal, source, returnToIdle: false, loopCycles: int.MaxValue);
    }

    private bool TryStartApprovedWakeExit(PlayableMotion completedSleepMotion)
    {
        var wakeBehaviorId = completedSleepMotion.BehaviorId switch
        {
            SleepCandidateBehaviorIds.MainLifecycle => WakeRiseCandidateBehaviorIds.SideWake,
            SleepCandidateBehaviorIds.SprawledFrontBreath => WakeRiseCandidateBehaviorIds.FrontWake,
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(wakeBehaviorId) ||
            _catalog.Find(wakeBehaviorId) is not { RuntimeEnabled: true, RuntimeApproved: true, AutonomousBindingEnabled: true })
            return false;

        var result = SubmitBehavior(
            BehaviorRequestSource.AutonomousTick,
            wakeBehaviorId,
            $"approved_sleep_exit:{completedSleepMotion.BehaviorId}",
            priority: 0);
        if (result == PetActionResult.Accepted)
            Trace("approved_sleep_exit_started", $"sleep={completedSleepMotion.BehaviorId} wake={wakeBehaviorId}");
        return result == PetActionResult.Accepted;
    }

    private void StartTerminalPoseHold(string behaviorId, StablePosture posture, string source, PlayableMotion? completedRequestMotion)
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
            posture switch
            {
                StablePosture.Stand => "站稳片刻",
                StablePosture.Sit => "坐稳片刻",
                _ => "趴稳片刻"
            },
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
            RenderScaleOverride: MotionVisualSizer.RenderScaleForMotion(completedMotion, DesktopMotionCatalog.ReferenceFramePath),
            SupportsHorizontalMirror: completedMotion.SupportsHorizontalMirror);

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
        var ownerExplicit = source is BehaviorRequestSource.OwnerUi or BehaviorRequestSource.OwnerDialogue or BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel;
        if (source == BehaviorRequestSource.AutonomousTick &&
            (FrontProneExpressionBehaviorIds.All.Contains(behaviorId) || behaviorId == ProneHappyHotPantingBehaviorIds.HappyHotPanting) &&
            !CanScheduleProneMicroevent(behaviorId))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "prone_microevent_pose_or_cooldown", "先安静趴一会儿");
            return PetActionResult.Deferred;
        }
        if (source == BehaviorRequestSource.AutonomousTick && PatrolWalkCandidateBehaviorIds.All.Contains(behaviorId) &&
            !CanSchedulePatrol(behaviorId))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "patrol_space_or_shared_cooldown", "先站稳休息片刻，再往有空间的方向走");
            return PetActionResult.Deferred;
        }
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
            loopCycles = _random.Next(2, 5);
        if (source == BehaviorRequestSource.AutonomousTick &&
            string.Equals(motion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            loopCycles = motion.Phases.Any(x => x.Loop) ? 3 : 1;
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

        if (motion.Deprecated &&
            string.Equals(motion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            return (false, "asset_deprecated_owner_rejected", $"{motion.DisplayName} 已由主人否决色彩和毛发质感，仅保留静态审计记录");

        if (executionMode == BehaviorExecutionMode.DeveloperPreview)
            return (true, "developer_preview", "开发者预览已允许");

        if (motion.IsExpired)
            return (false, "asset_deprecated", $"{motion.DisplayName} 已过期，只能作为动作参考预览");

        if (executionMode == BehaviorExecutionMode.PrototypePreview)
        {
            var sourceAllowed = source is BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel or BehaviorRequestSource.OwnerDialogue;
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
            source is not (BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel or BehaviorRequestSource.OwnerDialogue))
            return (false, "car_ride_source_forbidden", "兜风只允许主人从玩一下菜单或面板手动触发");

        if (string.Equals(motion.AssetBatch, FoodWaterCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) &&
            executionMode == BehaviorExecutionMode.Normal &&
            source is not (BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel or BehaviorRequestSource.OwnerDialogue))
            return (false, "food_water_source_forbidden", "吃饭和喝水只允许主人从吃一下菜单或素材面板手动触发");

        if (string.Equals(motion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) &&
            executionMode == BehaviorExecutionMode.Normal &&
            (source is not (BehaviorRequestSource.AutonomousTick or BehaviorRequestSource.OwnerDialogue) ||
             !SleepCandidateBehaviorIds.AutonomousAllowed.Contains(motion.BehaviorId)))
            return (false, "sleep_source_or_posture_route_forbidden", "该睡眠动作只允许从兼容姿态的自主日常路由触发");

        if (MockCommandActionIds.PrototypeWhitelist.Contains(motion.BehaviorId) &&
            executionMode == BehaviorExecutionMode.Normal &&
            source is not (BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel or BehaviorRequestSource.OwnerDialogue))
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
        int loopCycles,
        BehaviorRequest? behaviorRequest = null)
    {
        behaviorRequest ??= BehaviorRequest.FromIntent(
            source,
            RuntimeModeFor(executionMode),
            _now(),
            new SemanticIntent(SemanticIntentKind.None, motion.BehaviorId, NaturalLanguage: reason),
            context: reason);
        var stablePresentation = IsStableIdleBehavior(motion.BehaviorId) ||
            motion.BehaviorId.StartsWith(StableHoldPrefix, StringComparison.OrdinalIgnoreCase);
        var outcomeProfile = DesktopBehaviorOutcomeProfiles.Find(motion.BehaviorId) ?? DesktopBehaviorOutcomeProfiles.CreateFallback(motion);
        var tracksAgentLifecycle = executionMode == BehaviorExecutionMode.Normal && !stablePresentation;
        if (!stablePresentation &&
            _activeReducerExecution is not null &&
            _activeReducerExecution.Request.RequestId != behaviorRequest.RequestId)
            FinishReducerOwnedExecution(ExecutionStatus.Interrupted, EstimateCurrentCompletionRatio(), $"preempted_by:{motion.BehaviorId}");
        if (executionMode == BehaviorExecutionMode.Normal)
            RestorePreviewAgentState($"normal_request:{motion.BehaviorId}");
        else
            _previewAgentStateSnapshot ??= _petAgentState;

        if (executionMode == BehaviorExecutionMode.Normal &&
            string.Equals(motion.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(motion.BehaviorId, PatrolWalkCandidateBehaviorIds.WalkRight, StringComparison.OrdinalIgnoreCase))
                SetPetFacingRight(true, "patrol_walk_right");
            else if (string.Equals(motion.BehaviorId, PatrolWalkCandidateBehaviorIds.WalkLeft, StringComparison.OrdinalIgnoreCase))
                SetPetFacingRight(false, "patrol_walk_left");
        }

        var mirrorHorizontally = MotionHorizontalMirrorPolicy.Resolve(motion, _petFacesRight);
        _currentBehaviorId = motion.BehaviorId;
        _currentMotion = motion;
        _currentExecutionMode = executionMode;
        _currentStartedAt = _now();
        _currentInterruptible = motion.Interruptible;
        _lastBehaviorRequest = behaviorRequest;
        if (executionMode == BehaviorExecutionMode.Normal)
            _lastAccepted[motion.BehaviorId] = _currentStartedAt;
        if (executionMode == BehaviorExecutionMode.Normal && !stablePresentation)
            RecordBehaviorTrigger(motion, source, _currentStartedAt);
        if (executionMode == BehaviorExecutionMode.Normal &&
            source == BehaviorRequestSource.AutonomousTick &&
            (FrontProneExpressionBehaviorIds.All.Contains(motion.BehaviorId) ||
             string.Equals(motion.BehaviorId, ProneHappyHotPantingBehaviorIds.HappyHotPanting, StringComparison.OrdinalIgnoreCase)))
        {
            _nextFrontProneExpressionAt = _currentStartedAt + TimeSpan.FromSeconds(_random.Next(45, 121));
            _recentFrontProneExpressions.Enqueue(motion.BehaviorId);
            while (_recentFrontProneExpressions.Count > 2)
                _recentFrontProneExpressions.Dequeue();
        }
        if (executionMode == BehaviorExecutionMode.Normal &&
            source == BehaviorRequestSource.AutonomousTick &&
            string.Equals(motion.BehaviorId, StandingHappyExpectantBehaviorIds.HappyExpectant, StringComparison.OrdinalIgnoreCase))
            _nextStandingHappyExpectantAt = _currentStartedAt + TimeSpan.FromSeconds(_random.Next(75, 151));
        if (tracksAgentLifecycle)
        {
            _activeReducerExecution = new ActiveBehaviorExecution(
                behaviorRequest,
                outcomeProfile!,
                EstimateMotionDuration(motion, loopCycles));
            ReduceAgentState(new PetBehaviorStarted(
                _currentStartedAt,
                behaviorRequest.RequestId,
                motion.BehaviorId,
                motion.Phases.FirstOrDefault()?.Name ?? "intro",
                motion.Interruptible,
                source,
                executionMode), "behavior_started");
        }
        if (tracksAgentLifecycle)
            StartEpisodeForMotion(motion, behaviorRequest.CorrelationId, source);
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
        MotionRequested?.Invoke(this, new PetMotionRequest(
            motion,
            reason,
            returnToIdle,
            loopCycles,
            source,
            executionMode,
            _pendingRequestTimestamp,
            mirrorHorizontally,
            behaviorRequest.RequestId,
            behaviorRequest.CorrelationId,
            tracksAgentLifecycle));
        Trace("motion_requested", $"{motion.BehaviorId} request={behaviorRequest.RequestId} source={source} mode={executionMode} asset_batch={motion.AssetBatch} mirror={mirrorHorizontally} reason={reason}");
        OnPropertyChanged(nameof(CurrentBehaviorId));
        OnPropertyChanged(nameof(CurrentAction));
        OnPropertyChanged(nameof(LastTrigger));
        OnPropertyChanged(nameof(LastError));
    }

    private void RecordBehaviorTrigger(PlayableMotion motion, BehaviorRequestSource source, DateTimeOffset timestamp)
    {
        if (!_behaviorTriggerStatistics.TryGetValue(motion.BehaviorId, out var aggregate))
        {
            aggregate = new BehaviorTriggerAggregate();
            _behaviorTriggerStatistics[motion.BehaviorId] = aggregate;
        }

        aggregate.Record(timestamp, source);
        var category = ClassifyMechanism(motion, _behaviorCapabilities.Find(motion.BehaviorId));
        _behaviorTriggerEvents.Enqueue(new BehaviorTriggerEvent(timestamp, motion.BehaviorId, category.Key, source));
        while (_behaviorTriggerEvents.Count > BehaviorStatisticsEventLimit)
            _behaviorTriggerEvents.Dequeue();

        OnPropertyChanged(nameof(BehaviorMechanisms));
        OnPropertyChanged(nameof(BehaviorMechanismDashboard));
    }

    private BehaviorMechanismSnapshot BuildBehaviorMechanismSnapshot(PlayableMotion motion)
    {
        var capability = _behaviorCapabilities.Find(motion.BehaviorId);
        _behaviorTriggerStatistics.TryGetValue(motion.BehaviorId, out var aggregate);
        var category = ClassifyMechanism(motion, capability);
        var autonomous = capability?.AutonomousBindingEnabled == true;
        var weightAndFrequency = autonomous && capability is not null
            ? $"权重 {capability.BaseWeight:0.00} · 冷却 {FormatDuration(capability.Cooldown)} · 最短驻留 {FormatDuration(capability.MinimumDwell)}"
            : "主人明确触发，不参与自主评分";
        var gateSummary = capability is null
            ? "未建立能力目录"
            : $"起：{FormatPosture(capability.StartPostures)} · 终：{FormatPosture(capability.EndPosture)} · {(motion.Interruptible ? "可在安全点中断" : "等待安全结束")}";
        return new BehaviorMechanismSnapshot(
            MotionDisplayNameCatalog.Resolve(motion.BehaviorId, motion.DisplayName),
            motion.BehaviorId,
            category.Key,
            category.Name,
            category.Accent,
            TriggerMechanismFor(motion, autonomous),
            weightAndFrequency,
            gateSummary,
            aggregate?.Count ?? 0,
            aggregate?.AverageInterval ?? "尚无重复触发",
            aggregate?.LastTriggeredAt is { } last ? last.ToLocalTime().ToString("HH:mm:ss") : "本次启动未触发",
            aggregate?.SourceSummary ?? "--");
    }

    private BehaviorMechanismDashboardSnapshot BuildBehaviorMechanismDashboard()
    {
        var mechanisms = BehaviorMechanisms;
        var events = _behaviorTriggerEvents.ToArray();
        var total = events.Length;
        var groupedEvents = events
            .GroupBy(item => item.CategoryKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Timestamp).ToArray(), StringComparer.OrdinalIgnoreCase);
        var availableByCategory = mechanisms
            .GroupBy(item => item.CategoryKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Descriptor = new MechanismCategoryDescriptor(
                    group.Key,
                    group.First().CategoryName,
                    group.First().CategoryAccent,
                    MechanismCategoryOrder(group.Key)),
                MotionCount = group.Count()
            })
            .OrderBy(item => item.Descriptor.Order)
            .ThenBy(item => item.Descriptor.Name, StringComparer.CurrentCulture)
            .ToArray();
        var largestCategoryCount = Math.Max(1, availableByCategory
            .Select(item => groupedEvents.GetValueOrDefault(item.Descriptor.Key)?.Length ?? 0)
            .DefaultIfEmpty(0)
            .Max());
        var categories = availableByCategory
            .Select(item =>
            {
                var categoryEvents = groupedEvents.GetValueOrDefault(item.Descriptor.Key) ?? Array.Empty<BehaviorTriggerEvent>();
                var count = categoryEvents.Length;
                return new BehaviorMechanismCategorySnapshot(
                    item.Descriptor.Key,
                    item.Descriptor.Name,
                    item.Descriptor.Accent,
                    item.MotionCount,
                    count,
                    total == 0 ? "--" : $"{count / (double)total:P0}",
                    Math.Round(228d * count / largestCategoryCount, 1),
                    AverageIntervalFor(categoryEvents),
                    SourceSummaryFor(categoryEvents));
            })
            .ToArray();
        var autonomous = events.Count(item => item.Source == BehaviorRequestSource.AutonomousTick);
        var top = categories.OrderByDescending(item => item.SessionTriggerCount).ThenBy(item => item.Name, StringComparer.CurrentCulture).FirstOrDefault();
        var summary = total == 0
            ? "本次启动还没有真实运行记录。手动、对话或自主触发后，图表会即时更新。"
            : $"本次会话共 {total} 次真实动作；{top?.Name ?? "--"} 最多（{top?.SessionTriggerCount ?? 0} 次）。";
        var trend = BuildBehaviorTrend(events);
        return new BehaviorMechanismDashboardSnapshot(
            total,
            autonomous,
            total - autonomous,
            events.Select(item => item.BehaviorId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            summary,
            categories,
            trend,
            BuildBehaviorTrendLine(trend));
    }

    private IReadOnlyList<BehaviorMechanismTrendBucket> BuildBehaviorTrend(IReadOnlyList<BehaviorTriggerEvent> events)
    {
        var end = _now();
        var start = end - TimeSpan.FromHours(2);
        var bucketDuration = TimeSpan.FromTicks((end - start).Ticks / BehaviorTrendBucketCount);
        var counts = Enumerable.Range(0, BehaviorTrendBucketCount)
            .Select(index => events.Count(item =>
            {
                var bucketStart = start + TimeSpan.FromTicks(bucketDuration.Ticks * index);
                var bucketEnd = index == BehaviorTrendBucketCount - 1 ? end : bucketStart + bucketDuration;
                return item.Timestamp >= bucketStart &&
                       (index == BehaviorTrendBucketCount - 1 ? item.Timestamp <= bucketEnd : item.Timestamp < bucketEnd);
            }))
            .ToArray();
        var maximum = Math.Max(1, counts.Max());
        return counts.Select((count, index) =>
        {
            var labelAt = start + TimeSpan.FromTicks(bucketDuration.Ticks * (index + 1));
            return new BehaviorMechanismTrendBucket(
                labelAt.ToLocalTime().ToString("HH:mm"),
                count,
                count == 0 ? 2 : Math.Round(14 + 78d * count / maximum, 1),
                $"{labelAt.ToLocalTime():HH:mm} 前 10 分钟：{count} 次真实动作");
        }).ToArray();
    }

    private static PointCollection BuildBehaviorTrendLine(IReadOnlyList<BehaviorMechanismTrendBucket> buckets)
    {
        var maximum = Math.Max(1, buckets.Max(item => item.TriggerCount));
        return new PointCollection(buckets.Select((bucket, index) => new Point(
            8 + index * 46,
            124 - 94d * bucket.TriggerCount / maximum)));
    }

    private static string AverageIntervalFor(IReadOnlyList<BehaviorTriggerEvent> events)
    {
        if (events.Count < 2)
            return "尚无重复触发";
        var intervals = events.Zip(events.Skip(1), (left, right) => right.Timestamp - left.Timestamp)
            .Where(interval => interval >= TimeSpan.Zero)
            .ToArray();
        return intervals.Length == 0
            ? "尚无重复触发"
            : $"{intervals.Average(interval => interval.TotalSeconds):0.#} 秒";
    }

    private static string SourceSummaryFor(IEnumerable<BehaviorTriggerEvent> events)
    {
        var sourceCounts = events.GroupBy(item => item.Source)
            .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
            .Select(group => $"{DisplaySource(group.Key)} {group.Count()}");
        var summary = string.Join(" / ", sourceCounts);
        return string.IsNullOrWhiteSpace(summary) ? "--" : summary;
    }

    private static MechanismCategoryDescriptor ClassifyMechanism(PlayableMotion motion, BehaviorCapability? capability) =>
        motion.Effect is DesktopMotionEffect.BroomFlight or DesktopMotionEffect.Apparate or
            DesktopMotionEffect.Petrify or DesktopMotionEffect.PetrifyRelease or DesktopMotionEffect.Scourgify
            ? new("magic", "魔法特辑", "#7A5AA6", 60)
            : motion.Effect == DesktopMotionEffect.CarRide
                ? new("play", "玩一下", "#27846B", 70)
                : string.Equals(motion.AssetBatch, FoodWaterCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase)
                    ? new("interaction", "主人互动", "#B56A3D", 40)
                    : string.Equals(motion.Category, "口令动作", StringComparison.OrdinalIgnoreCase)
                        ? new("command", "口令", "#A64F73", 50)
                        : string.Equals(motion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(motion.AssetBatch, WakeRiseCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase)
                            ? new("sleep", "睡眠恢复", "#536E93", 20)
                            : string.Equals(motion.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase)
                                ? new("walk", "走动巡视", "#28728C", 30)
                                : capability?.AutonomousBindingEnabled == true
                                    ? new("daily", "自主日常", "#5B805F", 10)
                                    : motion.PrototypeUse || !motion.RuntimeEnabled
                                        ? new("candidate", "候选预览", "#7A756D", 90)
                                        : new("interaction", "主人互动", "#B56A3D", 40);

    private static int MechanismCategoryOrder(string key) => key switch
    {
        "daily" => 10,
        "sleep" => 20,
        "walk" => 30,
        "interaction" => 40,
        "command" => 50,
        "magic" => 60,
        "play" => 70,
        _ => 90
    };

    private static string TriggerMechanismFor(PlayableMotion motion, bool autonomous)
    {
        if (motion.Effect is DesktopMotionEffect.BroomFlight or DesktopMotionEffect.Apparate or
            DesktopMotionEffect.Petrify or DesktopMotionEffect.PetrifyRelease)
            return "主人魔法：右键 / 面板 / 对话";
        if (motion.Effect == DesktopMotionEffect.CarRide)
            return "主人邀请：右键 / 面板 / 对话";
        if (string.Equals(motion.AssetBatch, FoodWaterCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            return "主人互动：吃一下 / 面板 / 对话";
        if (string.Equals(motion.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            return "自主日常 / 对话“走一走”";
        if (string.Equals(motion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase))
            return "自主日常 / 对话“睡觉”";
        if (autonomous)
            return "自主日常；面板可手动展示";
        return "主人右键 / 面板触发";
    }

    private static string FormatDuration(TimeSpan duration) => duration <= TimeSpan.Zero
        ? "无"
        : duration.TotalSeconds < 60
            ? $"{duration.TotalSeconds:0.#} 秒"
            : $"{duration.TotalMinutes:0.#} 分钟";

    private static string FormatPosture(IEnumerable<StablePosture> postures) =>
        string.Join("/", postures.OrderBy(value => value).Select(posture => FormatPosture(posture)));

    private static string FormatPosture(StablePosture posture) => posture switch
    {
        StablePosture.Stand => "站姿",
        StablePosture.Sit => "坐姿",
        _ => "趴姿"
    };

    private static string DisplaySource(BehaviorRequestSource source) => source switch
    {
        BehaviorRequestSource.AutonomousTick => "自主",
        BehaviorRequestSource.OwnerDialogue => "对话",
        BehaviorRequestSource.OwnerContextMenu => "右键",
        BehaviorRequestSource.ControlPanel => "面板",
        BehaviorRequestSource.OwnerUi => "主人",
        BehaviorRequestSource.DeveloperPreview => "预览",
        _ => source.ToString()
    };

    private void StartEpisodeForMotion(PlayableMotion motion, Guid correlationId, BehaviorRequestSource source)
    {
        var episode = motion.Effect switch
        {
            DesktopMotionEffect.BroomFlight or DesktopMotionEffect.Apparate or DesktopMotionEffect.Petrify or DesktopMotionEffect.PetrifyRelease or DesktopMotionEffect.Scourgify => PetEpisodeKind.MagicActivity,
            DesktopMotionEffect.CarRide => PetEpisodeKind.VehicleActivity,
            _ when string.Equals(motion.AssetBatch, SleepCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) => PetEpisodeKind.Sleeping,
            _ when string.Equals(motion.AssetBatch, WakeRiseCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) => PetEpisodeKind.Recovering,
            _ when string.Equals(motion.AssetBatch, FoodWaterCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) && motion.BehaviorId == FoodWaterCandidateBehaviorIds.EatKibbleStandingV5 => PetEpisodeKind.Eating,
            _ when string.Equals(motion.AssetBatch, FoodWaterCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) => PetEpisodeKind.Drinking,
            _ when string.Equals(motion.AssetBatch, PatrolWalkCandidateBehaviorIds.AssetBatch, StringComparison.OrdinalIgnoreCase) => PetEpisodeKind.Exploring,
            _ when source is BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel or BehaviorRequestSource.OwnerDialogue or BehaviorRequestSource.OwnerUi => PetEpisodeKind.OwnerInteraction,
            _ => _petAgentState.Episode.Kind
        };
        if (episode == _petAgentState.Episode.Kind)
            return;
        var state = BehaviorEpisodeCatalog.Start(episode, _now(), $"behavior_started:{motion.BehaviorId}", correlationId);
        ReduceAgentState(new PetEpisodeChanged(
            state.StartedAt,
            state.Kind,
            state.MinimumDwell,
            state.ReasonCode,
            state.CorrelationId), "episode_started");
        Trace("episode_started", $"episode={episode} id={state.EpisodeId} correlation={correlationId} behavior={motion.BehaviorId}");
    }

    private void ReduceAgentState(PetAgentEvent agentEvent, string reason)
    {
        var before = _petAgentState;
        var reduced = _stateReducer.Reduce(before, agentEvent);
        if (ReferenceEquals(before, reduced) || before == reduced)
            return;
        _petAgentState = reduced.Clamp();
        QueueAgentStatePersistence(reason);
    }

    private void QueueAgentStatePersistence(string reason)
    {
        var store = _agentStateStore;
        if (store is null || _currentExecutionMode != BehaviorExecutionMode.Normal)
            return;
        var snapshot = (_petAgentState with
        {
            Runtime = _petAgentState.Runtime with
            {
                ActiveExecutionId = null,
                ActiveActionId = null,
                ActiveActionStartedAt = null,
                CurrentPhase = "idle",
                IsBusy = false,
                IsInterruptible = true
            }
        }).Clamp();
        lock (_agentStatePersistenceLock)
        {
            _agentStatePersistence = _agentStatePersistence.ContinueWith(
                    _ => store.SaveAsync(snapshot),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap()
                .ContinueWith(task =>
                {
                    if (task.IsFaulted)
                        Trace("agent_state_save_failed", $"reason={reason} error={task.Exception?.GetBaseException().GetType().Name}");
                }, TaskScheduler.Default);
        }
    }

    private void FinishReducerOwnedExecution(ExecutionStatus status, double completionRatio, string reasonCode)
    {
        var active = _activeReducerExecution;
        if (active is null)
            return;

        ReduceAgentState(new PetBehaviorFinished(
            _now(),
            active.Request.RequestId,
            active.Profile.BehaviorId,
            status,
            completionRatio,
            active.Profile.EndPosture,
            active.Profile.EndPoseId,
            active.Profile.StateEffects,
            active.Profile.OwnerInteraction,
            active.Profile.MemoryEligibility,
            active.Profile.PartialEffectPolicy,
            active.Request.Source,
            BehaviorExecutionMode.Normal,
            reasonCode), "behavior_finished");
        if (_agentState.ActiveExecutionId is null)
        {
            _activeReducerExecution = null;
            Trace("behavior_reducer_outcome",
                $"behavior={active.Profile.BehaviorId} execution={active.Request.RequestId} status={status} completion={completionRatio:0.000} reason={reasonCode}");
            if (status == ExecutionStatus.Completed &&
                _petAgentState.Episode.Kind is PetEpisodeKind.Eating or PetEpisodeKind.Drinking or PetEpisodeKind.OwnerInteraction or PetEpisodeKind.VehicleActivity)
            {
                var resting = BehaviorEpisodeCatalog.Start(PetEpisodeKind.Resting, _now(),
                    $"episode_completed:{_petAgentState.Episode.Kind}", active.Request.CorrelationId);
                ReduceAgentState(new PetEpisodeChanged(
                    resting.StartedAt,
                    resting.Kind,
                    resting.MinimumDwell,
                    resting.ReasonCode,
                    resting.CorrelationId), "episode_completed");
                Trace("episode_completed", $"episode={resting.Kind} id={resting.EpisodeId} correlation={resting.CorrelationId}");
            }
        }
    }

    private bool RestorePreviewAgentState(string reason)
    {
        if (_previewAgentStateSnapshot is null)
            return false;
        _petAgentState = _previewAgentStateSnapshot;
        _previewAgentStateSnapshot = null;
        Trace("behavior_preview_state_restored", $"reason={reason}");
        OnPropertyChanged(nameof(CurrentStablePosture));
        OnPropertyChanged(nameof(BehaviorAgentSnapshot));
        RaiseMetrics();
        return true;
    }

    private double EstimateCurrentCompletionRatio()
    {
        var active = _activeReducerExecution;
        if (active is null || active.EstimatedDuration <= TimeSpan.Zero)
            return 0;
        return Math.Clamp((_now() - _currentStartedAt).TotalMilliseconds / active.EstimatedDuration.TotalMilliseconds, 0, 1);
    }

    private static TimeSpan EstimateMotionDuration(PlayableMotion motion, int loopCycles)
    {
        long durationMs = 0;
        foreach (var phase in motion.Phases)
        {
            var cycles = phase.Loop ? Math.Max(1, loopCycles) : 1;
            durationMs += (long)phase.DurationTotalMs(motion.FrameDurationMs) * cycles;
        }
        return TimeSpan.FromMilliseconds(Math.Max(1, durationMs));
    }

    private static RuntimeMode RuntimeModeFor(BehaviorExecutionMode executionMode) => executionMode switch
    {
        BehaviorExecutionMode.Normal => RuntimeMode.Production,
        BehaviorExecutionMode.PrototypePreview => RuntimeMode.Preview,
        BehaviorExecutionMode.DeveloperPreview => RuntimeMode.DeveloperForced,
        _ => RuntimeMode.Production
    };

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

    private bool CanSchedulePatrol(string behaviorId)
    {
        if (_agentState.CurrentPosture != StablePosture.Stand || _agentState.IsBusy)
            return false;
        if (PatrolWalkCandidateBehaviorIds.All.Any(id => _lastAccepted.TryGetValue(id, out var time) &&
            _now() - time < TimeSpan.FromSeconds(45)))
            return false;
        var preferredRight = _petFacesRight ? _patrolCanMoveRight : !_patrolCanMoveLeft;
        return behaviorId == PatrolWalkCandidateBehaviorIds.WalkRight
            ? preferredRight && _patrolCanMoveRight
            : !preferredRight && _patrolCanMoveLeft;
    }

    private (string BehaviorId, string Reason) ChooseAutonomousBehavior(int decisionOrdinal)
    {
        var hour = _now().Hour;
        var workQuiet = hour is >= 9 and <= 18;
        var elapsed = _now() - _currentStartedAt;
        var candidates = new List<(string BehaviorId, double Score, string Reason)>();
        switch (_agentState.CurrentPosture)
        {
            case StablePosture.Stand:
                AddCurrentOrPreferredStableIdle(candidates, LifecycleCandidateBehaviorIds.StandIdleMicroloop,
                    0.16 + Comfort * 0.08 + (1 - _agentState.Arousal) * 0.05 + (workQuiet ? 0.04 : 0.01), "autonomous:brief_stable_stand_microloop");
                if (_petAgentState.Episode.Kind is PetEpisodeKind.Observing or PetEpisodeKind.Socializing &&
                    IsStandingHappyExpectantProfileAllowed(_agentState.CurrentPosture, _agentState.CurrentPoseId, _agentState.IsBusy) &&
                    CanScheduleStandingHappyExpectant(_now(), _nextStandingHappyExpectantAt, StandingHappyExpectantBehaviorIds.HappyExpectant) &&
                    _agentState.MoodValence >= 0.52 && _agentState.Stress < 0.58 && _agentState.Energy >= 0.32)
                    AddIfEnabled(candidates, StandingHappyExpectantBehaviorIds.HappyExpectant,
                        0.08 + _agentState.MoodValence * 0.09 + _agentState.SocialNeed * 0.04,
                        "autonomous:low_frequency_positive_standing_expression");
                if (elapsed >= MinimumAutonomousDwell(StablePosture.Stand) && Energy >= 0.18 && Stress < 0.85)
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
                        if (CanSchedulePatrol(PatrolWalkCandidateBehaviorIds.WalkLeft))
                            AddIfEnabled(candidates, PatrolWalkCandidateBehaviorIds.WalkLeft,
                                0.08 + _agentState.Boredom * 0.08 + Curiosity * 0.05,
                                "autonomous:low_frequency_patrol_left");
                        if (CanSchedulePatrol(PatrolWalkCandidateBehaviorIds.WalkRight))
                            AddIfEnabled(candidates, PatrolWalkCandidateBehaviorIds.WalkRight,
                                0.08 + _agentState.Boredom * 0.08 + Curiosity * 0.05,
                                "autonomous:low_frequency_patrol_right");
                    }
                }
                break;
            case StablePosture.Sit:
                AddCurrentOrPreferredStableIdle(candidates, LifecycleCandidateBehaviorIds.SitIdleMicroloop,
                    0.82 + Comfort * 0.12 + (workQuiet ? 0.14 : 0.03), "autonomous:stable_sit_microloop");
                if (elapsed >= MinimumAutonomousDwell(StablePosture.Sit))
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
                    AddCurrentOrPreferredStableIdle(candidates, LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4,
                        0.90 + Comfort * 0.14 + (workQuiet ? 0.16 : 0.04), "autonomous:approved_v4_front_prone_calm");
                    var lickReady = !_lastAccepted.TryGetValue(LifecycleReviewCandidateBehaviorIds.FrontProneLickV4, out var lastLick) ||
                        _now() - lastLick >= TimeSpan.FromSeconds(45);
                    if (lickReady && string.Equals(_currentBehaviorId, LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4, StringComparison.OrdinalIgnoreCase))
                        AddIfEnabled(candidates, LifecycleReviewCandidateBehaviorIds.FrontProneLickV4,
                            0.08 + Curiosity * 0.06 + Mood * 0.04, "autonomous:approved_v4_single_lick_microevent");
                    if (IsFrontProneExpressionProfileAllowed(
                            _agentState.CurrentPosture,
                            _agentState.CurrentPoseId,
                            _frontProneProfileActive,
                            _agentState.IsBusy) &&
                        string.Equals(_currentBehaviorId, LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4, StringComparison.OrdinalIgnoreCase) &&
                        _now() >= _nextFrontProneExpressionAt)
                    {
                        var calmFactor = Math.Clamp(1.0 - Stress * 0.75, 0.15, 1.0);
                        var awakeFactor = Math.Clamp((Energy + _agentState.Arousal) / 1.25, 0.20, 1.0);
                        AddFrontProneExpressionIfReady(candidates, FrontProneExpressionBehaviorIds.SatisfiedSmile,
                            0.50 * calmFactor * (0.75 + Mood * 0.45), "autonomous:front_prone_satisfied_smile");
                        AddFrontProneExpressionIfReady(candidates, FrontProneExpressionBehaviorIds.CuriousObserve,
                            0.30 * calmFactor * awakeFactor * (0.70 + Curiosity * 0.60 + _agentState.Boredom * 0.25), "autonomous:front_prone_curious_observe");
                        AddFrontProneExpressionIfReady(candidates, FrontProneExpressionBehaviorIds.KnowingLook,
                            0.20 * calmFactor * awakeFactor * (0.75 + _agentState.Focus * 0.35), "autonomous:front_prone_knowing_look");
                        if (IsProneHappyHotPantingProfileAllowed(
                                _agentState.CurrentPosture,
                                _agentState.CurrentPoseId,
                                _frontProneProfileActive,
                                _agentState.IsBusy,
                                Mood,
                                Stress))
                            AddFrontProneExpressionIfReady(candidates, ProneHappyHotPantingBehaviorIds.HappyHotPanting,
                                0.12 * calmFactor * awakeFactor * (0.65 + Mood * 0.45),
                                "autonomous:front_prone_happy_hot_panting");
                    }
                    if (elapsed >= TimeSpan.FromSeconds(18) &&
                        Energy < 0.62 &&
                        Stress < 0.72 &&
                        IsSleepAutonomousProfileAllowed(SleepCandidateBehaviorIds.SprawledFrontBreath, _agentState.CurrentPosture, _frontProneProfileActive))
                        AddIfEnabled(candidates, SleepCandidateBehaviorIds.SprawledFrontBreath,
                            0.06 + (1 - Energy) * 0.08 + Comfort * 0.04,
                            "autonomous:approved_front_sleep_breath_from_compatible_profile");
                }
                else
                {
                    AddCurrentOrPreferredStableIdle(candidates, LifecycleCandidateBehaviorIds.ProneIdleMicroloop,
                        0.86 + Comfort * 0.14 + (workQuiet ? 0.16 : 0.04), "autonomous:stable_prone_microloop");
                    if (elapsed >= MinimumAutonomousDwell(StablePosture.Prone) &&
                        IsProneHeadAutonomousProfileAllowed(_agentState.CurrentPosture, _frontProneProfileActive))
                    {
                        AddIfEnabled(candidates, ProneHeadCandidateBehaviorIds.HeadLowerTurnV4,
                            0.12 + Curiosity * 0.08 + Mood * 0.03,
                            "autonomous:approved_prone_head_lower_turn_microevent");
                        AddIfEnabled(candidates, AutonomousDailyCandidateBehaviorIds.ProneToSit,
                            0.07 + Energy * 0.05 + _agentState.Arousal * 0.03,
                            "autonomous:low_frequency_prone_to_sit_transition");
                        if (Energy < 0.58 &&
                            Stress < 0.72 &&
                            IsSleepAutonomousProfileAllowed(SleepCandidateBehaviorIds.MainLifecycle, _agentState.CurrentPosture, _frontProneProfileActive))
                            AddIfEnabled(candidates, SleepCandidateBehaviorIds.MainLifecycle,
                                0.07 + (1 - Energy) * 0.10 + Comfort * 0.04,
                                "autonomous:approved_sleep_lifecycle_from_compatible_prone_profile");
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
            if (IsPostureTransitionBehavior(candidate.BehaviorId) &&
                _lastAccepted.Any(x => IsPostureTransitionBehavior(x.Key) && _now() - x.Value < TimeSpan.FromSeconds(90)))
                penalty *= 0.35;
            var ownerPreference = AutonomousBehaviorWeightFor(candidate.BehaviorId, _autonomousPreferences);
            return candidate with { Score = Math.Max(0.05, candidate.Score * penalty * ownerPreference) };
        }).ToArray();
        var total = adjusted.Sum(x => x.Score);
        var decisionRandom = new Random(CombineDecisionSeed(
            _decisionSeed,
            decisionOrdinal,
            (int)_agentState.CurrentPosture));
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
        StablePosture.Stand => TimeSpan.FromSeconds(random.Next(14, 26)),
        StablePosture.Sit => TimeSpan.FromSeconds(random.Next(30, 53)),
        _ => TimeSpan.FromSeconds(random.Next(55, 96))
    };

    public static TimeSpan MinimumAutonomousDwell(StablePosture posture) => posture switch
    {
        StablePosture.Stand => TimeSpan.FromSeconds(14),
        StablePosture.Sit => TimeSpan.FromSeconds(24),
        _ => TimeSpan.FromSeconds(35)
    };

    public static bool ShouldKeepCurrentStableIdle(string currentBehaviorId, string selectedBehaviorId) =>
        string.Equals(currentBehaviorId, selectedBehaviorId, StringComparison.OrdinalIgnoreCase) &&
        IsStableIdleBehavior(currentBehaviorId);

    public static bool IsPostureTransitionBehavior(string behaviorId) => behaviorId is
        AutonomousDailyCandidateBehaviorIds.StandToSit or
        AutonomousDailyCandidateBehaviorIds.SitToProne or
        AutonomousDailyCandidateBehaviorIds.ProneToSit or
        AutonomousDailyCandidateBehaviorIds.SitToStand or
        LifecycleCandidateBehaviorIds.LivelyDailyP2 or
        LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1;

    public static double AutonomousBehaviorWeightFor(
        string behaviorId,
        AutonomousBehaviorPreferences preferences)
    {
        var normalized = preferences.Clamp();
        if (behaviorId is PatrolWalkCandidateBehaviorIds.WalkLeft or PatrolWalkCandidateBehaviorIds.WalkRight)
            return normalized.WalkingWeight;
        if (behaviorId is SleepCandidateBehaviorIds.MainLifecycle or SleepCandidateBehaviorIds.SprawledFrontBreath)
            return normalized.SleepingWeight;
        if (behaviorId is LifecycleCandidateBehaviorIds.StandIdleMicroloop or LifecycleReviewCandidateBehaviorIds.StandIdleV3R1)
            return normalized.StandingIdleWeight;
        if (behaviorId is LifecycleCandidateBehaviorIds.LivelyDailyP2 or LifecycleReviewCandidateBehaviorIds.LivelyDailyV3R1)
            return normalized.ProneRestWeight * 0.20;
        if (behaviorId is LifecycleCandidateBehaviorIds.ProneIdleMicroloop or LifecycleReviewCandidateBehaviorIds.LegacySideProneIdleV3R1)
            return normalized.ProneRestWeight * 0.45;
        if (behaviorId == FrontProneExpressionBehaviorIds.SatisfiedSmile) return 1.20;
        if (behaviorId == FrontProneExpressionBehaviorIds.CuriousObserve) return 1.05;
        if (behaviorId == FrontProneExpressionBehaviorIds.KnowingLook) return 0.90;
        if (behaviorId == ProneHappyHotPantingBehaviorIds.HappyHotPanting) return 0.85;
        if (behaviorId is Phase15BehaviorIds.ProneIdle or
            LifecycleReviewCandidateBehaviorIds.FrontProneIdleV4 or
            AutonomousDailyCandidateBehaviorIds.StandToSit or
            AutonomousDailyCandidateBehaviorIds.SitToProne)
            return normalized.ProneRestWeight;
        return 1.0;
    }

    private IReadOnlyDictionary<string, double> BuildAutonomousBehaviorWeightMultipliers() =>
        _behaviorCapabilities.Capabilities.ToDictionary(
            item => item.BehaviorId,
            item => AutonomousBehaviorWeightFor(item.BehaviorId, _autonomousPreferences),
            StringComparer.OrdinalIgnoreCase);

    public static int ChooseAutonomousProneLoopCycles(Random random) => random.Next(1, 3);

    private static int CombineDecisionSeed(int seed, int decisionCount, int posture)
    {
        unchecked
        {
            var combined = (uint)seed;
            combined ^= 0x9e3779b9u + (uint)decisionCount + (combined << 6) + (combined >> 2);
            combined ^= 0x9e3779b9u + (uint)posture + (combined << 6) + (combined >> 2);
            combined ^= combined >> 16;
            combined *= 0x7feb352du;
            combined ^= combined >> 15;
            combined *= 0x846ca68bu;
            combined ^= combined >> 16;
            return (int)combined;
        }
    }


    private void AddIfEnabled(List<(string BehaviorId, double Score, string Reason)> candidates, string behaviorId, double score, string reason)
    {
        if (AutonomousRuntimeAllowlist.Contains(behaviorId) &&
            _catalog.Find(behaviorId) is { RuntimeEnabled: true, AutonomousBindingEnabled: true })
            candidates.Add((behaviorId, Math.Max(0.05, score), reason));
    }

    private void AddFrontProneExpressionIfReady(
        List<(string BehaviorId, double Score, string Reason)> candidates,
        string behaviorId,
        double score,
        string reason)
    {
        if (!CanScheduleFrontProneExpression(_now(), _nextFrontProneExpressionAt, behaviorId, _recentFrontProneExpressions))
            return;
        AddIfEnabled(candidates, behaviorId, score, reason);
    }

    private void AddCurrentOrPreferredStableIdle(
        List<(string BehaviorId, double Score, string Reason)> candidates,
        string preferredBehaviorId,
        double score,
        string reason)
    {
        var current = IsStableIdleBehavior(_currentBehaviorId) &&
                      _catalog.Find(_currentBehaviorId) is { RuntimeEnabled: true, AutonomousBindingEnabled: true }
            ? _currentBehaviorId
            : preferredBehaviorId;
        AddIfEnabled(candidates, current, score, reason);
    }

    public static bool IsAutonomousRuntimeBehaviorAllowed(string behaviorId) =>
        AutonomousRuntimeAllowlist.Contains(behaviorId);

    public static bool IsProneHeadAutonomousProfileAllowed(StablePosture posture, bool frontProneProfileActive) =>
        posture == StablePosture.Prone && !frontProneProfileActive;

    public static bool IsFrontProneExpressionProfileAllowed(
        StablePosture posture,
        string? poseId,
        bool frontProneProfileActive,
        bool isBusy) =>
        posture == StablePosture.Prone &&
        frontProneProfileActive &&
        !isBusy &&
        string.Equals(PetPoseCompatibility.FamilyFor(poseId, posture), "prone.front", StringComparison.OrdinalIgnoreCase);

    public static bool CanScheduleFrontProneExpression(
        DateTimeOffset now,
        DateTimeOffset nextAllowedAt,
        string behaviorId,
        IReadOnlyCollection<string> recentExpressions) =>
        now >= nextAllowedAt &&
        (FrontProneExpressionBehaviorIds.All.Contains(behaviorId) ||
         string.Equals(behaviorId, ProneHappyHotPantingBehaviorIds.HappyHotPanting, StringComparison.OrdinalIgnoreCase)) &&
        !recentExpressions.Contains(behaviorId, StringComparer.OrdinalIgnoreCase);

    public static bool IsProneHappyHotPantingProfileAllowed(
        StablePosture posture,
        string? poseId,
        bool frontProneProfileActive,
        bool isBusy,
        double mood,
        double stress) =>
        IsFrontProneExpressionProfileAllowed(posture, poseId, frontProneProfileActive, isBusy) &&
        mood >= 0.55 &&
        stress <= 0.65;

    public static bool IsStandingHappyExpectantProfileAllowed(
        StablePosture posture,
        string? poseId,
        bool isBusy) =>
        posture == StablePosture.Stand &&
        !isBusy &&
        string.Equals(poseId, "stand.neutral.left_front", StringComparison.OrdinalIgnoreCase);

    public static bool CanScheduleStandingHappyExpectant(
        DateTimeOffset now,
        DateTimeOffset nextAllowedAt,
        string behaviorId) =>
        now >= nextAllowedAt &&
        string.Equals(behaviorId, StandingHappyExpectantBehaviorIds.HappyExpectant, StringComparison.OrdinalIgnoreCase);

    public static bool IsSleepAutonomousProfileAllowed(string behaviorId, StablePosture posture, bool frontProneProfileActive) =>
        SleepCandidateBehaviorIds.AutonomousAllowed.Contains(behaviorId) &&
        posture == StablePosture.Prone && behaviorId switch
        {
            SleepCandidateBehaviorIds.MainLifecycle => !frontProneProfileActive,
            SleepCandidateBehaviorIds.SprawledFrontBreath => frontProneProfileActive,
            _ => false
        };

    private void UpdateDecision(PetActionResult result, string source, string reasonCode, string userFacing)
    {
        CurrentDisposition = result switch
        {
            PetActionResult.Accepted => "愿意",
            PetActionResult.Deferred => "稍后",
            PetActionResult.Rejected => "暂时不想",
            PetActionResult.MissingAsset => "暂时做不到",
            PetActionResult.Interrupted => "已经停下",
            _ => "没有完成"
        };
        CurrentReason = userFacing;
        CurrentDecisionDetail = $"原因代码：{reasonCode}";
        LastSource = source switch
        {
            "OwnerContextMenu" => "右键菜单",
            "ControlPanel" => "控制面板",
            "Dialogue" => "对话",
            "OwnerDialogue" => "对话",
            "AutonomousTick" => "自主行为",
            "DeveloperPreview" => "开发者预览",
            "Startup" => "启动",
            _ => source
        };
        LastTrigger = reasonCode;
        LastError = result == PetActionResult.MissingAsset ? userFacing : LastError;
        Willingness = result switch
        {
            PetActionResult.Accepted => $"悟空愿意回应，现在正{CurrentAction}",
            PetActionResult.Deferred => "悟空想先保持现在的状态，过一会儿再回应",
            PetActionResult.Rejected => "悟空现在想安静一会儿",
            PetActionResult.MissingAsset => "这个动作暂时还不能完整展示",
            PetActionResult.Interrupted => "悟空已经停下，回到舒服的姿态",
            _ => "刚才的动作没有完成，悟空已经恢复稳定"
        };
        OnPropertyChanged(nameof(CurrentDisposition));
        OnPropertyChanged(nameof(CurrentReason));
        OnPropertyChanged(nameof(CurrentDecisionDetail));
        OnPropertyChanged(nameof(LastSource));
        OnPropertyChanged(nameof(LastTrigger));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(Willingness));
        Trace("decision", $"{result} source={source} reason={reasonCode}");
    }

    private void UpdateCommandDecision(
        PetActionResult result,
        BehaviorRequestSource source,
        ParticipationDecision decision,
        string actionId)
    {
        var retry = decision.RetryAt is { } retryAt
            ? $" · 可在 {retryAt:HH:mm:ss} 后再试"
            : string.Empty;
        UpdateDecision(result, source.ToString(), decision.ReasonCode, decision.UserFacingReason + retry);
        var componentLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["command_cooperativeness"] = "服从基线",
            ["relationship_trust"] = "信任",
            ["relationship_familiarity"] = "熟悉度",
            ["current_mood"] = "心情",
            ["available_energy"] = "精力",
            ["stress_safety"] = "压力安全",
            ["repetition_penalty"] = "重复疲劳"
        };
        var factors = decision.Components
            .OrderByDescending(item => Math.Abs(item.Value))
            .Take(3)
            .Select(item => componentLabels.GetValueOrDefault(item.Key, item.Key))
            .ToArray();
        CurrentDecisionDetail = $"口令 {actionId} · 意愿 {decision.WillingnessScore:P0}" +
            (factors.Length == 0 ? string.Empty : $" · 主要因素：{string.Join("、", factors)}");
        Willingness = result switch
        {
            PetActionResult.Accepted => $"接受 · {decision.UserFacingReason}",
            PetActionResult.Deferred => $"延后 · {decision.UserFacingReason}",
            PetActionResult.Rejected => $"拒绝 · {decision.UserFacingReason}",
            _ => Willingness
        };
        OnPropertyChanged(nameof(CurrentDecisionDetail));
        OnPropertyChanged(nameof(Willingness));
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
        OnPropertyChanged(nameof(AgentMoodProjection));
        RaiseAgentProfileProjection();
    }

    private void RaiseAgentProfileProjection()
    {
        OnPropertyChanged(nameof(TemperamentActivity));
        OnPropertyChanged(nameof(TemperamentAttachment));
        OnPropertyChanged(nameof(TemperamentSensitivity));
        OnPropertyChanged(nameof(TemperamentIndependence));
        OnPropertyChanged(nameof(TemperamentMischief));
        OnPropertyChanged(nameof(RelationshipTrust));
        OnPropertyChanged(nameof(RelationshipFamiliarity));
        OnPropertyChanged(nameof(RelationshipTouchAcceptance));
        OnPropertyChanged(nameof(RelationshipInitiativeAcceptance));
        OnPropertyChanged(nameof(RecentPositiveInteractions));
        OnPropertyChanged(nameof(RecentNegativeInteractions));
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private string BuildBehaviorAgentSnapshot()
    {
        var decision = _lastAgentDecision is null
            ? "none"
            : $"{_lastAgentDecision.SelectedActionId} {_lastAgentDecision.StartPosture}->{_lastAgentDecision.EndPosture} mood={_lastAgentDecision.MoodExpression} style={_lastAgentDecision.DialogueStyle}";
        return $"enabled={EnableBehaviorAgentMock}; posture={_agentState.CurrentPosture}; energy={_agentState.Energy:0.00}; hunger={_agentState.Hunger:0.00}; social={_agentState.SocialNeed:0.00}; boredom={_agentState.Boredom:0.00}; stress={_agentState.Stress:0.00}; mood={_agentState.MoodValence:0.00}; arousal={_agentState.Arousal:0.00}; temperament=({_temperament.Activity},{_temperament.Attachment},{_temperament.Sensitivity},{_temperament.Independence},{_temperament.Mischief}); autonomous_preferences=(walk={_autonomousPreferences.WalkingWeight:0.00},prone={_autonomousPreferences.ProneRestWeight:0.00},sleep={_autonomousPreferences.SleepingWeight:0.00},stand={_autonomousPreferences.StandingIdleWeight:0.00}); decision_memory={_petAgentState.DecisionMemory.Fingerprint[..Math.Min(12, _petAgentState.DecisionMemory.Fingerprint.Length)]}; memory_evidence=(conversation={_petAgentState.DecisionMemory.EvidenceCounts.GetValueOrDefault("confirmed_conversation")},album={_petAgentState.DecisionMemory.EvidenceCounts.GetValueOrDefault("album_description")}); initiative_unanswered={_petAgentState.InitiativeSpeechFeedback.ConsecutiveUnanswered}; last_decision={decision}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
