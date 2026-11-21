using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;

namespace ProfitFox.Tests.Unit.Domain.Entities.Signals;

/// <summary>
///     Spec signal-constructor: a saved signal and its versions (design match-market-data-structure, decision 1).
/// </summary>
public sealed class SignalDocumentTests
{
    private static readonly IReadOnlyDictionary<string, NodePosition> Layout = new Dictionary<string, NodePosition> { ["rate"] = new NodePosition(X: 10, Y: 20) };

    [Test]
    public void Create_gives_the_first_version()
    {
        GraphDefinition graph = SignalDocumentGenerator.GenerateGraph();

        var signal = SignalDocument.Create("funding-high", "описание", graph, Layout);

        Assert.Multiple(() =>
        {
            Assert.That(signal.Name, Is.EqualTo("funding-high"));
            Assert.That(signal.Description, Is.EqualTo("описание"));
            Assert.That(signal.Graph, Is.SameAs(graph));
            Assert.That(signal.Layout, Is.SameAs(Layout));
            Assert.That(signal.Version, Is.EqualTo(1));
            Assert.That(signal.DomainEvents, Is.Empty);
        });
    }

    [Test]
    public void Rename_changes_the_name_only()
    {
        SignalDocument signal = SignalDocumentGenerator.Generate(name: "funding-high", description: "описание", layout: Layout, version: 3);
        GraphDefinition graph = signal.Graph;

        signal.Rename("funding-hot");

        Assert.Multiple(() =>
        {
            Assert.That(signal.Name, Is.EqualTo("funding-hot"));
            Assert.That(signal.Description, Is.EqualTo("описание"));
            Assert.That(signal.Graph, Is.SameAs(graph));
            Assert.That(signal.Layout, Is.SameAs(Layout));
            Assert.That(signal.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public void Restore_changes_nothing_of_what_was_stored()
    {
        GraphDefinition graph = SignalDocumentGenerator.GenerateGraph();

        var signal = SignalDocument.Restore("funding-high", description: null, graph, Layout, version: 7);

        Assert.Multiple(() =>
        {
            Assert.That(signal.Name, Is.EqualTo("funding-high"));
            Assert.That(signal.Description, Is.Null);
            Assert.That(signal.Graph, Is.SameAs(graph));
            Assert.That(signal.Layout, Is.SameAs(Layout));
            Assert.That(signal.Version, Is.EqualTo(7));
            Assert.That(signal.DomainEvents, Is.Empty);
        });
    }
}
