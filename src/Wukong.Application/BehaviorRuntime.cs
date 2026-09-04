using Wukong.Domain;

namespace Wukong.Application;

public interface IRuntimeAssetCatalog
{
    bool IsProduction { get; }
    AnimationLifecycle? FindLifecycle(string behaviorId, RuntimeMode runtimeMode);
}

public interface IAnimationPlayer
{
    Task PlayPhaseAsync(string behaviorId, AnimationPhase phase, CancellationToken cancellationToken);
}

public interface IEventStore
{
    RuntimeMode RuntimeMode { get; }
    IReadOnlyList<WukongEvent> Events { get; }
    void Append(WukongEvent item);
}

public interface IMemoryCandidateStore
{
    RuntimeMode RuntimeMode { get; }
    IReadOnlyList<MemoryCandidate> Candidates { get; }
    void Append(MemoryCandidate item);
}

public interface IModelClient
{
    Task<ModelResponse> SendAsync(string ownerMessage, CancellationToken cancellationToken = default);
}

public sealed record BehaviorRuntimeOptions(
    TimeSpan MinimumDwell,
    TimeSpan Cooldown,
    double StressRejectThreshold)
{
    public static BehaviorRuntimeOptions Default { get; } =
        new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20), 0.85);
}

public sealed class BehaviorRequestService
{
    private readonly IRuntimeAssetCatalog _assetCatalog;
    private readonly AnimationLifecycleOrchestrator _orchestrator;
    private readonly IEventStore _productionEvents;
    private readonly IMemoryCandidateStore _productionMemory;
    private readonly BehaviorRuntimeOptions _options;
    private readonly Dictionary<string, DateTimeOffset> _lastAccepted = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentBehaviorId;
    private DateTimeOffset _currentStartedAt = DateTimeOffset.MinValue;
    private bool _currentInterruptible = true;

    public BehaviorRequestService(
        IRuntimeAssetCatalog assetCatalog,
        AnimationLifecycleOrchestrator orchestrator,
        IEventStore productionEvents,
        IMemoryCandidateStore productionMemory,
        BehaviorRuntimeOptions? options = null)
    {
        _assetCatalog = assetCatalog;
        _orchestrator = orchestrator;
        _productionEvents = productionEvents;
        _productionMemory = productionMemory;
        _options = options ?? BehaviorRuntimeOptions.Default;
    }

    public async Task<BehaviorRuntimeResult> SubmitAsync(
        BehaviorRequest request,
        RuntimeState state,
        IEventStore? isolatedEvents = null,
        IMemoryCandidateStore? isolatedMemory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);

        var eligibility = EvaluateEligibility(request, state);
        var arbitration = eligibility.Disposition == RequestDisposition.Accepted
            ? Arbitrate(request, state)
            : new ArbitrationDecision(eligibility.Disposition, null, eligibility.ReasonCode, Array.Empty<ArbitrationCandidate>());

        BehaviorOutcome? outcome = null;
        var executionStatuses = new List<ExecutionStatus>();
        if (arbitration is { Disposition: RequestDisposition.Accepted, SelectedBehaviorId: not null })
        {
            var lifecycle = _assetCatalog.FindLifecycle(arbitration.SelectedBehaviorId, request.RuntimeMode);
            if (lifecycle is null)
            {
                eligibility = EligibilityDecision.Deferred("asset_unavailable", "No runtime-approved asset is available.");
                arbitration = arbitration with { Disposition = RequestDisposition.Deferred, ReasonCode = "asset_unavailable" };
            }
            else
            {
                _lastAccepted[arbitration.SelectedBehaviorId] = request.RequestedAt;
                _currentBehaviorId = arbitration.SelectedBehaviorId;
                _currentStartedAt = request.RequestedAt;
                _currentInterruptible = lifecycle.SupportsSafeInterruption;
                outcome = await _orchestrator.ExecuteAsync(request, lifecycle, cancellationToken);
                executionStatuses.Add(outcome.Status);
                if (outcome.IsTerminal)
                    _currentBehaviorId = null;

                AppendOutcome(request, outcome, isolatedEvents, isolatedMemory);
            }
        }

        if (executionStatuses.Count == 0)
            executionStatuses.Add(eligibility.Disposition switch
            {
                RequestDisposition.Accepted => ExecutionStatus.Accepted,
                RequestDisposition.Rejected => ExecutionStatus.Rejected,
                _ => ExecutionStatus.Deferred
            });

