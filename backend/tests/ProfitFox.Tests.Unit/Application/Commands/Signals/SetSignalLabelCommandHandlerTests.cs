using System.Text.Json;

using ProfitFox.Application.Commands.Signals.SetSignalLabel;
using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;
using ProfitFox.Tests.Unit.Application.Services.Ideas;

namespace ProfitFox.Tests.Unit.Application.Commands.Signals;

/// <summary>
///     Change add-ideas-and-market, task 9.1: spec idea-labels «Метка сигнала».
/// </summary>
public sealed class SetSignalLabelCommandHandlerTests
{
    private MemorySignalRepository _signals = null!;
    private MemorySignalLabelRepository _labels = null!;

    [SetUp]
    public void Empty()
    {
        _signals = new MemorySignalRepository();
        _labels = new MemorySignalLabelRepository();
    }

    [Test]
    public async Task A_label_of_btc_drop_4h_is_saved_with_its_text_and_timeframe()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-drop-4h"), CancellationToken.None);

        SignalLabel label = await Handle(new SetSignalLabelCommand("btc-drop-4h", true, "↓ BTC", "4h"));

        Assert.That(label, Is.EqualTo(new SignalLabel(signal.Id, true, "↓ BTC", TimeSpan.FromHours(4))));
        Assert.That(await _labels.FindAsync(signal.Id, CancellationToken.None), Is.EqualTo(label));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("тринадцать зн")]
    public async Task A_text_that_is_empty_or_longer_than_twelve_characters_is_refused(string text)
    {
        await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-drop-4h"), CancellationToken.None);

        IdeasRequestException refused = Assert.ThrowsAsync<IdeasRequestException>(() => Handle(new SetSignalLabelCommand("btc-drop-4h", true, text, "4h")))!;

        Assert.That(refused.Errors.Select(error => (error.Code, error.Field)), Is.EqualTo(new[] { ("InvalidText", (string?)"text") }));
        Assert.That(await _labels.AllAsync(CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task A_text_of_twelve_characters_with_an_arrow_is_one_of_twelve()
    {
        await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-drop-4h"), CancellationToken.None);

        Assert.That((await Handle(new SetSignalLabelCommand("btc-drop-4h", true, "↓ двенадцать", "1h"))).Text, Is.EqualTo("↓ двенадцать"));
    }

    [Test]
    public async Task A_text_that_another_label_has_and_the_built_in_text_are_refused()
    {
        await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-drop-4h"), CancellationToken.None);
        await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-drop-1h"), CancellationToken.None);
        await Handle(new SetSignalLabelCommand("btc-drop-4h", true, "↓ BTC", "4h"));

        IdeasRequestException taken = Assert.ThrowsAsync<IdeasRequestException>(() => Handle(new SetSignalLabelCommand("btc-drop-1h", true, "↓ BTC", "1h")))!;
        IdeasRequestException builtIn = Assert.ThrowsAsync<IdeasRequestException>(() => Handle(new SetSignalLabelCommand("btc-drop-1h", true, "⚡ всплеск", "1h")))!;

        Assert.That(taken.Errors.Single().Code, Is.EqualTo("TakenText"));
        Assert.That(taken.Errors.Single().Reason, Does.Contain("такая метка уже есть"));
        Assert.That(builtIn.Errors.Single().Code, Is.EqualTo("TakenText"));
    }

    [Test]
    public async Task A_signal_whose_pair_candle_names_a_pair_not_listed_is_refused_on_that_node()
    {
        const string json =
            """
            {
              "formatVersion": 1,
              "outputs": [{ "name": "down", "type": "boolean" }],
              "nodes": [
                { "id": "btc", "kind": "pair-candle", "settings": { "symbol": "NOSUCHUSDT" } },
                { "id": "low", "kind": "compare", "settings": { "operation": "lt" }, "inputs": { "b": 1 } },
                { "id": "out", "kind": "outputs" }
              ],
              "wires": [{ "from": "btc.close", "to": "low.a" }, { "from": "low.result", "to": "out.down" }]
            }
            """;
        using var document = JsonDocument.Parse(json);
        await _signals.CreateAsync(SignalDocumentGenerator.Generate("no-such-drop", graph: StrategyGraph.ReadForm(document.RootElement).Value!), CancellationToken.None);

        IdeasRequestException refused = Assert.ThrowsAsync<IdeasRequestException>(() => Handle(new SetSignalLabelCommand("no-such-drop", true, "↓ ?", "4h")))!;

        Assert.That(refused.Errors.Select(error => (error.Code, error.Graph?.Node, error.Graph?.Setting)), Is.EqualTo(new[] { ("UnknownPair", (string?)"btc", (string?)"symbol") }));
    }

    [Test]
    public async Task A_timeframe_out_of_the_five_is_refused()
    {
        await _signals.CreateAsync(SignalDocumentGenerator.Generate("btc-drop-4h"), CancellationToken.None);

        IdeasRequestException refused = Assert.ThrowsAsync<IdeasRequestException>(() => Handle(new SetSignalLabelCommand("btc-drop-4h", true, "↓ BTC", "2m")))!;

        Assert.That(refused.Errors.Select(error => (error.Code, error.Field)), Is.EqualTo(new[] { ("InvalidTimeframe", (string?)"timeframe") }));
    }

    private Task<SignalLabel> Handle(SetSignalLabelCommand command)
    {
        return new SetSignalLabelCommandHandler(_signals, _labels, new ListedPairs()).Handle(command, CancellationToken.None);
    }
}
