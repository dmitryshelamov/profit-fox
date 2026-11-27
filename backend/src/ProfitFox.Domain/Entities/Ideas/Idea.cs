using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Entities.Ideas;

/// <summary>
///     An idea (change add-ideas-and-market, design.md, decision 5; spec strategy-ideas «Идея»): an entry order of an enabled
///     strategy at a closed candle of a pair, with its plan — side, size, time, stop and target as fractions — and labels;
///     then how far its cycle is counted and, once it closed, its exit and outcome. It keeps the id and the name of its
///     strategy, so the journal outlives the strategy. Until the first minute with trades it shows the close of its candle.
/// </summary>
public sealed class Idea
{
    public required Guid Id { get; init; }

    public required Guid StrategyId { get; init; }

    public required string StrategyName { get; init; }

    public required int StrategyVersion { get; init; }

    public required TimeSpan Timeframe { get; init; }

    public required string Symbol { get; init; }

    public required EntrySide Side { get; init; }

    /// <summary>
    ///     The close of the candle the idea came at.
    /// </summary>
    public required DateTimeOffset Moment { get; init; }

    public required decimal Size { get; init; }

    public required TimeSpan After { get; init; }

    public double? Stop { get; init; }

    public double? Target { get; init; }

    /// <summary>
    ///     The close of its candle: the price the idea shows until it has an entry.
    /// </summary>
    public required decimal CandleClose { get; init; }

    public IReadOnlyList<string> Labels { get; set; } = [];

    /// <summary>
    ///     The score of the idea (change add-idea-score, spec idea-score «Балл идеи»): the sum of the weights of its labels by
    ///     its side, by the weights now. The store keeps it with the idea and counts it again when the weights of one of its
    ///     labels change (design.md, decision 2); it changes nothing of the idea, its plan or its outcome, and saving the idea
    ///     does not write it.
    /// </summary>
    public int Score { get; init; }

    public IdeaState State { get; set; } = IdeaState.Open;

    /// <summary>
    ///     The open of the first minute with trades from the moment, and the price of the entry with the slippage; null
    ///     before that minute.
    /// </summary>
    public double? EntryRaw { get; set; }

    public double? EntryPrice { get; set; }

    public DateTimeOffset? EntryTime { get; set; }

    /// <summary>
    ///     The sum of rate × price of the payments counted so far (entry &lt; t), as the minute path sums them.
    /// </summary>
    public double FundingPaid { get; set; }

    /// <summary>
    ///     The time of the last payment counted in <see cref="FundingPaid" />: a payment told again after a restart is not
    ///     counted twice.
    /// </summary>
    public DateTimeOffset? FundingUntil { get; set; }

    /// <summary>
    ///     The close of the last minute with trades the idea saw: the price a payment is counted at.
    /// </summary>
    public double? LastClose { get; set; }

    /// <summary>
    ///     The open time of the last minute the cycle of the idea was walked through.
    /// </summary>
    public DateTimeOffset? ComputedUntil { get; set; }

    /// <summary>
    ///     The result now as a fraction (spec strategy-ideas «Результат сейчас») and when it was counted.
    /// </summary>
    public double? Now { get; set; }

    public DateTimeOffset? NowAt { get; set; }

    public DateTimeOffset? ExitTime { get; set; }

    public ExitReason ExitReason { get; set; }

    public bool Ambiguous { get; set; }

    /// <summary>
    ///     The price of the exit before the slippage — the level of the stop or of the target, or the close of the last minute
    ///     with trades; null before the exit.
    /// </summary>
    public double? ExitRaw { get; set; }

    /// <summary>
    ///     The outcome after costs as a fraction and in USDT; null until the idea is closed.
    /// </summary>
    public double? Result { get; set; }

    public double? ResultUsdt { get; set; }

    /// <summary>
    ///     Why an excluded idea has no outcome: «нет данных» or «пара снята с торгов».
    /// </summary>
    public string? Excluded { get; set; }

    public DateTimeOffset Until => Moment + After;

    /// <summary>
    ///     The price the idea shows as its entry: that of the entry, or the close of its candle before it.
    /// </summary>
    public double ShownEntry => EntryPrice ?? (double)CandleClose;

    /// <summary>
    ///     The price of the stop and of the target, counted from the entry (spec strategy-ideas «Цена входа идеи»); null
    ///     without them or before the entry.
    /// </summary>
    public double? StopPrice => EntryPrice is { } entry && Stop is { } stop ? entry * (1 - Direction * stop) : null;

    public double? TargetPrice => EntryPrice is { } entry && Target is { } target ? entry * (1 + Direction * target) : null;

    private double Direction => Side == EntrySide.Long ? 1 : -1;
}
