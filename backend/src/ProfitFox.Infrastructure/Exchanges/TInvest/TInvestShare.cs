namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>A share of TQBR as T-Invest lists it: the uid the data requests go by, the lot, the step of the price, the name and whether it can be sold short.</summary>
internal sealed record TInvestShare(string Uid, string Ticker, string Name, int Lot, decimal MinPriceIncrement, bool ShortEnabled);