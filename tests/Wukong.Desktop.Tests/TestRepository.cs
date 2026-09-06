using System.IO;

internal static class TestRepository
{
    private static readonly Lazy<string> CachedRoot = new(FindRoot);

    public static string Root => CachedRoot.Value;

    private static string FindRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Wukong.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Wukong repository root.");
    }
}
