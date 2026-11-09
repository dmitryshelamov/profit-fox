using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Domain.Services.IdeaAutos;

/// <summary>
///     The order an auto takes the ideas of a minute in and its two limits (decision 3 of add-burst-and-auto-limits). One plan is one auto in
///     one minute: the new trades of a side it took in the minute, and its open trades of a side, real and paper, at the start of the minute
///     with those it took in it. An idea over a limit is skipped and counted, one <see cref="LimitSkip" /> a side.
/// </summary>
public sealed class AutoMinutePlan
{
    private readonly int? _perMinute;
    private readonly int? _open;
    private readonly Dictionary<IdeaSide, int> _openBySide;
    private readonly Dictionary<IdeaSide, int> _taken = [];
    private readonly Dictionary<IdeaSide, LimitSkip> _skips = [];

    public AutoMinutePlan(IdeaAuto auto, AutoBooks books)
    {
        _perMinute = auto.MaxPerMinutePerSide;
        _open = auto.MaxOpenPerSide;
        _openBySide = books.OpenReal.Concat(books.OpenPaper.Select(paper => paper.Trade))
            .Where(trade => trade.IsOpen && trade.AutoId == auto.Id)
            .GroupBy(trade => trade.Side)
            .ToDictionary(side => side.Key, side => side.Count());
    }

    /// <summary>The skips of the minute, one a side, in the order the sides were first skipped.</summary>
    public IReadOnlyList<LimitSkip> Skips => [.. _skips.Values];

    /// <summary>
    ///     The order of the ideas of a minute by their promise, as indexes: the highest expectancy of the idea on 1 USDT of the amount first
    ///     (decision 6 of add-own-idea-numbers), then the highest reward to risk after the fees; ideas without an expectancy last, by their reward
    ///     to risk; equal ones in the order they were born.
    /// </summary>
    public static IReadOnlyList<int> Order(IReadOnlyList<(IdeaNumbers Numbers, IdeaShares Shares)> promises)
    {
        return [.. Enumerable.Range(0, promises.Count)
            .OrderByDescending(index => promises[index].Numbers.ExpectancyPerUsdt ?? decimal.MinValue)
            .ThenByDescending(index => promises[index].Shares.NetRiskReward)];
    }

    /// <summary>Whether the limits let the auto take one more idea of the side of the idea; one they do not is counted in the skip of its side.</summary>
    public bool Allows(Idea idea)
    {
        IdeaSide side = idea.Track.Plan.Side;
        int taken = _taken.GetValueOrDefault(side);

        if (taken >= _perMinute)
        {
            Skip(idea, IdeaTradeRefusal.MinuteLimit, _perMinute.Value);

            return false;
        }

        if (_openBySide.GetValueOrDefault(side) + taken >= _open)
        {
            Skip(idea, IdeaTradeRefusal.SideLimit, _open.Value);

            return false;
        }

        return true;
    }

    /// <summary>The auto took the idea: a paper trade or a request.</summary>
    public void Took(Idea idea)
    {
        IdeaSide side = idea.Track.Plan.Side;
        _taken[side] = _taken.GetValueOrDefault(side) + 1;
    }

    private void Skip(Idea idea, IdeaTradeRefusal reason, int limit)
    {
        IdeaSide side = idea.Track.Plan.Side;
        _skips[side] = _skips.TryGetValue(side, out LimitSkip? skip) ? skip with { Skipped = skip.Skipped + 1 } : new LimitSkip(idea, reason, 1, limit);
    }
}