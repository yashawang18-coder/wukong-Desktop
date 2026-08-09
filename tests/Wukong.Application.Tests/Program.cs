using Wukong.Application;
using Wukong.Contracts;
using Wukong.Domain;
using Wukong.Infrastructure;

var tests = new (string Name, Func<Task> Run)[]
{
    ("production closed registry defers asset-backed behavior", ProductionClosedRegistryDefers),
    ("same state clock seed gives deterministic arbitration", DeterministicArbitration),
    ("pose mismatch defers behavior", PoseMismatchDefers),
    ("cooldown defers repeated behavior", CooldownDefersRepeatedBehavior),
    ("minimum dwell defers concurrent behavior", MinimumDwellDefersConcurrentBehavior),
    ("developer source policy rejects production mode", SourcePolicyRejectsDeveloperProduction),
    ("high stress rejects touch", HighStressRejectsTouch),
    ("preview uses isolated event and memory stores", PreviewUsesIsolation),
    ("simulation and dev forced use isolated stores", SimulationAndDevForcedUseIsolation),
    ("developer trace includes scores and reasons", DeveloperTraceIncludesScoresAndReasons),
    ("animation completes normal lifecycle", AnimationCompletesNormalLifecycle),
    ("animation interruption uses interrupt fallback", AnimationInterruptsSafely),
    ("player failure converts to failed outcome", PlayerFailureIsOutcome)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"[PASS] {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"[FAIL] {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

static async Task ProductionClosedRegistryDefers()
{
    var service = CreateProductionService(out _, out var events, out _);
    var result = await service.SubmitAsync(Request(RuntimeMode.Production, seed: 1), RuntimeState.InitialProne());
    Assert(result.Eligibility.Disposition == RequestDisposition.Deferred, "closed production registry did not defer");
    Assert(result.Trace.PersistedToProduction == false, "deferred request persisted production event");
    Assert(events.Events.Count == 0, "deferred request wrote production event");
}

static async Task DeterministicArbitration()
{
    var first = CreateFixtureService(out _, out _, out _);
    var second = CreateFixtureService(out _, out _, out _);
    var request = Request(RuntimeMode.Preview, seed: 42);
    var state = RuntimeState.InitialProne();
    var a = await first.SubmitAsync(request, state, new InMemoryEventStore(RuntimeMode.Preview), new InMemoryMemoryCandidateStore(RuntimeMode.Preview));
    var b = await second.SubmitAsync(request, state, new InMemoryEventStore(RuntimeMode.Preview), new InMemoryMemoryCandidateStore(RuntimeMode.Preview));
    Assert(a.Arbitration.SelectedBehaviorId == b.Arbitration.SelectedBehaviorId, "selection differed");
    Assert(a.Arbitration.Candidates[0].FinalScore == b.Arbitration.Candidates[0].FinalScore, "score differed");
}

static async Task HighStressRejectsTouch()
{
    var service = CreateFixtureService(out _, out _, out _);
    var state = RuntimeState.InitialProne() with { Stress = 0.95 };
    var result = await service.SubmitAsync(Request(RuntimeMode.Preview, seed: 2), state);
    Assert(result.Eligibility.Disposition == RequestDisposition.Rejected, "high stress did not reject touch");
    Assert(result.Eligibility.ReasonCode == "stress_safety_limit", "wrong reject reason");
}

static async Task PoseMismatchDefers()
{
    var service = CreateFixtureService(out _, out _, out _);
    var state = RuntimeState.InitialProne() with { CurrentPose = "standing.awake.front" };
    var result = await service.SubmitAsync(Request(RuntimeMode.Preview, seed: 20), state);
    Assert(result.Eligibility.Disposition == RequestDisposition.Deferred, "pose mismatch did not defer");
    Assert(result.Eligibility.ReasonCode == "pose_mismatch", "wrong pose reason");
}

static async Task CooldownDefersRepeatedBehavior()
{
    var service = CreateFixtureService(
        out _,
        out _,
        out _,
        new BehaviorRuntimeOptions(TimeSpan.Zero, TimeSpan.FromSeconds(20), 0.85));
    var first = await service.SubmitAsync(
        RequestAt(RuntimeMode.Preview, BehaviorRequestSource.DeveloperPreview, seed: 21, second: 0),
        RuntimeState.InitialProne(),
        new InMemoryEventStore(RuntimeMode.Preview),
        new InMemoryMemoryCandidateStore(RuntimeMode.Preview));
    var second = await service.SubmitAsync(
        RequestAt(RuntimeMode.Preview, BehaviorRequestSource.DeveloperPreview, seed: 22, second: 1),
        RuntimeState.InitialProne(),
        new InMemoryEventStore(RuntimeMode.Preview),
        new InMemoryMemoryCandidateStore(RuntimeMode.Preview));

    Assert(first.Eligibility.Disposition == RequestDisposition.Accepted, "first request not accepted");
    Assert(second.Eligibility.Disposition == RequestDisposition.Deferred, "cooldown did not defer");
    Assert(second.Eligibility.ReasonCode == "cooldown", "wrong cooldown reason");
}

static async Task MinimumDwellDefersConcurrentBehavior()
{
    var registry = new FixtureRuntimeRegistryLoader().Load("tests/Fixtures/runtime-registry.fixture.json");
    var player = new BlockingAnimationPlayer();
    var service = new BehaviorRequestService(
        new RuntimeRegistryAssetCatalog(registry),
        new AnimationLifecycleOrchestrator(player),
        new InMemoryEventStore(RuntimeMode.Production),
        new InMemoryMemoryCandidateStore(RuntimeMode.Production),
        new BehaviorRuntimeOptions(TimeSpan.FromSeconds(10), TimeSpan.Zero, 0.85));

    var isolatedEvents = new InMemoryEventStore(RuntimeMode.Preview);
    var isolatedMemory = new InMemoryMemoryCandidateStore(RuntimeMode.Preview);
    var firstTask = service.SubmitAsync(
        RequestAt(RuntimeMode.Preview, BehaviorRequestSource.DeveloperPreview, seed: 23, second: 0),
        RuntimeState.InitialProne(),
        isolatedEvents,
        isolatedMemory);
    await player.Started.Task;

    var second = await service.SubmitAsync(
        RequestAt(RuntimeMode.Preview, BehaviorRequestSource.DeveloperPreview, seed: 24, second: 1),
        RuntimeState.InitialProne(),
        new InMemoryEventStore(RuntimeMode.Preview),
        new InMemoryMemoryCandidateStore(RuntimeMode.Preview));

    player.Release.SetResult(true);
    await firstTask;

    Assert(second.Eligibility.Disposition == RequestDisposition.Deferred, "minimum dwell did not defer");
    Assert(second.Eligibility.ReasonCode == "minimum_dwell", "wrong minimum dwell reason");
}

static async Task SourcePolicyRejectsDeveloperProduction()
{
    var service = CreateProductionService(out _, out _, out _);
    var result = await service.SubmitAsync(
        RequestAt(RuntimeMode.Production, BehaviorRequestSource.DeveloperPreview, seed: 25, second: 0),
        RuntimeState.InitialProne());
    Assert(result.Eligibility.Disposition == RequestDisposition.Rejected, "developer production source was not rejected");
    Assert(result.Eligibility.ReasonCode == "source_policy", "wrong source policy reason");
}

static async Task PreviewUsesIsolation()
{
    var service = CreateFixtureService(out _, out var productionEvents, out var productionMemory);
    var isolatedEvents = new InMemoryEventStore(RuntimeMode.Preview);
    var isolatedMemory = new InMemoryMemoryCandidateStore(RuntimeMode.Preview);
    var result = await service.SubmitAsync(Request(RuntimeMode.Preview, seed: 3), RuntimeState.InitialProne(), isolatedEvents, isolatedMemory);
    Assert(result.Outcome?.Status == ExecutionStatus.Completed, "preview did not complete");
    Assert(productionEvents.Events.Count == 0, "preview wrote production events");
    Assert(productionMemory.Candidates.Count == 0, "preview wrote production memory");
    Assert(isolatedEvents.Events.Count == 1, "preview did not write isolated event");
    Assert(isolatedMemory.Candidates.Count == 1, "preview did not write isolated memory");
}

static async Task SimulationAndDevForcedUseIsolation()
{
    foreach (var pair in new[]
    {
        (Mode: RuntimeMode.Simulation, Source: BehaviorRequestSource.DeveloperSimulation),
        (Mode: RuntimeMode.DeveloperForced, Source: BehaviorRequestSource.DeveloperForced)
    })
    {
        var service = CreateFixtureService(out _, out var productionEvents, out var productionMemory);
        var isolatedEvents = new InMemoryEventStore(pair.Mode);
        var isolatedMemory = new InMemoryMemoryCandidateStore(pair.Mode);
        var result = await service.SubmitAsync(
            RequestAt(pair.Mode, pair.Source, seed: 30 + (int)pair.Mode, second: 0),
            RuntimeState.InitialProne(),
            isolatedEvents,
            isolatedMemory);

        Assert(result.Outcome?.Status == ExecutionStatus.Completed, $"{pair.Mode} did not complete");
        Assert(productionEvents.Events.Count == 0, $"{pair.Mode} wrote production events");
        Assert(productionMemory.Candidates.Count == 0, $"{pair.Mode} wrote production memory");
        Assert(isolatedEvents.Events.Count == 1, $"{pair.Mode} did not write isolated event");
        Assert(isolatedMemory.Candidates.Count == 1, $"{pair.Mode} did not write isolated memory");
    }
}

static async Task DeveloperTraceIncludesScoresAndReasons()
{
    var acceptedService = CreateFixtureService(out _, out _, out _);
    var accepted = await acceptedService.SubmitAsync(
        Request(RuntimeMode.Preview, seed: 40),
        RuntimeState.InitialProne(),
        new InMemoryEventStore(RuntimeMode.Preview),
        new InMemoryMemoryCandidateStore(RuntimeMode.Preview));
    Assert(accepted.Trace.Candidates.Single().ScoreComponents.Select(x => x.Name).SequenceEqual(new[]
    {
        "base_weight",
        "state_fit",
        "relationship_fit",
        "context_fit",
        "seeded_jitter"
    }), "trace score components incomplete");

    var deferred = await CreateProductionService(out _, out _, out _).SubmitAsync(Request(RuntimeMode.Production, seed: 41), RuntimeState.InitialProne());
    Assert(deferred.Trace.EligibilityReasons.Contains("asset_unavailable"), "deferred reason missing from trace");

    var rejected = await CreateFixtureService(out _, out _, out _).SubmitAsync(
        Request(RuntimeMode.Preview, seed: 42),
        RuntimeState.InitialProne() with { Stress = 0.95 });
    Assert(rejected.Trace.EligibilityReasons.Contains("stress_safety_limit"), "rejected reason missing from trace");
}

static async Task AnimationCompletesNormalLifecycle()
{
    var service = CreateFixtureService(out var player, out _, out _);
    var result = await service.SubmitAsync(Request(RuntimeMode.Preview, seed: 4), RuntimeState.InitialProne(), new InMemoryEventStore(RuntimeMode.Preview), new InMemoryMemoryCandidateStore(RuntimeMode.Preview));
    Assert(result.Outcome?.Status == ExecutionStatus.Completed, "normal lifecycle did not complete");
    Assert(player.Played.Select(x => x.Phase).SequenceEqual(new[] { AnimationPhase.Intro, AnimationPhase.Loop, AnimationPhase.Exit }), "normal phases wrong");
}

static async Task AnimationInterruptsSafely()
{
    var service = CreateFixtureService(out var player, out _, out _);
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var result = await service.SubmitAsync(
        Request(RuntimeMode.Preview, seed: 5),
        RuntimeState.InitialProne(),
        new InMemoryEventStore(RuntimeMode.Preview),
        new InMemoryMemoryCandidateStore(RuntimeMode.Preview),
        cts.Token);
    Assert(result.Outcome?.Status == ExecutionStatus.Interrupted, "interrupt did not produce interrupted outcome");
    Assert(player.Played.Select(x => x.Phase).SequenceEqual(new[] { AnimationPhase.InterruptExit, AnimationPhase.Fallback }), "interrupt phases wrong");
}

static async Task PlayerFailureIsOutcome()
{
    var service = CreateFixtureService(out var player, out _, out _);
    player.FailOnPhase = AnimationPhase.Loop;
    var result = await service.SubmitAsync(Request(RuntimeMode.Preview, seed: 6), RuntimeState.InitialProne(), new InMemoryEventStore(RuntimeMode.Preview), new InMemoryMemoryCandidateStore(RuntimeMode.Preview));
    Assert(result.Outcome?.Status == ExecutionStatus.Failed, "player fault did not fail outcome");
    Assert(player.Played.Any(x => x.BehaviorId == "wk.core.prone_idle" && x.Phase == AnimationPhase.Fallback), "fallback not played");
}

static BehaviorRequestService CreateProductionService(
    out RecordingAnimationPlayer player,
    out InMemoryEventStore events,
    out InMemoryMemoryCandidateStore memory)
{
    var registry = new ProductionRuntimeRegistryLoader().Load("contracts/runtime/asset-registry.json");
    return CreateService(registry, out player, out events, out memory);
}

static BehaviorRequestService CreateFixtureService(
    out RecordingAnimationPlayer player,
    out InMemoryEventStore events,
    out InMemoryMemoryCandidateStore memory,
    BehaviorRuntimeOptions? options = null)
{
    var registry = new FixtureRuntimeRegistryLoader().Load("tests/Fixtures/runtime-registry.fixture.json");
    return CreateService(registry, out player, out events, out memory, options);
}

static BehaviorRequestService CreateService(
    RuntimeAssetRegistry registry,
    out RecordingAnimationPlayer player,
    out InMemoryEventStore events,
    out InMemoryMemoryCandidateStore memory,
    BehaviorRuntimeOptions? options = null)
{
    player = new RecordingAnimationPlayer();
    events = new InMemoryEventStore(RuntimeMode.Production);
    memory = new InMemoryMemoryCandidateStore(RuntimeMode.Production);
    return new BehaviorRequestService(
        new RuntimeRegistryAssetCatalog(registry),
        new AnimationLifecycleOrchestrator(player),
        events,
        memory,
        options ?? new BehaviorRuntimeOptions(TimeSpan.Zero, TimeSpan.Zero, 0.85));
}

static BehaviorRequest Request(RuntimeMode mode, int seed) =>
    RequestAt(
        mode,
        mode == RuntimeMode.Production ? BehaviorRequestSource.OwnerUi : BehaviorRequestSource.DeveloperPreview,
        seed,
        second: 0);

static BehaviorRequest RequestAt(RuntimeMode mode, BehaviorRequestSource source, int seed, int second) =>
    BehaviorRequest.FromIntent(
        source,
        mode,
        new DateTimeOffset(2026, 8, 9, 12, 0, second, TimeSpan.Zero),
        new SemanticIntent(SemanticIntentKind.Touch, "wk.interaction.prone_touch"),
        seed: seed);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class RecordingAnimationPlayer : IAnimationPlayer
{
    private readonly List<(string BehaviorId, AnimationPhase Phase)> _played = new();

    public IReadOnlyList<(string BehaviorId, AnimationPhase Phase)> Played => _played;
    public AnimationPhase? FailOnPhase { get; set; }

    public Task PlayPhaseAsync(string behaviorId, AnimationPhase phase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailOnPhase == phase)
            throw new InvalidOperationException($"Fixture player failure at {phase}.");
        _played.Add((behaviorId, phase));
        return Task.CompletedTask;
    }
}

sealed class BlockingAnimationPlayer : IAnimationPlayer
{
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task PlayPhaseAsync(string behaviorId, AnimationPhase phase, CancellationToken cancellationToken)
    {
        Started.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
    }
}
