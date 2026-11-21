using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Signals;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Strategies;

/// <summary>
///     Spec strategy-constructor «Версии стратегии» on the database (change add-strategy-constructor, task 3.2): a graph that
///     differs by content makes the next version, a layout does not; rename and delete; a row of <c>strategy_signals</c> for
///     each signal node of each version; and the versions of signals read for the checks of a strategy.
/// </summary>
public sealed class StrategyRepositoryTests
{
    private readonly CancellationToken _none = CancellationToken.None;
    private IStrategyRepository _strategies = null!;
    private ISignalRepository _signals = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _strategies = BenchServices.Get<IStrategyRepository>();
        _signals = BenchServices.Get<ISignalRepository>();
    }

    [Test]
    public async Task A_saved_strategy_is_version_1_and_keeps_its_signal_node_as_written()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);

        StrategyDocument created = await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id, description: "Шорт на фандинге"), _none);
        StrategyDocument read = Read(await _strategies.ReadAsync("funding-short", _none));

        Assert.That((created.Version, read.Version, read.Name, read.Description, read.Id), Is.EqualTo((1, 1, "funding-short", "Шорт на фандинге", created.Id)));
        Assert.That(read.Graph.Nodes.Select(node => node.Kind), Is.EqualTo(new[] { "signal", "enter", "close-after" }));
        Assert.That(read.Graph.Nodes[0].Settings["signal"], Is.EqualTo(signal.Id.ToString()));
        Assert.That(read.Layout["enter"], Is.EqualTo(new NodePosition(100, 0)));
    }

    [Test]
    public async Task A_changed_close_after_is_version_2_and_the_graph_of_version_1_stays()
    {
        StrategyDocument strategy = await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal: Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);

        StrategyDocument replaced = await _strategies.ReplaceAsync(
            StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null, after: "8h")),
            _none);

        Assert.That(replaced.Version, Is.EqualTo(2));
        Assert.That(Read(await _strategies.ReadAsync("funding-short", _none)).Version, Is.EqualTo(2));
        Assert.That(await AfterOf(strategy.Id, version: 1), Is.EqualTo("4h"));
        Assert.That(await AfterOf(strategy.Id, version: 2), Is.EqualTo("8h"));
    }

    [Test]
    public async Task Moving_a_node_changes_only_the_layout_and_adds_no_version()
    {
        GraphDefinition graph = StrategyDocumentGenerator.GenerateGraph(signal: null);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: graph), _none);
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null, after: "8h")), _none);

        StrategyDocument moved = await _strategies.ReplaceAsync(
            StrategyDocumentGenerator.Generate(
                Guid.Empty,
                graph: StrategyDocumentGenerator.GenerateGraph(signal: null, after: "8h"),
                layout: new Dictionary<string, NodePosition> { ["enter"] = new(40, 200.5) }),
            _none);

        Assert.That(moved.Version, Is.EqualTo(2));
        Assert.That(await Count("strategy_versions"), Is.EqualTo(2));
        Assert.That(Read(await _strategies.ReadAsync("funding-short", _none)).Layout, Is.EqualTo(new Dictionary<string, NodePosition> { ["enter"] = new(40, 200.5) }));
    }

    [Test]
    public async Task A_taken_name_is_refused_on_create_and_rename()
    {
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, name: "price-drop-long", graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);

        Assert.That(
            () => _strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none),
            Throws.InstanceOf<StrategyLibraryException>().With.Property(nameof(StrategyLibraryException.Problem)).EqualTo(StrategyProblem.NameTaken));
        Assert.That(
            () => _strategies.RenameAsync("price-drop-long", "funding-short", _none),
            Throws.InstanceOf<StrategyLibraryException>().With.Property(nameof(StrategyLibraryException.Problem)).EqualTo(StrategyProblem.NameTaken));
        Assert.That(await Count("strategies"), Is.EqualTo(2));
    }

    [Test]
    public async Task Rename_keeps_the_id_and_the_versions()
    {
        StrategyDocument strategy = await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null, after: "8h")), _none);

        await _strategies.RenameAsync("funding-short", "funding-short-8h", _none);

        StrategyDocument renamed = Read(await _strategies.ReadAsync("funding-short-8h", _none));
        Assert.That((renamed.Id, renamed.Version), Is.EqualTo((strategy.Id, 2)));
        Assert.That(await _strategies.ExistsAsync("funding-short", _none), Is.False);
        Assert.That(await AfterOf(strategy.Id, version: 1), Is.EqualTo("4h"));
    }

    [Test]
    public async Task Delete_removes_the_strategy_with_its_versions_and_their_signal_rows()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id), _none);
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(signal.Id, graph: StrategyDocumentGenerator.GenerateGraph(signal.Id, after: "8h")), _none);

        await _strategies.DeleteAsync("funding-short", _none);

        Assert.That(await _strategies.ReadAsync("funding-short", _none), Is.Null);
        Assert.That((await Count("strategies"), await Count("strategy_versions"), await Count("strategy_signals")), Is.EqualTo((0, 0, 0)));
    }

    [Test]
    public async Task Each_version_has_a_row_for_each_of_its_signal_nodes()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        await _signals.ReplaceAsync(SignalDocumentGenerator.Generate(graph: SignalDocumentGenerator.GenerateGraph(threshold: 0.001m)), _none);
        StrategyDocument strategy = await _strategies.CreateAsync(StrategyDocumentGenerator.Generate(signal.Id), _none);
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(signal.Id, graph: StrategyDocumentGenerator.GenerateGraph(signal.Id, signalVersion: 2)), _none);
        await _strategies.ReplaceAsync(StrategyDocumentGenerator.Generate(signal.Id, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), _none);

        (int Version, string Node, Guid SignalId, int SignalVersion)[] rows = await Rows();

        Assert.That(rows, Is.EqualTo(new[] { (1, "s", signal.Id, 1), (2, "s", signal.Id, 2) }), "version 3 has no signal node");
        Assert.That(await _strategies.UsingSignalAsync(signal.Id, _none), Is.EqualTo(new[] { "funding-short" }));
        Assert.That(strategy.Version, Is.EqualTo(1));
    }

    [Test]
    public async Task The_versions_of_a_signal_come_with_its_name_and_latest_version()
    {
        SignalDocument signal = await _signals.CreateAsync(SignalDocumentGenerator.Generate(), _none);
        await _signals.ReplaceAsync(SignalDocumentGenerator.Generate(graph: SignalDocumentGenerator.GenerateGraph(threshold: 0.001m)), _none);
        await _signals.RenameAsync("funding-high", "funding-spike", _none);

        IReadOnlyList<SignalVersion> versions = await _signals.ReadVersionsAsync(
            [new SignalReference(signal.Id, 1), new SignalReference(signal.Id, 7), new SignalReference(Guid.NewGuid(), 1)],
            _none);

        Assert.That(versions.Select(version => (version.Reference.Version, version.Name, version.LatestVersion, version.Graph is null)),
            Is.EquivalentTo(new[] { (1, "funding-spike", 2, false), (7, "funding-spike", 2, true) }), "an unknown signal gives nothing");
        GraphDefinition first = versions.Single(version => version.Reference.Version == 1).Graph!;
        Assert.That(first.Parameters.Single().Value, Is.EqualTo(GraphValue.FromNumber(0.0005m)));
        Assert.That(await _signals.ReadVersionsAsync([], _none), Is.Empty);
    }

    private static StrategyDocument Read(StrategyRead? read)
    {
        Assert.That(read, Is.Not.Null);
        Assert.That(read!.Errors, Is.Empty);
        return read.Strategy!;
    }

    private static async Task<string> AfterOf(Guid strategy, int version)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>(
            "SELECT n -> 'inputs' ->> 'after' FROM strategy_versions, jsonb_array_elements(graph -> 'nodes') n WHERE strategy_id = @Id AND version = @Version AND n ->> 'kind' = 'close-after'",
            new { Id = strategy, Version = version }) ?? "";
    }

    private static async Task<(int, string, Guid, int)[]> Rows()
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return [.. await connection.QueryAsync<(int, string, Guid, int)>("SELECT version, node, signal_id, signal_version FROM strategy_signals ORDER BY version, node")];
    }

    private static async Task<int> Count(string table)
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }
}
