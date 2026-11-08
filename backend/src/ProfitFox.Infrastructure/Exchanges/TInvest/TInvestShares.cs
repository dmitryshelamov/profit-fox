using ProfitFox.Application.Exceptions;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Resolves a ticker into a share of the main board TQBR, kept for an hour: the data requests go by the uid of the share, and the
///     lot turns its volumes into shares. A share the system trades is in roubles, trades through the API, and has a positive lot and
///     price step; any other share is not one. One for the process.
/// </summary>
internal sealed class TInvestShares(TInvestCall call, TInvestTokenSource tokens, TimeProvider time)
{
    public const string ClassCode = "TQBR";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTimeOffset Expires, TInvestShare Share)> _cache = [];

    /// <summary>The share of the ticker, or <c>null</c> when T-Invest lists none on TQBR that the system trades.</summary>
    /// <exception cref="ExchangeUnavailableException">T-Invest did not answer, or there is no T-Invest account.</exception>
    public async Task<TInvestShare?> TryShareByAsync(string ticker, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        lock (_gate)
        {
            if (_cache.TryGetValue(ticker, out (DateTimeOffset Expires, TInvestShare Share) cached) && now < cached.Expires)
            {
                return cached.Share;
            }
        }

        string token = await tokens.GetAsync(cancellationToken);
        var request = new InstrumentRequest { IdType = InstrumentIdType.Ticker, ClassCode = ClassCode, Id = ticker };
        ShareResponse response;

        try
        {
            response = await call.OnceAsync(token, invoker => new InstrumentsService.InstrumentsServiceClient(invoker).ShareByAsync(request), cancellationToken);
        }
        catch (TInvestNotFoundException)
        {
            return null;
        }

        TInvestShare? share = Tradable(response.Instrument);

        if (share is not null)
        {
            lock (_gate)
            {
                _cache[ticker] = (now + Lifetime, share);
            }
        }

        return share;
    }

    /// <exception cref="ExchangeException">T-Invest lists no such share the system trades.</exception>
    /// <exception cref="ExchangeUnavailableException">T-Invest did not answer, or there is no T-Invest account.</exception>
    public async Task<TInvestShare> ShareByAsync(string ticker, CancellationToken cancellationToken)
    {
        return await TryShareByAsync(ticker, cancellationToken)
               ?? throw new ExchangeException($"T-Invest lists no share {ticker} on {ClassCode} that trades through the API in roubles.");
    }

    /// <summary>The share as the system trades it, or <c>null</c> for a share off TQBR, not in roubles, closed to the API, or without a positive lot or price step.</summary>
    public static TInvestShare? Tradable(Share share)
    {
        decimal step = share.MinPriceIncrement?.ToDecimal() ?? 0m;

        return share.ClassCode == ClassCode
               && string.Equals(share.Currency, "rub", StringComparison.OrdinalIgnoreCase)
               && share.ApiTradeAvailableFlag
               && share.Lot > 0
               && step > 0m
            ? new TInvestShare(share.Uid, share.Ticker, share.Name, share.Lot, step, share.ShortEnabledFlag, share.Exchange)
            : null;
    }
}