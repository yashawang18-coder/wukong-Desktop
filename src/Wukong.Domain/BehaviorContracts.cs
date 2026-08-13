namespace Wukong.Domain;

public enum InputEventKind
{
    PointerDown,
    PointerMove,
    PointerUp,
    PointerWheel,
    ContextMenuCommand,
    AutonomousTick,
    ModelMessage,
    DeveloperCommand
}

public enum BehaviorRequestSource
{
    OwnerUi,
    OwnerContextMenu,
    ControlPanel,
    Dialogue,
    ContextMenu,
    Model,
    AutonomousTick,
    DeveloperPreview,
    DeveloperSimulation,
    DeveloperForced
}

public enum BehaviorExecutionMode
{
    Normal,
    PrototypePreview,
    DeveloperPreview
}

public enum RuntimeMode
{
    Production,
    Preview,
    Simulation,
    DeveloperForced
}

public enum SemanticIntentKind
{
    None,
    Idle,
    Touch,
    Stop,
    Quiet,
    ModelSuggested,
    AutonomousRest
}

public enum RequestDisposition
{
    Accepted,
    Rejected,
    Deferred
}

public enum ExecutionStatus
{
    Requested,
    Accepted,
    Deferred,
    Rejected,
    Started,
    Progressed,
    Completed,
    Interrupted,
    Failed
}

public enum AnimationPhase
{
    PreparePose,
    Intro,
    Loop,
    Exit,
    InterruptExit,
    Fallback,
    SafeEndPose
}

public sealed record InputEvent(
    Guid EventId,
    InputEventKind Kind,
    DateTimeOffset OccurredAt,
    BehaviorRequestSource Source,
    IReadOnlyDictionary<string, string> Data)
{
    public static InputEvent Create(
        InputEventKind kind,
        DateTimeOffset occurredAt,
        BehaviorRequestSource source,
        IReadOnlyDictionary<string, string>? data = null) =>
        new(Guid.NewGuid(), kind, occurredAt, source, data ?? new Dictionary<string, string>());
}

public sealed record SemanticIntent(
    SemanticIntentKind Kind,
    string? CanonicalBehaviorId = null,
    double Confidence = 1.0,
    string? NaturalLanguage = null)
{
    public bool NamesConcreteBehavior => !string.IsNullOrWhiteSpace(CanonicalBehaviorId);
}

public sealed record BehaviorRequest(
    Guid RequestId,
    Guid CorrelationId,
    BehaviorRequestSource Source,
    RuntimeMode RuntimeMode,
    DateTimeOffset RequestedAt,
    SemanticIntent Intent,
    int Priority,
    string Context,
    TimeSpan? DeferFor,
    int? Seed)
{
    public static BehaviorRequest FromIntent(
        BehaviorRequestSource source,
        RuntimeMode runtimeMode,
        DateTimeOffset requestedAt,
        SemanticIntent intent,
        int priority = 0,
        string context = "general",
        TimeSpan? deferFor = null,
        int? seed = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), source, runtimeMode, requestedAt, intent, priority, context, deferFor, seed);
}

public sealed record EligibilityDecision(
    RequestDisposition Disposition,
    string ReasonCode,
    string UserFacingReason,
    DateTimeOffset? RetryAt = null)
{
    public static EligibilityDecision Accepted(string reasonCode = "accepted") =>
        new(RequestDisposition.Accepted, reasonCode, "Request accepted.");

    public static EligibilityDecision Rejected(string reasonCode, string reason) =>
        new(RequestDisposition.Rejected, reasonCode, reason);

    public static EligibilityDecision Deferred(string reasonCode, string reason, DateTimeOffset? retryAt = null) =>
        new(RequestDisposition.Deferred, reasonCode, reason, retryAt);
}

public sealed record ScoreComponent(string Name, double Value);

public sealed record ArbitrationCandidate(
    string BehaviorId,
    IReadOnlyList<ScoreComponent> ScoreComponents,
    double FinalScore,
    bool Selected,
    IReadOnlyList<string> GateReasons);

