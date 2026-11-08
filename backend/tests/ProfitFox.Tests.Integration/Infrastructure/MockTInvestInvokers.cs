using Grpc.Core;

using ProfitFox.Infrastructure.Exchanges.TInvest;
using ProfitFox.Tests.Shared.TInvest;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>Puts <see cref="MockTInvestApi" /> where the channel to T-Invest would be: no test host opens a connection to T-Invest.</summary>
internal sealed class MockTInvestInvokers(MockTInvestApi api) : ITInvestCallInvokers
{
    public CallInvoker Invoker => api;
}