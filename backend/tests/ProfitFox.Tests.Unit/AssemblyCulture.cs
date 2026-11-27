using ProfitFox.Tests.Shared.Cultures;

[assembly: SetCulture(TestCulture.Name)]
[assembly: SetUICulture(TestCulture.Name)]

/// <summary>
///     Runs the whole assembly in the culture of the Mac of the owner, on every thread (change add-ideas-and-market, task
///     18.4): it comes before every other set-up of the assembly.
/// </summary>
[SetUpFixture]
public sealed class AssemblyCulture
{
    [OneTimeSetUp]
    public void Apply()
    {
        TestCulture.Apply();
    }
}
