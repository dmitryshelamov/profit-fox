namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>The decrypted key of an exchange account for one call; its text never shows the secret.</summary>
public sealed record ExchangeKey(string ApiKey, string Secret)
{
    public override string ToString()
    {
        return $"ExchangeKey {{ ApiKey = {ApiKey} }}";
    }
}