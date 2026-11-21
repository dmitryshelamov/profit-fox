using System.Reflection;

using ProfitFox.Application.Contracts.Runs;
using ProfitFox.Application.Contracts.Runs.Converters;
using ProfitFox.Domain.ValueObjects.Ledger;

namespace ProfitFox.Tests.Unit.Application.Contracts.Runs.Converters;

/// <summary>
///     Every value of a line of the ledger reaches what the API shows of it, the kind of a finished run included (design
///     match-market-data-structure, decision 4).
/// </summary>
public sealed class LedgerEntryInternalConverterTests
{
    private static readonly LedgerEntry Entry = new LedgerEntry
    {
        RunId = "run-7",
        Time = new DateTimeOffset(year: 2026, month: 11, day: 2, hour: 3, minute: 4, second: 5, TimeSpan.Zero),
        Hypothesis = "H0003",
        Hash = "hash",
        Version = 4,
        EngineVersion = 5,
        Period = "holdout",
        Status = RunStatus.Rejected,
        DataHash = "data",
        Attempts = 2,
        Reason = "reason",
        RequiredT = 1.5,
        Power = 0.8,
        Blocks = 25,
        Events = 300,
        T = -0.5,
        Mean = 0.001,
        FirstHalfMean = 0.002,
        SecondHalfMean = 0.003,
        ReadSeconds = 12.5,
        Corrects = "run-1"
    };

    [Test]
    public void Every_field_of_the_line_reaches_the_internal_line()
    {
        LedgerEntryInternal converted = Entry.ToInternal();

        PropertyInfo[] fields = typeof(LedgerEntryInternal).GetProperties().Where(property => property.Name != "EqualityContract").ToArray();
        Assert.That(fields, Has.Length.EqualTo(21));
        Assert.Multiple(() =>
        {
            foreach (PropertyInfo field in fields)
            {
                object? value = typeof(LedgerEntry).GetProperty(field.Name)!.GetValue(Entry);
                Assert.That(value, Is.Not.Null, $"{field.Name} is given in the test");
                Assert.That(field.GetValue(converted), Is.EqualTo(value), field.Name);
            }
        });
    }

    [Test]
    public void A_finished_run_keeps_its_line_and_its_kind()
    {
        FinishedRunInternal converted = new FinishedRun(Entry, Parity: true).ToInternal();

        Assert.That(converted.Parity, Is.True);
        Assert.That(converted.Entry, Is.EqualTo(Entry.ToInternal()));
    }
}
