using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.ValueObjects.IdeaAutos;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.IdeaAutos;

/// <summary>
///     What stops an auto taking an idea that fits its conditions, beyond the checks of the entry itself (<see cref="IdeaTradeRefusals" /> for the
///     real ones, <see cref="PaperEntry" /> for the paper ones): the pair held by a trade, the daily stop and the open trades of one side. It only
///     names the reasons, with what the first one is about.
/// </summary>
public static class AutoRefusals
{
    /// <summary>
    ///     A pair is held by an open trade of the auto itself (<see cref="OwnPair" />), and then by an open real trade of another auto or a manual
    ///     one on the account of the auto, the earliest of them.
    /// </summary>
    public static AutoRefusal? PairTaken(IdeaAuto auto, Instrument instrument, AutoBooks books)
    {
        if (OwnPair(auto, instrument, books) is { } own)
        {
            return own;
        }

        IdeaTrade? other = books.OpenReal
            .Where(trade => trade.IsOpen && trade.AccountId == auto.AccountId && trade.Instrument == instrument)
            .OrderBy(Since)
            .FirstOrDefault();

        if (other is null)
        {
            return null;
        }

        if (other.AutoId is not { } otherId)
        {
            return Taken(new PairHolder(PairHolderKind.Manual, null, false, false, books.AccountName, Since(other)));
        }

        IdeaAuto holder = books.Autos[otherId];

        return Taken(new PairHolder(PairHolderKind.OtherAuto, holder.Name, holder.Position < auto.Position, false, books.AccountName, Since(other)));
    }

    /// <summary>The pair is held by an open trade of the auto itself, paper or real, on any account: the only holder that stops a paper trade.</summary>
    public static AutoRefusal? OwnPair(IdeaAuto auto, Instrument instrument, AutoBooks books)
    {
        IEnumerable<(bool Paper, IdeaTrade Trade)> own = books.OpenReal.Where(trade => trade.AutoId == auto.Id).Select(trade => (false, trade))
            .Concat(books.OpenPaper.Where(paper => paper.AutoId == auto.Id).Select(paper => (true, paper.Trade)));
        (bool Paper, IdeaTrade Trade) held = own.Where(item => item.Trade.IsOpen && item.Trade.Instrument == instrument).OrderBy(item => Since(item.Trade)).FirstOrDefault();

        return held.Trade is null ? null : Taken(new PairHolder(PairHolderKind.SameAuto, auto.Name, false, held.Paper, null, Since(held.Trade)));
    }

    /// <returns>The reasons of an auto with money in a fixed order: the pair, the daily stop, the open trades of the side.</returns>
    public static IReadOnlyList<AutoRefusal> Check(IdeaAuto auto, Instrument instrument, IdeaSide side, AutoBooks books, DailyLossState dailyLoss, int? maxSameDirection)
    {
        var reasons = new List<AutoRefusal>();

        if (PairTaken(auto, instrument, books) is { } taken)
        {
            reasons.Add(taken);
        }

        if (dailyLoss.Holds)
        {
            reasons.Add(new AutoRefusal(IdeaTradeRefusal.DailyStop, RefusalDetail.Numbers(dailyLoss.Sum, dailyLoss.Limit!.Value)));
        }

        int sameSide = books.OpenReal.Count(trade => trade.IsOpen && trade.Side == side);

        if (sameSide >= maxSameDirection)
        {
            reasons.Add(new AutoRefusal(IdeaTradeRefusal.TooManySameDirection, RefusalDetail.Numbers(sameSide, maxSameDirection.Value)));
        }

        return reasons;
    }

    private static AutoRefusal Taken(PairHolder holder)
    {
        return new AutoRefusal(IdeaTradeRefusal.PairTaken, RefusalDetail.Pair(holder));
    }

    private static DateTimeOffset Since(IdeaTrade trade)
    {
        return trade.OpenedAt ?? trade.CreatedAt;
    }
}