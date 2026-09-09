using Wukong.Application;
using Wukong.Domain;

internal static class BehaviorAgentCoreTests
{
    public static void ElapsedTimeEvolutionIsTickFrequencyIndependent()
    {
        var now = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        var reducer = new PetStateReducer();
        var oneStep = reducer.Reduce(PetAgentState.CreateDefault(now), new PetTimeAdvanced(now.AddMinutes(1)));
        var manySteps = PetAgentState.CreateDefault(now);
        for (var second = 1; second <= 60; second++)
            manySteps = reducer.Reduce(manySteps, new PetTimeAdvanced(now.AddSeconds(second)));

        AssertClose(oneStep.Runtime.Energy, manySteps.Runtime.Energy, "energy depended on tick frequency");
        AssertClose(oneStep.Runtime.Hunger, manySteps.Runtime.Hunger, "hunger depended on tick frequency");
        AssertClose(oneStep.Runtime.Thirst, manySteps.Runtime.Thirst, "thirst depended on tick frequency");
        AssertClose(oneStep.Runtime.SocialNeed, manySteps.Runtime.SocialNeed, "social need depended on tick frequency");
        AssertClose(oneStep.Runtime.Boredom, manySteps.Runtime.Boredom, "boredom depended on tick frequency");
        AssertClose(oneStep.Runtime.Stress, manySteps.Runtime.Stress, "stress depended on tick frequency");
        Assert(oneStep.Clock.AppliedElapsed == TimeSpan.FromMinutes(1), "elapsed time was not recorded");
    }

