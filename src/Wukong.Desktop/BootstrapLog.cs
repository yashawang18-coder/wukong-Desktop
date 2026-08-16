using System.IO;
using System.Text.Json;
using Wukong.Infrastructure;

namespace Wukong.Desktop;

public static class BootstrapLog
{
    public static void WriteRaw(string message)
    {
        WriteRawInternal(message);
    }

    public static void WriteRaw(string message, object? payload)
    {
        WriteRawInternal($"{message}_{CompactPayload(payload)}");
    }

    public static void Write(string message, object? payload = null)
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = string.IsNullOrWhiteSpace(local)
                ? Path.Combine(AppContext.BaseDirectory, "logs", "bootstrap")
                : Path.Combine(local, "Wukong", "logs", "bootstrap");
            WriteToDirectory(root, message, payload);
        }
        catch
        {
        }
    }

    public static void WriteToDirectory(string root, string message, object? payload = null, DateTimeOffset? at = null)
    {
        try
        {
            Directory.CreateDirectory(root);

            var now = at ?? DateTimeOffset.Now;
            var path = Path.Combine(root, $"{now:yyyyMMdd}.log");
            var line = JsonSerializer.Serialize(new
            {
                at = now,
                message = SensitiveDataRedactor.Redact(message),
                payload = SensitiveDataRedactor.RedactPayload(payload)
            });
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void WriteRawInternal(string eventName)
    {
        var now = DateTimeOffset.Now;
        try
        {
            var root = GetPrimaryRoot();
            Directory.CreateDirectory(root);
            File.AppendAllText(
                Path.Combine(root, $"{now:yyyyMMdd}.log"),
                $"{now:o} event={SafeToken(eventName)}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            WriteFallback(now, eventName, ex);
        }
    }

    private static string GetPrimaryRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(local)
            ? Path.Combine(AppContext.BaseDirectory, "logs", "bootstrap")
            : Path.Combine(local, "Wukong", "logs", "bootstrap");
    }

    private static void WriteFallback(DateTimeOffset at, string eventName, Exception exception)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "Wukong-bootstrap-fallback.log"),
                $"{at:o} event={SafeToken(eventName)} primary_write_failed={exception.GetType().Name}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static string SafeToken(string value) =>
        new(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_').ToArray());

    private static string CompactPayload(object? payload)
    {
        if (payload is null)
            return "null";

        try
        {
            var json = JsonSerializer.Serialize(payload);
            return SensitiveDataRedactor.Redact(json);
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }
}
