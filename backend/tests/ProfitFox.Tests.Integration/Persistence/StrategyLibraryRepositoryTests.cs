using System.Text.Json.Nodes;

using Npgsql;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

public class StrategyLibraryRepositoryTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private NpgsqlDataSource _dataSource = null!;
    private StrategyLibraryRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataSource = await MarketDataDatabase.CreateAsync();
        _repository = new StrategyLibraryRepository(_dataSource);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Empty_library_is_at_revision_zero()
    {
        StrategyLibrary library = await _repository.LoadAsync(CancellationToken.None);

        (library.Revision, library.Strategies.Count, library.Composites.Count).ShouldBe((0L, 0, 0));
    }

    [Test]
    public async Task Saved_strategy_is_read_back_exactly()
    {
        Strategy strategy = Strategy("ladder");

        (await _repository.SaveAsync(Saving(strategy), 0, CancellationToken.None)).ShouldBeTrue();

        StrategyLibrary library = await _repository.LoadAsync(CancellationToken.None);
        library.Revision.ShouldBe(1L);
        Strategy loaded = library.Strategies.ShouldHaveSingleItem();
        (loaded.Id, loaded.Name, loaded.CreatedAt, loaded.UpdatedAt).ShouldBe((strategy.Id, strategy.Name, Created, Created.AddMinutes(5)));
        Same(loaded.Definition, strategy.Definition);
        loaded.Definition.Parameters.Single(parameter => parameter.Name == "priceDropForBuy").Default.ShouldBe(new NumberValue(0.01m));
        Same((await _repository.GetStrategyAsync(strategy.Id, CancellationToken.None))!.Definition, strategy.Definition);
        (await _repository.GetStrategiesAsync(CancellationToken.None)).Select(item => item.Id).ShouldBe([strategy.Id]);
    }

    [Test]
    public async Task Lists_follow_the_order_of_creation()
    {
        Strategy older = Strategy("ema") with { Id = Guid.Parse("ffffffff-0000-0000-0000-000000000000") };
        Strategy newer = Strategy("dca") with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), CreatedAt = Created.AddDays(1) };
        await _repository.SaveAsync(Saving(newer), 0, CancellationToken.None);
        await _repository.SaveAsync(Saving(older), 1, CancellationToken.None);

        (await _repository.GetStrategiesAsync(CancellationToken.None)).Select(strategy => strategy.Name).ShouldBe(["ema", "dca"]);
        (await _repository.LoadAsync(CancellationToken.None)).Strategies.Select(strategy => strategy.Name).ShouldBe(["ema", "dca"]);
    }

    [Test]
    public async Task Save_replaces_an_existing_strategy()
    {
        Strategy strategy = Strategy("ema");
        await _repository.SaveAsync(Saving(strategy), 0, CancellationToken.None);

        await _repository.SaveAsync(Saving(strategy with { Name = "EMA 2" }), 1, CancellationToken.None);

        (await _repository.GetStrategiesAsync(CancellationToken.None)).ShouldHaveSingleItem().Name.ShouldBe("EMA 2");
    }

    [Test]
    public async Task Save_at_an_old_revision_writes_nothing()
    {
        await _repository.SaveAsync(Saving(Strategy("ema")), 0, CancellationToken.None);

        (await _repository.SaveAsync(Saving(Strategy("dca")), 0, CancellationToken.None)).ShouldBeFalse();

        StrategyLibrary library = await _repository.LoadAsync(CancellationToken.None);
        (library.Revision, library.Strategies.Count).ShouldBe((1L, 1));
    }

    [Test]
    public async Task Strategy_is_deleted_only_at_the_current_revision()
    {
        Strategy strategy = Strategy("ema");
        await _repository.SaveAsync(Saving(strategy), 0, CancellationToken.None);

        (await _repository.SaveAsync(Removing(strategy.Id), 0, CancellationToken.None)).ShouldBeFalse();
        (await _repository.GetStrategyAsync(strategy.Id, CancellationToken.None)).ShouldNotBeNull();

        (await _repository.SaveAsync(Removing(strategy.Id), 1, CancellationToken.None)).ShouldBeTrue();
        (await _repository.GetStrategyAsync(strategy.Id, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Composite_node_is_saved_read_and_deleted()
    {
        var node = new CompositeNode(Guid.NewGuid(), "Мой сигнал", Signal(), Created, Created);

        (await _repository.SaveAsync(Change(composites: [node]), 0, CancellationToken.None)).ShouldBeTrue();
        (await _repository.SaveAsync(Change(composites: [node]), 0, CancellationToken.None)).ShouldBeFalse();

        CompositeNode loaded = (await _repository.LoadAsync(CancellationToken.None)).Composites.ShouldHaveSingleItem();
        loaded.Name.ShouldBe("Мой сигнал");
        JsonNode.DeepEquals(StrategyJson.WriteComposite(loaded.Definition), StrategyJson.WriteComposite(node.Definition)).ShouldBeTrue();
        (await _repository.GetCompositeAsync(node.Id, CancellationToken.None))!.Id.ShouldBe(node.Id);
        (await _repository.GetCompositesAsync(CancellationToken.None)).ShouldHaveSingleItem().Id.ShouldBe(node.Id);

        (await _repository.SaveAsync(Change(removedComposites: [node.Id]), 1, CancellationToken.None)).ShouldBeTrue();
        (await _repository.GetCompositeAsync(node.Id, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Version_is_read_back_without_losing_decimal_precision()
    {
        StrategyDefinition definition = StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder"));
        StrategyDefinition precise = definition with
        {
            Parameters = [.. definition.Parameters.Select(parameter => parameter.Name == "priceDropForBuy" ? parameter with { Default = new NumberValue(0.0123456789012345678901234567m) } : parameter)]
        };
        Strategy strategy = Strategy("ladder") with { Definition = precise };
        var version = new StrategyVersion(strategy.Id, 1, StrategySnapshot.Of(precise, BuiltInComposites.Catalog), Created);

        (await _repository.SaveAsync(Change([strategy], versions: [version]), 0, CancellationToken.None)).ShouldBeTrue();

        (await _repository.LoadAsync(CancellationToken.None)).LatestVersion(strategy.Id).ShouldBe(version);
        (await _repository.GetVersionAsync(strategy.Id, 1, CancellationToken.None)).ShouldBe(version);
        (await _repository.GetVersionAsync(strategy.Id, 2, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Library_loads_only_the_latest_version_of_each_strategy()
    {
        Strategy strategy = Strategy("ema");
        var snapshot = StrategySnapshot.Of(strategy.Definition, BuiltInComposites.Catalog);
        await _repository.SaveAsync(Change([strategy], versions: [new StrategyVersion(strategy.Id, 1, snapshot, Created)]), 0, CancellationToken.None);
        var second = new StrategyVersion(strategy.Id, 2, snapshot, Created.AddHours(1));

        await _repository.SaveAsync(Change(versions: [second]), 1, CancellationToken.None);

        (await _repository.LoadAsync(CancellationToken.None)).LatestVersion(strategy.Id).ShouldBe(second);
        (await _repository.GetVersionAsync(strategy.Id, 1, CancellationToken.None))!.Number.ShouldBe(1);
    }

    [Test]
    public async Task Save_at_an_old_revision_writes_neither_the_change_nor_its_versions()
    {
        await _repository.SaveAsync(Saving(Strategy("ema")), 0, CancellationToken.None);
        Strategy late = Strategy("dca");
        var version = new StrategyVersion(late.Id, 1, StrategySnapshot.Of(late.Definition, BuiltInComposites.Catalog), Created);

        (await _repository.SaveAsync(Change([late], versions: [version]), 0, CancellationToken.None)).ShouldBeFalse();

        (await _repository.GetStrategyAsync(late.Id, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetVersionAsync(late.Id, 1, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Deleted_strategy_takes_its_versions_along()
    {
        Strategy strategy = Strategy("ema");
        await _repository.SaveAsync(
            Change([strategy], versions: [new StrategyVersion(strategy.Id, 1, StrategySnapshot.Of(strategy.Definition, BuiltInComposites.Catalog), Created)]),
            0,
            CancellationToken.None);

        await _repository.SaveAsync(Removing(strategy.Id), 1, CancellationToken.None);

        (await _repository.GetVersionAsync(strategy.Id, 1, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Bot_is_saved_read_and_deleted_without_losing_decimal_precision()
    {
        Strategy strategy = Strategy("dca");
        var bot = new Bot(
            Guid.NewGuid(),
            "DCA BTC",
            strategy.Id,
            new Dictionary<string, NodeValue> { ["limitPerOrder"] = new NumberValue(12.345678901234567890m), ["purchaseInterval"] = new DurationValue(TimeSpan.FromHours(4)) },
            new Instrument(MarketCategory.Linear, "BTCUSDT"),
            Interval.FourHours,
            1000.000000000001m,
            Created,
            Created.AddMinutes(1));

        (await _repository.SaveAsync(Change([strategy], bots: [bot]), 0, CancellationToken.None)).ShouldBeTrue();

        Bot loaded = (await _repository.LoadAsync(CancellationToken.None)).Bots.ShouldHaveSingleItem();
        loaded.ShouldBe(bot with { ParameterValues = loaded.ParameterValues });
        loaded.ParameterValues.ShouldBe(bot.ParameterValues);
        (await _repository.GetBotAsync(bot.Id, CancellationToken.None))!.Balance.ShouldBe(1000.000000000001m);
        (await _repository.GetBotsAsync(CancellationToken.None)).Select(item => item.Id).ShouldBe([bot.Id]);

        (await _repository.SaveAsync(Change(removedBots: [bot.Id]), 1, CancellationToken.None)).ShouldBeTrue();
        (await _repository.GetBotAsync(bot.Id, CancellationToken.None)).ShouldBeNull();
    }

    // Scenario "Конкурирующие сохранения".
    [Test]
    public async Task Of_a_bot_change_and_a_strategy_change_read_at_one_revision_only_the_first_is_saved()
    {
        StrategyDefinition ema = StrategyJson.ParseStrategy(StrategyFixtures.Element("ema"));
        var setup = new StrategyLibrary(0, [], []);
        setup.AddStrategy(Guid.NewGuid(), "EMA", ema, Created);
        Strategy strategy = setup.Strategies.Single();
        Bot bot = setup.AddBot(Guid.NewGuid(), "EMA BTC", strategy.Id, new Dictionary<string, NodeValue>(), new Instrument(MarketCategory.Spot, "BTCUSDT"), Interval.OneHour, 1000m, Created);
        await _repository.SaveAsync(setup.Change, 0, CancellationToken.None);
        StrategyLibrary first = await _repository.LoadAsync(CancellationToken.None);
        StrategyLibrary second = await _repository.LoadAsync(CancellationToken.None);
        first.UpdateBot(bot.Id, bot.Name, new Dictionary<string, NodeValue> { ["fast"] = new NumberValue(10m) }, bot.Instrument, bot.Interval, bot.Balance, Created);
        second.UpdateStrategy(strategy.Id, "EMA", WithoutFast(ema), Created);

        (await _repository.SaveAsync(first.Change, first.Revision, CancellationToken.None)).ShouldBeTrue();
        (await _repository.SaveAsync(second.Change, second.Revision, CancellationToken.None)).ShouldBeFalse();

        StrategyLibrary saved = await _repository.LoadAsync(CancellationToken.None);
        saved.Strategies.Single().Definition.Parameters.Select(parameter => parameter.Name).ShouldContain("fast");
        saved.Bots.Single().ParameterValues["fast"].ShouldBe(new NumberValue(10m));
    }

    private static StrategyDefinition WithoutFast(StrategyDefinition ema)
    {
        return ema with
        {
            Parameters = [.. ema.Parameters.Where(parameter => parameter.Name != "fast")],
            Graph = ema.Graph with
            {
                Wires = [.. ema.Graph.Wires.Where(wire => wire.From != new PinRef("params", "fast"))],
                Nodes =
                [
                    .. ema.Graph.Nodes.Select(node => node.Id == "cross" ? node with { Inputs = new Dictionary<string, NodeValue> { ["fast"] = new NumberValue(5m) } } : node)
                ]
            }
        };
    }

    private static StrategyLibraryChange Saving(Strategy strategy)
    {
        return Change([strategy]);
    }

    private static StrategyLibraryChange Removing(Guid strategy)
    {
        return Change(removedStrategies: [strategy]);
    }

    private static StrategyLibraryChange Change(
        IReadOnlyList<Strategy>? strategies = null,
        IReadOnlyList<Guid>? removedStrategies = null,
        IReadOnlyList<CompositeNode>? composites = null,
        IReadOnlyList<Guid>? removedComposites = null,
        IReadOnlyList<StrategyVersion>? versions = null,
        IReadOnlyList<Bot>? bots = null,
        IReadOnlyList<Guid>? removedBots = null)
    {
        return new StrategyLibraryChange(strategies ?? [], removedStrategies ?? [], composites ?? [], removedComposites ?? [], versions ?? [], bots ?? [], removedBots ?? []);
    }

    private static Strategy Strategy(string fixture)
    {
        return new Strategy(Guid.NewGuid(), fixture, StrategyJson.ParseStrategy(StrategyFixtures.Element(fixture)), Created, Created.AddMinutes(5));
    }

    private static CompositeDefinition Signal()
    {
        return new CompositeDefinition(
            [new PinDeclaration("x", PinType.Number)],
            [new PinDeclaration("on", PinType.Boolean)],
            new Graph(
                [new GraphNode("in", new SimpleNodeType(SimpleNodeKind.Inputs), new Dictionary<string, NodeValue>(), new NodeSettings())],
                [],
                new Dictionary<string, NodePosition>()));
    }

    private static void Same(StrategyDefinition actual, StrategyDefinition expected)
    {
        JsonNode.DeepEquals(StrategyJson.WriteStrategy(actual), StrategyJson.WriteStrategy(expected)).ShouldBeTrue();
    }
}