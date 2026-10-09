using Microsoft.Win32;
using System.IO;

namespace Wukong.Desktop;

public sealed record StartupRegistrationResult(bool Applied, bool Enabled, string Status, string? Command);

public interface IStartupRegistrationStore
{
    string? Read(string valueName);
    void Write(string valueName, string command);
    void Delete(string valueName);
}

public sealed class RegistryStartupRegistrationStore : IStartupRegistrationStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void Write(string valueName, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new UnauthorizedAccessException("Windows startup key is unavailable.");
        key.SetValue(valueName, command, RegistryValueKind.String);
    }

    public void Delete(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

public sealed class WindowsStartupRegistration
{
    public const string ValueName = "Wukong.Desktop.Companion";
    private readonly IStartupRegistrationStore _store;

    public WindowsStartupRegistration(IStartupRegistrationStore? store = null) =>
        _store = store ?? new RegistryStartupRegistrationStore();

    public StartupRegistrationResult Synchronize(bool enabled, string? executablePath = null)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, false, "unsupported_platform", null);
        executablePath ??= Environment.ProcessPath;
        if (!enabled)
        {
            _store.Delete(ValueName);
            return new(true, false, "disabled", null);
        }
        if (!IsOwnerExecutable(executablePath))
            return new(false, false, "publish_executable_required", null);

        var command = BuildCommand(executablePath!);
        if (!string.Equals(_store.Read(ValueName), command, StringComparison.Ordinal))
            _store.Write(ValueName, command);
        return new(true, true, "enabled", command);
    }

    public static string BuildCommand(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\" --autostart";

    public static bool IsOwnerExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return false;
        if (!string.Equals(Path.GetFileName(executablePath), "Wukong.Desktop.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var normalized = Path.GetFullPath(executablePath).Replace('/', '\\');
        return !normalized.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("\\bin\\Release\\", StringComparison.OrdinalIgnoreCase);
    }
}
