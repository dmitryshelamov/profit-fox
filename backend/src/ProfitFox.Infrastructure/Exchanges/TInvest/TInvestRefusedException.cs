using ProfitFox.Application.Exceptions;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>T-Invest refused a call; <see cref="Code" /> is the error code of the detail of its status, <c>null</c> when the detail is not a number.</summary>
internal class TInvestRefusedException(int? code, string message) : ExchangeException(message)
{
    public int? Code { get; } = code;
}