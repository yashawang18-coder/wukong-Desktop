using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
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
    MissingAsset
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

public sealed record MotionPhase(string Name, IReadOnlyList<string> Frames, bool Loop);

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
    string Disposition = "Enabled")
{
    public bool IsUsable => Phases.Any(x => x.Frames.Count > 0);
    public string FirstFrame => Phases.SelectMany(x => x.Frames).FirstOrDefault() ?? string.Empty;
    public string FirstFrameFileName => Path.GetFileName(FirstFrame);
    public int FrameCount => Phases.Sum(x => x.Frames.Count);
    public double Fps => FrameDurationMs <= 0 ? 0 : 1000.0 / FrameDurationMs;
    public string PreviewStatus => IsUsable ? "Preview ready" : "Missing frames";
    public string RuntimeStatus => RuntimeEnabled ? "Runtime enabled" : "Preview only / locked";
    public string PhaseSummary => string.Join(" / ", Phases.Select(x => $"{x.Name}:{x.Frames.Count}"));
}

public sealed class DesktopMotionCatalog
{
    private readonly Dictionary<string, PlayableMotion> _motions;

    private DesktopMotionCatalog(IEnumerable<PlayableMotion> motions) =>
        _motions = motions.Where(x => x.IsUsable).ToDictionary(x => x.BehaviorId, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PlayableMotion> Motions => _motions.Values.OrderBy(x => x.BehaviorId).ToList();

    public PlayableMotion? Find(string behaviorId) =>
        _motions.TryGetValue(behaviorId, out var motion) ? motion : null;

    public PlayableMotion RequiredIdle =>
        Find(Phase15BehaviorIds.ProneIdle) ??
        _motions.Values.FirstOrDefault() ??
        throw new InvalidOperationException("No playable Wukong motion assets were found.");

    public static DesktopMotionCatalog Load(string baseDirectory)
    {
        var root = Path.Combine(baseDirectory, "WukongAssets");
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
                status: "Runtime: visible approved breathing keyframes",
                disposition: "Enabled"),
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
                status: "Preview only: 24 frames are too subtle for visible idle",
                missing: "visible breathing amplitude review",
                disposition: "Preview only"),
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
                status: "Runtime: pose and style compatible",
                disposition: "Enabled"),
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
                status: "Preview only: pose and facing transition missing",
                missing: "transition_in / transition_out / interrupt_exit",
                startPose: "stand.neutral.left_front",
                endPose: "stand.neutral.right_front",
                disposition: "Transition locked"),
            Motion(
                Phase15BehaviorIds.SideSleepBreath,
                "Side sleep breathing",
                "Autonomous",
                "left-side-lying",
                260,
                true,
                root,
                "actions/WK-CORE-SLEEP-BREATH-v2/approved-keyframes/v1",
                loop: true,
                runtimeEnabled: false,
                status: "Preview only: sleep pose lacks safe enter/exit",
                missing: "fall_asleep / wake_up / interrupt_exit",
                startPose: "sleep.side.right",
                endPose: "sleep.side.right",
                disposition: "Runtime disabled"),
            Motion(
                Phase15BehaviorIds.EnterSleep,
                "Enter sleep",
                "Autonomous",
                "left-side-lying",
                260,
                true,
                root,
                "actions/WK-CORE-SLEEP-BREATH-v2/approved-keyframes/v1",
                loop: false,
                runtimeEnabled: false,
                status: "Runtime disabled: missing prone-to-sleep intro/exit",
                missing: "fall_asleep / wake_up / interrupt_exit",
                startPose: "sleep.side.right",
                endPose: "sleep.side.right",
                disposition: "Runtime disabled"),
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
                status: "Preview only: return transition and renderer QA needed",
                missing: "stand_to_prone interrupt_exit / renderer QA",
                startPose: "prone.awake.left_front",
                endPose: "stand.neutral.left_front",
                disposition: "Transition locked"),
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
                status: "Preview only: 3 approved keyframes need full runtime sequence",
                missing: "intro / loop / exit / interrupt_exit",
                disposition: "Preview only"),
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
                status: "Preview only: not part of runtime touch chain",
                missing: "runtime compatibility review",
                disposition: "Preview only"),
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
                status: "Preview only: interrupt exit missing",
                missing: "interrupt_exit",
                startPose: "stand.neutral.left_front",
                endPose: "prone.awake.left_front",
                disposition: "Transition locked"),
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
                status: "Preview only: missing walk_start / walk_stop / safe_interrupt_exit",
                missing: "intro / exit / interrupt_exit",
                startPose: "stand.neutral.left_front",
                endPose: "stand.neutral.left_front",
                disposition: "Transition locked")
        };

        return new DesktopMotionCatalog(motions);
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
            touchRoot);
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
}

