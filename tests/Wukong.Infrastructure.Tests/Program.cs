using Wukong.Domain;
using Wukong.Infrastructure;

var tests = new (string Name, Func<Task> Run)[]
{
    ("fake model respects boundary", FakeModelRespectsBoundary),
    ("log redacts secrets", LogRedactsSecrets),
    ("file log rolls by retention and total bytes", FileLogRolls),
    ("file log failures do not throw", FileLogFailuresDoNotThrow)
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

static async Task FakeModelRespectsBoundary()
{
    var response = await new FakeModelClient().SendAsync("摸摸悟空 sk-testSecret123456");
    Assert(response.RespectsModelBoundary, "fake model tried to force behavior or asset path");
    Assert(response.Intent?.Kind == SemanticIntentKind.Touch, "fake model did not return semantic touch intent");
    Assert(response.MemoryCandidate?.Summary.Contains("sk-test", StringComparison.OrdinalIgnoreCase) == false, "memory candidate leaked secret");
}

static Task LogRedactsSecrets()
{
    var secretText = "Authorization: Bearer abcdef token=secret123 API key=top sk-secret1234567890 C:\\Users\\alice\\AppData\\file.txt";
    var redacted = SensitiveDataRedactor.Redact(secretText);
    Assert(redacted.Contains("[redacted]", StringComparison.Ordinal), "log did not redact credential-like text");
    Assert(!redacted.Contains("abcdef", StringComparison.Ordinal), "authorization leaked");
    Assert(!redacted.Contains("secret123", StringComparison.Ordinal), "token leaked");
    Assert(!redacted.Contains("C:\\", StringComparison.Ordinal), "absolute path leaked");
    Assert(!redacted.Contains("alice", StringComparison.OrdinalIgnoreCase), "username leaked");
    Assert(RollingFileLogStore.DefaultRetention == TimeSpan.FromDays(30), "retention changed");
    Assert(RollingFileLogStore.DefaultTotalBytesLimit == 50L * 1024 * 1024, "size limit changed");
    return Task.CompletedTask;
}

static Task FileLogRolls()
{
    var root = TestLogRoot();
    var sizeRoot = TestLogRoot();
    try
    {
        var log = new RollingFileLogStore(root, TimeSpan.FromDays(30), totalBytesLimit: 4096);
        log.Append(RuntimeMode.Production, "event", new { message = "old", token = "token-secret" }, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        log.Append(RuntimeMode.Preview, "trace", new { message = "preview", path = "C:\\Users\\alice\\secret.txt" }, new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero));
        log.Append(RuntimeMode.Simulation, "trace", new { message = new string('x', 220) }, new DateTimeOffset(2026, 8, 10, 0, 1, 0, TimeSpan.Zero));
        log.Append(RuntimeMode.DeveloperForced, "trace", new { message = new string('y', 220) }, new DateTimeOffset(2026, 8, 10, 0, 2, 0, TimeSpan.Zero));

        var files = log.GetLogFiles();
        Assert(files.All(x => !x.FullName.Contains("20260101", StringComparison.Ordinal)), "retention did not remove oldest file");
        Assert(files.Any(x => x.FullName.Contains("preview", StringComparison.OrdinalIgnoreCase)) ||
               files.Any(x => x.FullName.Contains("simulation", StringComparison.OrdinalIgnoreCase)) ||
               files.Any(x => x.FullName.Contains("developerforced", StringComparison.OrdinalIgnoreCase)),
            "isolated runtime logs were not separated by mode");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file.FullName);
            Assert(!text.Contains("C:\\", StringComparison.Ordinal), "file log leaked path");
            Assert(!text.Contains("token-secret", StringComparison.Ordinal), "file log leaked token");
        }

        var sizeLog = new RollingFileLogStore(sizeRoot, TimeSpan.FromDays(30), totalBytesLimit: 520);
        sizeLog.Append(RuntimeMode.Production, "first", new { message = new string('a', 220) }, new DateTimeOffset(2026, 8, 10, 1, 0, 0, TimeSpan.Zero));
        sizeLog.Append(RuntimeMode.Production, "second", new { message = new string('b', 220) }, new DateTimeOffset(2026, 8, 11, 1, 1, 0, TimeSpan.Zero));
        var sizeFiles = sizeLog.GetLogFiles();
        foreach (var file in sizeFiles)
            file.Refresh();
        var totalSize = sizeFiles.Sum(x => x.Length);
        Assert(totalSize <= 520, $"total byte limit not enforced: {totalSize} across {string.Join(", ", sizeFiles.Select(x => x.Name + ":" + x.Length))}");
        Assert(sizeFiles.All(x => !x.FullName.Contains("20260810", StringComparison.Ordinal)), "total byte cleanup did not delete oldest file");
    }
    finally
    {
        TryDeleteDirectory(root);
        TryDeleteDirectory(sizeRoot);
    }
    return Task.CompletedTask;
}

static Task FileLogFailuresDoNotThrow()
{
    var root = TestLogRoot();
    Directory.CreateDirectory(Path.GetDirectoryName(root)!);
    File.WriteAllText(root, "not a directory");
    try
    {
        var log = new RollingFileLogStore(root);
        log.Append(RuntimeMode.Production, "event", new { apiKey = "sk-secret123456" });
    }
    finally
    {
        TryDeleteFile(root);
    }
    return Task.CompletedTask;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string TestLogRoot() =>
    Path.Combine(Directory.GetCurrentDirectory(), ".wukong-log-tests", Guid.NewGuid().ToString("N"));

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    catch
    {
    }
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
