using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Strategies;

namespace ProfitFox.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 6.3: spec strategy-ideas «Хранение идей» — an idea written, moved on, closed and
///     excluded, and kept with its strategy gone or renamed.
/// </summary>
[NonParallelizable]
public sealed class IdeaRepositoryTests
{
    private IIdeaRepository _ideas = null!;

    [SetUp]
    public async Task Empty()
    {
        await BenchDatabase.Reset();
        _ideas = BenchServices.Get<IIdeaRepository>();
    }

    /// <summary>
    ///     Task 18.6: what changed in many ideas goes in one statement, the empty values with it.
    /// </summary>
    [Test]
    public async Task Many_ideas_are_written_by_one_statement()
    {
        Idea moved = IdeaGenerator.Generate(stop: 0.02);
        Idea closed = IdeaGenerator.Generate(symbol: "ETHUSDT");
        Idea excluded = IdeaGenerator.Generate(symbol: "SOLUSDT");
        Idea untouched = IdeaGenerator.Generate(symbol: "XRPUSDT");
        foreach (Idea idea in new[] { moved, closed, excluded, untouched })
        {
            await _ideas.AddAsync(idea, CancellationToken.None);
        }

        moved.EntryRaw = 96200;
        moved.EntryPrice = 96151.9;
        moved.EntryTime = moved.Moment;
        moved.FundingPaid = 0.5;
        moved.FundingUntil = moved.Moment.AddHours(1);
        moved.ComputedUntil = moved.Moment.AddMinutes(30);
        moved.Now = -0.003;
        moved.NowAt = moved.Moment.AddMinutes(31);
        closed.EntryRaw = 3000;
        closed.EntryPrice = 2998.5;
        closed.EntryTime = closed.Moment;
        closed.State = IdeaState.Closed;
        closed.ExitTime = closed.Moment.AddHours(4);
        closed.ExitReason = ExitReason.Time;
        closed.Ambiguous = true;
        closed.ExitRaw = 2990;
        closed.Result = 0.0041;
        closed.ResultUsdt = 0.41;
        excluded.State = IdeaState.Excluded;
        excluded.Excluded = "нет данных";

        await _ideas.SaveAllAsync([moved, closed, excluded], CancellationToken.None);
        await _ideas.SaveAllAsync([], CancellationToken.None);

        Idea readMoved = (await _ideas.FindAsync(moved.Id, CancellationToken.None))!;
        Idea readClosed = (await _ideas.FindAsync(closed.Id, CancellationToken.None))!;
        Idea readExcluded = (await _ideas.FindAsync(excluded.Id, CancellationToken.None))!;
        Assert.That(
            (readMoved.EntryRaw, readMoved.EntryPrice, readMoved.EntryTime, readMoved.StopPrice, readMoved.FundingPaid, readMoved.FundingUntil, readMoved.ComputedUntil, readMoved.Now, readMoved.NowAt, readMoved.State),
            Is.EqualTo((moved.EntryRaw, moved.EntryPrice, moved.EntryTime, moved.StopPrice, moved.FundingPaid, moved.FundingUntil, moved.ComputedUntil, moved.Now, moved.NowAt, IdeaState.Open)));
        Assert.That(
            (readClosed.State, readClosed.ExitTime, readClosed.ExitReason, readClosed.Ambiguous, readClosed.ExitRaw, readClosed.Result, readClosed.ResultUsdt),
            Is.EqualTo((IdeaState.Closed, closed.ExitTime, ExitReason.Time, true, (double?)2990, (double?)0.0041, (double?)0.41)));
        Assert.That((readExcluded.State, readExcluded.Excluded, readExcluded.EntryPrice), Is.EqualTo((IdeaState.Excluded, "нет данных", (double?)null)));
        Assert.That((await _ideas.FindAsync(untouched.Id, CancellationToken.None))!.State, Is.EqualTo(IdeaState.Open));
    }