public sealed record ArbitrationDecision(
    RequestDisposition Disposition,
    string? SelectedBehaviorId,
    string ReasonCode,
    IReadOnlyList<ArbitrationCandidate> Candidates);

public sealed record AnimationLifecycle(
    string BehaviorId,
    IReadOnlyList<AnimationPhase> NormalPath,
    IReadOnlyList<AnimationPhase> InterruptPath,
    string? FallbackBehaviorId)
{
    public bool IsComplete =>
        NormalPath.Contains(AnimationPhase.Intro) &&
        NormalPath.Contains(AnimationPhase.Loop) &&
        NormalPath.Contains(AnimationPhase.Exit) &&
        SupportsSafeInterruption;

    public bool SupportsSafeInterruption =>
        InterruptPath.Contains(AnimationPhase.InterruptExit) &&
        InterruptPath.Contains(AnimationPhase.Fallback);
}

public sealed record StateDelta(
    double Energy = 0,
    double Fatigue = 0,
    double Stress = 0,
    double Curiosity = 0,
    double SocialDesire = 0);

public sealed record BehaviorOutcome(
    Guid RequestId,
    string BehaviorId,
    ExecutionStatus Status,
    double CompletionRatio,
    StateDelta StateDelta,
    string? InterruptionReason = null,
    string? FailureReason = null,
    bool MemoryEligible = false)
{
    public bool IsTerminal => Status is ExecutionStatus.Completed or ExecutionStatus.Interrupted or ExecutionStatus.Failed;
}

public sealed record RuntimeState(
    double Energy,
    double Fatigue,
    double Stress,
    double Curiosity,
    double SocialDesire,
    string CurrentPose,
    string? CurrentBehaviorId,
    AnimationPhase? CurrentPhase)
{
    public static RuntimeState InitialProne() =>
        new(0.70, 0.20, 0.10, 0.50, 0.50, "prone.awake.left_front", null, null);

    public RuntimeState Apply(BehaviorOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!outcome.IsTerminal)
            return this with { CurrentBehaviorId = outcome.BehaviorId };

        return this with
        {
            Energy = Clamp01(Energy + outcome.StateDelta.Energy),
            Fatigue = Clamp01(Fatigue + outcome.StateDelta.Fatigue),
            Stress = Clamp01(Stress + outcome.StateDelta.Stress),
            Curiosity = Clamp01(Curiosity + outcome.StateDelta.Curiosity),
            SocialDesire = Clamp01(SocialDesire + outcome.StateDelta.SocialDesire),
            CurrentBehaviorId = null,
            CurrentPhase = null
        };
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}

public sealed record WukongEvent(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset At,
    ExecutionStatus Status,
    string Kind,
    string Summary);

public sealed record MemoryCandidate(
    Guid CandidateId,
    Guid CorrelationId,
    string Source,
    string Summary,
    double Importance,
    bool ProductionEligible);

public sealed record DeveloperTrace(
    Guid CorrelationId,
    DateTimeOffset CreatedAt,
    RuntimeMode RuntimeMode,
    IReadOnlyList<string> EligibilityReasons,
    IReadOnlyList<ArbitrationCandidate> Candidates,
    IReadOnlyList<ExecutionStatus> ExecutionStatuses,
    bool PersistedToProduction);

public sealed record ModelResponse(
    string Reply,
    SemanticIntent? Intent,
    MemoryCandidate? MemoryCandidate)
{
    public IReadOnlyList<string> AssetPaths { get; init; } = Array.Empty<string>();
    public bool ForceBehaviorExecution { get; init; }

    public bool RespectsModelBoundary =>
        AssetPaths.Count == 0 && !ForceBehaviorExecution;
}

public sealed record BehaviorRuntimeResult(
    BehaviorRequest Request,
    EligibilityDecision Eligibility,
    ArbitrationDecision Arbitration,
    BehaviorOutcome? Outcome,
    DeveloperTrace Trace);
