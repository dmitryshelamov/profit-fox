namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     A share of TQBR as T-Invest lists it: the uid the data requests go by, the lot, the step of the price, the name, whether it can be
///     sold short and the trading schedule it keeps (<see cref="Exchange" />, such as <c>moex_mrng_evng_e_wknd_dlr</c>).
/// </summary>
internal sealed record TInvestShare(string Uid, string Ticker, string Name, int Lot, decimal MinPriceIncrement, bool ShortEnabled, string Exchange);