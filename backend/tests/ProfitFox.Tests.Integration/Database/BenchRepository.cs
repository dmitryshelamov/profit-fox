namespace ProfitFox.Tests.Integration.Database;

/// <summary>
///     The <c>bench/</c> folder of the repository the tests run in: H0001, the first hypothesis.
/// </summary>
internal static class BenchRepository
{
    public static readonly string Root = Find();

    public static string Bench => Path.Combine(Root, "bench");

    /// <summary>
    ///     The text of <c>hypothesis.json</c> of H0001 as it is in the repository.
    /// </summary>
    public static string H0001Text()
    {
        return File.ReadAllText(Path.Combine(Bench, "hypotheses", "H0001-funding-short-4h", "hypothesis.json"));
    }

    private static string Find()
    {
        DirectoryInfo? folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (folder is not null && !Path.Exists(Path.Combine(folder.FullName, ".git")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The tests do not run inside the repository.");
    }
}
