using Wukong.Domain;

namespace Wukong.Application;

public enum StablePosture
{
    Stand,
    Sit,
    Prone
}

public enum BehaviorDecisionSource
{
    OwnerCommand,
    Autonomous,
    Dialogue
}

public enum OwnerCommandKind
{
    None,
    Sit,
    Down,
    Paw,
    Jump,
    Spin,
    Eat
}

public sealed record TemperamentProfile(
    int Activity,
    int Attachment,
    int Sensitivity,
    int Independence,
    int Mischief)
{
    public static TemperamentProfile Default { get; } = new(56, 62, 42, 38, 35);
    public double Activity01 => Clamp01(Activity);
    public double Attachment01 => Clamp01(Attachment);
    public double Sensitivity01 => Clamp01(Sensitivity);
    public double Independence01 => Clamp01(Independence);
    public double Mischief01 => Clamp01(Mischief);
    private static double Clamp01(int value) => Math.Clamp(value, 0, 100) / 100.0;
}

public sealed record PetRuntimeState(
    StablePosture CurrentPosture,
    double Energy,
    double Hunger,
    double SocialNeed,
    double Boredom,
    double Stress,
    double MoodValence,
    double Arousal,
    DateTimeOffset? LastInteractionAt,
    string? LastActionId,
    int RepeatedActionCount,
    string? ActiveActionId,
    bool IsBusy)
{
    public static PetRuntimeState Default { get; } = new(
        StablePosture.Prone, 0.68, 0.24, 0.48, 0.34, 0.12, 0.72, 0.42,
        null, null, 0, null, false);

    public double Curiosity { get; init; } = 0.46;
    public double Comfort { get; init; } = 0.78;
    public double Focus { get; init; } = 0.58;

    public PetRuntimeState Clamp() => this with
    {
        Energy = Clamp01(Energy),
        Hunger = Clamp01(Hunger),
        SocialNeed = Clamp01(SocialNeed),
        Boredom = Clamp01(Boredom),
        Stress = Clamp01(Stress),
        MoodValence = Clamp01(MoodValence),
        Arousal = Clamp01(Arousal),
        Curiosity = Clamp01(Curiosity),
        Comfort = Clamp01(Comfort),
        Focus = Clamp01(Focus),
        RepeatedActionCount = Math.Max(0, RepeatedActionCount)
    };

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}

public sealed record RelationshipState(
    double Trust,
    double Familiarity,
    int RecentPositiveInteractions,
    int RecentNegativeInteractions)
{
    public static RelationshipState Default { get; } = new(0.70, 0.56, 0, 0);
    public double TouchAcceptance { get; init; } = 0.72;
    public double InitiativeAcceptance { get; init; } = 0.64;
    public double Trust01 => Math.Clamp(Trust, 0, 1);
    public double Familiarity01 => Math.Clamp(Familiarity, 0, 1);
    public double TouchAcceptance01 => Math.Clamp(TouchAcceptance, 0, 1);
    public double InitiativeAcceptance01 => Math.Clamp(InitiativeAcceptance, 0, 1);
}

public sealed record BehaviorDecisionContext(
    TemperamentProfile Temperament,
    PetRuntimeState State,
    RelationshipState Relationship,
    OwnerCommandKind OwnerCommand,
    IReadOnlyList<string> RecentBehaviorHistory,
    DateTimeOffset Now,
    IReadOnlyDictionary<string, DateTimeOffset> Cooldowns,
    int RandomSeed,
    bool AllowInitiative,
    bool IsNonInterruptible);

public sealed record TransitionStep(string ActionId, StablePosture FromPosture, StablePosture ToPosture, bool MockOnly, string Reason);

public sealed record BehaviorCandidateScore(
    string ActionId,
    IReadOnlyList<ScoreComponent> Components,
    double FinalScore,
    bool Eliminated,
    IReadOnlyList<string> Reasons);

public sealed record PetDecision(
    Guid DecisionId,
    BehaviorDecisionSource Source,
    string Intent,
    string SelectedActionId,
    StablePosture StartPosture,
    StablePosture EndPosture,
    IReadOnlyList<TransitionStep> TransitionPlan,
    string MoodExpression,
    string DialogueStyle,
    string DialogueHint,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<BehaviorCandidateScore> CandidateScores,
    DateTimeOffset CreatedAt);

