namespace ProfitFox.Tests.Unit;

/// <summary>
///     The root of the repository the tests run in: the nearest folder up from the test binaries that has <c>.git</c>.
/// </summary>
internal static class Repository
{
    public static readonly string Root = Find();

    private static string Find()
    {
        var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (folder is not null && !Path.Exists(Path.Combine(folder.FullName, ".git")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The tests do not run inside the repository.");
    }
}
