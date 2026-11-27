using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Services.Ideas;

/// <summary>
///     The ideas in memory (change add-ideas-and-market): an idea of a strategy on a pair, timeframe and candle is one, as the
///     unique index of the database keeps it; a saved idea is kept as the object written.
/// </summary>
public sealed class MemoryIdeaRepository : IIdeaRepository
{
    public List<Idea> Ideas { get; } = [];

    public int Saves { get; private set; }

    /// <summary>
    ///     How many of the next writes of a new idea fail, as a write to a database that is down does (task 18.8).
    /// </summary>
    public int FailingAdds { get; set; }

    public Task<bool> AddAsync(Idea idea, CancellationToken cancellationToken)
    {
        if (FailingAdds > 0)
        {
            FailingAdds--;
            throw new InvalidOperationException("the database is down");
        }

        if (Ideas.Any(held => held.StrategyId == idea.StrategyId && held.Symbol == idea.Symbol && held.Timeframe == idea.Timeframe && held.Moment == idea.Moment))
        {
            return Task.FromResult(false);
        }

        Ideas.Add(idea);
        return Task.FromResult(true);
    }

    /// <summary>
    ///     How many statements of writing changes went to the store: one a call with ideas (task 18.6).
    /// </summary>
    public List<int> Writes { get; } = [];

    public Task SaveAsync(Idea idea, CancellationToken cancellationToken)
    {
        Saves++;
        Writes.Add(1);
        return Task.CompletedTask;
    }

    public Task SaveAllAsync(IReadOnlyCollection<Idea> ideas, CancellationToken cancellationToken)
    {
        Saves += ideas.Count;
        if (ideas.Count > 0)
        {
            Writes.Add(ideas.Count);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Idea>> OpenAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Idea>>([.. Ideas.Where(idea => idea.State == IdeaState.Open)]);
    }

    public Task<Idea?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult(Ideas.FirstOrDefault(idea => idea.Id == id));
    }

    public Task<ListPage<Idea>> ListAsync(IdeaListQuery query, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The lists of ideas are those of the database.");
    }

    public Task<ListPage<IdeaSummary>> SummaryAsync(IdeaListQuery query, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The summary is that of the database.");
    }

    public Task<IReadOnlyList<IdeaScoreRow>> ScoresAsync(IdeaListQuery query, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The outcomes by score are those of the database.");
    }

    public Task<(int Open, int Closed)> CountsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult((Ideas.Count(idea => idea.State == IdeaState.Open), Ideas.Count(idea => idea.State == IdeaState.Closed)));
    }

    public Task<IReadOnlyList<IdeaStrategyCount>> StrategyCountsAsync(IdeaListQuery query, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The counts by strategy are those of the database.");
    }
}

/// <summary>
///     The snapshot of the market in memory: the last one written.
/// </summary>
public sealed class MemoryMarketPairRepository : IMarketPairRepository
{
    public IReadOnlyList<MarketPair> Pairs { get; private set; } = [];

    public int Writes { get; private set; }

    /// <summary>
    ///     Whether the write of the snapshot fails, as a write to a database that is down does (task 18.8).
    /// </summary>
    public bool Failing { get; set; }

    public Task ReplaceAsync(IReadOnlyList<MarketPair> pairs, CancellationToken cancellationToken)
    {
        if (Failing)
        {
            throw new InvalidOperationException("the database is down");
        }

        Pairs = pairs;
        Writes++;
        return Task.CompletedTask;
    }

    public Task<ListPage<MarketPairRow>> ListAsync(MarketListQuery query, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The list of the market is that of the database.");
    }

    public Task<MarketPairRow?> FindAsync(string symbol, CancellationToken cancellationToken)
    {
        return Task.FromResult(Pairs.FirstOrDefault(pair => pair.Symbol == symbol) is { } pair ? new MarketPairRow(pair, []) : null);
    }

