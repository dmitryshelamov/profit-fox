using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.Entities.IdeaAutos.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Domain.Services.IdeaAutos;

/// <summary>
///     What the open trades take of the budgets (decision 1): of one auto, its real trades on any account and its paper ones; of all the autos, the real
///     trades of every auto, a deleted one too. A trade takes its amount after the volume was rounded, as it holds it.
/// </summary>
public static class AutoBudget
{
    public static decimal Used(Guid autoId, AutoBooks books)
    {
        return books.OpenReal.Concat(books.OpenPaper.Select(paper => paper.Trade))
            .Where(trade => trade.IsOpen && trade.AutoId == autoId)
            .Sum(trade => trade.Amount);
    }

    /// <summary>
    ///     The auto cannot hold one more trade of its amount, so it leaves the ideas alone (owner, 2026-11-07): its own budget; with money also the
    ///     budget of all the autos and «Не больше открытых сделок», which count the real trades only.
    /// </summary>
    public static bool IsFull(IdeaAuto auto, AutoBooks books, IdeaTradingSettings settings)
    {
        if (Used(auto.Id, books) + auto.Amount > auto.Budget)
        {
            return true;
        }

        return auto.Mode == IdeaAutoMode.Live
               && (AutosUsed(books.OpenReal) + auto.Amount > settings.AutosBudget || books.OpenReal.Count(trade => trade.IsOpen) >= settings.MaxOpenTrades);
    }

    public static decimal AutosUsed(IEnumerable<IdeaTrade> openReal)
    {
        return openReal.Where(trade => trade.IsOpen && trade.AutoId is not null).Sum(trade => trade.Amount);
    }
}