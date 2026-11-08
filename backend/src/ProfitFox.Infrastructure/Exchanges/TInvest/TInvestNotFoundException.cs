namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>T-Invest answered <c>NOT_FOUND</c>: there is no such instrument, account or order.</summary>
internal sealed class TInvestNotFoundException(int? code, string message) : TInvestRefusedException(code, message);