    public Task<FilterCount> CountAsync(IReadOnlyList<FilterCondition> conditions, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The count is that of the database.");
    }
}

/// <summary>
///     Whether strategies give ideas, in memory.
/// </summary>
public sealed class MemoryStrategyIdeasRepository : IStrategyIdeasRepository
{
    private readonly Dictionary<Guid, StrategyIdeas> _ideas = [];

    public Task<StrategyIdeas?> FindAsync(Guid strategyId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_ideas.GetValueOrDefault(strategyId));
    }

    public Task SetAsync(StrategyIdeas ideas, CancellationToken cancellationToken)
    {
        _ideas[ideas.StrategyId] = ideas;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StrategyIdeas>> AllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<StrategyIdeas>>([.. _ideas.Values]);
    }

    public void Remove(Guid strategyId)
    {
        _ideas.Remove(strategyId);
    }
}

/// <summary>
///     The labels of signals in memory: a text that is on is one of its kind.
/// </summary>
public sealed class MemorySignalLabelRepository : ISignalLabelRepository
{
    private readonly Dictionary<Guid, SignalLabel> _labels = [];

    public Task<SignalLabel?> FindAsync(Guid signalId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_labels.GetValueOrDefault(signalId));
    }

    public Task<bool> SetAsync(SignalLabel label, CancellationToken cancellationToken)
    {
        if (label.Enabled && _labels.Values.Any(held => held.Enabled && held.SignalId != label.SignalId && held.Text == label.Text))
        {
            return Task.FromResult(false);
        }

        _labels[label.SignalId] = label;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<SignalLabel>> AllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<SignalLabel>>([.. _labels.Values]);
    }
}

/// <summary>
///     Recent history for the tests: the candles a test gives each pair by its id, closed by the moment asked, or «loading»
///     for a pair a test says is loading; no payments unless given. It records what was asked.
/// </summary>
public sealed class FakeRecentHistory : IRecentHistory
{
    public Dictionary<string, Func<TimeSpan, DateTimeOffset, IReadOnlyList<HistoryCandle>>> Candles { get; } = [];

    public Dictionary<string, List<HistoryPayment>> Payments { get; } = [];

    public HashSet<string> Loading { get; } = [];

    /// <summary>
    ///     The points of open interest a test gives each pair by its id (change add-idea-score).
    /// </summary>
    public Dictionary<string, List<OpenInterestPoint>> Points { get; } = [];

    /// <summary>
    ///     The ids of the pairs whose candles the service refuses, with the message of the refusal (task 18.3).
    /// </summary>
    public Dictionary<string, string> Failing { get; } = [];

    /// <summary>
    ///     The ids of the pairs whose candles do not come in time: the read ends as the timeout of <c>HttpClient</c> ends it,
    ///     by a <see cref="TaskCanceledException" /> while the token of the caller is alive (task 19.2).
    /// </summary>
    public HashSet<string> TimingOut { get; } = [];

    /// <summary>
    ///     The ids of the pairs whose candles the service refuses with this exception (task 19.4).
    /// </summary>
    public Dictionary<string, Exception> Refusing { get; } = [];

    /// <summary>
    ///     The ids of the pairs whose points of open interest the service refuses with this exception (change add-idea-score,
    ///     task 8.6); their candles still come.
    /// </summary>
    public Dictionary<string, Exception> OpenInterestRefusing { get; } = [];

    public List<string> Asked { get; } = [];

