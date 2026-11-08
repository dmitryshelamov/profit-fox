using ProfitFox.Application.Exceptions;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>T-Invest answered <c>NOT_FOUND</c>: there is no such instrument or account.</summary>
internal sealed class TInvestNotFoundException(string message) : ExchangeException(message);