public sealed record DialogueContextSnapshot(
    string PersonalitySummary,
    string CurrentMood,
    string EnergyLevel,
    string StressLevel,
    string SocialNeed,
    StablePosture CurrentPosture,
    string? CurrentAction,
    string LastOwnerEvent,
    string RelationshipSummary,
    string DialogueTone,
    string InitiativeLevel,
    string ResponseLength,
    string BehaviorIntent,
    IReadOnlyList<string> ForbiddenClaims);

public sealed record PetStateUpdate(
    PetRuntimeState State,
    RelationshipState Relationship,
    IReadOnlyList<string> Events);

public static class MockCommandActionIds
{
    public const string Sit = "wk.command.sit";
    public const string Down = "wk.command.lie_down";
    public const string PawSit = "wk.command.paw_sit";
    public const string PawProne = "wk.command.paw_prone";
    public const string Jump = "wk.command.jump";
    public const string Spin = "wk.command.spin";
    public const string EatSit = "wk.command.eat_sit";
    public const string EatProne = "wk.command.eat_prone";
    public const string MockStandToSit = "wk.mock.transition.stand_to_sit";
    public const string MockSitToProne = "wk.mock.transition.sit_to_prone";
    public const string MockProneToSit = "wk.mock.transition.prone_to_sit";
    public const string MockSitToStand = "wk.mock.transition.sit_to_stand";
    public const string MaintainCurrentIdle = "wk.agent.maintain_current_idle";
    public const string OrientToOwner = "wk.agent.orient_to_owner";
    public const string QuietSit = "wk.agent.quiet_sit";
    public const string QuietProne = "wk.agent.quiet_prone";
    public const string RequestAttention = "wk.agent.request_attention";
    public const string PlayfulJump = "wk.agent.playful_jump";
    public const string PlayfulSpin = "wk.agent.playful_spin";
    public const string AskForFood = "wk.agent.ask_for_food";
    public const string Rest = "wk.agent.rest";
    public const string Observe = "wk.agent.observe";

    public static readonly IReadOnlySet<string> PrototypeWhitelist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Sit, Down, PawSit, PawProne, Jump, Spin, EatSit, EatProne,
            MockStandToSit, MockSitToProne, MockProneToSit, MockSitToStand,
            MaintainCurrentIdle, OrientToOwner, QuietSit, QuietProne, RequestAttention,
            PlayfulJump, PlayfulSpin, AskForFood, Rest, Observe
        };
}

