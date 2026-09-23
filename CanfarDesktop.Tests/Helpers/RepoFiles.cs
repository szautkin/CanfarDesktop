using Xunit;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>The app's own source tree, for the guards that read it rather than run it.</summary>
internal static class RepoFiles
{
    /// <summary>Up from the test binary until the app's project file turns up.</summary>
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CanfarDesktop.csproj")))
            dir = dir.Parent;

        Assert.True(dir is not null, "could not find the repository root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    /// <summary>A path in the repository, written with forward slashes whatever the platform.</summary>
    public static string PathTo(string relative)
        => Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// The source files matching a pattern.
    ///
    /// <para>Build output is left out — <c>obj</c> keeps a copy of every XAML file per platform and
    /// configuration, and those go stale the moment the source is edited — and so are hidden folders,
    /// which hold <c>.git</c> and any agent worktrees with a whole second checkout in them.</para>
    /// </summary>
    public static IEnumerable<string> Sources(string pattern)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(Root()));

        while (pending.TryPop(out var dir))
        {
            foreach (var file in dir.EnumerateFiles(pattern)) yield return file.FullName;
            foreach (var sub in dir.EnumerateDirectories())
                if (!sub.Name.StartsWith('.') && sub.Name is not ("bin" or "obj")) pending.Push(sub);
        }
    }
}
