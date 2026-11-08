using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.Ports.ExchangeAccounts;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.ExchangeAccounts;

public class BrokerAccountCodesTests
{
    [Test]
    public void Every_type_status_and_access_has_a_code()
    {
        Enum.GetValues<BrokerAccountType>().Select(type => type.ToCode()).ShouldBe(["brokerage", "iis", "other"]);
        Enum.GetValues<BrokerAccountStatus>().Select(status => status.ToCode()).ShouldBe(["new", "open", "closed"]);
        Enum.GetValues<BrokerAccountAccess>().Select(access => access.ToCode()).ShouldBe(["full", "read_only", "none"]);
    }
}