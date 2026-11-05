using ProfitFox.Application.LiveTrading.Runtime;
using ProfitFox.Application.Ports.LiveTrading;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.LiveTrading.Runtime;

/// <summary>What a session does with an order it just sent or cancelled, by the answer of the exchange: the same for a bot and for a trade.</summary>
public class OrderSendingTests
{
    [TestCase(true, OrderOutcomeKind.Accepted, SendStep.Taken)]
    [TestCase(false, OrderOutcomeKind.Accepted, SendStep.CancelTaken)]
    [TestCase(true, OrderOutcomeKind.AlreadyDone, SendStep.CancelTakenAndRefresh)]
    [TestCase(false, OrderOutcomeKind.AlreadyDone, SendStep.CancelTakenAndRefresh)]
    [TestCase(true, OrderOutcomeKind.Transient, SendStep.Deferred)]
    [TestCase(false, OrderOutcomeKind.Transient, SendStep.Deferred)]
    [TestCase(true, OrderOutcomeKind.Permanent, SendStep.Rejected)]
    [TestCase(false, OrderOutcomeKind.Permanent, SendStep.CancelRefused)]
    public void Answer_of_the_exchange_decides_the_next_step(bool placing, OrderOutcomeKind answer, SendStep expected)
    {
        OrderSending.Resolve(placing, answer).ShouldBe(expected);
    }
}