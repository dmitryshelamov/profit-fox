using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Signals;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 6.2: the ideas of a strategy, the label of a signal and the lease of the scanner in
///     the bench database.
/// </summary>
[NonParallelizable]
public sealed class IdeaSettingsRepositoryTests
{
    private static readonly TimeSpan[] Hours = [TimeSpan.FromHours(1), TimeSpan.FromHours(4)];

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
    }

    [Test]
    public async Task The_ideas_of_a_strategy_are_written_replaced_and_go_with_the_strategy()
    {
        IStrategyRepository strategies = BenchServices.Get<IStrategyRepository>();
        IStrategyIdeasRepository ideas = BenchServices.Get<IStrategyIdeasRepository>();
        Guid id = (await strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), CancellationToken.None)).Id;

        await ideas.SetAsync(new StrategyIdeas(id, Enabled: true, Hours), CancellationToken.None);
        StrategyIdeas? first = await ideas.FindAsync(id, CancellationToken.None);
        await ideas.SetAsync(new StrategyIdeas(id, Enabled: false, [TimeSpan.FromMinutes(5)]), CancellationToken.None);

        Assert.That((first!.Enabled, first.Timeframes), Is.EqualTo((true, Hours)).Using<(bool, IReadOnlyList<TimeSpan>)>((a, b) => a.Item1 == b.Item1 && a.Item2.SequenceEqual(b.Item2)));
        Assert.That((await ideas.AllAsync(CancellationToken.None)).Single().Timeframes, Is.EqualTo(new[] { TimeSpan.FromMinutes(5) }));

        await strategies.DeleteAsync("funding-short", CancellationToken.None);

        Assert.That(await ideas.FindAsync(id, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task A_label_is_one_of_its_text_among_those_that_are_on_and_goes_with_its_signal()
    {
        ISignalRepository signals = BenchServices.Get<ISignalRepository>();
        ISignalLabelRepository labels = BenchServices.Get<ISignalLabelRepository>();
        Guid drop = (await signals.CreateAsync(SignalDocumentGenerator.Generate("btc-drop-4h"), CancellationToken.None)).Id;
        Guid other = (await signals.CreateAsync(SignalDocumentGenerator.Generate("other"), CancellationToken.None)).Id;

        Assert.That(await labels.SetAsync(new SignalLabel(drop, Enabled: true, "↓ BTC", TimeSpan.FromHours(4)), CancellationToken.None), Is.True);
        Assert.That(await labels.SetAsync(new SignalLabel(other, Enabled: true, "↓ BTC", TimeSpan.FromHours(1)), CancellationToken.None), Is.False, "the text is taken");
        Assert.That(await labels.SetAsync(new SignalLabel(other, Enabled: false, "↓ BTC", TimeSpan.FromHours(1)), CancellationToken.None), Is.True, "a label that is off");
        Assert.That(await labels.FindAsync(drop, CancellationToken.None), Is.EqualTo(new SignalLabel(drop, true, "↓ BTC", TimeSpan.FromHours(4))));

        await signals.DeleteAsync("btc-drop-4h", CancellationToken.None);

        Assert.That((await labels.AllAsync(CancellationToken.None)).Select(label => label.SignalId), Is.EqualTo(new[] { other }));
    }

    /// <summary>
    ///     Task 18.13: the enabled strategies and labels with their latest version numbers in one answer; the ones that are
    ///     off are left out.
    /// </summary>
    [Test]
    public async Task The_sources_of_the_scanner_are_the_enabled_ones_with_their_versions()
    {
        IStrategyRepository strategies = BenchServices.Get<IStrategyRepository>();
        ISignalRepository signals = BenchServices.Get<ISignalRepository>();
        Guid on = (await strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), CancellationToken.None)).Id;
        Guid off = (await strategies.CreateAsync(StrategyDocumentGenerator.Generate(Guid.Empty, name: "other", graph: StrategyDocumentGenerator.GenerateGraph(signal: null)), CancellationToken.None)).Id;
        Guid drop = (await signals.CreateAsync(SignalDocumentGenerator.Generate("btc-drop-4h"), CancellationToken.None)).Id;
        Guid quiet = (await signals.CreateAsync(SignalDocumentGenerator.Generate("quiet"), CancellationToken.None)).Id;
        await BenchServices.Get<IStrategyIdeasRepository>().SetAsync(new StrategyIdeas(on, Enabled: true, Hours), CancellationToken.None);
        await BenchServices.Get<IStrategyIdeasRepository>().SetAsync(new StrategyIdeas(off, Enabled: false, Hours), CancellationToken.None);
        await BenchServices.Get<ISignalLabelRepository>().SetAsync(new SignalLabel(drop, Enabled: true, "↓ BTC", TimeSpan.FromHours(4)), CancellationToken.None);
        await BenchServices.Get<ISignalLabelRepository>().SetAsync(new SignalLabel(quiet, Enabled: false, "тихо", TimeSpan.FromHours(1)), CancellationToken.None);

        IdeaSources sources = await BenchServices.Get<IIdeaSourceRepository>().SourcesAsync(CancellationToken.None);

        Assert.That(sources.Strategies.Select(source => (source.StrategyId, source.Name, source.Version)), Is.EqualTo(new[] { (on, "funding-short", 1) }));
        Assert.That(sources.Strategies.Single().Timeframes, Is.EqualTo(Hours));
        Assert.That(sources.Labels, Is.EqualTo(new[] { new LabelSource(drop, "btc-drop-4h", 1, "↓ BTC", TimeSpan.FromHours(4)) }));
    }

    [Test]
    public async Task Two_holders_do_not_take_the_lease_at_once_and_a_lease_that_ran_out_is_taken()
    {
        IScannerLeaseRepository lease = BenchServices.Get<IScannerLeaseRepository>();

        Assert.That(await lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None), Is.True);
        Assert.That(await lease.TakeAsync("w-2", TimeSpan.FromSeconds(30), CancellationToken.None), Is.False, "w-1 holds it");
        Assert.That(await lease.TakeAsync("w-1", TimeSpan.FromMilliseconds(1), CancellationToken.None), Is.True, "renewed by its holder");
        await lease.ReportAsync("w-1", IdeaGeneratorNoon, 310, 831, CancellationToken.None, failed: 12, failure: "answered 400", warmupLeft: TimeSpan.FromSeconds(511.2));
        (ScannerState held, _) = (await lease.StateAsync(CancellationToken.None))!.Value;
        Assert.That(
            held,
            Is.EqualTo(new ScannerState("w-1", held.Until, IdeaGeneratorNoon, 310, 831, Failed: 12, Failure: "answered 400", WarmupLeft: TimeSpan.FromSeconds(512))),
            "the time left in whole seconds rounded up");
        await Task.Delay(50);

        Assert.That(await lease.TakeAsync("w-2", TimeSpan.FromSeconds(30), CancellationToken.None), Is.True, "the lease of w-1 ran out");
        (ScannerState taken, DateTimeOffset now) = (await lease.StateAsync(CancellationToken.None))!.Value;
        Assert.That(
            (taken.Worker, taken.LiveSince, taken.Warmed, taken.Failed, taken.Failure, taken.WarmupLeft),
            Is.EqualTo(("w-2", (DateTimeOffset?)null, 0, 0, (string?)null, (TimeSpan?)null)),
            "a new holder starts over");
        Assert.That(taken.Until, Is.GreaterThan(now));
        await lease.ReleaseAsync("w-2", CancellationToken.None);
        Assert.That(await lease.TakeAsync("w-1", TimeSpan.FromSeconds(30), CancellationToken.None), Is.True, "given up");
    }

    private static readonly DateTimeOffset IdeaGeneratorNoon = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
}
