using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    Scourgify
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
    IReadOnlyList<int>? FrameDurationsMs = null)
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
    string CandidateProfile = "")
{
    public bool IsUsable => Phases.Any(x => x.Frames.Count > 0);
    public string FirstFrame => Phases.SelectMany(x => x.Frames).FirstOrDefault() ?? string.Empty;
    public string FirstFrameFileName => Path.GetFileName(FirstFrame);
    public int FrameCount => Phases.Sum(x => x.Frames.Count);
    public double Fps => FrameDurationMs <= 0 ? 0 : 1000.0 / FrameDurationMs;
    public string PreviewStatus => IsUsable ? "Preview ready" : "Missing frames";
    public string RuntimeStatus => RuntimeEnabled ? "Runtime enabled" : "Preview only / locked";
    public string PhaseSummary => string.Join(" / ", Phases.Select(x => $"{x.Name}:{x.Frames.Count}"));
    public bool HasVariableFrameDurations => Phases.Any(x => x.HasVariableDurations);
}

public sealed class DesktopMotionCatalog
{
    private readonly Dictionary<string, PlayableMotion> _motions;

    private DesktopMotionCatalog(IEnumerable<PlayableMotion> motions, string loadSummary)
    {
        _motions = motions.Where(x => x.IsUsable).ToDictionary(x => x.BehaviorId, StringComparer.OrdinalIgnoreCase);
        LoadSummary = loadSummary;
    }

    public IReadOnlyList<PlayableMotion> Motions => _motions.Values.OrderBy(x => x.BehaviorId).ToList();
    public string LoadSummary { get; }

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

        var commandCandidates = LoadCommandCandidates(root).ToArray();
        var magicCandidates = LoadMagicCandidates(root).ToArray();
        var lifecycleCandidates = LoadLifecycleCandidates(root).ToArray();
        var summary = $"asset_root=WukongAssets; built_in={motions.Length}; command_candidates={commandCandidates.Length}; magic_candidates={magicCandidates.Length}; lifecycle_candidates={lifecycleCandidates.Length}; manifests=action-batches/WK-COMMAND-ACTION-CANDIDATES-v3/manifest.json,action-batches/{MagicBehaviorIds.AssetBatch}/manifest.json,action-batches/{LifecycleCandidateBehaviorIds.AssetBatch}/manifest.json";
        BootstrapLog.WriteRaw($"asset_catalog_loaded {summary}");
        return new DesktopMotionCatalog(motions.Concat(commandCandidates).Concat(magicCandidates).Concat(lifecycleCandidates), summary);
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
                ? "候选素材：验收失败，待返工"
                : "候选素材：待运行验收";

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
                Disposition: "Developer preview only");
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
                "??????",
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
                Disposition: "Developer candidate profile only",
                PrototypeUse: false,
                AssetBatch: manifest.BatchId,
                Description: action.Description,
                CandidateProfile: action.CandidateProfile ?? manifest.CandidateProfile);
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
                phases.Add(new MotionPhase(phase.Name, frames, phase.Loop));
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
                action.FrameDurationMs,
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
                    : null);
        }
    }

    private static DesktopMotionEffect ParseMagicEffect(string? value) =>
        Enum.TryParse<DesktopMotionEffect>(value, ignoreCase: true, out var effect)
            ? effect
            : DesktopMotionEffect.None;

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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

public static class LifecycleCandidateBehaviorIds
{
    public const string AssetBatch = "WK-RUNTIME-LIFECYCLE-MICROLOOPS-CANDIDATE-v2";
    public const string LivelyDailyP2 = "wk.candidate.lifecycle.lively_daily_p2";
    public const string StandIdleMicroloop = "wk.candidate.lifecycle.stand_idle_microloop";
    public const string SitIdleMicroloop = "wk.candidate.lifecycle.sit_idle_microloop";
    public const string ProneIdleMicroloop = "wk.candidate.lifecycle.prone_idle_microloop";
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

public static class InteractionBehaviorIds
{
    public const string EatOnce = "wk.interaction.eat_once";
    public const string PlayOnce = "wk.interaction.play_once";
}

public static class MagicBehaviorIds
{
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

public sealed record MagicMockBatchManifest(
    [property: JsonPropertyName("batch_id")] string BatchId,
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
    [property: JsonPropertyName("frames")] IReadOnlyList<CommandActionFrameManifest> Frames);

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
        TimeSpan.FromMilliseconds(800),
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
        Description: "Owner-only interactive petrification coin candidate");

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
    BehaviorExecutionMode ExecutionMode = BehaviorExecutionMode.Normal);

