using System.Reflection;

using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Contracts.Runs.Converters;
using ProfitFox.Domain.Entities.Runs;
using ProfitFox.Domain.ValueObjects.Runs;

namespace ProfitFox.Tests.Unit.Application.Contracts.Runs.Converters;

/// <summary>
///     Every value of a run reaches what the API shows of it (design match-market-data-structure, decision 4).
/// </summary>
public sealed class RunInternalConverterTests
{
    [Test]
    public void Every_field_of_the_run_reaches_the_internal_run()
    {
        var queuedAt = new DateTimeOffset(year: 2026, month: 11, day: 2, hour: 3, minute: 4, second: 5, TimeSpan.Zero);
        var run = Run.Restore(
            "run-7",
            "H0003",
            version: 4,
            "new-data",
            parity: true,
            RunState.Running,
            queuedAt,
            queuedAt.AddMinutes(1),
            queuedAt.AddMinutes(9),
            "worker-2",
            queuedAt.AddMinutes(2),
            lostLeases: 1,
            cancelRequested: true,
            pairsDone: 10,
            pairsTotal: 20);

        RunInternal converted = run.ToInternal();

        PropertyInfo[] fields = typeof(RunInternal).GetProperties().Where(property => property.Name != "EqualityContract").ToArray();
        Assert.That(fields, Has.Length.EqualTo(15));
        Assert.Multiple(() =>
        {
            foreach (PropertyInfo field in fields)
            {
                Assert.That(field.GetValue(converted), Is.EqualTo(typeof(Run).GetProperty(field.Name)!.GetValue(run)), field.Name);
            }
        });
    }
}
