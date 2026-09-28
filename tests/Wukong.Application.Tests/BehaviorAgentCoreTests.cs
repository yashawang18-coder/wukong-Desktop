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

    public static void CapabilityCatalogPrefersApprovedRuntimeDuplicate()
    {
        var legacy = Capability("wk.command.jump", BehaviorParticipationMode.UsuallyCooperative,
            BehaviorEffortLevel.High, BehaviorRequestSource.OwnerContextMenu) with
        {
            ProductionApproved = false,
            RuntimeUse = false,
            ProductionAsset = false
        };
        var approved = legacy with
        {
            ProductionApproved = true,
            RuntimeUse = true,
            ProductionAsset = true,
            EndPoseId = "stand.neutral.left_front"
        };

        var catalog = new BehaviorCapabilityCatalog(new[] { legacy, approved });
        var selected = catalog.Find("wk.command.jump")
            ?? throw new InvalidOperationException("approved duplicate capability was not indexed");

        Assert(selected.ProductionApproved && selected.RuntimeUse && selected.ProductionAsset,
            "duplicate behavior id resolved to a superseded runtime candidate");
        Assert(selected.EndPoseId == "stand.neutral.left_front",
            "duplicate behavior id did not preserve the approved runtime capability");
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

    public static void OwnerBehaviorPreferencesAffectScores()
    {
        var now = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);
        var state = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Stand,
                CurrentPoseId = "stand.neutral.left_front"
            }
        };
        var walk = Capability("walk", BehaviorParticipationMode.Autonomous, BehaviorEffortLevel.Low,
            BehaviorRequestSource.AutonomousTick) with
        {
            Category = BehaviorSemanticCategory.Explore,
            AutonomousBindingEnabled = true
        };
        var stand = Capability("stand", BehaviorParticipationMode.Autonomous, BehaviorEffortLevel.Low,
            BehaviorRequestSource.AutonomousTick) with
        {
            Category = BehaviorSemanticCategory.StableIdle,
            AutonomousBindingEnabled = true
        };
        var catalog = new BehaviorCapabilityCatalog(new[] { walk, stand });
        var input = new BehaviorDecisionInput(
            BehaviorRequestSource.AutonomousTick, now, "current", now.Subtract(TimeSpan.FromMinutes(1)), true,
            new Dictionary<string, DateTimeOffset>(), Array.Empty<string>(), 73, false, true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "walk", "stand" },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["walk"] = 1.8,
                ["stand"] = 0.3
            });
        var engine = new BehaviorDecisionEngine();
        var first = engine.Decide(state, catalog, input);
        var second = engine.Decide(state, catalog, input);
        var walkPreference = first.Candidates.Single(x => x.BehaviorId == "walk").Components
            .Single(x => x.Name == "owner_behavior_preference").Value;
        var standPreference = first.Candidates.Single(x => x.BehaviorId == "stand").Components
            .Single(x => x.Name == "owner_behavior_preference").Value;

        Assert(walkPreference > 0 && standPreference < 0, "owner behavior preference did not adjust utility scores");
        Assert(first.SelectedBehaviorId == "walk", "higher walking preference did not win the decision");
        Assert(first.Candidates.Select(x => x.FinalScore).SequenceEqual(second.Candidates.Select(x => x.FinalScore)),
            "behavior preferences made seeded decisions nondeterministic");
    }

    public static void RelationshipAndLongTermMemoryAffectDecisionScores()
    {
        var now = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        var observe = Capability("observe.owner", BehaviorParticipationMode.Autonomous, BehaviorEffortLevel.Low,
            BehaviorRequestSource.AutonomousTick) with
        {
            Category = BehaviorSemanticCategory.Observe,
            AutonomousBindingEnabled = true,
            StartPostures = new HashSet<StablePosture> { StablePosture.Prone },
            EndPosture = StablePosture.Prone,
            StartPoseFamily = "prone.non_front",
            EndPoseId = "prone.awake.left_front"
        };
        var catalog = new BehaviorCapabilityCatalog(new[] { observe });
        var baseState = PetAgentState.CreateDefault(now) with
        {
            Episode = new PetEpisodeState(PetEpisodeKind.Resting, now.AddMinutes(-2), TimeSpan.Zero, "test"),
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Prone,
                CurrentPoseId = "prone.awake.left_front"
            },
            Relationship = new RelationshipState(0.10, 0.10, 0, 8),
            Preferences = new Dictionary<string, LearnedBehaviorPreference>(StringComparer.OrdinalIgnoreCase)
        };
        var learnedState = baseState with
        {
            Relationship = new RelationshipState(0.95, 0.90, 12, 0),
            Preferences = new Dictionary<string, LearnedBehaviorPreference>(StringComparer.OrdinalIgnoreCase)
            {
                [observe.BehaviorId] = new(observe.BehaviorId, 0.12, 1, "owner_feedback", now.AddMinutes(-1))
            },
            RecentExperience = new[]
            {
                new PetRecentExperience(now.AddMinutes(-4), "behavior_finished", observe.BehaviorId,
                    ExecutionStatus.Completed, 1, "completed"),
                new PetRecentExperience(now.AddMinutes(-2), "behavior_finished", observe.BehaviorId,
                    ExecutionStatus.Completed, 1, "completed")
            }
        };
        var input = new BehaviorDecisionInput(
            BehaviorRequestSource.AutonomousTick, now, "idle", now.AddMinutes(-2), true,
            new Dictionary<string, DateTimeOffset>(), Array.Empty<string>(), 108, false, true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { observe.BehaviorId });
        var engine = new BehaviorDecisionEngine();
        var baseline = engine.Decide(baseState, catalog, input).Candidates.Single();
        var learned = engine.Decide(learnedState, catalog, input).Candidates.Single();

        Assert(Component(learned, "relationship") > Component(baseline, "relationship"),
            "relationship did not affect the behavior score");
        Assert(Component(learned, "memory_preference") > 0,
            "persisted learned preference did not affect the behavior score");
        Assert(Component(learned, "experience_memory") > 0,
            "recent completed experience did not affect the behavior score");
        Assert(learned.FinalScore > baseline.FinalScore,
            "relationship and memory did not change the final utility score");
    }

    public static void ReducerPersistsBoundedRelationshipAndBehaviorLearning()
    {
        var now = new DateTimeOffset(2026, 9, 16, 11, 0, 0, TimeSpan.Zero);
        var reducer = new PetStateReducer();
        var executionId = Guid.NewGuid();
        var initial = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Sit,
                CurrentPoseId = "sit.neutral.left_front"
            }
        };
        var started = reducer.Reduce(initial, new PetBehaviorStarted(
            now, executionId, "wk.command.paw_sit", "intro", false,
            BehaviorRequestSource.OwnerContextMenu, BehaviorExecutionMode.Normal));
        var completed = reducer.Reduce(started, new PetBehaviorFinished(
            now.AddSeconds(2), executionId, "wk.command.paw_sit", ExecutionStatus.Completed, 1,
            StablePosture.Sit, "sit.neutral.left_front", new PetStateEffects(SocialNeed: -0.04),
            OwnerInteraction: true, MemoryEligible: true, PartialEffectPolicy.Proportional,
            BehaviorRequestSource.OwnerContextMenu, BehaviorExecutionMode.Normal, "completed"));

        Assert(completed.Relationship.Trust > initial.Relationship.Trust, "successful owner action did not increase trust");
        Assert(completed.Relationship.Familiarity > initial.Relationship.Familiarity, "successful owner action did not increase familiarity");
        Assert(completed.Preferences.TryGetValue("wk.command.paw_sit", out var preference) && preference.EffectiveWeight > 0,
            "successful behavior was not retained as bounded long-term preference");

        var spoken = reducer.Reduce(completed, new PetInitiativeSpeechOccurred(now.AddMinutes(1), "Companionship"));
        Assert(spoken.Runtime.LastInitiativeSpeechAt == now.AddMinutes(1), "initiative speech time was not recorded");
        Assert(spoken.Runtime.LastInteractionAt == completed.Runtime.LastInteractionAt,
            "initiative speech incorrectly counted as an owner response");
        var answered = reducer.Reduce(spoken, new PetOwnerInteractionObserved(now.AddMinutes(2), "OwnerTouch", 1, true));
        Assert(answered.Relationship.TouchAcceptance > spoken.Relationship.TouchAcceptance,
            "positive touch did not update touch acceptance");
        Assert(answered.Relationship.InitiativeAcceptance > spoken.Relationship.InitiativeAcceptance,
            "response to recent initiative did not update initiative acceptance");
        Assert(answered.RecentExperience.Count <= PetStateReducer.MaximumRecentExperience,
            "recent experience exceeded its bounded capacity");
    }

    public static void ControlPanelExecutionDoesNotTeachPersonalityOrMemory()
    {
        var now = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        var reducer = new PetStateReducer();
        var executionId = Guid.NewGuid();
        var initial = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Stand,
                CurrentPoseId = "stand.neutral.left_front",
                Energy = 0.65,
                MoodValence = 0.55
            }
        };
        var started = reducer.Reduce(initial, new PetBehaviorStarted(
            now, executionId, "panel.review.motion", "intro", true,
            BehaviorRequestSource.ControlPanel, BehaviorExecutionMode.Normal));
        Assert(started.Runtime.ActiveExecutionId == executionId && started.Runtime.IsBusy,
            "control-panel execution did not enter the shared lifecycle");
        Assert(started.RecentExperience.SequenceEqual(initial.RecentExperience),
            "control-panel start entered recent behavior memory");

        var completed = reducer.Reduce(started, new PetBehaviorFinished(
            now.AddSeconds(2), executionId, "panel.review.motion", ExecutionStatus.Completed, 1,
            StablePosture.Sit, "sit.neutral.left_front",
            new PetStateEffects(Energy: -0.20, SocialNeed: -0.15, MoodValence: 0.20),
            OwnerInteraction: true, MemoryEligible: true, PartialEffectPolicy.Proportional,
            BehaviorRequestSource.ControlPanel, BehaviorExecutionMode.Normal, "panel_review_complete"));

        Assert(completed.Runtime.CurrentPosture == StablePosture.Sit &&
               completed.Runtime.CurrentPoseId == "sit.neutral.left_front" &&
               !completed.Runtime.IsBusy,
            "control-panel completion did not preserve physical lifecycle coherence");
        AssertClose(initial.Runtime.Energy, completed.Runtime.Energy,
            "control-panel execution changed decision-state energy");
        AssertClose(initial.Runtime.MoodValence, completed.Runtime.MoodValence,
            "control-panel execution changed decision-state mood");
        Assert(completed.Runtime.LastActionId == initial.Runtime.LastActionId,
            "control-panel execution entered repetition memory");
        Assert(completed.Relationship == initial.Relationship,
            "control-panel execution changed relationship state");
        Assert(completed.Preferences.Count == initial.Preferences.Count,
            "control-panel execution reinforced a learned preference");
        Assert(completed.RecentExperience.SequenceEqual(initial.RecentExperience),
            "control-panel completion entered recent behavior memory");
    }

    public static void CommandWillingnessIsCooperativeDeterministicAndStateSensitive()
    {
        var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var policy = new BehaviorParticipationPolicy();
        var lowEffort = Capability("wk.command.sit", BehaviorParticipationMode.UsuallyCooperative,
            BehaviorEffortLevel.Low, BehaviorRequestSource.OwnerContextMenu);
        var highEffort = Capability("wk.command.jump", BehaviorParticipationMode.UsuallyCooperative,
            BehaviorEffortLevel.High, BehaviorRequestSource.OwnerContextMenu);
        var independent = PetAgentState.CreateDefault(now) with
        {
            Temperament = TemperamentProfile.Default with { Independence = 100, CommandCooperativeness = 0.82 },
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Stand,
                CurrentPoseId = "stand.neutral.left_front",
                Energy = 0.55,
                Stress = 0.30
            }
        };
        var first = policy.Evaluate(lowEffort, independent, BehaviorRequestSource.OwnerContextMenu, now);
        var second = policy.Evaluate(lowEffort, independent, BehaviorRequestSource.OwnerContextMenu, now);
        Assert(first.Disposition == RequestDisposition.Accepted, "independence incorrectly caused low-effort disobedience");
        Assert(first.Disposition == second.Disposition && first.ReasonCode == second.ReasonCode &&
               Math.Abs(first.WillingnessScore - second.WillingnessScore) < 0.0000001 &&
               first.Components.Count == second.Components.Count &&
               first.Components.All(pair => second.Components.TryGetValue(pair.Key, out var value) && value == pair.Value),
            "owner command willingness was nondeterministic");

        var exhausted = independent with { Runtime = independent.Runtime with { Energy = 0.05, Stress = 0.90 } };
        Assert(policy.Evaluate(highEffort, exhausted, BehaviorRequestSource.OwnerContextMenu, now).Disposition == RequestDisposition.Rejected,
            "high-effort command ignored severe energy and stress state");
        var lowTrust = independent with { Relationship = new RelationshipState(0.10, 0.10, 0, 4) };
        var highTrust = independent with { Relationship = new RelationshipState(0.95, 0.90, 12, 0) };
        Assert(policy.Evaluate(highEffort, highTrust, BehaviorRequestSource.OwnerContextMenu, now).WillingnessScore >
               policy.Evaluate(highEffort, lowTrust, BehaviorRequestSource.OwnerContextMenu, now).WillingnessScore,
            "relationship did not influence command willingness");
    }

    public static void InitiativeSpeechUsesRelationshipMemoryAndUnansweredCooldown()
    {
        var now = new DateTimeOffset(2026, 9, 16, 13, 0, 0, TimeSpan.Zero);
        var service = new InitiativeSpeechDecisionService();
        var state = PetRuntimeState.Default with
        {
            SocialNeed = 0.96,
            Boredom = 0.20,
            Hunger = 0.10,
            Thirst = 0.10,
            Stress = 0.05,
            LastInitiativeSpeechAt = now.AddMinutes(-5),
            LastInteractionAt = now.AddHours(-2)
        };
        var context = new InitiativeSpeechContext(
            state, TemperamentProfile.Default,
            RelationshipState.Default with { InitiativeAcceptance = 0.92, Trust = 0.90, Familiarity = 0.90 },
            now, now.AddMinutes(-5), true, false, false, false, 45)
        {
            RecentExperience = new[]
            {
                new PetRecentExperience(now.AddMinutes(-5), "initiative_speech", "Companionship",
                    ExecutionStatus.Completed, 1, "initiative_spoken")
            }
        };
        var unanswered = service.Decide(context);
        Assert(!unanswered.ShouldSpeak && unanswered.ReasonCode == "initiative_cooldown",
            $"unanswered initiative did not extend the cooldown: {unanswered.ReasonCode}");

        var answeredContext = context with
        {
            State = state with { LastInteractionAt = now.AddMinutes(-5) },
            LastSpokenAt = now.AddMinutes(-30),
            RecentExperience = context.RecentExperience.Select(item => item with { At = now.AddMinutes(-30) }).ToArray()
        };
        var answered = service.Decide(answeredContext);
        var companionship = answered.Candidates.Single(x => x.Topic == InitiativeSpeechTopic.Companionship);
        Assert(companionship.ReasonCodes.Any(x => x.StartsWith("relationship=", StringComparison.Ordinal)),
            "initiative candidate did not expose relationship scoring");
        Assert(companionship.ReasonCodes.Any(x => x.StartsWith("topic_repeat_penalty=", StringComparison.Ordinal)),
            "initiative candidate did not expose topic-memory suppression");

        var exhaustedBudget = answeredContext with
        {
            RecentExperience = Enumerable.Range(0, 6)
                .Select(index => new PetRecentExperience(now.AddMinutes(-index * 20), "initiative_speech", "Curiosity",
                    ExecutionStatus.Completed, 1, "initiative_spoken"))
                .ToArray()
        };
        Assert(service.Decide(exhaustedBudget).ReasonCode == "initiative_budget_exhausted",
            "initiative speech budget did not suppress excessive spontaneous speech");
    }

    public static void DialogueAlbumAndInteractionMemoryBecomeBoundedDecisionWeights()
    {
        var now = new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
        var projector = new PetDecisionMemoryProjector();
        var profile = projector.Project(new[]
        {
            new PetMemoryEvidence("conversation-1", PetMemoryEvidenceSource.ConfirmedConversation, "老爸常带我去草地玩，我很喜欢一起出门。"),
            new PetMemoryEvidence("album-1", PetMemoryEvidenceSource.AlbumDescription, "和老爸出去玩", PetMemoryTheme.Play),
            new PetMemoryEvidence("album-2", PetMemoryEvidenceSource.AlbumDescription, "南京日落旅行", PetMemoryTheme.Explore)
        }, now);
        Assert(profile.CategoryWeight(BehaviorSemanticCategory.Explore) > 0,
            "album and conversation evidence did not produce an explore weight");
        Assert(profile.CategoryWeight(BehaviorSemanticCategory.Explore) <= 0.10,
            "decision-memory category weight exceeded its safety bound");
        Assert(profile.InitiativeTopicWeight(InitiativeSpeechTopic.Play) > 0,
            "memory evidence did not influence initiative topic weight");

        var capability = Capability("wk.observe.memory", BehaviorParticipationMode.Autonomous,
            BehaviorEffortLevel.Low, BehaviorRequestSource.AutonomousTick) with
        {
            Category = BehaviorSemanticCategory.Explore
        };
        var state = PetAgentState.CreateDefault(now) with
        {
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Stand,
                CurrentPoseId = "stand.neutral.left_front"
            },
            DecisionMemory = profile
        };
        var decision = new BehaviorDecisionEngine().Decide(
            state,
            new BehaviorCapabilityCatalog(new[] { capability }),
            new BehaviorDecisionInput(
                BehaviorRequestSource.AutonomousTick,
                now,
                "stable_stand_idle",
                now.AddMinutes(-2),
                true,
                new Dictionary<string, DateTimeOffset>(),
                Array.Empty<string>(),
                77,
                true,
                true));
        Assert(Component(decision.Candidates.Single(), "decision_memory") > 0,
            "projected memory was not included in the behavior utility score");

        var unavailable = capability with { ProductionApproved = false, RuntimeUse = false };
        var gated = new BehaviorDecisionEngine().Decide(
            state,
            new BehaviorCapabilityCatalog(new[] { unavailable }),
            new BehaviorDecisionInput(
                BehaviorRequestSource.AutonomousTick, now, "stable_stand_idle", now.AddMinutes(-2), true,
                new Dictionary<string, DateTimeOffset>(), Array.Empty<string>(), 77, true, true));
        Assert(gated.Disposition == RequestDisposition.Deferred &&
               gated.Candidates.Single().GateReasons.Contains("runtime_capability_unavailable"),
            "decision memory bypassed the runtime asset gate");
    }

    public static void InitiativeFeedbackTracksUnansweredAndExplicitDialogueResponse()
    {
        var now = new DateTimeOffset(2026, 9, 21, 11, 0, 0, TimeSpan.Zero);
        var reducer = new PetStateReducer();
        var initial = PetAgentState.CreateDefault(now);
        var first = reducer.Reduce(initial, new PetInitiativeSpeechOccurred(now, "Play"));
        var second = reducer.Reduce(first, new PetInitiativeSpeechOccurred(now.AddMinutes(20), "Curiosity"));
        Assert(second.InitiativeSpeechFeedback.ConsecutiveUnanswered == 1,
            "a second initiative did not record the unanswered previous initiative");
        var answered = reducer.Reduce(second, new PetOwnerDialogueObserved(now.AddMinutes(21)));
        Assert(answered.InitiativeSpeechFeedback.PendingTopic is null &&
               answered.InitiativeSpeechFeedback.ConsecutiveUnanswered == 0,
            "an explicit owner dialogue response did not clear pending initiative feedback");
        Assert(answered.Relationship.InitiativeAcceptance > second.Relationship.InitiativeAcceptance,
            "an explicit response did not improve bounded initiative acceptance");
    }

    public static void CommandDeferralIncludesRetryAndExplainableComponents()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        var capability = Capability("wk.command.jump", BehaviorParticipationMode.UsuallyCooperative,
            BehaviorEffortLevel.High, BehaviorRequestSource.OwnerContextMenu);
        var state = PetAgentState.CreateDefault(now) with
        {
            Temperament = TemperamentProfile.Default with { CommandCooperativeness = 0.50 },
            Relationship = RelationshipState.Default with { Trust = 0.35, Familiarity = 0.30 },
            Runtime = PetRuntimeState.Default with
            {
                CurrentPosture = StablePosture.Stand,
                CurrentPoseId = "stand.neutral.left_front",
                Energy = 0.35,
                Stress = 0.62,
                MoodValence = 0.42
            }
        };
        var decision = new BehaviorParticipationPolicy().Evaluate(
            capability, state, BehaviorRequestSource.OwnerContextMenu, now);
        Assert(decision.Disposition == RequestDisposition.Deferred,
            $"moderately unavailable command should defer rather than fake acceptance: {decision.Disposition}");
        Assert(decision.RetryAt > now, "deferred command did not expose a retry time");
        Assert(decision.Components.ContainsKey("available_energy") && decision.Components.ContainsKey("stress_safety"),
            "deferred command did not expose explainable state components");
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

    private static double Component(BehaviorDecisionCandidate candidate, string name) =>
        candidate.Components.Single(item => item.Name == name).Value;

    private static void AssertEquivalentState(PetAgentState expected, PetAgentState actual, string message)
    {
        Assert(expected.Runtime == actual.Runtime, message);
        Assert(expected.Relationship == actual.Relationship, message);
        Assert(expected.Temperament == actual.Temperament, message);
        Assert(expected.Episode == actual.Episode, message);
        Assert(expected.Clock == actual.Clock, message);
        Assert(expected.DecisionMemory.Fingerprint == actual.DecisionMemory.Fingerprint &&
               expected.DecisionMemory.CategoryWeights.Count == actual.DecisionMemory.CategoryWeights.Count &&
               expected.DecisionMemory.CategoryWeights.All(pair =>
                   actual.DecisionMemory.CategoryWeights.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
               expected.DecisionMemory.InitiativeTopicWeights.Count == actual.DecisionMemory.InitiativeTopicWeights.Count &&
               expected.DecisionMemory.InitiativeTopicWeights.All(pair =>
                   actual.DecisionMemory.InitiativeTopicWeights.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
               expected.DecisionMemory.EvidenceCounts.Count == actual.DecisionMemory.EvidenceCounts.Count &&
               expected.DecisionMemory.EvidenceCounts.All(pair =>
                   actual.DecisionMemory.EvidenceCounts.TryGetValue(pair.Key, out var value) && value == pair.Value),
            message);
        Assert(expected.InitiativeSpeechFeedback == actual.InitiativeSpeechFeedback, message);
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