public sealed class BehaviorAgentMockEngine
{
    private static readonly TimeSpan OwnerCooldown = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AutonomousCooldown = TimeSpan.FromSeconds(16);
    private static readonly IReadOnlySet<string> AutonomousActionAllowlist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MockCommandActionIds.MaintainCurrentIdle,
            MockCommandActionIds.OrientToOwner,
            MockCommandActionIds.QuietSit,
            MockCommandActionIds.QuietProne,
            MockCommandActionIds.RequestAttention,
            MockCommandActionIds.AskForFood,
            MockCommandActionIds.Rest,
            MockCommandActionIds.Observe
        };

    public static bool IsAutonomousActionAllowed(string actionId) =>
        AutonomousActionAllowlist.Contains(actionId);

    public PetDecision Decide(BehaviorDecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State.Clamp();
        if (context.IsNonInterruptible)
        {
            return BuildBlockedDecision(context, state, "busy_non_interruptible");
        }

        return context.OwnerCommand == OwnerCommandKind.None
            ? DecideAutonomous(context with { State = state })
            : DecideOwnerCommand(context with { State = state });
    }

    public DialogueContextSnapshot BuildDialogueContext(PetDecision decision, BehaviorDecisionContext context)
    {
        var state = context.State.Clamp();
        var temperament = context.Temperament;
        var tone = decision.DialogueStyle;
        var initiative = context.AllowInitiative && temperament.Attachment01 + state.SocialNeed > 1.1
            ? "may_initiate_briefly"
            : "reply_only";
        var length = state.Energy < 0.25 || state.Stress > 0.72 ? "short" : "brief";
        return new DialogueContextSnapshot(
            $"activity={temperament.Activity}; attachment={temperament.Attachment}; sensitivity={temperament.Sensitivity}; independence={temperament.Independence}; mischief={temperament.Mischief}",
            state.MoodValence >= 0.65 ? "positive" : state.MoodValence <= 0.35 ? "low" : "neutral",
            Band(state.Energy),
            Band(state.Stress),
            Band(state.SocialNeed),
            state.CurrentPosture,
            state.ActiveActionId,
            decision.Intent,
            $"trust={context.Relationship.Trust01:0.00}; familiarity={context.Relationship.Familiarity01:0.00}",
            tone,
            initiative,
            length,
            decision.SelectedActionId,
            new[]
            {
                "Do not claim an action that is not in TransitionPlan.",
                "Do not claim a posture that differs from EndPosture.",
                "Do not name asset paths or files.",
                "Do not claim production approval for mock assets."
            });
    }

    public PetStateUpdate ApplyOutcome(
        PetRuntimeState state,
        RelationshipState relationship,
        PetDecision decision,
        bool completed,
        DateTimeOffset completedAt)
    {
        var next = state.Clamp();
        var relation = relationship;
        var events = new List<string> { $"decision:{decision.DecisionId}", $"action:{decision.SelectedActionId}" };

        if (!completed)
        {
            next = next with { IsBusy = false, ActiveActionId = null, Stress = Clamp01(next.Stress + 0.03) };
            events.Add("outcome:interrupted_or_failed");
            return new PetStateUpdate(next, relation, events);
        }

        var repeat = string.Equals(next.LastActionId, decision.SelectedActionId, StringComparison.OrdinalIgnoreCase)
            ? next.RepeatedActionCount + 1
            : 0;
        var ownerInteraction = decision.Source is BehaviorDecisionSource.OwnerCommand or BehaviorDecisionSource.Dialogue;
        next = next with
        {
            CurrentPosture = decision.EndPosture,
            LastActionId = decision.SelectedActionId,
            LastInteractionAt = ownerInteraction ? completedAt : next.LastInteractionAt,
            RepeatedActionCount = repeat,
            ActiveActionId = null,
            IsBusy = false,
            Energy = Clamp01(next.Energy + EnergyDelta(decision.SelectedActionId)),
            Hunger = Clamp01(next.Hunger + HungerDelta(decision.SelectedActionId)),
            SocialNeed = Clamp01(next.SocialNeed + (ownerInteraction ? -0.05 : 0.005)),
            Boredom = Clamp01(next.Boredom - 0.08),
            Stress = Clamp01(next.Stress - 0.02),
            MoodValence = Clamp01(next.MoodValence + 0.025)
        };
        if (ownerInteraction)
        {
            relation = relation with
            {
                Trust = Clamp01(relation.Trust + 0.01),
                Familiarity = Clamp01(relation.Familiarity + 0.006),
                RecentPositiveInteractions = relation.RecentPositiveInteractions + 1
            };
        }
        events.Add("outcome:completed");
        events.Add($"posture:{decision.EndPosture}");
        return new PetStateUpdate(next, relation, events);
    }

    public PetRuntimeState ApplyRepeatedClick(PetRuntimeState state, TemperamentProfile temperament, int clickCount)
    {
        var stressDelta = Math.Max(0, clickCount - 1) * (0.02 + temperament.Sensitivity01 * 0.06);
        return state.Clamp() with { Stress = Clamp01(state.Stress + stressDelta), Arousal = Clamp01(state.Arousal + 0.04) };
    }

    private static PetDecision DecideOwnerCommand(BehaviorDecisionContext context)
    {
        var selected = ResolveOwnerCommandAction(context.State.CurrentPosture, context.OwnerCommand);
        var plan = PlanOwnerTransition(context.State.CurrentPosture, context.OwnerCommand, selected);
        var scores = ScoreOwnerCandidates(context, selected).ToArray();
        var mood = MoodExpression(context);
        var reasons = new List<string> { "owner_command_priority", $"posture={context.State.CurrentPosture}" };
        if (plan.Any(x => x.MockOnly))
            reasons.Add("mock_transition_gap");
        return new PetDecision(
            Guid.NewGuid(),
            BehaviorDecisionSource.OwnerCommand,
            context.OwnerCommand.ToString(),
            selected,
            context.State.CurrentPosture,
            EndPostureFor(selected),
            plan,
            mood,
            DialogueStyle(context),
            DialogueHintFor(selected, context),
            reasons,
            scores,
            context.Now);
    }

    private static PetDecision DecideAutonomous(BehaviorDecisionContext context)
    {
        var candidates = AutonomousCandidates(context).ToArray();
        var selected = candidates
            .Where(x => !x.Eliminated)
            .OrderByDescending(x => x.FinalScore)
            .FirstOrDefault()
            ?? candidates.First(x => x.ActionId == MockCommandActionIds.MaintainCurrentIdle);
        return new PetDecision(
            Guid.NewGuid(),
            BehaviorDecisionSource.Autonomous,
            "autonomous_tick",
            selected.ActionId,
            context.State.CurrentPosture,
            AutonomousEndPosture(selected.ActionId, context.State.CurrentPosture),
            new[] { new TransitionStep(selected.ActionId, context.State.CurrentPosture, AutonomousEndPosture(selected.ActionId, context.State.CurrentPosture), true, "mock_only") },
            MoodExpression(context),
            DialogueStyle(context),
            DialogueHintFor(selected.ActionId, context),
            selected.Reasons.Count == 0 ? new[] { "utility_selected" } : selected.Reasons,
            candidates,
            context.Now);
    }

    private static PetDecision BuildBlockedDecision(BehaviorDecisionContext context, PetRuntimeState state, string reason) =>
        new(
            Guid.NewGuid(),
            context.OwnerCommand == OwnerCommandKind.None ? BehaviorDecisionSource.Autonomous : BehaviorDecisionSource.OwnerCommand,
            context.OwnerCommand.ToString(),
            MockCommandActionIds.MaintainCurrentIdle,
            state.CurrentPosture,
            state.CurrentPosture,
            Array.Empty<TransitionStep>(),
            "still",
            "quiet",
            "Current action is not safely interruptible.",
            new[] { reason },
            Array.Empty<BehaviorCandidateScore>(),
            context.Now);

    private static IEnumerable<BehaviorCandidateScore> ScoreOwnerCandidates(BehaviorDecisionContext context, string selected)
    {
        foreach (var action in new[]
        {
            MockCommandActionIds.Sit, MockCommandActionIds.Down, MockCommandActionIds.PawSit, MockCommandActionIds.PawProne,
            MockCommandActionIds.Jump, MockCommandActionIds.Spin, MockCommandActionIds.EatSit, MockCommandActionIds.EatProne
        })
        {
            var eliminated = action != selected;
            var components = ScoreComponents(context, action, baseWeight: action == selected ? 3.0 : 0.2);
            yield return new BehaviorCandidateScore(
                action,
                components,
                components.Sum(x => x.Value),
                eliminated,
                eliminated ? new[] { "not_matching_owner_command_or_posture" } : Array.Empty<string>());
        }
    }

    private static IEnumerable<BehaviorCandidateScore> AutonomousCandidates(BehaviorDecisionContext context)
    {
        foreach (var action in AutonomousActionAllowlist)
        {
            var reasons = HardConstraintReasons(context, action).ToList();
            var components = ScoreComponents(context, action, baseWeight: BaseWeight(action));
            yield return new BehaviorCandidateScore(action, components, components.Sum(x => x.Value), reasons.Count > 0, reasons);
        }
    }

    private static IReadOnlyList<ScoreComponent> ScoreComponents(BehaviorDecisionContext context, string action, double baseWeight)
    {
        var state = context.State;
        var temperament = context.Temperament;
        var relationship = context.Relationship;
        var random = new Random(HashCode.Combine(context.RandomSeed, action, state.CurrentPosture));
        var jitter = (random.NextDouble() - 0.5) * 0.08;
        return new[]
        {
            new ScoreComponent("base_weight", baseWeight),
            new ScoreComponent("temperament", TemperamentScore(temperament, action)),
            new ScoreComponent("runtime_state", RuntimeStateScore(state, action)),
            new ScoreComponent("relationship", RelationshipScore(relationship, state, temperament, action)),
            new ScoreComponent("context", ContextScore(context, action)),
            new ScoreComponent("cooldown_penalty", CooldownPenalty(context, action)),
            new ScoreComponent("repetition_penalty", RepetitionPenalty(state, action)),
            new ScoreComponent("transition_cost", TransitionCost(state.CurrentPosture, action)),
            new ScoreComponent("bounded_randomness", jitter)
        };
    }

    private static IEnumerable<string> HardConstraintReasons(BehaviorDecisionContext context, string action)
    {
        if (!context.AllowInitiative && action is MockCommandActionIds.RequestAttention or MockCommandActionIds.PlayfulJump or MockCommandActionIds.PlayfulSpin or MockCommandActionIds.AskForFood)
            yield return "initiative_disabled";
        if (context.State.IsBusy)
            yield return "busy";
        if (context.Cooldowns.TryGetValue(action, out var last) && context.Now - last < AutonomousCooldown)
            yield return "cooldown";
        if (action is MockCommandActionIds.PlayfulJump or MockCommandActionIds.PlayfulSpin && context.State.Energy < 0.32)
            yield return "energy_too_low";
        if (action is MockCommandActionIds.PlayfulJump or MockCommandActionIds.PlayfulSpin && context.State.Stress > 0.62)
            yield return "stress_too_high";
    }

    private static string ResolveOwnerCommandAction(StablePosture posture, OwnerCommandKind command) => command switch
    {
        OwnerCommandKind.Sit => MockCommandActionIds.Sit,
        OwnerCommandKind.Down => MockCommandActionIds.Down,
        OwnerCommandKind.Paw => posture == StablePosture.Prone ? MockCommandActionIds.PawProne : MockCommandActionIds.PawSit,
        OwnerCommandKind.Jump => MockCommandActionIds.Jump,
        OwnerCommandKind.Spin => MockCommandActionIds.Spin,
        OwnerCommandKind.Eat => posture == StablePosture.Prone ? MockCommandActionIds.EatProne : MockCommandActionIds.EatSit,
        _ => MockCommandActionIds.MaintainCurrentIdle
    };

    private static IReadOnlyList<TransitionStep> PlanOwnerTransition(StablePosture posture, OwnerCommandKind command, string selected)
    {
        var plan = new List<TransitionStep>();
        void Add(string id, StablePosture from, StablePosture to, string reason) => plan.Add(new TransitionStep(id, from, to, true, reason));

        switch (command)
        {
            case OwnerCommandKind.Sit:
                if (posture == StablePosture.Stand) Add(MockCommandActionIds.Sit, StablePosture.Stand, StablePosture.Sit, "stable_stand_to_stable_sit");
                else if (posture == StablePosture.Prone)
                {
                    Add(MockCommandActionIds.MockProneToSit, StablePosture.Prone, StablePosture.Sit, "missing_formal_prone_to_sit_transition");
                    Add(MockCommandActionIds.Sit, StablePosture.Sit, StablePosture.Sit, "light_sit_response");
                }
                else Add(MockCommandActionIds.Sit, StablePosture.Sit, StablePosture.Sit, "already_sit_light_response");
                break;
            case OwnerCommandKind.Down:
                if (posture == StablePosture.Stand) Add(MockCommandActionIds.Sit, StablePosture.Stand, StablePosture.Sit, "planned_stand_to_sit");
                if (posture != StablePosture.Prone) Add(MockCommandActionIds.Down, StablePosture.Sit, StablePosture.Prone, "stable_sit_to_stable_prone");
                else Add(MockCommandActionIds.Down, StablePosture.Prone, StablePosture.Prone, "already_prone_light_response");
                break;
            case OwnerCommandKind.Paw:
                if (posture == StablePosture.Stand) Add(MockCommandActionIds.Sit, StablePosture.Stand, StablePosture.Sit, "planned_stand_to_sit_before_paw");
                plan.Add(new TransitionStep(selected, posture == StablePosture.Prone ? StablePosture.Prone : StablePosture.Sit, EndPostureFor(selected), true, "posture_branch"));
                break;
            case OwnerCommandKind.Eat:
                if (posture == StablePosture.Stand) Add(MockCommandActionIds.Sit, StablePosture.Stand, StablePosture.Sit, "planned_stand_to_sit_before_eat");
                plan.Add(new TransitionStep(selected, posture == StablePosture.Prone ? StablePosture.Prone : StablePosture.Sit, EndPostureFor(selected), true, "posture_branch"));
                break;
            case OwnerCommandKind.Jump:
            case OwnerCommandKind.Spin:
                if (posture == StablePosture.Prone)
                {
                    Add(MockCommandActionIds.MockProneToSit, StablePosture.Prone, StablePosture.Sit, "missing_formal_prone_to_sit_transition");
                    Add(MockCommandActionIds.MockSitToStand, StablePosture.Sit, StablePosture.Stand, "missing_formal_sit_to_stand_transition");
                }
                else if (posture == StablePosture.Sit)
                {
                    Add(MockCommandActionIds.MockSitToStand, StablePosture.Sit, StablePosture.Stand, "missing_formal_sit_to_stand_transition");
                }
                Add(selected, StablePosture.Stand, StablePosture.Stand, "stand_only_command");
                break;
        }

        return plan;
    }

    private static StablePosture EndPostureFor(string action) => action switch
    {
        MockCommandActionIds.Sit or MockCommandActionIds.PawSit or MockCommandActionIds.EatSit or MockCommandActionIds.QuietSit => StablePosture.Sit,
        MockCommandActionIds.Down or MockCommandActionIds.PawProne or MockCommandActionIds.EatProne or MockCommandActionIds.QuietProne or MockCommandActionIds.Rest => StablePosture.Prone,
        _ => StablePosture.Stand
    };

    private static StablePosture AutonomousEndPosture(string action, StablePosture current) => action switch
    {
        MockCommandActionIds.QuietSit => StablePosture.Sit,
        MockCommandActionIds.QuietProne or MockCommandActionIds.Rest => StablePosture.Prone,
        MockCommandActionIds.PlayfulJump or MockCommandActionIds.PlayfulSpin => StablePosture.Stand,
        _ => current
    };

    private static double BaseWeight(string action) => action switch
    {
        MockCommandActionIds.MaintainCurrentIdle => 0.80,
        MockCommandActionIds.OrientToOwner => 0.58,
        MockCommandActionIds.QuietSit => 0.48,
        MockCommandActionIds.QuietProne => 0.54,
        MockCommandActionIds.RequestAttention => 0.36,
        MockCommandActionIds.PlayfulJump => 0.28,
        MockCommandActionIds.PlayfulSpin => 0.26,
        MockCommandActionIds.AskForFood => 0.25,
        MockCommandActionIds.Rest => 0.45,
        MockCommandActionIds.Observe => 0.42,
        _ => 0.50
    };

    private static double TemperamentScore(TemperamentProfile t, string action) => action switch
    {
        MockCommandActionIds.PlayfulJump or MockCommandActionIds.PlayfulSpin => t.Activity01 * 0.75 + t.Mischief01 * 0.35,
        MockCommandActionIds.RequestAttention or MockCommandActionIds.OrientToOwner => t.Attachment01 * 0.65 - t.Independence01 * 0.22,
        MockCommandActionIds.MaintainCurrentIdle or MockCommandActionIds.Rest => t.Independence01 * 0.45,
        MockCommandActionIds.Observe => t.Activity01 * 0.35 + t.Mischief01 * 0.15 + t.Independence01 * 0.12 + t.Sensitivity01 * 0.08,
        _ => t.Activity01 * 0.12
    };

    private static double RuntimeStateScore(PetRuntimeState s, string action) => action switch
    {
        MockCommandActionIds.PlayfulJump or MockCommandActionIds.PlayfulSpin => s.Energy * 0.70 + s.Boredom * 0.62 - s.Stress * 0.85,
        MockCommandActionIds.RequestAttention => s.SocialNeed * 0.84 + s.Boredom * 0.18 - s.Stress * 0.18,
        MockCommandActionIds.AskForFood => s.Hunger * 0.92 - s.Stress * 0.15,
        MockCommandActionIds.Rest => (1 - s.Energy) * 0.72 + s.Stress * 0.24,
        MockCommandActionIds.QuietSit or MockCommandActionIds.QuietProne => s.Stress * 0.30 + (1 - s.Arousal) * 0.26,
        MockCommandActionIds.Observe => s.CuriosityLike() * 0.32 + s.SocialNeed * 0.15,
        _ => 0.18 + (1 - s.Stress) * 0.14
    };

    private static double RelationshipScore(RelationshipState r, PetRuntimeState s, TemperamentProfile t, string action) =>
        action is MockCommandActionIds.RequestAttention or MockCommandActionIds.OrientToOwner
            ? r.Trust01 * 0.20 + r.Familiarity01 * 0.18 + t.Attachment01 * s.SocialNeed * 0.18
            : r.Trust01 * 0.05;

    private static double ContextScore(BehaviorDecisionContext c, string action)
    {
        var recentInteractionBonus = c.State.LastInteractionAt is not null && c.Now - c.State.LastInteractionAt.Value < TimeSpan.FromSeconds(20)
            ? 0.15
            : 0.0;
        return action is MockCommandActionIds.OrientToOwner or MockCommandActionIds.RequestAttention
            ? recentInteractionBonus
            : 0.0;
    }

    private static double CooldownPenalty(BehaviorDecisionContext c, string action) =>
        c.Cooldowns.TryGetValue(action, out var last) && c.Now - last < (c.OwnerCommand == OwnerCommandKind.None ? AutonomousCooldown : OwnerCooldown)
            ? -2.0
            : 0.0;

    private static double RepetitionPenalty(PetRuntimeState state, string action) =>
        string.Equals(state.LastActionId, action, StringComparison.OrdinalIgnoreCase)
            ? -0.22 * Math.Clamp(state.RepeatedActionCount + 1, 1, 5)
            : 0.0;

    private static double TransitionCost(StablePosture posture, string action)
    {
        var end = EndPostureFor(action);
        if (posture == end || action.StartsWith("wk.agent.", StringComparison.Ordinal))
            return 0.0;
        return (posture, end) is (StablePosture.Stand, StablePosture.Prone) or (StablePosture.Prone, StablePosture.Stand)
            ? -0.36
            : -0.16;
    }

    private static string MoodExpression(BehaviorDecisionContext context)
    {
        var s = context.State;
        if (s.Stress > 0.68)
            return "cautious";
        if (s.Energy > 0.65 && s.MoodValence > 0.62 && context.Temperament.Activity > 60)
            return "bright";
        if (s.Energy < 0.28)
            return "tired";
        return "calm";
    }

    private static string DialogueStyle(BehaviorDecisionContext context)
    {
        var s = context.State;
        var t = context.Temperament;
        if (s.Stress > 0.70 && t.Sensitivity > 55)
            return "careful_short";
        if (s.Energy < 0.30)
            return "sleepy_brief";
        if (t.Independence > 65)
            return "composed";
        if (t.Attachment > 65 && s.SocialNeed > 0.58)
            return "warm_attentive";
        if (t.Mischief > 60 && s.Energy > 0.52 && s.Stress < 0.45)
            return "playful";
        return "gentle";
    }

    private static string DialogueHintFor(string selected, BehaviorDecisionContext context) => selected switch
    {
        MockCommandActionIds.Jump or MockCommandActionIds.PlayfulJump => "Keep the reply lively but do not claim another action.",
        MockCommandActionIds.Spin or MockCommandActionIds.PlayfulSpin => "Playful response; mention a small spin only if this action is selected.",
        MockCommandActionIds.EatSit or MockCommandActionIds.EatProne or MockCommandActionIds.AskForFood => "Food-related response; keep posture consistent.",
        MockCommandActionIds.PawSit or MockCommandActionIds.PawProne => "Hand/paw response; posture branch must match current posture.",
        _ => context.State.Stress > 0.65 ? "Use a quiet, cautious response." : "Use a short local fallback response."
    };

    private static string Band(double value) => value switch
    {
        < 0.34 => "low",
        > 0.66 => "high",
        _ => "medium"
    };

    private static double EnergyDelta(string action) => action switch
    {
        MockCommandActionIds.Jump or MockCommandActionIds.Spin or MockCommandActionIds.PlayfulJump or MockCommandActionIds.PlayfulSpin => -0.11,
        MockCommandActionIds.EatSit or MockCommandActionIds.EatProne or MockCommandActionIds.AskForFood => 0.03,
        MockCommandActionIds.Rest or MockCommandActionIds.QuietProne => 0.04,
        _ => -0.02
    };

    private static double HungerDelta(string action) => action is MockCommandActionIds.EatSit or MockCommandActionIds.EatProne or MockCommandActionIds.AskForFood ? -0.12 : 0.01;
    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}

file static class PetRuntimeStateExtensions
{
    public static double CuriosityLike(this PetRuntimeState state) => Math.Clamp((state.Boredom + state.Arousal) / 2.0, 0, 1);
}
