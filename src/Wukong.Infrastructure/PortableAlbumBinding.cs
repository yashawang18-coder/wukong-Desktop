namespace Wukong.Infrastructure;

public static class PortableAlbumBinding
{
    public const string FileName = "album-root.txt";

    public static string Resolve(string albumsDirectory, string profileDirectory)
    {
        var dataRoot = Path.GetDirectoryName(Path.GetFullPath(profileDirectory))!;
        var configured = Environment.GetEnvironmentVariable("WUKONG_ALBUM_ROOT");
        var fromEnvironment = ResolveValue(configured, dataRoot);
        if (fromEnvironment is not null && Directory.Exists(fromEnvironment)) return fromEnvironment;
        try
        {
            var preference = Path.Combine(profileDirectory, FileName);
            if (File.Exists(preference))
            {
                var bound = ResolveValue(File.ReadAllText(preference).Trim(), dataRoot);
                if (bound is not null && Directory.Exists(bound)) return bound;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Path.GetFullPath(albumsDirectory);
    }

    public static void Save(string directory, string profileDirectory)
    {
        var dataRoot = Path.GetDirectoryName(Path.GetFullPath(profileDirectory))!;
        var absolute = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(dataRoot, absolute);
        var value = IsContainedRelative(relative) ? relative.Replace('\\', '/') : absolute;
        Directory.CreateDirectory(profileDirectory);
        var path = Path.Combine(profileDirectory, FileName);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, value); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string? ResolveValue(string? value, string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (Path.IsPathFullyQualified(value)) return Path.GetFullPath(value);
            if (!IsContainedRelative(value)) return null;
            var full = Path.GetFullPath(Path.Combine(dataRoot, value));
            return IsContainedRelative(Path.GetRelativePath(dataRoot, full)) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    private static bool IsContainedRelative(string relative) => !Path.IsPathRooted(relative) &&
        relative != ".." && !relative.Replace('\\', '/').StartsWith("../", StringComparison.Ordinal);
}
