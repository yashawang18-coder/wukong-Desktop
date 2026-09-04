using Wukong.Domain;

var tests = new (string Name, Action Run)[]
{
    ("request decisions include accepted rejected deferred", RequestDecisionsExposeRequiredStates),
    ("execution statuses include lifecycle outcomes", ExecutionStatusesExposeRequiredStates),
    ("runtime state only changes from behavior outcome", RuntimeStateAppliesOutcome),
    ("developer modes are isolated from production", DeveloperModesAreDistinct),
    ("model response cannot force assets or execution", ModelBoundaryIsExplicit)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"[PASS] {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"[FAIL] {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return 1;
}

return 0;

static void RequestDecisionsExposeRequiredStates()
{
    var names = Enum.GetNames<RequestDisposition>().ToHashSet(StringComparer.Ordinal);
    Assert(names.SetEquals(new[] { "Accepted", "Rejected", "Deferred" }), "request dispositions changed");
}

static void ExecutionStatusesExposeRequiredStates()
{
    foreach (var required in new[]
             {
                 ExecutionStatus.Started,
                 ExecutionStatus.Progressed,
                 ExecutionStatus.Completed,
                 ExecutionStatus.Interrupted,
                 ExecutionStatus.Failed
             })
    {
        Assert(Enum.IsDefined(required), $"missing {required}");
    }
}

static void RuntimeStateAppliesOutcome()
{
    var request = BehaviorRequest.FromIntent(
        BehaviorRequestSource.OwnerUi,
        RuntimeMode.Production,
        DateTimeOffset.UnixEpoch,
        new SemanticIntent(SemanticIntentKind.Touch, "wk.interaction.prone_touch"));
    var state = RuntimeState.InitialProne();
    var inFlight = state.Apply(new BehaviorOutcome(
        request.RequestId,
        "wk.interaction.prone_touch",
        ExecutionStatus.Started,
        0,
        new StateDelta(Stress: 0.9)));
    Assert(inFlight.Stress == state.Stress, "non-terminal outcome changed state");
    Assert(inFlight.CurrentBehaviorId == "wk.interaction.prone_touch", "in-flight behavior not tracked");

    var completed = inFlight.Apply(new BehaviorOutcome(
        request.RequestId,
        "wk.interaction.prone_touch",
        ExecutionStatus.Completed,
        1,
        new StateDelta(Stress: -0.05, SocialDesire: 0.1),
        MemoryEligible: true));
    Assert(completed.Stress < inFlight.Stress, "terminal outcome did not apply bounded delta");
    Assert(completed.CurrentBehaviorId is null, "terminal outcome did not release behavior");
}

static void DeveloperModesAreDistinct()
{
    Assert(RuntimeMode.Preview != RuntimeMode.Production, "preview collapsed into production");
    Assert(RuntimeMode.Simulation != RuntimeMode.Production, "simulation collapsed into production");
    Assert(RuntimeMode.DeveloperForced != RuntimeMode.Production, "dev forced collapsed into production");
}

static void ModelBoundaryIsExplicit()
{
    var safe = new ModelResponse(
        "ok",
        new SemanticIntent(SemanticIntentKind.ModelSuggested),
        null);
    Assert(safe.RespectsModelBoundary, "safe model response failed boundary");

    var unsafeResponse = safe with
    {
        AssetPaths = new[] { "assets/actions/example.png" },
        ForceBehaviorExecution = true
    };
    Assert(!unsafeResponse.RespectsModelBoundary, "unsafe model response passed boundary");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
