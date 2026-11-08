using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>
///     The decrypted key of an exchange account for one call; its text never shows the secret. On Bybit <see cref="ApiKey" /> is the API
///     key and <see cref="Secret" /> its secret; on T-Invest they are the number of the brokerage account and the token.
/// </summary>
public sealed record ExchangeKey(string ApiKey, string Secret, Exchange Exchange = Exchange.Bybit)
{
    public override string ToString()
    {
        return $"ExchangeKey {{ ApiKey = {ApiKey}, Exchange = {Exchange} }}";
    }
}