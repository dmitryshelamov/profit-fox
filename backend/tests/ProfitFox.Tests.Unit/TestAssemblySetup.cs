using ProfitFox.Tests.Shared.Network;

namespace ProfitFox.Tests.Unit;

[SetUpFixture]
public class TestAssemblySetup
{
    [OneTimeSetUp]
    public void BlockOutboundHttp()
    {
        OutboundHttpGuard.Install();
    }
}