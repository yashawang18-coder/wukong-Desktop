using Wukong.Application;
using Wukong.Domain;

internal static class BehaviorContinuityTests
{
    public static void OwnerIntentNormalizationIsSharedAndDeterministic()
    {
        var normalizer = new OwnerIntentNormalizer();
        Assert(normalizer.Normalize("去喝水").Kind == NormalizedOwnerIntentKind.Drink, "drink intent mismatch");
        Assert(normalizer.Normalize("去睡觉").IntentId == "episode.sleep", "sleep intent mismatch");
        Assert(normalizer.Normalize("转圈").Kind == NormalizedOwnerIntentKind.Spin, "spin intent mismatch");
        Assert(normalizer.Normalize("今天开心吗").Kind == NormalizedOwnerIntentKind.None, "social chat became an action");
    }

    public static void DialogueClaimsCannotInventRuntimeFacts()
    {
        var validator = new DialogueStateClaimValidator();
        var truth = new DialogueTruthSnapshot(
            PetEpisodeKind.Exploring,
            StablePosture.Stand,
            "wk.candidate.autonomous.patrol_walk_left_v1",
            Guid.NewGuid(),
            true,
            Array.Empty<PetRecentExperience>());
        var invalid = validator.Validate(validator.InferClaims("我正在睡觉"), truth);
        Assert(!invalid.IsValid && invalid.UsedSafeFallback, "false sleeping claim was not blocked");
        Assert(invalid.Text.Contains("走", StringComparison.Ordinal), "fallback did not describe exploring");

        var valid = validator.Validate(validator.InferClaims("我正在走走看看"), truth);
        Assert(valid.IsValid, "truthful walking claim was rejected");
    }

    public static void PreparingAndCompletedClaimsRequireEvidence()
    {
        var validator = new DialogueStateClaimValidator();
        var requestId = Guid.NewGuid();
        var preparing = new DialogueAct(
            DialogueActType.BehaviorAccepted,
            "好呀，我去喝水。",
            requestId,
            new[] { new DialogueClaim(DialogueStateClaim.Drinking, DialogueTemporalClaim.Preparing) });
        var truth = new DialogueTruthSnapshot(
            PetEpisodeKind.OwnerInteraction,
            StablePosture.Sit,
            "wk.daily.sit_to_stand",
            requestId,
            true,
            Array.Empty<PetRecentExperience>());
        Assert(validator.Validate(preparing, truth).IsValid, "accepted queued request was not valid preparing evidence");

        var completed = preparing with
        {
            Type = DialogueActType.BehaviorCompleted,
            Text = "我喝完啦。",
            Claims = new[] { new DialogueClaim(DialogueStateClaim.Drinking, DialogueTemporalClaim.Completed) }
        };
        Assert(!validator.Validate(completed, truth).IsValid, "completion claim passed without completed outcome");
        var withOutcome = truth with
        {
            RecentOutcomes = new[]
            {
                new PetRecentExperience(DateTimeOffset.UtcNow, "behavior_finished", "wk.interaction.drink_water",
                    ExecutionStatus.Completed, 1, "completed")
            }
        };
        Assert(validator.Validate(completed, withOutcome).IsValid, "completion evidence was ignored");
    }

    public static void SleepingEpisodeHoldsAndUsesElapsedTime()
    {
        var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var sleeping = BehaviorEpisodeCatalog.Start(PetEpisodeKind.Sleeping, now, "test");
        var state = PetAgentState.CreateDefault(now) with
        {
            Episode = sleeping,
            Runtime = PetRuntimeState.Default with { Energy = 0.20, CurrentPosture = StablePosture.Prone }
        };
        var policy = new PetEpisodePolicy();
        Assert(!policy.Evaluate(state, now.AddMinutes(2)).Changed, "sleeping switched before minimum duration");
        Assert(!policy.Evaluate(state, now.AddMinutes(10)).Changed, "sleeping switched without a wake transition");

        var reducer = new PetStateReducer();
        var advanced = reducer.Reduce(state, new PetTimeAdvanced(now.AddSeconds(120)));
        Assert(advanced.Runtime.Energy > state.Runtime.Energy, "sleep did not restore energy by elapsed time");
        Assert(advanced.Clock.AppliedElapsed == TimeSpan.FromSeconds(120), "elapsed duration was not recorded");
    }

    public static void EpisodeDefinitionsUseProductionDurationsAndHysteresis()
    {
        var sleep = BehaviorEpisodeCatalog.Get(PetEpisodeKind.Sleeping);
        var rest = BehaviorEpisodeCatalog.Get(PetEpisodeKind.Resting);
        Assert(sleep.MinimumDuration == TimeSpan.FromMinutes(3), "sleep minimum duration changed");
        Assert(sleep.PreferredDuration == TimeSpan.FromMinutes(10), "sleep preferred duration changed");
        Assert(rest.MinimumDuration == TimeSpan.FromSeconds(45), "rest minimum duration changed");
        Assert(rest.SwitchMargin > 0, "rest switch hysteresis is missing");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
