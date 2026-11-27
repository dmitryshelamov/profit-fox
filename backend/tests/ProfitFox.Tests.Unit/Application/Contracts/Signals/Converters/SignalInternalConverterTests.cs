using ProfitFox.Application.Contracts.Signals;
using ProfitFox.Application.Contracts.Signals.Converters;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;

namespace ProfitFox.Tests.Unit.Application.Contracts.Signals.Converters;

/// <summary>
///     Every value of a saved signal reaches what the API shows of it (design match-market-data-structure, decision 4).
/// </summary>
public sealed class SignalInternalConverterTests
{
    private static readonly SignalDocument Signal = SignalDocumentGenerator.Generate(
        "funding-high",
        "описание",
        layout: new Dictionary<string, NodePosition> { ["rate"] = new NodePosition(X: 10, Y: 20) },
        version: 3);

    [Test]
    public void A_signal_in_the_list_keeps_its_name_description_and_version()
    {
        Assert.That(Signal.ToEntryInternal(), Is.EqualTo(new SignalEntryInternal("funding-high", "описание", Version: 3, Error: null) { Id = Signal.Id, Output = "event" }));
    }

    [Test]
    public void Every_field_of_the_signal_reaches_the_internal_signal()
    {
        SignalInternal converted = Signal.ToInternal();

        Assert.Multiple(() =>
        {
            Assert.That(converted.Id, Is.EqualTo(Signal.Id));
            Assert.That(converted.Name, Is.EqualTo("funding-high"));
            Assert.That(converted.Description, Is.EqualTo("описание"));
            Assert.That(converted.Graph, Is.SameAs(Signal.Graph));
            Assert.That(converted.Layout, Is.SameAs(Signal.Layout));
            Assert.That(converted.Version, Is.EqualTo(3));
        });
    }
}
