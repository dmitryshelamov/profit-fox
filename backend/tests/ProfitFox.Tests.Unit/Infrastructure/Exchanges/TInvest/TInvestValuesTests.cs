using Google.Protobuf.WellKnownTypes;

using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 7: <c>units + nano / 1e9</c> both ways, in decimal, and timestamps in UTC.</summary>
public class TInvestValuesTests
{
    [Test]
    public void Quotation_is_units_and_nanos()
    {
        new Quotation { Units = 305, Nano = 500000000 }.ToDecimal().ShouldBe(305.5m);
    }

    [Test]
    public void Money_value_is_units_and_nanos()
    {
        new MoneyValue { Currency = "rub", Units = 50000, Nano = 10 }.ToDecimal().ShouldBe(50000.00000001m);
    }

    [Test]
    public void Negative_value_has_negative_nanos()
    {
        new Quotation { Units = -1, Nano = -500000000 }.ToDecimal().ShouldBe(-1.5m);
        (-1.5m).ToQuotation().ShouldBe(new Quotation { Units = -1, Nano = -500000000 });
    }

    [TestCase("305.5", 305L, 500000000)]
    [TestCase("0.01", 0L, 10000000)]
    [TestCase("12", 12L, 0)]
    [TestCase("0.000000001", 0L, 1)]
    public void Decimal_becomes_a_quotation_without_floating_point(string value, long units, int nano)
    {
        decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture).ToQuotation().ShouldBe(new Quotation { Units = units, Nano = nano });
    }

    [Test]
    public void Timestamp_is_read_as_utc()
    {
        DateTimeOffset moment = Timestamp.FromDateTimeOffset(DateTimeOffset.Parse("2024-03-10T12:30:00Z")).ToUtcMoment();

        (moment, moment.Offset).ShouldBe((DateTimeOffset.Parse("2024-03-10T12:30:00Z"), TimeSpan.Zero));
    }

    [Test]
    public void Moment_with_an_offset_becomes_a_utc_timestamp()
    {
        DateTimeOffset.Parse("2024-03-10T15:30:00+03:00").ToProtoTimestamp().ShouldBe(Timestamp.FromDateTimeOffset(DateTimeOffset.Parse("2024-03-10T12:30:00Z")));
    }
}