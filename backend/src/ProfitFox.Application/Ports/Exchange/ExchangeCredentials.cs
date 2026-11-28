namespace ProfitFox.Application.Ports.Exchange;

/// <summary>
///     The key of an account and its decrypted secret, for the calls of the exchange (change take-idea-trades). A record
///     would print the secret: <see cref="ToString" /> names the key only.
/// </summary>
public sealed class ExchangeCredentials(string apiKey, string secret)
{
    public string ApiKey { get; } = apiKey;

    public string Secret { get; } = secret;

    public override string ToString()
    {
        return $"key {ApiKey}";
    }
}
