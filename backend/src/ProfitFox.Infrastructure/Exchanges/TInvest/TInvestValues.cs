using Google.Protobuf.WellKnownTypes;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>The values of Invest API: <c>Quotation</c> and <c>MoneyValue</c> are <c>units + nano / 1e9</c>, read and written in decimal; timestamps are UTC.</summary>
internal static class TInvestValues
{
    private const decimal NanosInUnit = 1_000_000_000m;

    public static decimal ToDecimal(this Quotation quotation)
    {
        return quotation.Units + quotation.Nano / NanosInUnit;
    }

    public static decimal ToDecimal(this MoneyValue money)
    {
        return money.Units + money.Nano / NanosInUnit;
    }

    /// <summary>The units and the nanos of a value, both with the sign of the value, as the API writes them.</summary>
    public static Quotation ToQuotation(this decimal value)
    {
        decimal units = decimal.Truncate(value);

        return new Quotation { Units = (long)units, Nano = (int)((value - units) * NanosInUnit) };
    }

    /// <summary>The moment of a timestamp, in UTC.</summary>
    public static DateTimeOffset ToUtcMoment(this Timestamp timestamp)
    {
        return timestamp.ToDateTimeOffset().ToUniversalTime();
    }

    public static Timestamp ToProtoTimestamp(this DateTimeOffset moment)
    {
        return Timestamp.FromDateTimeOffset(moment.ToUniversalTime());
    }
}