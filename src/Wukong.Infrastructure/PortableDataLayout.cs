namespace Wukong.Infrastructure;

public sealed record PortableDataLayout(
    string RootDirectory,
    string ProfileDirectory,
    string AgentDirectory,
    string AlbumsDirectory,
    string DefaultsDirectory,
    bool UsesExecutableDirectory)
{
    public const string DataRootEnvironmentVariable = "WUKONG_DATA_ROOT";
    public const string LegacyMigrationMarkerFileName = ".legacy-migration-v1-complete";

    private static readonly string[] ProfileFiles =
    {
        "pet-profile.json",
        "owner-profile.json",
        "pet-prompt.txt",
        "pet-scale.txt"
    };

    private static readonly string[] AgentFiles =
    {
        "model-providers.json",
        "memory-configuration.json",
        "conversation-history.json",
        "memory-candidates.json"
    };

    public static PortableDataLayout CreateDefault() => Initialize(
        AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wukong"));

    public static PortableDataLayout Initialize(string executableDirectory, string? legacyRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        var baseDirectory = Path.GetFullPath(executableDirectory);
        var configuredRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        var portableRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(baseDirectory, "WukongData")
            : Path.GetFullPath(configuredRoot);
        var defaultsRoot = Path.Combine(baseDirectory, "WukongDefaults");

        if (TryInitialize(portableRoot, defaultsRoot, legacyRoot))
            return Create(portableRoot, defaultsRoot, IsUnder(portableRoot, baseDirectory));

        var fallbackRoot = legacyRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wukong");
        if (!TryInitialize(fallbackRoot, defaultsRoot, legacyRoot: null))
            throw new IOException("Wukong could not initialize its local data directory.");
        return Create(fallbackRoot, defaultsRoot, usesExecutableDirectory: false);
    }

    private static bool TryInitialize(string root, string defaultsRoot, string? legacyRoot)
    {
        try
        {
            var profile = Path.Combine(root, "profile");
            var agent = Path.Combine(root, "agent");
            var albums = Path.Combine(root, "albums");
            Directory.CreateDirectory(profile);
            Directory.CreateDirectory(agent);
            Directory.CreateDirectory(albums);

            var migrationMarker = Path.Combine(root, LegacyMigrationMarkerFileName);
            if (!File.Exists(migrationMarker) &&
                !string.IsNullOrWhiteSpace(legacyRoot) &&
                !PathsEqual(root, legacyRoot) &&
                Directory.Exists(legacyRoot))
            {
                CopyMissingFiles(Path.Combine(legacyRoot, "profile"), profile, ProfileFiles);
                CopyMissingFiles(Path.Combine(legacyRoot, "agent"), agent, AgentFiles);
            }
            if (!File.Exists(migrationMarker))
                File.WriteAllText(migrationMarker, "Legacy user data migration has completed.\n");

            CopyDefaults(Path.Combine(defaultsRoot, "profile"), profile);
            CopyDefaults(Path.Combine(defaultsRoot, "agent"), agent);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static PortableDataLayout Create(string root, string defaultsRoot, bool usesExecutableDirectory) => new(
        Path.GetFullPath(root),
        Path.Combine(Path.GetFullPath(root), "profile"),
        Path.Combine(Path.GetFullPath(root), "agent"),
        Path.Combine(Path.GetFullPath(root), "albums"),
        Path.GetFullPath(defaultsRoot),
        usesExecutableDirectory);

    private static void CopyDefaults(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            CopyIfMissing(file, Path.Combine(destination, Path.GetFileName(file)));
    }

    private static void CopyMissingFiles(string source, string destination, IEnumerable<string> names)
    {
        if (!Directory.Exists(source))
            return;
        foreach (var name in names)
            CopyIfMissing(Path.Combine(source, name), Path.Combine(destination, name));

        foreach (var avatar in Directory.GetFiles(source, "pet-avatar.*", SearchOption.TopDirectoryOnly))
            CopyIfMissing(avatar, Path.Combine(destination, Path.GetFileName(avatar)));
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
