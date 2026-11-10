namespace ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Market.Contracts;

/// <summary>A row of <c>trade_flow_minutes</c>, or a bar summed from them, without its pair.</summary>
internal sealed class TradeFlowMinuteDb
{
    public DateTimeOffset OpenTime { get; init; }

    public decimal BuyVolume { get; init; }

    public decimal SellVolume { get; init; }

    public decimal BuyTurnover { get; init; }

    public decimal SellTurnover { get; init; }

    public int BuyTrades { get; init; }

    public int SellTrades { get; init; }
}