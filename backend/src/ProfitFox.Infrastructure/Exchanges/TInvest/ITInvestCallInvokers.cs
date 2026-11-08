using Grpc.Core;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>Gives the <see cref="CallInvoker" /> the calls to T-Invest go through: the one channel of the process, or a stand-in in tests.</summary>
internal interface ITInvestCallInvokers
{
    CallInvoker Invoker { get; }
}