    [Test]
    public async Task An_idea_is_written_moved_on_and_closed()
    {
        Idea idea = IdeaGenerator.Generate(stop: 0.02, labels: ["↓ BTC", "⚡ всплеск"]);

        Assert.That(await _ideas.AddAsync(idea, CancellationToken.None), Is.True);
        idea.EntryRaw = 96200;
        idea.EntryPrice = 96151.9;
        idea.EntryTime = idea.Moment;
        idea.FundingPaid = 0.5;
        idea.LastClose = 96412.5;
        idea.ComputedUntil = idea.Moment.AddMinutes(30);
        idea.Now = -0.003;
        idea.NowAt = idea.Moment.AddMinutes(31);
        await _ideas.SaveAsync(idea, CancellationToken.None);
        Idea moved = (await _ideas.OpenAsync(CancellationToken.None)).Single();

        Assert.That(
            (moved.EntryRaw, moved.EntryPrice, moved.EntryTime, moved.FundingPaid, moved.LastClose, moved.ComputedUntil, moved.Now, moved.Labels),
            Is.EqualTo((idea.EntryRaw, idea.EntryPrice, idea.EntryTime, idea.FundingPaid, idea.LastClose, idea.ComputedUntil, idea.Now, idea.Labels)));
        Assert.That((moved.Stop, moved.After, moved.Until, moved.CandleClose), Is.EqualTo(((double?)0.02, TimeSpan.FromHours(4), idea.Moment.AddHours(4), 96180m)));

        idea.State = IdeaState.Closed;
        idea.ExitTime = idea.Moment.AddHours(4);
        idea.ExitReason = ExitReason.Time;
        idea.ExitRaw = 96000;
        idea.Result = 0.0041;
        idea.ResultUsdt = 0.41;
        await _ideas.SaveAsync(idea, CancellationToken.None);

        Assert.That(await _ideas.OpenAsync(CancellationToken.None), Is.Empty);
        Idea closed = (await _ideas.FindAsync(idea.Id, CancellationToken.None))!;
        Assert.That((closed.State, closed.ExitTime, closed.ExitReason, closed.Result, closed.ResultUsdt), Is.EqualTo((IdeaState.Closed, idea.ExitTime, ExitReason.Time, (double?)0.0041, (double?)0.41)));
    }

    [Test]
    public async Task An_idea_of_a_strategy_on_a_pair_timeframe_and_candle_is_written_once()
    {
        var strategy = Guid.NewGuid();
        Assert.That(await _ideas.AddAsync(IdeaGenerator.Generate(strategy), CancellationToken.None), Is.True);

        Assert.That(await _ideas.AddAsync(IdeaGenerator.Generate(strategy), CancellationToken.None), Is.False, "the same candle again");
        Assert.That(await _ideas.AddAsync(IdeaGenerator.Generate(strategy, moment: IdeaGenerator.Noon.AddHours(4)), CancellationToken.None), Is.True, "a later candle");
        Assert.That(await _ideas.AddAsync(IdeaGenerator.Generate(strategy, timeframe: TimeSpan.FromHours(1)), CancellationToken.None), Is.True, "another timeframe");
        Assert.That(await _ideas.AddAsync(IdeaGenerator.Generate(strategy, symbol: "XRPUSDT"), CancellationToken.None), Is.True, "another pair");
    }

    [Test]
    public async Task An_excluded_idea_keeps_its_reason_and_frees_the_pair()
    {
        var strategy = Guid.NewGuid();
        Idea idea = IdeaGenerator.Generate(strategy);
        await _ideas.AddAsync(idea, CancellationToken.None);

        idea.State = IdeaState.Excluded;
        idea.Excluded = "нет данных";
        await _ideas.SaveAsync(idea, CancellationToken.None);

        Assert.That((await _ideas.FindAsync(idea.Id, CancellationToken.None))!.Excluded, Is.EqualTo("нет данных"));
        Assert.That(await _ideas.AddAsync(IdeaGenerator.Generate(strategy, moment: IdeaGenerator.Noon.AddHours(4)), CancellationToken.None), Is.True);
    }

    [Test]
    public async Task The_ideas_of_a_deleted_strategy_stay_with_its_name_and_those_of_a_renamed_one_take_the_new_name()
    {
        IStrategyRepository strategies = BenchServices.Get<IStrategyRepository>();
        Guid squeeze = (await strategies.CreateAsync(
            StrategyDocumentGenerator.Generate(Guid.Empty, name: "squeeze-short", graph: StrategyDocumentGenerator.GenerateGraph(signal: null)),
            CancellationToken.None)).Id;
        Guid funding = (await strategies.CreateAsync(
            StrategyDocumentGenerator.Generate(Guid.Empty, name: "funding-short", graph: StrategyDocumentGenerator.GenerateGraph(signal: null)),
            CancellationToken.None)).Id;
        Idea deleted = IdeaGenerator.Generate(squeeze, name: "squeeze-short");
        Idea renamed = IdeaGenerator.Generate(funding, name: "funding-short");
        await _ideas.AddAsync(deleted, CancellationToken.None);
        await _ideas.AddAsync(renamed, CancellationToken.None);

        await strategies.DeleteAsync("squeeze-short", CancellationToken.None);
        await strategies.RenameAsync("funding-short", "squeeze-v2", ProfitFox.Domain.ValueObjects.Strategies.Enums.StrategyKind.Idea, CancellationToken.None);

        Assert.That((await _ideas.FindAsync(deleted.Id, CancellationToken.None))!.StrategyName, Is.EqualTo("squeeze-short"));
        Assert.That((await _ideas.FindAsync(renamed.Id, CancellationToken.None))!.StrategyName, Is.EqualTo("squeeze-v2"));
    }
}
