using Wukong.Application;
using Wukong.Domain;

internal static class CompanionQualityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public static void CommandRepetitionIsActionScopedAndExpires()
    {
        var policy = new BehaviorParticipationPolicy();
        var state = Stand() with
        {
            Runtime = Stand().Runtime with
            {
                LastActionId = "jump", RepeatedActionCount = 8, LastInteractionAt = Now.AddSeconds(-2)
            }
        };
        var sit = Capability("sit") with { ParticipationMode = BehaviorParticipationMode.UsuallyCooperative };
        Require(policy.Evaluate(sit, state, BehaviorRequestSource.OwnerContextMenu, Now).Disposition == RequestDisposition.Accepted,
            "another command inherited jump repetitions");
        var repeated = sit with { BehaviorId = "jump" };
        Require(policy.Evaluate(repeated, state, BehaviorRequestSource.OwnerContextMenu, Now).Disposition == RequestDisposition.Rejected,
            "actual repeated command lost tolerance protection");
        var rested = state with { Runtime = state.Runtime with { LastInteractionAt = Now.AddHours(-1) } };
        Require(policy.Evaluate(repeated, rested, BehaviorRequestSource.OwnerContextMenu, Now).Disposition == RequestDisposition.Accepted,
            "repetition refusal never expired");
        var wrongPose = sit with { StartPoseFamily = "prone.front" };
        Require(policy.Evaluate(wrongPose, Stand(), BehaviorRequestSource.OwnerContextMenu, Now).ReasonCode == "pose_transition_required",
            "command willingness bypassed visual-pose compatibility");
    }

    public static void MemoryEvidenceIsDeduplicatedSignedAndDecayed()
    {
        var projector = new PetDecisionMemoryProjector();
        var item = new PetMemoryEvidence("album", PetMemoryEvidenceSource.AlbumDescription, "喜欢散步") { ObservedAt = Now };
        var once = projector.Project(new[] { item }, Now);
        var repeated = projector.Project(Enum.GetValues<PetMemoryTheme>().Select(theme => item with { SuggestedTheme = theme }), Now);
        Require(once.CategoryWeight(BehaviorSemanticCategory.Explore) == repeated.CategoryWeight(BehaviorSemanticCategory.Explore),
            "one album was counted once per retrieval query");
        Require(repeated.EvidenceCounts["album_description"] == 1, "duplicate evidence count inflated");
        var labelOnly = projector.Project(new[] { item with { Text = "无描述", SuggestedTheme = PetMemoryTheme.Food } }, Now);
        Require(labelOnly.CategoryWeight(BehaviorSemanticCategory.Food) == 0, "search hint became evidence");
        var negative = projector.Project(new[] { item with { Text = "不喜欢散步" } }, Now);
        Require(negative.CategoryWeight(BehaviorSemanticCategory.Explore) < 0, "aversion increased affinity");
        var old = projector.Project(new[] { item with { ObservedAt = Now.AddDays(-180) } }, Now);
        Require(old.CategoryWeight(BehaviorSemanticCategory.Explore) < once.CategoryWeight(BehaviorSemanticCategory.Explore) / 2,
            "old evidence did not decay");
        var lowConfidence = projector.Project(new[] { item with { Confidence = 0 } }, Now);
        Require(lowConfidence.CategoryWeight(BehaviorSemanticCategory.Explore) == 0, "zero confidence influenced behavior");
    }

    public static void EpisodeCooldownAndAvailabilityAreEnforced()
    {
        var state = Stand() with { Runtime = Stand().Runtime with { Curiosity = 0.95, Focus = 0.95 } };
        var policy = new PetEpisodePolicy();
        var available = new HashSet<PetEpisodeKind> { PetEpisodeKind.Resting };
        Require(!policy.Evaluate(state, Now.AddMinutes(2), available).Changed, "unavailable episode entered");
        var exhausted = state with { Runtime = state.Runtime with { Energy = 0.02 } };
        Require(policy.Evaluate(exhausted, Now.AddMinutes(2), available).Episode.Kind == PetEpisodeKind.Resting,
            "exhaustion selected an unavailable episode instead of compatible rest");
        var reducer = new PetStateReducer();
        var observing = reducer.Reduce(state, new PetEpisodeChanged(Now.AddMinutes(2), PetEpisodeKind.Observing,
            TimeSpan.FromSeconds(20), "observe"));
        var resting = reducer.Reduce(observing, new PetEpisodeChanged(Now.AddMinutes(3), PetEpisodeKind.Resting,
            TimeSpan.FromSeconds(45), "rest"));
        Require(resting.EpisodeLastEndedAt[PetEpisodeKind.Observing] == Now.AddMinutes(3), "episode end not recorded");
        var cooldownState = resting with { Episode = resting.Episode with { MinimumDwell = TimeSpan.Zero } };
        Require(policy.Evaluate(cooldownState, Now.AddMinutes(3).AddSeconds(5)).ReasonCodes.Contains("episode_reentry_cooldown"),
            "episode immediately reentered");
        var same = reducer.Reduce(resting, new PetEpisodeChanged(Now.AddMinutes(4), PetEpisodeKind.Resting, TimeSpan.Zero, "duplicate"));
        Require(same.Episode.StartedAt == resting.Episode.StartedAt, "idle reset episode dwell indefinitely");
    }

    public static void TenThousandAutonomousChoicesRespectHardGates()
    {
        var idle = Capability("idle");
        var command = Capability("jump") with { ParticipationMode = BehaviorParticipationMode.UsuallyCooperative };
        var unavailable = Capability("unapproved") with { ProductionApproved = false };
        var incompatible = Capability("front") with { StartPoseFamily = "prone.front" };
        var catalog = new BehaviorCapabilityCatalog(new[] { idle, command, unavailable, incompatible });
        var engine = new BehaviorDecisionEngine();
        for (var seed = 0; seed < 100; seed++)
        for (var step = 0; step < 100; step++)
        {
            var state = Stand();
            var input = new BehaviorDecisionInput(BehaviorRequestSource.AutonomousTick, Now.AddSeconds(step), "idle",
                Now.AddMinutes(-10), true, new Dictionary<string, DateTimeOffset>(), Array.Empty<string>(), seed,
                false, true);
            var first = engine.Decide(state, catalog, input);
            var again = engine.Decide(state, catalog, input);
            Require(first.SelectedBehaviorId == "idle" && again.SelectedBehaviorId == first.SelectedBehaviorId,
                "hard-gated action selected or nondeterministic result");
            var busy = engine.Decide(state with { Runtime = state.Runtime with { IsBusy = true } }, catalog, input);
            Require(busy.Disposition == RequestDisposition.Deferred && busy.SelectedBehaviorId is null,
                "ordinary autonomous tick interrupted even though current player was active");
        }
    }

    public static void InterruptedOutcomesSpendButDoNotReward()
    {
        var reducer = new PetStateReducer();
        var id = Guid.NewGuid();
        var initial = Stand();
        var started = reducer.Reduce(initial, new PetBehaviorStarted(Now, id, "meal", "intro", true,
            BehaviorRequestSource.OwnerContextMenu, BehaviorExecutionMode.Normal));
        var finish = new PetBehaviorFinished(Now.AddSeconds(1), id, "meal", ExecutionStatus.Interrupted, 0.5,
            StablePosture.Sit, "sit.neutral.left_front", new PetStateEffects(Energy: -0.02, Hunger: -0.4, MoodValence: 0.1),
            true, true, PartialEffectPolicy.Proportional, BehaviorRequestSource.OwnerContextMenu, BehaviorExecutionMode.Normal, "stop");
        var stopped = reducer.Reduce(started, finish);
        Require(Math.Abs(stopped.Runtime.Energy - initial.Runtime.Energy + 0.01) < 1e-9, "partial energy cost missing");
        Require(stopped.Runtime.Hunger == initial.Runtime.Hunger && stopped.Runtime.MoodValence == initial.Runtime.MoodValence,
            "unfinished action granted success benefits");
        Require(stopped.Runtime.CurrentPoseId == initial.Runtime.CurrentPoseId && stopped.Preferences.Count == 0,
            "interruption changed stable pose or learned a preference");
        var premature = reducer.Reduce(started, finish with { Status = ExecutionStatus.Progressed });
        Require(premature.Runtime.IsBusy, "nonterminal callback settled state");
        var late = reducer.Reduce(started, finish with { At = Now.AddSeconds(-1) });
        Require(late.Runtime.IsBusy, "completion predating start settled state");
    }

    public static void PanelFailureAndAutonomousSuccessDoNotTeachPreferences()
    {
        var reducer = new PetStateReducer();
        foreach (var source in new[] { BehaviorRequestSource.ControlPanel, BehaviorRequestSource.AutonomousTick })
        {
            var initial = Stand();
            var id = Guid.NewGuid();
            var started = reducer.Reduce(initial, new PetBehaviorStarted(Now, id, "rest", "intro", true, source, BehaviorExecutionMode.Normal));
            var ended = reducer.Reduce(started, new PetBehaviorFinished(Now.AddSeconds(1), id, "rest",
                source == BehaviorRequestSource.ControlPanel ? ExecutionStatus.Failed : ExecutionStatus.Completed,
                1, StablePosture.Stand, initial.Runtime.CurrentPoseId, new PetStateEffects(Stress: -0.02),
                false, true, PartialEffectPolicy.Proportional, source, BehaviorExecutionMode.Normal, "test"));
            Require(ended.Preferences.Count == 0, "self-selection or panel inspection taught a preference");
            if (source == BehaviorRequestSource.ControlPanel)
                Require(ended.Runtime.Stress == initial.Runtime.Stress, "panel failure changed pet stress");
        }
    }

    public static void SleepingAndRecentChatSuppressSpeechAndNegativeReplyCounts()
    {
        Require(InitiativeSpeechDecisionService.IsExplicitQuietReply("先安静一会！"), "explicit quiet feedback missed");
        Require(!InitiativeSpeechDecisionService.IsExplicitQuietReply("我不是让你别说了，继续呀"), "negated statement was treated as a quiet request");
        Require(!InitiativeSpeechDecisionService.IsExplicitQuietReply("今天工作不太开心"), "unrelated negative mood became pet rejection");
        var service = new InitiativeSpeechDecisionService();
        var context = new InitiativeSpeechContext(Stand().Runtime, TemperamentProfile.Default, RelationshipState.Default,
            Now, null, true, false, false, false, 5);
        Require(!service.Decide(context with { Episode = PetEpisodeKind.Sleeping }).ShouldSpeak, "sleep initiated speech");
        Require(service.Decide(context with { State = context.State with { LastInteractionAt = Now.AddSeconds(-30) } }).ReasonCode
                == "recent_owner_interaction", "initiative overlapped explicit chat");
        var reducer = new PetStateReducer();
        var spoken = reducer.Reduce(Stand(), new PetInitiativeSpeechOccurred(Now, "Play"));
        var answered = reducer.Reduce(spoken, new PetOwnerDialogueObserved(Now.AddSeconds(30), false));
        Require(answered.InitiativeSpeechFeedback.PendingTopic is null, "negative answer was mistaken for silence");
        Require(answered.Relationship.InitiativeAcceptance < spoken.Relationship.InitiativeAcceptance, "negative feedback ignored");
    }

    private static PetAgentState Stand() => PetAgentState.CreateDefault(Now) with
    {
        Runtime = PetRuntimeState.Default with { CurrentPosture = StablePosture.Stand, CurrentPoseId = "stand.neutral.left_front" }
    };

    private static BehaviorCapability Capability(string id) => new(id, BehaviorSemanticCategory.StableIdle,
        BehaviorParticipationMode.Autonomous, BehaviorInterruptionPolicy.SafePreempt,
        new HashSet<BehaviorRequestSource> { BehaviorRequestSource.AutonomousTick, BehaviorRequestSource.OwnerContextMenu },
        new HashSet<StablePosture> { StablePosture.Stand }, StablePosture.Stand, BehaviorEffortLevel.Low,
        new HashSet<PetEpisodeKind> { PetEpisodeKind.Resting }, true, true, true, true, false,
        TimeSpan.Zero, TimeSpan.Zero, 0.5, new PetStateEffects()) { StartPoseFamily = "stand" };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