    Task<RecentRead<HistoryCandle>> IRecentHistory.Candles(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
    {
        lock (Asked)
        {
            Asked.Add($"candles {instrumentId}");
        }

        if (Loading.Contains(instrumentId))
        {
            return Task.FromResult(RecentRead<HistoryCandle>.StillLoading);
        }

        if (Failing.TryGetValue(instrumentId, out string? failure))
        {
            throw new HistoryUnavailableException(failure);
        }

        if (Refusing.TryGetValue(instrumentId, out Exception? refusal))
        {
            throw refusal;
        }

        if (TimingOut.Contains(instrumentId))
        {
            throw new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.",
                new TimeoutException("The operation was canceled."));
        }

        IReadOnlyList<HistoryCandle> candles = Candles.TryGetValue(instrumentId, out Func<TimeSpan, DateTimeOffset, IReadOnlyList<HistoryCandle>>? of) ? of(interval, before) : [];
        return Task.FromResult(new RecentRead<HistoryCandle>(false, [.. candles.Where(candle => candle.OpenTime + interval <= before).TakeLast(limit)]));
    }

    public Task<RecentRead<HistoryPayment>> Funding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        IReadOnlyList<HistoryPayment> payments = Payments.TryGetValue(instrumentId, out List<HistoryPayment>? found) ? found : [];
        return Task.FromResult(new RecentRead<HistoryPayment>(false, [.. payments.Where(payment => payment.Time >= from && payment.Time < to)]));
    }

    public Task<RecentRead<OpenInterestPoint>> OpenInterest(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation, IReadSteps? steps = null)
    {
        return Task.FromResult(new RecentRead<OpenInterestPoint>(false, []));
    }

    // The last point of each interval of the window with its own time, as the service gives them (change add-idea-score).
    public Task<RecentRead<OpenInterestPoint>> OpenInterestPoints(string instrumentId, TimeSpan interval, DateTimeOffset before, int count, CancellationToken cancellation)
    {
        lock (Asked)
        {
            Asked.Add($"open-interest {instrumentId}");
        }

        if (Loading.Contains(instrumentId))
        {
            return Task.FromResult(RecentRead<OpenInterestPoint>.StillLoading);
        }

        if (OpenInterestRefusing.TryGetValue(instrumentId, out Exception? refusal))
        {
            throw refusal;
        }

        long step = interval.Ticks;
        var to = new DateTimeOffset(before.UtcTicks / step * step, TimeSpan.Zero);
        DateTimeOffset from = to - (interval * count);
        IEnumerable<OpenInterestPoint> points = Points.GetValueOrDefault(instrumentId) ?? [];
        return Task.FromResult(new RecentRead<OpenInterestPoint>(false, [
            .. points
                .Where(point => point.Time >= from && point.Time < to)
                .GroupBy(point => point.Time.UtcTicks / step)
                .Select(group => group.MaxBy(point => point.Time)!)
                .OrderBy(point => point.Time)
        ]));
    }
}

/// <summary>
///     The enabled strategies and labels with their latest versions from the stores in memory, without reading a graph
///     (task 18.13).
/// </summary>
public sealed class MemoryIdeaSourceRepository(
    ProfitFox.Tests.Unit.Application.Commands.Strategies.MemoryStrategyRepository strategies,
    ProfitFox.Tests.Unit.Application.Commands.Signals.MemorySignalRepository signals,
    MemoryStrategyIdeasRepository enabled,
    MemorySignalLabelRepository labels) : IIdeaSourceRepository
{
    public async Task<IdeaSources> SourcesAsync(CancellationToken cancellationToken)
    {
        var on = (await enabled.AllAsync(cancellationToken)).Where(item => item.Enabled && item.Timeframes.Count > 0).ToDictionary(item => item.StrategyId);
        var marked = (await labels.AllAsync(cancellationToken)).Where(label => label.Enabled).ToDictionary(label => label.SignalId);
        return new IdeaSources(
            [.. strategies.Documents.Where(strategy => on.ContainsKey(strategy.Id)).Select(strategy => new StrategySource(strategy.Id, strategy.Name, strategy.Version, on[strategy.Id].Timeframes))],
            [.. signals.Documents.Where(signal => marked.ContainsKey(signal.Id)).Select(signal => new LabelSource(signal.Id, signal.Name, signal.Version, marked[signal.Id].Text, marked[signal.Id].Timeframe))]);
    }
}