public static class Phase15BehaviorIds
{
    public const string ProneIdle = "wk.phase15.prone_idle";
    public const string ProneBreath = "wk.phase15.prone_breath";
    public const string ProneIdleV3Candidate = "wk.phase15.prone_idle_v3_candidate";
    public const string LookAround = "wk.phase15.look_around";
    public const string SideSleepBreath = "wk.phase15.side_sleep_breath";
    public const string EnterSleep = "wk.phase15.enter_sleep";
    public const string SafeStand = "wk.phase15.safe_stand";
    public const string StrokeEnjoy = "wk.phase15.stroke_enjoy";
    public const string ProneTouch = "wk.phase15.prone_touch";
}

public sealed record PetMotionRequest(PlayableMotion Motion, string Trigger, bool ReturnToIdle, int LoopCycles);

public sealed class DesktopRuntimeHost : INotifyPropertyChanged
{
    private readonly DesktopMotionCatalog _catalog;
    private readonly Random _random = new(1508);
    private readonly Dictionary<string, DateTimeOffset> _lastAccepted = new(StringComparer.OrdinalIgnoreCase);
    private readonly RollingFileLogStore _logs = RollingFileLogStore.CreateDefault();
    private DateTimeOffset _lastTapAt = DateTimeOffset.MinValue;
    private int _tapBurst;
    private DateTimeOffset _currentStartedAt = DateTimeOffset.MinValue;
    private string _currentBehaviorId = Phase15BehaviorIds.ProneIdle;
    private bool _currentInterruptible = true;

