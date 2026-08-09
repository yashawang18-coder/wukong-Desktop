using System.Windows;
using Wukong.Desktop;
using Wukong.Domain;

var tests = new (string Name, Action Run)[]
{
    ("input adapter emits input events", InputAdapterEmitsEvents)
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
foreach (var failure in failures) Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

static void InputAdapterEmitsEvents()
{
    var item = DesktopInputEventAdapter.PointerUp(new Point(12.5, 7));
    Assert(item.Kind == InputEventKind.PointerUp, "wrong input kind");
    Assert(item.Source == BehaviorRequestSource.OwnerUi, "wrong source");
    Assert(item.Data["x"] == "12.5", "x coordinate not captured");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
