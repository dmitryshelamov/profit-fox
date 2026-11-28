namespace ProfitFox.Infrastructure.Security;

/// <summary>
///     The master key of the server (change add-exchange-accounts, design.md, decision 1): the setting
///     <see cref="Setting" />, exactly 32 bytes in base64. The value never goes into a message.
/// </summary>
internal static class MasterKey
{
    public const string Setting = "ExchangeAccounts:MasterKey";

    public const int Bytes = 32;

    /// <summary>
    ///     What the start says when the key is not there or not of its kind; it names the setting, not the value.
    /// </summary>
    public const string Refusal = "The setting " + Setting + " is not set or is not 32 bytes in base64: the API does not start without it.";

    /// <summary>
    ///     The bytes of a key written in base64, or null when it is not 32 bytes.
    /// </summary>
    public static byte[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var bytes = new byte[Bytes + 3];
        return Convert.TryFromBase64String(text.Trim(), bytes, out int written) && written == Bytes ? bytes[..Bytes] : null;
    }
}
