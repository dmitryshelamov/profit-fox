using Microsoft.Extensions.Logging;

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

    public Task<bool> AddAsync(Idea idea, CancellationToken cancellationToken)
    {
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

    public Task<(int Open, int Closed)> CountsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult((Ideas.Count(idea => idea.State == IdeaState.Open), Ideas.Count(idea => idea.State == IdeaState.Closed)));
    }
}

/// <summary>
///     The snapshot of the market in memory: the last one written.
/// </summary>
public sealed class MemoryMarketPairRepository : IMarketPairRepository
{
    public IReadOnlyList<MarketPair> Pairs { get; private set; } = [];

    public int Writes { get; private set; }

    public Task ReplaceAsync(IReadOnlyList<MarketPair> pairs, CancellationToken cancellationToken)
    {
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
    ///     The ids of the pairs whose candles the service refuses, with the message of the refusal (task 18.3).
    /// </summary>
    public Dictionary<string, string> Failing { get; } = [];

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

        IReadOnlyList<HistoryCandle> candles = Candles.TryGetValue(instrumentId, out Func<TimeSpan, DateTimeOffset, IReadOnlyList<HistoryCandle>>? of) ? of(interval, before) : [];
        return Task.FromResult(new RecentRead<HistoryCandle>(false, [.. candles.Where(candle => candle.OpenTime + interval <= before).TakeLast(limit)]));
    }

    public Task<RecentRead<HistoryPayment>> Funding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        IReadOnlyList<HistoryPayment> payments = Payments.TryGetValue(instrumentId, out List<HistoryPayment>? found) ? found : [];
        return Task.FromResult(new RecentRead<HistoryPayment>(false, [.. payments.Where(payment => payment.Time >= from && payment.Time < to)]));
    }

    public Task<RecentRead<OpenInterestPoint>> OpenInterest(string instrumentId, TimeSpan interval, DateTimeOffset before, int limit, CancellationToken cancellation)
    {
        return Task.FromResult(new RecentRead<OpenInterestPoint>(false, []));
    }
}

/// <summary>
///     A logger that keeps what was written, for the tests that look at the journal (task 18.3).
/// </summary>
public sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
