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
///     <c>openedAt</c> is the first fill of the entry and <c>secondsToOutcome</c> the time from it to the close of a closed trade;
///     <c>promise</c> is what the idea promised at the entry and <c>ideaOutcome</c> how the idea ended, none for a trade made before
///     the journal or while the idea is open. An open trade has the last price of its pair and what it is worth at it, none while it waits for
///     its entry or when the exchange gave no price. <c>origin</c> tells who took the trade, <c>manual</c>, <c>auto</c> or <c>paper</c>, with the id and the name
///     of the auto, the name of a deleted one too. <c>features</c> are the codes of the features of the market its idea fired with
///     (<c>day_drop</c>, <c>btc_drop</c>, <c>high_funding</c>), copied when the trade was made. <c>ideaEndsAt</c> is the end of the lifetime of
///     the idea of a trade in work while the idea is active, none while it waits for its entry, once it closed and for a closed trade.
///     <c>leverage</c> is the leverage the trade entered with (one off <c>linear</c>) and <c>margin</c> its amount at it; both are none for a trade
///     made before the leverage, whose leverage is unknown.
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
    string? ResultR,
    DateTimeOffset? OpenedAt,
    long? SecondsToOutcome,
    IdeaPromiseResponse? Promise,
    IdeaOutcomeResponse? IdeaOutcome,
    string? LastPrice,
    string? CurrentResult,
    string? CurrentResultR,
    [property: Required] string Origin,
    Guid? AutoId,
    string? AutoName,
    [property: Required] IReadOnlyList<string> Features,
    DateTimeOffset? IdeaEndsAt,
    int? Leverage,
    string? Margin)
{
    public static IdeaTradeResponse Of(IdeaJournalTrade view)
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
            trade.ResultR is { } resultR ? Text(resultR) : null,
            trade.OpenedAt,
            trade.IsOpen ? null : trade.TimeToOutcome is { } time ? (long)time.TotalSeconds : null,
            IdeaPromiseResponse.Of(trade.Promise),
            IdeaOutcomeResponse.Of(trade.IdeaOutcome),
            view.LastPrice is { } price ? Text(price) : null,
            view.CurrentResult is { } current ? Text(current) : null,
            view.CurrentResultR is { } currentR ? Text(currentR) : null,
            view.Origin switch
            {
                IdeaTradeOrigin.Auto => "auto",
                IdeaTradeOrigin.Paper => "paper",
                _ => "manual"
            },
            trade.AutoId,
            view.AutoName,
            IdeaCodes.Codes(trade.Features),
            view.IdeaEndsAt,
            trade.Leverage,
            trade.Leverage is null ? null : Text(trade.Margin));
    }

    internal static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}