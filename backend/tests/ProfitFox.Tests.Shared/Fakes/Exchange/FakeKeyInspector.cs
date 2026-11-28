using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Tests.Shared.Fakes.Exchange;

/// <summary>
///     Bybit in memory for the tests of the accounts (change add-exchange-accounts, design.md, decision 10): what it says of
///     each API key, or the refusal it throws, the free money of each key, and every call it got. A key it was not told of is
///     a trading key of a unified account with the UID <see cref="DefaultUid" />.
/// </summary>
public sealed class FakeKeyInspector : IExchangeKeyInspector
{
    public const long DefaultUid = 104857321;

    private readonly Dictionary<string, Func<ExchangeKeyInfo>> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<decimal>> _free = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<string?>> _modes = new(StringComparer.Ordinal);

    /// <summary>
    ///     The calls, as the method, the key and the secret they were made with.
    /// </summary>
    public List<(string Call, string ApiKey, string Secret)> Calls { get; } = [];

    /// <summary>
    ///     A key Bybit takes: of a unified account, may trade contracts, may not withdraw.
    /// </summary>
    public static ExchangeKeyInfo Trading(long uid = DefaultUid)
    {
        return new ExchangeKeyInfo(uid, false, true, ["AccountTransfer"], ["Order", "Position"]);
    }

    public FakeKeyInspector Key(string apiKey, ExchangeKeyInfo info)
    {
        _keys[apiKey] = () => info;
        return this;
    }

    /// <summary>
    ///     Bybit refuses the key by throwing <paramref name="refusal" />.
    /// </summary>
    public FakeKeyInspector Key(string apiKey, Exception refusal)
    {
        _keys[apiKey] = () => throw refusal;
        return this;
    }

    public FakeKeyInspector Free(string apiKey, decimal free)
    {
        _free[apiKey] = () => free;
        return this;
    }

    public FakeKeyInspector Free(string apiKey, Exception refusal)
    {
        _free[apiKey] = () => throw refusal;
        return this;
    }

    /// <summary>
    ///     The margin mode Bybit answers for the key, or the refusal it throws; none for a key it was not told of.
    /// </summary>
    public FakeKeyInspector Mode(string apiKey, string? mode)
    {
        _modes[apiKey] = () => mode;
        return this;
    }

    public FakeKeyInspector Mode(string apiKey, Exception refusal)
    {
        _modes[apiKey] = () => throw refusal;
        return this;
    }

    public Task<string?> MarginModeAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        Calls.Add(("mode", apiKey, secret));
        return Task.FromResult(_modes.TryGetValue(apiKey, out Func<string?>? mode) ? mode() : null);
    }

    public Task<ExchangeKeyInfo> InspectAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        Calls.Add(("inspect", apiKey, secret));
        return Task.FromResult(_keys.TryGetValue(apiKey, out Func<ExchangeKeyInfo>? info) ? info() : Trading());
    }

    public Task<decimal> FreeAsync(string apiKey, string secret, CancellationToken cancellationToken)
    {
        Calls.Add(("free", apiKey, secret));
        return Task.FromResult(_free.TryGetValue(apiKey, out Func<decimal>? free) ? free() : 0m);
    }
}
