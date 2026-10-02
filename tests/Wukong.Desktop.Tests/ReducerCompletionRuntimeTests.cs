using System.Text.Json;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class ReducerCompletionRuntimeTests
{
    public static void DuplicateCompletionCannotRewardTwice()
    {
        var fixture = new Fixture();
        var action = fixture.Command("Sit");
        fixture.Complete(action);
        var settled = Snapshot(fixture.Runtime);
        var count = fixture.Requests.Count;
        fixture.Complete(action);
        Assert(Snapshot(fixture.Runtime) == settled, "duplicate completion changed reducer state");
        Assert(fixture.Requests.Count == count, "duplicate completion replaced terminal presentation");
    }

    public static void LateCompletionAfterStopIsIgnored()
    {
        var fixture = new Fixture();
        var action = fixture.Command("Sit");
        fixture.Now += TimeSpan.FromMilliseconds(100);
        fixture.Runtime.StopAsync().GetAwaiter().GetResult();
        var stopped = Snapshot(fixture.Runtime);
        var count = fixture.Requests.Count;
        fixture.Complete(action);
        fixture.Runtime.FailMotion(action.RequestId, action.Motion.BehaviorId, "late_failure");
        Assert(Snapshot(fixture.Runtime) == stopped, "stopped action settled a second time");
        Assert(fixture.Requests.Count == count, "late stopped callback replaced idle");
        Assert(!fixture.Runtime.AgentStateSnapshot.Runtime.IsBusy, "stop left an active execution");
    }

    public static void FailedExecutionSettlesOnlyOnce()
    {
        var fixture = new Fixture();
        var action = fixture.Command("Sit");
        fixture.Now += TimeSpan.FromMilliseconds(100);
        fixture.Runtime.FailMotion(action.RequestId, action.Motion.BehaviorId, "test_decode_failure");
        var failed = Snapshot(fixture.Runtime);
        var count = fixture.Requests.Count;
        fixture.Runtime.FailMotion(action.RequestId, action.Motion.BehaviorId, "duplicate_failure");
        fixture.Complete(action);
        Assert(Snapshot(fixture.Runtime) == failed, "failure or late success settled twice");
        Assert(fixture.Requests.Count == count, "failed callback replaced recovery idle");
    }

    public static void StaleHoldCannotInterruptNewOwnerAction()
    {
        var fixture = new Fixture();
        fixture.Complete(fixture.Command("Sit"));
        var hold = fixture.Requests.Last();
        Assert(hold.Motion.BehaviorId.StartsWith("wk.runtime.posture_hold.", StringComparison.Ordinal),
            "fixture did not reach a terminal hold");
        var next = fixture.Command("Paw");
        var before = Snapshot(fixture.Runtime);
        var count = fixture.Requests.Count;
        fixture.Complete(hold);
        Assert(Snapshot(fixture.Runtime) == before && fixture.Requests.Count == count,
            "old terminal hold interrupted a newer owner action");
        Assert(fixture.Runtime.AgentStateSnapshot.Runtime.ActiveExecutionId == next.RequestId,
            "new execution identity was lost");
    }

    public static void RepeatedPreviewUsesExecutionIdentity()
    {
        var fixture = new Fixture();
        var first = fixture.Preview();
        var second = fixture.Preview();
        Assert(first.RequestId != second.RequestId, "preview reused execution identity");
        var before = Snapshot(fixture.Runtime);
        var count = fixture.Requests.Count;
        fixture.Complete(first);
        Assert(Snapshot(fixture.Runtime) == before && fixture.Requests.Count == count,
            "old preview completion cancelled current preview");
        fixture.Complete(second);
        Assert(!fixture.Runtime.AgentStateSnapshot.Runtime.IsBusy, "preview leaked busy state");
        Assert(fixture.Runtime.AgentStateSnapshot.RecentExperience.Count == 0,
            "preview recorded production experience");
    }

    public static void StableIdleCompletionDoesNotRecordActivity()
    {
        var fixture = new Fixture();
        fixture.Runtime.StopAsync().GetAwaiter().GetResult();
        var idle = fixture.Requests.Last();
        var before = Snapshot(fixture.Runtime);
        var count = fixture.Requests.Count;
        for (var index = 0; index < 20; index++) fixture.Complete(idle);
        Assert(Snapshot(fixture.Runtime) == before && fixture.Requests.Count == count,
            "infinite idle callback produced activity, rewards or playback churn");
    }

    public static void PreviewFailureRestoresWithoutLearning()
    {
        var fixture = new Fixture();
        var before = fixture.Runtime.AgentStateSnapshot;
        var preview = fixture.Preview();
        fixture.Runtime.FailMotion(preview.RequestId, preview.Motion.BehaviorId, "test_preview_decode_failure");
        var after = fixture.Runtime.AgentStateSnapshot;
        Assert(after.Runtime.Energy == before.Runtime.Energy && after.Runtime.Stress == before.Runtime.Stress,
            "preview failure applied real state costs");
        Assert(after.Relationship == before.Relationship && after.RecentExperience.SequenceEqual(before.RecentExperience),
            "preview failure contaminated relationship or experience");
        Assert(!after.Runtime.IsBusy && after.Runtime.ActiveExecutionId is null, "preview failure leaked execution state");
        Assert(fixture.Requests.Last().ExecutionMode == BehaviorExecutionMode.Normal,
            "preview failure did not return to stable presentation");
    }

    public static void WindowDecodeFailureReachesReducer()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                _ = System.Windows.Application.Current ?? new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
                window = new MainWindow();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var load = (Task)typeof(MainWindow).GetField("_agentStateLoadTask", flags)!.GetValue(window)!;
                load.GetAwaiter().GetResult();
                var runtime = (DesktopRuntimeHost)typeof(MainWindow).GetField("_runtime", flags)!.GetValue(window)!;
                runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default,
                    PetRuntimeState.Default with
                    {
                        CurrentPosture = StablePosture.Stand, CurrentPoseId = "stand.neutral.left_front",
                        Energy = 0.8, Stress = 0.1
                    }, RelationshipState.Default, 42);
                PetMotionRequest? captured = null;
                runtime.MotionRequested += (_, request) => captured = request;
                var result = runtime.SubmitOwnerCommandAsync("Sit", BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
                Assert(result == PetActionResult.Accepted && captured is not null, "test command not started");
                var action = captured!;
                var setFrame = typeof(MainWindow).GetMethod("SetFrame",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("frame renderer not found");
                setFrame.Invoke(window, new object?[]
                {
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png"),
                    "test_missing_frame", null
                });
                var frame = new System.Windows.Threading.DispatcherFrame();
                window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                var state = runtime.AgentStateSnapshot;
                Assert(state.Runtime.ActiveExecutionId is null && !state.Runtime.IsBusy,
                    "WPF decode failure left reducer execution busy");
                Assert(state.RecentExperience.Any(item => item.BehaviorId == action.Motion.BehaviorId &&
                    item.Status == ExecutionStatus.Failed),
                    "WPF decode failure was not reported as Failed");
                Assert(!state.RecentExperience.Any(item => item.BehaviorId == action.Motion.BehaviorId &&
                    item.Status == ExecutionStatus.Completed),
                    "WPF decode failure was reported as success");
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    public static void ThirtyMinuteVirtualContinuityRejectsStaleCallbacks()
    {
        var fixture = new Fixture();
        fixture.Runtime.StopAsync().GetAwaiter().GetResult();
        PetMotionRequest? previous = null;
        var completed = 0;
        for (var second = 0; second < 1800; second++)
        {
            fixture.Now += TimeSpan.FromSeconds(1);
            fixture.Runtime.SubmitAutonomousTickAsync().GetAwaiter().GetResult();
            var active = fixture.Requests.Last();
            if (active.TracksAgentLifecycle &&
                fixture.Runtime.AgentStateSnapshot.Runtime.ActiveExecutionId == active.RequestId)
            {
                Assert(DesktopRuntimeHost.IsAutonomousRuntimeBehaviorAllowed(active.Motion.BehaviorId),
                    "virtual daily run selected an owner-only or unavailable action: " + active.Motion.BehaviorId);
                fixture.Complete(active);
                previous = active;
                completed++;
            }
            if (previous is not null)
            {
                var snapshot = Snapshot(fixture.Runtime);
                fixture.Complete(previous);
                Assert(Snapshot(fixture.Runtime) == snapshot, "late callback altered continuity state");
            }
            Assert(!fixture.Runtime.AgentStateSnapshot.Runtime.IsBusy, "virtual run got stuck busy");
            Assert(fixture.Runtime.AgentStateSnapshot.RecentExperience.Count <= PetStateReducer.MaximumRecentExperience,
                "recent experience grew without bound");
        }
        Assert(completed > 0, "virtual run did not exercise any finite execution");
    }

    private static string Snapshot(DesktopRuntimeHost runtime) => JsonSerializer.Serialize(runtime.AgentStateSnapshot);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        public DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        public DesktopRuntimeHost Runtime { get; }
        public List<PetMotionRequest> Requests { get; } = new();
        public Fixture()
        {
            Runtime = new DesktopRuntimeHost(now: () => Now);
            Runtime.UpdateBehaviorAgentMock(TemperamentProfile.Default,
                PetRuntimeState.Default with
                {
                    CurrentPosture = StablePosture.Stand,
                    CurrentPoseId = "stand.neutral.left_front",
                    Energy = 0.8, Stress = 0.1
                }, RelationshipState.Default, 42);
            Runtime.MotionRequested += (_, request) => Requests.Add(request);
        }
        public PetMotionRequest Command(string command)
        {
            var result = Runtime.SubmitOwnerCommandAsync(command, BehaviorRequestSource.OwnerContextMenu).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted, $"command rejected: {command}: {Runtime.CurrentReason}");
            return Requests.Last();
        }
        public PetMotionRequest Preview()
        {
            var result = Runtime.SubmitDeveloperMotionAsync(LifecycleCandidateBehaviorIds.LivelyDailyP2).GetAwaiter().GetResult();
            Assert(result == PetActionResult.Accepted, "developer preview not accepted");
            return Requests.Last();
        }
        public void Complete(PetMotionRequest request) =>
            Runtime.CompleteMotion(request.RequestId, request.Motion.BehaviorId, request.Motion.Phases.Last().Name);
    }
}
