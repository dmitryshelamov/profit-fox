using System.Globalization;

using ProfitFox.Tests.Shared.Cultures;

namespace ProfitFox.Tests.Unit.Cultures;

/// <summary>
///     Change add-ideas-and-market, task 18.4: the tests run in <c>ru-RU</c> on the thread of a test and on the threads of
///     the pool, so a number written through the culture of the machine shows here.
/// </summary>
public sealed class TestCultureTests
{
    [Test]
    public async Task The_tests_run_in_ru_RU_on_every_thread()
    {
        string pool = await Task.Run(() => CultureInfo.CurrentCulture.Name);
        string thread = "";
        var other = new Thread(() => thread = CultureInfo.CurrentCulture.Name);
        other.Start();
        other.Join();

        Assert.That((CultureInfo.CurrentCulture.Name, pool, thread), Is.EqualTo((TestCulture.Name, TestCulture.Name, TestCulture.Name)));
        Assert.That(0.02m.ToString(CultureInfo.CurrentCulture), Is.EqualTo("0,02"), "the culture is the real one, not the invariant one");
    }
}
