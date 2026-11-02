namespace ProfitFox.Application.ExchangeAccounts;

/// <summary>The <c>ExchangeAccounts</c> section of the service settings.</summary>
public sealed class ExchangeAccountSettings
{
    /// <summary>The key that encrypts the secrets of exchange accounts: 32 bytes in base64.</summary>
    public string? MasterKey { get; init; }
}