using System.ComponentModel.DataAnnotations;
using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.IdeaTrades;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>
///     A trade by an idea: the pair, the setup, the interval, the type and the side of its idea as codes, the account, the levels and the size it
///     was entered with, its status and the time of it, the reason of an «Ошибка» and the result of a closed trade in money and in R (none before).
///     Numbers are decimal strings; <see cref="FilledPrice" /> is the average price the entry was filled at, none before any fill.
/// </summary>
public sealed record IdeaTradeResponse(
    Guid Id,
    Guid IdeaId,
    Guid AccountId,
    [property: Required] string AccountName,
    [property: Required] string Category,
    [property: Required] string Symbol,
    [property: Required] string Setup,
    [property: Required] string Interval,
    [property: Required] string Type,
    [property: Required] string Side,
    [property: Required] string EntryPrice,
    string? FilledPrice,
    [property: Required] string Stop,
    [property: Required] string Target,
    [property: Required] string Quantity,
    [property: Required] string Amount,
    [property: Required] string Risk,
    [property: Required] string Status,
    DateTimeOffset StatusAt,
    DateTimeOffset CreatedAt,
    string? Reason,
    string? Result,
    string? ResultR)
{
    public static IdeaTradeResponse Of(IdeaTradeView view)
    {
        IdeaTrade trade = view.Trade;

        return new IdeaTradeResponse(
            trade.Id,
            trade.IdeaId,
            trade.AccountId,
            view.AccountName,
            trade.Instrument.Category.ToCode(),
            trade.Instrument.Symbol,
            trade.Setup.ToCode(),
            trade.Interval.ToCode(),
            trade.Type.ToCode(),
            trade.Side.ToCode(),
            Text(trade.EntryPrice),
            trade.Entry.Quantity > 0m ? Text(trade.Entry.Value / trade.Entry.Quantity) : null,
            Text(trade.Stop),
            Text(trade.Target),
            Text(trade.Quantity),
            Text(trade.Amount),
            Text(trade.Risk),
            trade.Status.ToCode(),
            trade.StatusAt,
            trade.CreatedAt,
            trade.Reason,
            trade.Result is { } result ? Text(result) : null,
            trade.ResultR is { } resultR ? Text(resultR) : null);
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}