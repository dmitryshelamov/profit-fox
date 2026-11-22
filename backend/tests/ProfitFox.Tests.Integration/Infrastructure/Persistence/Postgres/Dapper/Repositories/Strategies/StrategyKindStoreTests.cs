using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitFox.Application.Commands.Strategies.CreateStrategy;
using ProfitFox.Application.Commands.Strategies.RenameStrategy;
using ProfitFox.Application.Commands.Strategies.ReplaceStrategy;
using ProfitFox.Application.Contracts.Strategies;
using ProfitFox.Application.Queries.Strategies;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;

/// <summary>
///     Change add-strategy-backtest, task 2.2, on the database: spec strategy-constructor «Вид у прежних стратегий» — a
///     strategy saved without a kind reads with the kind of its graph and gets it written by its first save or rename — and
///     «Вид при создании стратегии» («Вид не меняется», «Копия лесенки»).
/// </summary>
[NonParallelizable]
public sealed class StrategyKindStoreTests
{
    private static readonly Dictionary<string, NodePosition> NoLayout = [];
    private readonly CancellationToken _none = CancellationToken.None;
    private IStrategyRepository _strategies = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _strategies = BenchServices.Get<IStrategyRepository>();
    }

    [Test]
    public async Task The_former_funding_short_reads_as_a_category_of_ideas()
    {
        await SaveWithoutKind("funding-short", StrategyDocumentGenerator.GenerateGraph(signal: null, legacy: true));

        StrategyDocument read = (await _strategies.ReadAsync("funding-short", _none))!.Strategy!;

        Assert.That(read.Kind, Is.EqualTo(StrategyKind.Idea));
        Assert.That(read.Raised, Is.True, "the graph of the form before the node position is lifted, and its kind is that of the lifted graph");
        Assert.That(await KindOf("funding-short"), Is.Null, "reading writes nothing");
    }

    [Test]
    public async Task The_former_ladder_reads_as_a_strategy_for_a_bot()
    {
        await SaveWithoutKind("ladder", Ladder());

        StrategyInternal read = await BenchServices.Sender.Send(new GetStrategyQuery("ladder"), _none);
        IReadOnlyList<StrategyEntryInternal> list = await BenchServices.Sender.Send(new GetStrategiesQuery(), _none);

        Assert.That(read.Kind, Is.EqualTo(StrategyKind.Bot));
        Assert.That(list.Single().Kind, Is.EqualTo(StrategyKind.Bot));
    }

    /// <summary>
    ///     Task 12.7: a version by its number of a strategy without a written kind has the kind of the latest version, as the
    ///     list and the editor show it (design.md, decision 1), not the kind of its own graph.
    /// </summary>
    [Test]
    public async Task A_former_version_of_a_strategy_without_a_kind_has_the_kind_of_the_latest_version()
    {
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, name: "mixed", graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(Guid.Empty, name: "mixed", graph: Ladder(), kind: StrategyKind.Idea), _none);
        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE strategies SET kind = NULL WHERE name = 'mixed'");
        }

        StrategyDocument latest = (await _strategies.ReadAsync("mixed", _none))!.Strategy!;
        StrategyDocument first = (await _strategies.ReadVersionAsync("mixed", 1, _none))!.Strategy!;

        Assert.That(latest.Kind, Is.EqualTo(StrategyKind.Bot), "the latest version is the ladder");
        Assert.That(first.Kind, Is.EqualTo(StrategyKind.Bot), "version 1 is a strategy by plan, but the strategy is one for a bot");
        Assert.That(first.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task The_first_save_of_a_former_strategy_writes_its_kind()
    {
        await SaveWithoutKind("funding-short", StrategyDocumentGenerator.GenerateGraph(signal: null));

        await BenchServices.Sender.Send(new ReplaceStrategyCommand("funding-short", "Шорт", Json(StrategyDocumentGenerator.GenerateGraph(signal: null)), NoLayout), _none);

        Assert.That(await KindOf("funding-short"), Is.EqualTo("idea"));
    }

    [Test]
    public async Task The_rename_of_a_former_strategy_writes_its_kind()
    {
        await SaveWithoutKind("ladder", Ladder());

        await BenchServices.Sender.Send(new RenameStrategyCommand("ladder", "ladder-v2"), _none);

        Assert.That(await KindOf("ladder-v2"), Is.EqualTo("bot"));
    }

    [Test]
    public async Task A_rename_keeps_a_written_kind()
    {
        // A strategy by plan created as one for a bot stays one for a bot: the kind is chosen, not computed.
        await BenchServices.Sender.Send(new CreateStrategyCommand("funding-short", null, Json(StrategyDocumentGenerator.GenerateGraph(signal: null)), NoLayout, StrategyKind.Bot), _none);

        StrategyInternal renamed = await BenchServices.Sender.Send(new RenameStrategyCommand("funding-short", "funding-bot"), _none);

        Assert.That(renamed.Kind, Is.EqualTo(StrategyKind.Bot));
        Assert.That(await KindOf("funding-bot"), Is.EqualTo("bot"));
    }

    [Test]
    public async Task The_kind_chosen_on_create_is_written()
    {
        StrategyInternal created = await BenchServices.Sender.Send(
            new CreateStrategyCommand("funding-short", null, Json(StrategyDocumentGenerator.GenerateGraph(signal: null)), NoLayout, StrategyKind.Idea),
            _none);

        Assert.That(created.Kind, Is.EqualTo(StrategyKind.Idea));
        Assert.That(await KindOf("funding-short"), Is.EqualTo("idea"));
    }

    [Test]
    public async Task A_save_with_another_kind_is_refused_and_the_kind_stays()
    {
        GraphDefinition graph = StrategyDocumentGenerator.GenerateGraph(signal: null);
        await BenchServices.Sender.Send(new CreateStrategyCommand("funding-short", null, Json(graph), NoLayout, StrategyKind.Idea), _none);

        StrategyLibraryException refusal = Assert.ThrowsAsync<StrategyLibraryException>(() =>
            BenchServices.Sender.Send(new ReplaceStrategyCommand("funding-short", null, Json(StrategyDocumentGenerator.GenerateGraph(signal: null, after: "8h")), NoLayout, StrategyKind.Bot), _none))!;

        Assert.That(refusal.Problem, Is.EqualTo(StrategyProblem.Invalid));
        Assert.That(refusal.Errors.Single().Field, Is.EqualTo("kind"));
        Assert.That(await KindOf("funding-short"), Is.EqualTo("idea"));
        Assert.That((await _strategies.ReadAsync("funding-short", _none))!.Strategy!.Version, Is.EqualTo(1), "nothing was saved");
    }

    [Test]
    public async Task The_store_refuses_another_kind_too()
    {
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null), kind: StrategyKind.Idea), _none);

        StrategyLibraryException refusal = Assert.ThrowsAsync<StrategyLibraryException>(() =>
            _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null, after: "8h"), kind: StrategyKind.Bot), _none))!;

        Assert.That(refusal.Errors.Single().Field, Is.EqualTo("kind"));
        Assert.That(await KindOf("funding-short"), Is.EqualTo("idea"));
    }

    [Test]
    public async Task A_copy_of_the_ladder_is_a_strategy_for_a_bot()
    {
        await BenchServices.Sender.Send(new CreateStrategyCommand("ladder", null, Json(Ladder()), NoLayout, StrategyKind.Bot), _none);
        StrategyInternal original = await BenchServices.Sender.Send(new GetStrategyQuery("ladder"), _none);

        // A copy is the graph of the original saved under a new name with the kind of the original.
        StrategyInternal copy = await BenchServices.Sender.Send(new CreateStrategyCommand("ladder-2", null, Json(original.Graph), NoLayout, original.Kind), _none);

        Assert.That(copy.Kind, Is.EqualTo(StrategyKind.Bot));
        Assert.That(await KindOf("ladder-2"), Is.EqualTo("bot"));
    }

    [Test]
    public void The_column_takes_only_bot_and_idea()
    {
        Assert.That(
            async () =>
            {
                await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
                await connection.ExecuteAsync("INSERT INTO strategies (id, name, layout, version, kind) VALUES (gen_random_uuid(), 'x', '{}', 1, 'plan')");
            },
            Throws.InstanceOf<PostgresException>().With.Property(nameof(PostgresException.SqlState)).EqualTo("23514"));
    }

    private static GraphDefinition Ladder()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ladder.json")));
        return StrategyGraph.ReadForm(document.RootElement).Value!;
    }

    private static JsonElement Json(GraphDefinition graph)
    {
        using var document = JsonDocument.Parse(StrategyGraph.Write(graph));
        return document.RootElement.Clone();
    }

    // A strategy as it was saved before strategies had a kind: the column is empty.
    private async Task SaveWithoutKind(string name, GraphDefinition graph)
    {
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, name: name, graph: graph), _none);
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE strategies SET kind = NULL WHERE name = @Name", new { Name = name });
    }

    private static async Task<string?> KindOf(string name)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string?>("SELECT kind FROM strategies WHERE name = @Name", new { Name = name });
    }
}
