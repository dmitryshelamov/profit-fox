using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Application.Contracts.Trades;

/// <summary>
///     An account the trading Worker polls instead of streaming, and since when (spec idea-trades «Связь с биржей на экране»).
/// </summary>
public sealed record PolledAccountInternal(Guid Id, string Name, DateTimeOffset Since);

/// <summary>
///     The state of the risk module (change take-idea-trades, design.md, decision 13; spec trade-risk «Раздел „Риск“»): the
///     settings, the limits not set, the loss of the day since its start, the trades that hold their pairs and the sum of their
///     amounts, the emergency stop with the symbols of the trades in «Ошибка» it does not close, whether a trading Worker works
///     and which, the number of accounts, those on the stream and those polled.
/// </summary>
public sealed record RiskStateInternal(
    TradeSettings Settings,
    IReadOnlyList<string> Missing,
    DayLoss Day,
    int OpenTrades,
    decimal OpenAmount,
    TradeHalt Halt,
    IReadOnlyList<string> NotClosed,
    bool TraderAlive,
    string? Trader,
    int Accounts,
    int Streamed,
    IReadOnlyList<PolledAccountInternal> Polled);
