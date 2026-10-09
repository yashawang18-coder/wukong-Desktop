using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Wukong.Application;
using Wukong.Desktop;
using Wukong.Domain;

internal static class BaseAssetExecutionTests
{
    public static void BaseCardsExposePreviewAndOwnerExecution()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                }
                var panel = new ControlPanelWindow(new DesktopRuntimeHost());
                var list = panel.FindName("AssetList") as ItemsControl
                    ?? throw new InvalidOperationException("base asset list missing");
                var card = list.ItemTemplate.LoadContent() as DependencyObject
                    ?? throw new InvalidOperationException("base asset card template missing");
                var buttons = Descendants<Button>(card).Select(x => x.Content?.ToString()).ToArray();
                Assert(buttons.Contains("查看动画", StringComparer.Ordinal), "base asset card lost preview action");
                Assert(buttons.Contains("让悟空执行", StringComparer.Ordinal), "base asset card has no owner execution action");
                panel.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }

    public static void BaseExecutionUsesApprovedPathWithoutLearning()
    {
        var runtime = new DesktopRuntimeHost();
        var method = typeof(DesktopRuntimeHost).GetMethod(
            "SubmitBaseMotionAsync",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(string), typeof(BehaviorRequestSource)],
            modifiers: null)
            ?? throw new InvalidOperationException("base asset owner execution API is missing");

        var agentStateBefore = runtime.AgentStateSnapshot;
        PetMotionRequest? request = null;
        runtime.MotionRequested += (_, value) => request = value;
        var task = method.Invoke(runtime, [LifecycleCandidateBehaviorIds.ProneIdleMicroloop, BehaviorRequestSource.ControlPanel]) as Task<PetActionResult>
            ?? throw new InvalidOperationException("base asset owner execution did not return a task");
        var result = task.GetAwaiter().GetResult();

        Assert(result == PetActionResult.Accepted, $"compatible approved base motion was not accepted: {result} / {runtime.CurrentReason}");
        Assert(request is
        {
            Source: BehaviorRequestSource.ControlPanel,
            ExecutionMode: BehaviorExecutionMode.Normal
        }, "approved base asset did not use the Normal request path");
        var captured = request ?? throw new InvalidOperationException("base asset execution did not emit a motion request");
        Assert(string.Equals(captured.Motion.BehaviorId, LifecycleCandidateBehaviorIds.ProneIdleMicroloop, StringComparison.Ordinal),
            "base asset execution selected a different motion");
        runtime.CompleteMotion(captured.RequestId, captured.Motion.BehaviorId, captured.Motion.Phases.Last().Name);
        AssertLearningStateUnchanged(agentStateBefore, runtime.AgentStateSnapshot);

        var commandTask = method.Invoke(runtime, [MockCommandActionIds.Jump, BehaviorRequestSource.ControlPanel]) as Task<PetActionResult>
            ?? throw new InvalidOperationException("command rejection did not return a task");
        Assert(commandTask.GetAwaiter().GetResult() == PetActionResult.Deferred,
            "base asset API accepted a command-only motion");
    }

    private static void AssertLearningStateUnchanged(PetAgentState before, PetAgentState after)
    {
        Assert(before.Temperament == after.Temperament, "control-panel execution changed temperament");
        Assert(before.Relationship == after.Relationship, "control-panel execution changed relationship state");
        Assert(before.DecisionMemory.GeneratedAt == after.DecisionMemory.GeneratedAt &&
               before.DecisionMemory.Fingerprint == after.DecisionMemory.Fingerprint &&
               MapsEqual(before.DecisionMemory.CategoryWeights, after.DecisionMemory.CategoryWeights) &&
               MapsEqual(before.DecisionMemory.InitiativeTopicWeights, after.DecisionMemory.InitiativeTopicWeights) &&
               MapsEqual(before.DecisionMemory.EvidenceCounts, after.DecisionMemory.EvidenceCounts),
            "control-panel execution changed decision memory");
        Assert(before.InitiativeSpeechFeedback == after.InitiativeSpeechFeedback,
            "control-panel execution changed initiative-speech memory");

        var beforeRuntime = before.Runtime;
        var afterRuntime = after.Runtime;
        Assert(beforeRuntime.Energy == afterRuntime.Energy &&
               beforeRuntime.Hunger == afterRuntime.Hunger &&
               beforeRuntime.Thirst == afterRuntime.Thirst &&
               beforeRuntime.SocialNeed == afterRuntime.SocialNeed &&
               beforeRuntime.Boredom == afterRuntime.Boredom &&
               beforeRuntime.Stress == afterRuntime.Stress &&
               beforeRuntime.MoodValence == afterRuntime.MoodValence &&
               beforeRuntime.Arousal == afterRuntime.Arousal &&
               beforeRuntime.Curiosity == afterRuntime.Curiosity &&
               beforeRuntime.Comfort == afterRuntime.Comfort &&
               beforeRuntime.Focus == afterRuntime.Focus &&
               beforeRuntime.LastInteractionAt == afterRuntime.LastInteractionAt &&
               beforeRuntime.LastActionId == afterRuntime.LastActionId &&
               beforeRuntime.RepeatedActionCount == afterRuntime.RepeatedActionCount,
            "control-panel execution changed needs, mood or recent-action learning state");

        Assert(before.RecentExperience.SequenceEqual(after.RecentExperience),
            "control-panel execution appended recent experience");
        Assert(before.Preferences.Count == after.Preferences.Count &&
               before.Preferences.All(pair => after.Preferences.TryGetValue(pair.Key, out var value) && value == pair.Value),
            "control-panel execution reinforced a learned preference");
    }

    private static bool MapsEqual<T>(IReadOnlyDictionary<string, T> left, IReadOnlyDictionary<string, T> right) =>
        left.Count == right.Count &&
        left.All(pair => right.TryGetValue(pair.Key, out var value) && EqualityComparer<T>.Default.Equals(value, pair.Value));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