    public DesktopRuntimeHost()
    {
        _catalog = DesktopMotionCatalog.Load(AppContext.BaseDirectory);
        CurrentAsset = _catalog.RequiredIdle.FirstFrame;
        CurrentAction = _catalog.RequiredIdle.DisplayName;
        CurrentBehaviorId = _catalog.RequiredIdle.BehaviorId;
        Trace("phase15_registry_loaded", $"motions={_catalog.Motions.Count}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PetMotionRequest>? MotionRequested;

    public ObservableCollection<string> TraceLines { get; } = new();
    public IReadOnlyList<PlayableMotion> Motions => _catalog.Motions;

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
    public double Energy { get; private set; } = 0.68;
    public double Hunger { get; private set; } = 0.22;
    public double Mood { get; private set; } = 0.72;
    public double Curiosity { get; private set; } = 0.46;
    public double Social { get; private set; } = 0.52;
    public double Stress { get; private set; } = 0.12;
    public double Focus { get; private set; } = 0.58;
    public double Comfort { get; private set; } = 0.78;

    public PetActionResult StartIdle(string source = "Startup")
    {
        var idle = _catalog.RequiredIdle;
        Accept(idle, source, "idle_ready", returnToIdle: false, loopCycles: int.MaxValue);
        return PetActionResult.Accepted;
    }

    public Task RecordInputAsync(InputEvent inputEvent)
    {
        Trace("input", $"{inputEvent.Kind} source={inputEvent.Source}");
        return Task.CompletedTask;
    }

    public Task<PetActionResult> SubmitGestureAsync(PetGestureKind gesture, BehaviorRequestSource source)
    {
        var now = DateTimeOffset.Now;
        Trace("gesture", gesture.ToString());
        var behaviorId = gesture switch
        {
            PetGestureKind.OwnerTouch => Phase15BehaviorIds.ProneTouch,
            PetGestureKind.Stroke => Phase15BehaviorIds.StrokeEnjoy,
            PetGestureKind.RapidTap => Phase15BehaviorIds.LookAround,
            _ => Phase15BehaviorIds.ProneIdle
        };

        if (gesture == PetGestureKind.OwnerTouch)
        {
            var previousTapAt = _lastTapAt;
            _tapBurst = now - previousTapAt <= TimeSpan.FromMilliseconds(900) ? _tapBurst + 1 : 1;
            _lastTapAt = now;
            if (GestureInterpreter.IsRapidTap(now, previousTapAt, _tapBurst))
                behaviorId = Phase15BehaviorIds.LookAround;
        }

        return Task.FromResult(SubmitBehavior(source, behaviorId, $"gesture:{gesture}", priority: 6));
    }

    public Task<PetActionResult> SubmitContextMenuIntentAsync(SemanticIntent intent)
    {
        var behaviorId = intent.Kind switch
        {
            SemanticIntentKind.Touch => Phase15BehaviorIds.ProneTouch,
            SemanticIntentKind.Quiet or SemanticIntentKind.Stop => Phase15BehaviorIds.ProneIdle,
            _ => Phase15BehaviorIds.LookAround
        };
        return Task.FromResult(SubmitBehavior(BehaviorRequestSource.ContextMenu, behaviorId, $"menu:{intent.Kind}", priority: 5));
    }

    public Task<PetActionResult> SubmitOwnerCommandAsync(string command)
    {
        var behaviorId = command switch
        {
            "叫过来" => Phase15BehaviorIds.LookAround,
            "伸爪" => Phase15BehaviorIds.SafeStand,
            "摸摸" => Phase15BehaviorIds.ProneTouch,
            "喂食" => Phase15BehaviorIds.LookAround,
            "玩耍" => Phase15BehaviorIds.LookAround,
            "邀请外出" => Phase15BehaviorIds.SafeStand,
            "停下" => Phase15BehaviorIds.ProneIdle,
            _ => Phase15BehaviorIds.ProneIdle
        };
        return Task.FromResult(SubmitBehavior(BehaviorRequestSource.OwnerUi, behaviorId, $"owner_command:{command}", priority: 8, force: command == "停下"));
    }

    public Task SubmitAutonomousTickAsync()
    {
        Energy = Clamp01(Energy - 0.015);
        Hunger = Clamp01(Hunger + 0.006);
        Curiosity = Clamp01(Curiosity + 0.018);
        Comfort = Clamp01(Comfort + 0.004);
        RaiseMetrics();

        if (DateTimeOffset.Now - _currentStartedAt < TimeSpan.FromSeconds(14))
            return Task.CompletedTask;

        var choice = ChooseAutonomousBehavior();
        SubmitBehavior(BehaviorRequestSource.AutonomousTick, choice.BehaviorId, choice.Reason, priority: -5);
        return Task.CompletedTask;
    }

    public async Task SubmitFakeModelMessageAsync(string text)
    {
        var redacted = SensitiveDataRedactor.Redact(text);
        Reply = string.IsNullOrWhiteSpace(text) ? "让我再趴一会儿" : $"我听见了：{redacted}。让我再趴一会儿";
        Trace("model_reply", Reply);
        _logs.Append(RuntimeMode.Production, "model_response", new { Reply });
        OnPropertyChanged(nameof(Reply));
        await SubmitContextMenuIntentAsync(new SemanticIntent(SemanticIntentKind.ModelSuggested, Phase15BehaviorIds.LookAround));
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

        Mood = Clamp01(Mood + 0.01);
        Comfort = Clamp01(Comfort + 0.01);
        if (behaviorId == Phase15BehaviorIds.ProneTouch || behaviorId == Phase15BehaviorIds.StrokeEnjoy)
            Social = Clamp01(Social + 0.04);
        Trace("motion_completed", $"{behaviorId} phase={phase}");
        StartIdle("safe_return");
        RaiseMetrics();
    }

    private PetActionResult SubmitBehavior(BehaviorRequestSource source, string behaviorId, string trigger, int priority, bool force = false)
    {
        var motion = _catalog.Find(behaviorId);
        if (motion is null)
        {
            UpdateDecision(PetActionResult.MissingAsset, source.ToString(), "missing_asset", $"缺少素材：{behaviorId}");
            return PetActionResult.MissingAsset;
        }
        if (!force && !motion.RuntimeEnabled)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "runtime_locked", $"{trigger} locked: {motion.Status}");
            return PetActionResult.Deferred;
        }

        var now = DateTimeOffset.Now;
        if (!force && !_currentInterruptible && _currentBehaviorId != behaviorId)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "current_not_interruptible", "当前动作不能安全中断");
            return PetActionResult.Deferred;
        }

        if (!force && now - _currentStartedAt < TimeSpan.FromSeconds(3) && _currentBehaviorId != Phase15BehaviorIds.ProneIdle)
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "minimum_dwell", "当前动作还在最短驻留时间内");
            return PetActionResult.Deferred;
        }

        if (!force &&
            _lastAccepted.TryGetValue(behaviorId, out var last) &&
            now - last < TimeSpan.FromSeconds(source == BehaviorRequestSource.AutonomousTick ? 25 : 6))
        {
            UpdateDecision(PetActionResult.Deferred, source.ToString(), "cooldown", "动作冷却中，已延后");
            return PetActionResult.Deferred;
        }

        Accept(motion, source.ToString(), trigger, returnToIdle: behaviorId != Phase15BehaviorIds.ProneIdle, loopCycles: behaviorId == Phase15BehaviorIds.ProneIdle ? int.MaxValue : 2);
        return PetActionResult.Accepted;
    }

    private void Accept(PlayableMotion motion, string source, string reason, bool returnToIdle, int loopCycles)
    {
        _currentBehaviorId = motion.BehaviorId;
        _currentStartedAt = DateTimeOffset.Now;
        _currentInterruptible = motion.Interruptible;
        _lastAccepted[motion.BehaviorId] = _currentStartedAt;
        CurrentBehaviorId = motion.BehaviorId;
        CurrentAction = motion.DisplayName;
        LastTrigger = reason;
        LastError = "无";
        UpdateDecision(PetActionResult.Accepted, source, reason, "接受");
        MotionRequested?.Invoke(this, new PetMotionRequest(motion, reason, returnToIdle, loopCycles));
        Trace("motion_requested", $"{motion.BehaviorId} reason={reason}");
        OnPropertyChanged(nameof(CurrentBehaviorId));
        OnPropertyChanged(nameof(CurrentAction));
        OnPropertyChanged(nameof(LastTrigger));
        OnPropertyChanged(nameof(LastError));
    }

    private (string BehaviorId, string Reason) ChooseAutonomousBehavior()
    {
        var hour = DateTimeOffset.Now.Hour;
        var workQuiet = hour is >= 9 and <= 18;
        var candidates = new[]
        {
            (BehaviorId: Phase15BehaviorIds.ProneBreath, Score: Comfort + (workQuiet ? 0.35 : 0.10), Reason: "autonomous:comfort_breath"),
            (BehaviorId: Phase15BehaviorIds.ProneIdle, Score: 0.55 + Comfort * 0.25, Reason: "autonomous:quiet_idle")
        };

        return candidates
            .Select(x => x with { Score = x.Score + _random.NextDouble() * 0.08 })
            .OrderByDescending(x => x.Score)
            .First() is var selected
            ? (selected.BehaviorId, selected.Reason)
            : (Phase15BehaviorIds.ProneBreath, "autonomous:fallback_breath");
    }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
