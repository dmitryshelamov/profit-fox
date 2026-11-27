using System.Globalization;

namespace ProfitFox.Tests.Shared.Cultures;

/// <summary>
///     The culture the test assemblies run in (change add-ideas-and-market, task 18.4): <c>ru-RU</c>, the culture of the
///     Mac of the owner, whose decimal separator is a comma. Code that writes or reads a number or a date through the culture
///     of the machine fails the tests here, not only there.
/// </summary>
public static class TestCulture
{
    public const string Name = "ru-RU";

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(Name);

    /// <summary>
    ///     Sets the culture on the current thread and as the default of every thread that sets none.
    /// </summary>
    public static void Apply()
    {
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }
}