    public static void ReducerIgnoresPreviewDuplicateAndStaleCompletion()
    {
        var now = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        var reducer = new PetStateReducer();
        var initial = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Stand,
                CurrentPoseId = "stand.neutral.left_front",
                Energy = 0.70
            }
        };
        var previewId = Guid.NewGuid();
        var previewStarted = reducer.Reduce(initial, Started(now, previewId, "preview", BehaviorExecutionMode.DeveloperPreview));
        AssertEquivalentState(initial, previewStarted, "developer preview changed formal state");

        var executionId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var started = reducer.Reduce(initial, Started(now, executionId, "resting.motion", BehaviorExecutionMode.Normal));
        Assert(started.Runtime.ActiveExecutionId == executionId && started.Runtime.IsBusy, "normal execution did not start");

        var stale = reducer.Reduce(started, Finished(now.AddSeconds(1), staleId, "resting.motion", -0.10));
        AssertEquivalentState(started, stale, "stale completion changed state");

        var completed = reducer.Reduce(started, Finished(now.AddSeconds(2), executionId, "resting.motion", -0.10));
        Assert(!completed.Runtime.IsBusy && completed.Runtime.ActiveExecutionId is null, "completion did not clear execution");
        AssertClose(0.60, completed.Runtime.Energy, "completion effect was not applied exactly once");
        Assert(completed.Runtime.CurrentPosture == StablePosture.Sit, "completed posture was not committed");
        Assert(completed.Runtime.CurrentPoseId == "sit.neutral.left_front", "completed pose was not committed");

        var duplicate = reducer.Reduce(completed, Finished(now.AddSeconds(3), executionId, "resting.motion", -0.10));
        AssertEquivalentState(completed, duplicate, "duplicate completion settled twice");

        var previewFinished = reducer.Reduce(completed, new PetBehaviorFinished(
            now.AddSeconds(4), previewId, "preview", ExecutionStatus.Completed, 1,
            StablePosture.Prone, "prone.awake.front", new PetStateEffects(Energy: -0.5),
            true, true, PartialEffectPolicy.Proportional,
            BehaviorRequestSource.DeveloperPreview, BehaviorExecutionMode.DeveloperPreview, "preview_complete"));
        AssertEquivalentState(completed, previewFinished, "preview completion changed formal state");
    }

    public static void EpisodePolicyUsesDwellAndImmediateRecovery()
    {
        var now = new DateTimeOffset(2026, 9, 10, 11, 0, 0, TimeSpan.Zero);
        var policy = new PetEpisodePolicy();
        var state = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with { Curiosity = 0.90 }
        };

        var held = policy.Evaluate(state, now.AddSeconds(20));
        Assert(!held.Changed && held.Episode.Kind == PetEpisodeKind.Resting, "episode ignored minimum dwell");

        var observing = policy.Evaluate(state, now.AddSeconds(70));
        Assert(observing.Changed && observing.Episode.Kind == PetEpisodeKind.Observing, "eligible observing transition was not selected");

        var exhausted = state with { Runtime = state.Runtime with { Energy = 0.05 } };
        var recovering = policy.Evaluate(exhausted, now.AddSeconds(5));
        Assert(recovering.Changed && recovering.Episode.Kind == PetEpisodeKind.Recovering, "urgent recovery did not bypass dwell");
    }

    public static void ParticipationPolicyKeepsOwnerModesDistinct()
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var state = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Stand,
                CurrentPoseId = "stand.neutral.left_front",
                Energy = 0.05,
                Stress = 0.90
            }
        };
        var policy = new BehaviorParticipationPolicy();
        var forced = Capability("magic", BehaviorParticipationMode.ForcedByOwner, BehaviorEffortLevel.High,
            BehaviorRequestSource.OwnerContextMenu);
        var command = Capability("command", BehaviorParticipationMode.UsuallyCooperative, BehaviorEffortLevel.High,
            BehaviorRequestSource.OwnerContextMenu);

        Assert(policy.Evaluate(forced, state, BehaviorRequestSource.OwnerContextMenu, now).Disposition == RequestDisposition.Accepted,
            "owner-forced magic was subjected to willingness scoring");
        Assert(policy.Evaluate(command, state, BehaviorRequestSource.OwnerContextMenu, now).Disposition == RequestDisposition.Rejected,
            "high-effort command ignored severe runtime state");
        Assert(policy.Evaluate(forced, state, BehaviorRequestSource.Dialogue, now).ReasonCode == "source_not_allowed",
            "dialogue bypassed owner-only source gate");
    }

    public static void DecisionEngineIsDeterministicAndHardGated()
    {
        var now = new DateTimeOffset(2026, 9, 10, 13, 0, 0, TimeSpan.Zero);
        var state = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Prone,
                CurrentPoseId = "prone.awake.left_front"
            }
        };
        var idle = Capability("idle", BehaviorParticipationMode.Autonomous, BehaviorEffortLevel.Low,
            BehaviorRequestSource.AutonomousTick) with
        {
            Category = BehaviorSemanticCategory.StableIdle,
            StartPostures = new HashSet<StablePosture> { StablePosture.Prone },
            EndPosture = StablePosture.Prone,
            StartPoseFamily = "prone.non_front",
            EndPoseId = "prone.awake.left_front",
            AutonomousBindingEnabled = true
        };
        var forbidden = Capability("command.jump", BehaviorParticipationMode.UsuallyCooperative, BehaviorEffortLevel.High,
            BehaviorRequestSource.OwnerContextMenu) with
        {
            Category = BehaviorSemanticCategory.OwnerCommand,
            StartPostures = new HashSet<StablePosture> { StablePosture.Prone }
        };
        var catalog = new BehaviorCapabilityCatalog(new[] { idle, forbidden });
        var input = new BehaviorDecisionInput(
            BehaviorRequestSource.AutonomousTick, now, "idle", now.Subtract(TimeSpan.FromMinutes(1)), true,
            new Dictionary<string, DateTimeOffset>(), Array.Empty<string>(), 42, false, true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "idle" });
        var engine = new BehaviorDecisionEngine();
        var first = engine.Decide(state, catalog, input);
        var second = engine.Decide(state, catalog, input);

        Assert(first.SelectedBehaviorId == "idle" && second.SelectedBehaviorId == "idle", "deterministic eligible action was not selected");
        Assert(first.Candidates.Select(x => x.FinalScore).SequenceEqual(second.Candidates.Select(x => x.FinalScore)),
            "same seed and state produced different scores");
        var rejected = first.Candidates.Single(x => x.BehaviorId == "command.jump");
        Assert(rejected.GateReasons.Contains("episode_rollout_not_bound") && rejected.GateReasons.Contains("source_not_allowed"),
            "hard gate did not exclude command-only behavior");
    }

    private static PetBehaviorStarted Started(
        DateTimeOffset at,
        Guid executionId,
        string behaviorId,
        BehaviorExecutionMode mode) =>
        new(at, executionId, behaviorId, "intro", true, BehaviorRequestSource.AutonomousTick, mode);

    private static PetBehaviorFinished Finished(
        DateTimeOffset at,
        Guid executionId,
        string behaviorId,
        double energy) =>
        new(at, executionId, behaviorId, ExecutionStatus.Completed, 1,
            StablePosture.Sit, "sit.neutral.left_front", new PetStateEffects(Energy: energy),
            false, false, PartialEffectPolicy.Proportional,
            BehaviorRequestSource.AutonomousTick, BehaviorExecutionMode.Normal, "completed");

    private static BehaviorCapability Capability(
        string behaviorId,
        BehaviorParticipationMode mode,
        BehaviorEffortLevel effort,
        params BehaviorRequestSource[] sources) =>
        new(
            behaviorId,
            BehaviorSemanticCategory.StableIdle,
            mode,
            BehaviorInterruptionPolicy.SafePreempt,
            sources.ToHashSet(),
            new HashSet<StablePosture> { StablePosture.Stand },
            StablePosture.Stand,
            effort,
            new HashSet<PetEpisodeKind> { PetEpisodeKind.Resting },
            ProductionApproved: true,
            RuntimeUse: true,
            ProductionAsset: true,
            AutonomousBindingEnabled: mode == BehaviorParticipationMode.Autonomous,
            SupportsWindowTranslation: false,
            MinimumDwell: TimeSpan.Zero,
            Cooldown: TimeSpan.Zero,
            BaseWeight: 0.5,
            StateEffects: new PetStateEffects())
        {
            StartPoseFamily = "stand",
            EndPoseId = "stand.neutral.left_front"
        };

    private static void AssertClose(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.0000001)
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }

    private static void AssertEquivalentState(PetAgentState expected, PetAgentState actual, string message)
    {
        Assert(expected.Runtime == actual.Runtime, message);
        Assert(expected.Relationship == actual.Relationship, message);
        Assert(expected.Temperament == actual.Temperament, message);
        Assert(expected.Episode == actual.Episode, message);
        Assert(expected.Clock == actual.Clock, message);
        Assert(expected.RecentExperience.SequenceEqual(actual.RecentExperience), message);
        Assert(expected.Preferences.Count == actual.Preferences.Count &&
               expected.Preferences.All(pair => actual.Preferences.TryGetValue(pair.Key, out var value) && value == pair.Value),
            message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
