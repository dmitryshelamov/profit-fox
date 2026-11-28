namespace ProfitFox.Infrastructure.Security;

/// <summary>
///     The section <c>ExchangeAccounts</c> of the settings of the API: the master key in base64, 32 bytes.
/// </summary>
internal sealed class ExchangeAccountsSettings
{
    public string? MasterKey { get; set; }
}
