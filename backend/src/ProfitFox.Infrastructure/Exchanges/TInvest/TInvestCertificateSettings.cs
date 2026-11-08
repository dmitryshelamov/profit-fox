namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The pinned chain of the server certificate of T-Invest: the folder of the <c>.cer</c> files next to the app and the SHA-256
///     thumbprints of the root (Russian Trusted Root CA) and of the issuing Sub CA. Empty thumbprints pin nothing: the system trust alone decides.
/// </summary>
internal sealed record TInvestCertificateSettings
{
    public string Directory { get; init; } = Path.Combine("Exchanges", "TInvest", "Certificates");

    public string RootThumbprint { get; init; } = string.Empty;

    public string SubThumbprint { get; init; } = string.Empty;
}