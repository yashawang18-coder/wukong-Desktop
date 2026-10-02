using System.Diagnostics;
using System.Windows;

namespace Wukong.Desktop;

public static class PatrolWalkTiming
{
    public static int DurationMs(PlayableMotion motion, int cycles) => motion.Phases
        .Sum(phase => phase.DurationTotalMs(motion.FrameDurationMs) * (phase.Loop ? Math.Max(1, cycles) : 1));

    // Integral of velocity: stationary contact holds, acceleration, cruise, braking.
    public static double TravelSeconds(MotionPhase phase, int frameMs, double elapsedMs)
    {
        var total = phase.DurationTotalMs(frameMs);
        var elapsed = Math.Clamp(elapsedMs, 0, total);
        if (phase.Name == "intro")
        {
            var hold = phase.DurationForFrame(0, frameMs);
            var active = Math.Max(1, total - hold);
            var t = Math.Max(0, elapsed - hold);
            return t * t / (2 * active * 1000);
        }
        if (phase.Name == "exit")
        {
            var active = Math.Max(1, total - phase.DurationForFrame(phase.Frames.Count - 1, frameMs));
            var t = Math.Min(active, elapsed);
            return (t - t * t / (2 * active)) / 1000;
        }
        return elapsed / 1000;
    }
}

public partial class MainWindow
{
    private Task? _patrolPlaybackTask;
    private bool _patrolStopRequested;

    private Point WalkingTransitionGround(PlayableMotion? motion, string path)
    {
        var baseline = 0.5;
        if (motion?.AssetBatch == PatrolWalkCandidateBehaviorIds.AssetBatch)
            baseline = 900.0 / 1024;
        else if (System.IO.File.Exists(path))
        {
            var metrics = MotionVisualSizer.Measure(path);
            baseline = (metrics.Bounds.Y + metrics.Bounds.Height) / (double)metrics.CanvasHeight;
        }
        return new Point(Left + Width / 2, Top + Height / 2 + PetImage.Height * (baseline - 0.5));
    }

    private async Task PlayPatrolTimelineAsync(PetMotionRequest request, CancellationToken token)
    {
        var motion = request.Motion;
        var cycles = request.LoopCycles == int.MaxValue ? 2 : Math.Max(1, request.LoopCycles);
        var area = WindowPlacement.CurrentWorkingArea(this);
        var start = ClampToWorkArea(new Point(Left, Top), area, Width, Height);
        var duration = TimeSpan.FromMilliseconds(PatrolWalkTiming.DurationMs(motion, cycles));
        var target = ChoosePatrolWalkTarget(start, area, Width, Height, motion.Direction, duration);
        var travelSeconds = motion.Phases.Sum(phase =>
            PatrolWalkTiming.TravelSeconds(phase, motion.FrameDurationMs, double.MaxValue) * (phase.Loop ? cycles : 1));
        var velocity = (target.X - start.X) / Math.Max(0.001, travelSeconds);
        var distance = 0.0;
        _runtime.ReportPerformance($"patrol_walk_motion direction={motion.Direction} mirror={request.MirrorHorizontally} distance_px={Math.Abs(target.X-start.X):0.0} duration_ms={duration.TotalMilliseconds:0} cycles={cycles}");
        foreach (var phase in motion.Phases)
        {
            var repeats = phase.Loop ? cycles : 1;
            for (var cycle = 0; cycle < repeats; cycle++)
            {
                var clock = Stopwatch.StartNew();
                var due = 0.0;
                for (var index = 0; index < phase.Frames.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (_activeRequest?.RequestId != request.RequestId)
                        return;
                    SetFrame(phase.Frames[index], phase.Name);
                    due += phase.DurationForFrame(index, motion.FrameDurationMs);
                    do
                    {
                        token.ThrowIfCancellationRequested();
                        var travel = PatrolWalkTiming.TravelSeconds(phase, motion.FrameDurationMs, Math.Min(clock.Elapsed.TotalMilliseconds, due));
                        var point = ClampToWorkArea(new Point(start.X + distance + velocity * travel, start.Y),
                            WindowPlacement.CurrentWorkingArea(this), Width, Height);
                        Left = point.X;
                        Top = point.Y;
                        var remaining = due - clock.Elapsed.TotalMilliseconds;
                        if (remaining <= 0)
                            break;
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(16, remaining)), token);
                    } while (true);
                }
                distance += velocity * PatrolWalkTiming.TravelSeconds(phase, motion.FrameDurationMs, double.MaxValue);
                if (_patrolStopRequested)
                    break;
            }
        }
    }
}
