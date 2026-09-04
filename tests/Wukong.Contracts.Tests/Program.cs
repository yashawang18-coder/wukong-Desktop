using Wukong.Contracts;

var tests = new (string Name, Action Run)[]
{
    ("production registry is currently empty", ProductionRegistryIsEmpty),
    ("production loader rejects non approved binding", ProductionLoaderRejectsCandidate),
    ("production loader rejects runtime use disabled binding", ProductionLoaderRejectsRuntimeUseFalse),
    ("production loader rejects incomplete lifecycle", ProductionLoaderRejectsIncompleteLifecycle),
    ("fixture loader is explicit and non production", FixtureLoaderIsIsolated)
};

return Run(tests);

static void ProductionRegistryIsEmpty()
{
    var registry = new ProductionRuntimeRegistryLoader().Load("contracts/runtime/asset-registry.json");
    Assert(registry.IsProduction, "registry not marked production");
    Assert(registry.Bindings.Count == 0, "production registry unexpectedly has runtime bindings");
}

static void ProductionLoaderRejectsCandidate()
{
    var path = WriteRegistry("runtime_approved_false", """
{
  "schema_version": 1,
  "registry_version": 1,
  "bindings": [
    {
      "behavior_id": "wk.interaction.prone_touch",
      "asset_id": "candidate.prone_touch",
      "runtime_approved": false,
      "runtime_use": true
    }
  ]
}
""");
    try
    {
        _ = new ProductionRuntimeRegistryLoader().Load(path);
        throw new InvalidOperationException("production loader accepted runtime_approved=false");
    }
    catch (RuntimeRegistryValidationException)
    {
    }
    finally
    {
        TryDeleteFile(path);
    }
}

static void ProductionLoaderRejectsRuntimeUseFalse()
{
    var path = WriteRegistry("runtime_use_false", """
{
  "schema_version": 1,
  "registry_version": 1,
  "bindings": [
    {
      "behavior_id": "wk.interaction.prone_touch",
      "asset_id": "candidate.prone_touch",
      "runtime_approved": false,
      "runtime_use": true
    }
  ]
}
""");
    try
    {
        _ = new ProductionRuntimeRegistryLoader().Load(path);
        throw new InvalidOperationException("production loader accepted runtime_use=false");
    }
    catch (RuntimeRegistryValidationException)
    {
    }
    finally
    {
        TryDeleteFile(path);
    }
}

static void ProductionLoaderRejectsIncompleteLifecycle()
{
    var path = WriteRegistry("incomplete_lifecycle", """
{
  "schema_version": 1,
  "registry_version": 1,
  "bindings": [
    {
      "behavior_id": "wk.core.walk_left",
      "asset_id": "candidate.walk_left",
      "runtime_approved": true,
      "runtime_use": true,
      "normal_path": [ "Loop" ],
      "interrupt_path": [ "Fallback" ]
    }
  ]
}
""");
    try
    {
        _ = new ProductionRuntimeRegistryLoader().Load(path);
        throw new InvalidOperationException("production loader accepted incomplete lifecycle");
    }
    catch (RuntimeRegistryValidationException)
    {
    }
    finally
    {
        TryDeleteFile(path);
    }
}

static void FixtureLoaderIsIsolated()
{
    var registry = new FixtureRuntimeRegistryLoader().Load("tests/Fixtures/runtime-registry.fixture.json");
    Assert(!registry.IsProduction, "fixture registry marked production");
    Assert(registry.Bindings.Count == 2, "fixture bindings missing");
}

static int Run((string Name, Action Run)[] tests)
{
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
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return failures.Count == 0 ? 0 : 1;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string WriteRegistry(string name, string json)
{
    var root = Path.Combine(Directory.GetCurrentDirectory(), ".wukong-log-tests");
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, $"{name}.json");
    File.WriteAllText(path, json);
    return path;
}

static void TryDeleteFile(string path)
{
    try
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    catch
    {
    }
}