        var trace = new DeveloperTrace(
            request.CorrelationId,
            request.RequestedAt,
            request.RuntimeMode,
            new[] { eligibility.ReasonCode },
            arbitration.Candidates,
            executionStatuses,
            PersistedToProduction: request.RuntimeMode == RuntimeMode.Production && outcome is not null);

        return new BehaviorRuntimeResult(request, eligibility, arbitration, outcome, trace);
    }

    private EligibilityDecision EvaluateEligibility(BehaviorRequest request, RuntimeState state)
    {
        if (request.RuntimeMode == RuntimeMode.Production &&
            request.Source is BehaviorRequestSource.DeveloperPreview or BehaviorRequestSource.DeveloperSimulation or BehaviorRequestSource.DeveloperForced)
            return EligibilityDecision.Rejected("source_policy", "Developer sources require an isolated runtime mode.");

        if (state.Stress >= _options.StressRejectThreshold &&
            request.Intent.Kind is SemanticIntentKind.Touch or SemanticIntentKind.ModelSuggested)
            return EligibilityDecision.Rejected("stress_safety_limit", "Wukong is too stressed for this interaction.");

        if (_currentBehaviorId is not null && _currentStartedAt != DateTimeOffset.MinValue)
        {
            var elapsed = request.RequestedAt - _currentStartedAt;
            if (!_currentInterruptible)
                return EligibilityDecision.Deferred("current_not_interruptible", "Current behavior cannot be interrupted safely.");
            if (elapsed < _options.MinimumDwell)
                return EligibilityDecision.Deferred("minimum_dwell", "Current behavior is still in its minimum dwell window.", _currentStartedAt + _options.MinimumDwell);
        }

        var behaviorId = ResolveBehaviorId(request.Intent);
        if (behaviorId is null)
            return EligibilityDecision.Deferred("intent_unresolved", "No canonical behavior could be resolved.");

        if (RequiresPronePose(behaviorId) &&
            !state.CurrentPose.StartsWith("prone.", StringComparison.OrdinalIgnoreCase))
            return EligibilityDecision.Deferred("pose_mismatch", "Current pose is not eligible for this behavior.");

        if (_lastAccepted.TryGetValue(behaviorId, out var lastAccepted) &&
            request.RequestedAt - lastAccepted < _options.Cooldown)
            return EligibilityDecision.Deferred("cooldown", "Behavior is cooling down.", lastAccepted + _options.Cooldown);

        if (_assetCatalog.FindLifecycle(behaviorId, request.RuntimeMode) is null)
            return EligibilityDecision.Deferred("asset_unavailable", "No runtime-approved asset is available.");

        return EligibilityDecision.Accepted();
    }

    private ArbitrationDecision Arbitrate(BehaviorRequest request, RuntimeState state)
    {
        var behaviorId = ResolveBehaviorId(request.Intent);
        if (behaviorId is null)
            return new ArbitrationDecision(RequestDisposition.Deferred, null, "intent_unresolved", Array.Empty<ArbitrationCandidate>());

        var seed = request.Seed ?? HashCode.Combine(
            behaviorId,
            state.CurrentPose,
            Math.Round(state.Stress, 3),
            request.RequestedAt.UtcDateTime.Ticks);
        var jitter = new Random(seed).NextDouble() * 0.0001;
        var components = new[]
        {
            new ScoreComponent("base_weight", 1.0),
            new ScoreComponent("state_fit", 1.0 - state.Stress),
            new ScoreComponent("relationship_fit", state.SocialDesire),
            new ScoreComponent("context_fit", request.Context == "general" ? 0.1 : 0),
            new ScoreComponent("seeded_jitter", jitter)
        };
        var finalScore = components.Sum(x => x.Value);
        var candidate = new ArbitrationCandidate(behaviorId, components, finalScore, Selected: true, Array.Empty<string>());
        return new ArbitrationDecision(RequestDisposition.Accepted, behaviorId, "selected", new[] { candidate });
    }

    private void AppendOutcome(
        BehaviorRequest request,
        BehaviorOutcome outcome,
        IEventStore? isolatedEvents,
        IMemoryCandidateStore? isolatedMemory)
    {
        var events = request.RuntimeMode == RuntimeMode.Production
            ? _productionEvents
            : isolatedEvents ?? new InMemoryEventStore(request.RuntimeMode);
        var memory = request.RuntimeMode == RuntimeMode.Production
            ? _productionMemory
            : isolatedMemory ?? new InMemoryMemoryCandidateStore(request.RuntimeMode);

        events.Append(new WukongEvent(
            Guid.NewGuid(),
            request.CorrelationId,
            request.RequestedAt,
            outcome.Status,
            "behavior_outcome",
            $"{outcome.BehaviorId}:{outcome.Status}"));

        if (outcome.MemoryEligible)
        {
            memory.Append(new MemoryCandidate(
                Guid.NewGuid(),
                request.CorrelationId,
                "behavior_outcome",
                outcome.BehaviorId,
                outcome.CompletionRatio,
                ProductionEligible: request.RuntimeMode == RuntimeMode.Production));
        }
    }

    private static string? ResolveBehaviorId(SemanticIntent intent)
    {
        if (!string.IsNullOrWhiteSpace(intent.CanonicalBehaviorId))
            return intent.CanonicalBehaviorId;
        return intent.Kind switch
        {
            SemanticIntentKind.Idle or SemanticIntentKind.AutonomousRest => "wk.core.prone_idle",
            SemanticIntentKind.Touch => "wk.interaction.prone_touch",
            SemanticIntentKind.Stop or SemanticIntentKind.Quiet => "wk.core.prone_idle",
            _ => null
        };
    }

    private static bool RequiresPronePose(string behaviorId) =>
        behaviorId.Contains(".prone_", StringComparison.OrdinalIgnoreCase) ||
        behaviorId.Contains("_prone", StringComparison.OrdinalIgnoreCase);
}

