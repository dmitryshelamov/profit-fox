using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

public class TInvestMethodsTests
{
    [TestCase("tinkoff.public.invest.api.contract.v1.UsersService", "users", "Users")]
    [TestCase("tinkoff.public.invest.api.contract.v1.InstrumentsService", "instruments", "Instruments")]
    [TestCase("tinkoff.public.invest.api.contract.v1.MarketDataService", "market_data", "MarketData")]
    [TestCase("tinkoff.public.invest.api.contract.v1.MarketDataStreamService", "market_data", "MarketData")]
    [TestCase("tinkoff.public.invest.api.contract.v1.OperationsService", "operations", "Operations")]
    [TestCase("tinkoff.public.invest.api.contract.v1.OrdersService", "orders", "Orders")]
    [TestCase("tinkoff.public.invest.api.contract.v1.StopOrdersService", "orders", "Orders")]
    public void Service_names_its_group_with_its_codes(string service, string code, string key)
    {
        TInvestMethodGroup group = TInvestMethods.GroupOf(service);

        group.ToCode().ShouldBe(code);
        group.SettingsKey().ShouldBe(key);
    }

    [Test]
    public void Every_group_has_a_code_of_its_own()
    {
        Enum.GetValues<TInvestMethodGroup>().Select(group => group.ToCode()).ShouldBe(["users", "instruments", "market_data", "operations", "orders"]);
    }

    [Test]
    public void Unknown_service_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TInvestMethods.GroupOf("tinkoff.public.invest.api.contract.v1.SandboxService"));
    }
}