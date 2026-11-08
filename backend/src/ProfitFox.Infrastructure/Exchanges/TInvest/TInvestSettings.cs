namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The <c>TInvest</c> section of the service settings: where the app reaches T-Invest Invest API, how it names itself, how long a
///     call may take, the requests a minute each group of methods may make with one token in this process, and the certificates the
///     server chains to.
/// </summary>
internal sealed record TInvestSettings
{
    /// <summary>The host of the live Invest API; the only one the settings accept besides a test host under <c>.invalid</c>.</summary>
    public const string OfficialHost = "invest-public-api.tbank.ru";

    public string Endpoint { get; init; } = $"https://{OfficialHost}:443";

    /// <summary>The <c>x-app-name</c> header of every call.</summary>
    public string AppName { get; init; } = "profit-fox";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Requests a minute by group of methods (<c>Users</c>, <c>Instruments</c>, <c>MarketData</c>, <c>Operations</c>, <c>Orders</c>).</summary>
    public Dictionary<string, int> Limits { get; init; } = new Dictionary<string, int>
    {
        ["Users"] = 50,
        ["Instruments"] = 150,
        ["MarketData"] = 100,
        ["Operations"] = 100,
        ["Orders"] = 50
    };

    public TInvestCertificateSettings Certificates { get; init; } = new TInvestCertificateSettings();

    /// <summary>Why the settings are wrong, or <c>null</c>: the endpoint goes only to the official host, or to a test host under <c>.invalid</c>.</summary>
    public string? Problem()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return $"TInvest:Endpoint must be an https address, not '{Endpoint}'.";
        }

        return endpoint.Host == OfficialHost || endpoint.Host.EndsWith(".invalid", StringComparison.Ordinal)
            ? null
            : $"TInvest:Endpoint must point at {OfficialHost}, not '{endpoint.Host}'.";
    }
}