public sealed class AnimationLifecycleOrchestrator
{
    private readonly IAnimationPlayer _player;

    public AnimationLifecycleOrchestrator(IAnimationPlayer player) =>
        _player = player ?? throw new ArgumentNullException(nameof(player));

    public async Task<BehaviorOutcome> ExecuteAsync(
        BehaviorRequest request,
        AnimationLifecycle lifecycle,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var phase in lifecycle.NormalPath)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _player.PlayPhaseAsync(lifecycle.BehaviorId, phase, cancellationToken);
            }

            return new BehaviorOutcome(
                request.RequestId,
                lifecycle.BehaviorId,
                ExecutionStatus.Completed,
                1,
                new StateDelta(SocialDesire: 0.02),
                MemoryEligible: true);
        }
        catch (OperationCanceledException)
        {
            foreach (var phase in lifecycle.InterruptPath)
                await _player.PlayPhaseAsync(lifecycle.BehaviorId, phase, CancellationToken.None);

            return new BehaviorOutcome(
                request.RequestId,
                lifecycle.BehaviorId,
                ExecutionStatus.Interrupted,
                0.5,
                new StateDelta(Stress: -0.01),
                InterruptionReason: "cancelled");
        }
        catch (Exception ex)
        {
            if (lifecycle.FallbackBehaviorId is not null)
                await _player.PlayPhaseAsync(lifecycle.FallbackBehaviorId, AnimationPhase.Fallback, CancellationToken.None);

            return new BehaviorOutcome(
                request.RequestId,
                lifecycle.BehaviorId,
                ExecutionStatus.Failed,
                0,
                new StateDelta(Stress: 0.01),
                FailureReason: ex.GetType().Name);
        }
    }
}

public sealed class InMemoryEventStore : IEventStore
{
    private readonly List<WukongEvent> _events = new();

    public InMemoryEventStore(RuntimeMode runtimeMode) => RuntimeMode = runtimeMode;

    public RuntimeMode RuntimeMode { get; }
    public IReadOnlyList<WukongEvent> Events => _events;
    public void Append(WukongEvent item) => _events.Add(item);
}

public sealed class InMemoryMemoryCandidateStore : IMemoryCandidateStore
{
    private readonly List<MemoryCandidate> _candidates = new();

    public InMemoryMemoryCandidateStore(RuntimeMode runtimeMode) => RuntimeMode = runtimeMode;

    public RuntimeMode RuntimeMode { get; }
    public IReadOnlyList<MemoryCandidate> Candidates => _candidates;
    public void Append(MemoryCandidate item) => _candidates.Add(item);
}
