namespace Weaver.Core;

public static class WeaverDatabase
{
    /// <summary>
    /// Resolve the absolute path to weaver.db. Order: the WEAVER_DB env var,
    /// then walk up from the working dir and the binary dir looking for
    /// data/weaver.db. Lets the app run from anywhere (repo root, project dir,
    /// a clone) without a configured path — handy in a live demo.
    /// </summary>
    public static string Locate()
    {
        var env = Environment.GetEnvironmentVariable("WEAVER_DB");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return Path.GetFullPath(env);

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "data", "weaver.db");
                if (File.Exists(candidate)) return candidate;
            }
        }

        // Not found — return a best-guess so the caller can report it clearly.
        return Path.Combine(Directory.GetCurrentDirectory(), "data", "weaver.db");
    }

    /// <summary>
    /// Path to the writable boards store, alongside weaver.db. Created if absent.
    /// </summary>
    public static string LocateBoards()
    {
        var dir = Path.GetDirectoryName(Locate()) ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "boards.db");
    }
}