public sealed class DesktopRuntimeHost : INotifyPropertyChanged
{
    private readonly DesktopMotionCatalog _catalog;
    private readonly PetrifiedCoinAssets? _coinAssets;
    private readonly PetrifiedCoinOptions _coinOptions;
    private readonly Func<DateTimeOffset> _now;
    private readonly Random _random = new(1508);
    private readonly Dictionary<string, DateTimeOffset> _lastAccepted = new(StringComparer.OrdinalIgnoreCase);
    private readonly RollingFileLogStore _logs = RollingFileLogStore.CreateDefault();
    private DateTimeOffset _lastTapAt = DateTimeOffset.MinValue;
    private int _tapBurst;
    private DateTimeOffset _currentStartedAt = DateTimeOffset.MinValue;
    private string _currentBehaviorId = Phase15BehaviorIds.ProneIdle;
    private bool _currentInterruptible = true;
    private DateTimeOffset? _coinActivityAt;
    private BehaviorRequestSource _coinPreviewSource = BehaviorRequestSource.OwnerContextMenu;

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
        Trace("asset_catalog_loaded", $"{_catalog.LoadSummary}; motions={_catalog.Motions.Count}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PetMotionRequest>? MotionRequested;
    public event EventHandler<int>? PetPixelSizeRequested;

    public ObservableCollection<string> TraceLines { get; } = new();
    public IReadOnlyList<PlayableMotion> Motions => _catalog.Motions;
    public IReadOnlyList<PlayableMotion> MagicMotions => _catalog.Motions
        .Where(x => string.Equals(x.Category, "宠物魔法", StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.DisplayName)
        .ToArray();
    public IReadOnlyList<PlayableMotion> LifecycleCandidateMotions => _catalog.Motions
        .Where(x => string.Equals(x.Category, "??????", StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.BehaviorId)
        .ToArray();
    public bool IsPetrified { get; private set; }
    public bool IsCoinAssetsReady => _coinAssets is not null;
    public PetrifiedCoinState? CurrentCoinState { get; private set; }
    public PetrifiedCoinSide? CurrentCoinSide { get; private set; }

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
        Accept(idle, BehaviorRequestSource.OwnerUi, BehaviorExecutionMode.Normal, source, returnToIdle: false, loopCycles: int.MaxValue);
        return PetActionResult.Accepted;
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
        return Task.FromResult(SubmitBehavior(BehaviorRequestSource.OwnerContextMenu, behaviorId, $"menu:{intent.Kind}", priority: 5));
    }

    public Task<PetActionResult> SubmitOwnerCommandAsync(string command)
    {
        var behaviorId = ResolveOwnerCommandBehavior(command);
        if (command is "停下" or "停")
            return StopAsync("owner_command:stop");
        return Task.FromResult(SubmitBehavior(BehaviorRequestSource.OwnerContextMenu, behaviorId, $"owner_command:{command}", priority: 8));
    }

    public Task<PetActionResult> SubmitDeveloperMotionAsync(string behaviorId) =>
        Task.FromResult(SubmitBehavior(BehaviorRequestSource.DeveloperForced, behaviorId, $"developer_force:{behaviorId}", priority: 100, executionMode: BehaviorExecutionMode.DeveloperPreview, bypassRuntimeGate: true));

    public Task<PetActionResult> SubmitDeveloperCandidateMotionAsync(string behaviorId) =>
        Task.FromResult(SubmitBehavior(BehaviorRequestSource.DeveloperForced, behaviorId, $"developer_candidate:{behaviorId}", priority: 100, executionMode: BehaviorExecutionMode.DeveloperPreview, bypassRuntimeGate: true));

    public void RequestPetPixelSize(int pixels)
    {
        var clamped = Math.Clamp(pixels, 128, 256);
        PetPixelSizeRequested?.Invoke(this, clamped);
        Trace("developer_size", $"candidate_profile={LifecycleCandidateBehaviorIds.AssetBatch} pixels={clamped}");
    }

    public Task<PetActionResult> SubmitMagicAsync(string behaviorId, BehaviorRequestSource source)
    {
        if (behaviorId == MagicBehaviorIds.PetrificusTotalus && IsPetrified)
            behaviorId = MagicBehaviorIds.PetrificusRelease;
        return Task.FromResult(SubmitBehavior(source, behaviorId, $"magic:{behaviorId}", priority: 20, executionMode: BehaviorExecutionMode.PrototypePreview));
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
        Energy = Clamp01(Energy - 0.015);
        Hunger = Clamp01(Hunger + 0.006);
        Curiosity = Clamp01(Curiosity + 0.018);
        Comfort = Clamp01(Comfort + 0.004);
        RaiseMetrics();

        if (_now() - _currentStartedAt < TimeSpan.FromSeconds(14))
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

        Mood = Clamp01(Mood + 0.01);
        Comfort = Clamp01(Comfort + 0.01);
        if (behaviorId == Phase15BehaviorIds.ProneTouch || behaviorId == Phase15BehaviorIds.StrokeEnjoy)
            Social = Clamp01(Social + 0.04);
        Trace("motion_completed", $"{behaviorId} phase={phase}");
        StartIdle("safe_return");
        RaiseMetrics();
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
        var ownerExplicit = source is BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel;
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
        var loopCycles = behaviorId == Phase15BehaviorIds.ProneIdle || keepPetrified ? int.MaxValue : 2;
        Accept(motion, source, executionMode, trigger, returnToIdle: behaviorId != Phase15BehaviorIds.ProneIdle && !keepPetrified, loopCycles: loopCycles);
        return PetActionResult.Accepted;
    }

    private static (bool Allowed, string ReasonCode, string UserFacingReason) EvaluateGate(
        BehaviorRequestSource source,
        BehaviorExecutionMode executionMode,
        PlayableMotion motion)
    {
        if (executionMode == BehaviorExecutionMode.DeveloperPreview)
            return (true, "developer_preview", "开发者预览已允许");

        if (executionMode == BehaviorExecutionMode.PrototypePreview)
        {
            var sourceAllowed = source is BehaviorRequestSource.OwnerContextMenu or BehaviorRequestSource.ControlPanel;
            if (!sourceAllowed)
                return (false, "prototype_source_forbidden", "该入口不允许原型展示");
            if (!MagicBehaviorIds.PrototypeWhitelist.Contains(motion.BehaviorId))
                return (false, "prototype_not_whitelisted", "该行为不在魔法原型白名单中");
            if (!motion.PrototypeUse)
                return (false, "prototype_use_disabled", "该素材未开启原型展示");
            return (true, "prototype_preview_allowed", "原型展示已允许");
        }

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

    private void Accept(
        PlayableMotion motion,
        BehaviorRequestSource source,
        BehaviorExecutionMode executionMode,
        string reason,
        bool returnToIdle,
        int loopCycles)
    {
        _currentBehaviorId = motion.BehaviorId;
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
        MotionRequested?.Invoke(this, new PetMotionRequest(motion, reason, returnToIdle, loopCycles, source, executionMode));
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
        var hour = DateTimeOffset.Now.Hour;
        var workQuiet = hour is >= 9 and <= 18;
        var elapsed = _now() - _currentStartedAt;
        var candidates = new List<(string BehaviorId, double Score, string Reason)>
        {
            (Phase15BehaviorIds.ProneBreath, Comfort + (workQuiet ? 0.35 : 0.10), "autonomous:comfort_breath"),
            (Phase15BehaviorIds.ProneIdle, 0.55 + Comfort * 0.25, "autonomous:quiet_idle")
        };

        if (_catalog.Find(LifecycleCandidateBehaviorIds.ProneIdleMicroloop)?.RuntimeEnabled == true)
            candidates.Add((LifecycleCandidateBehaviorIds.ProneIdleMicroloop, 0.62 + Comfort * 0.16, "autonomous:stable_prone_microloop"));

        if (elapsed >= TimeSpan.FromSeconds(45) && _catalog.Find(LifecycleCandidateBehaviorIds.LivelyDailyP2)?.RuntimeEnabled == true)
            candidates.Add((LifecycleCandidateBehaviorIds.LivelyDailyP2, 0.34 + Curiosity * 0.32 + Mood * 0.10 - Stress * 0.20, "autonomous:low_frequency_lively_daily"));

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
