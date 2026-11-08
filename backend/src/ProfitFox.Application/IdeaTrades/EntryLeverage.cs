using ProfitFox.Application.Ports.LiveTrading;

namespace ProfitFox.Application.IdeaTrades;

/// <summary>
///     Decision 1: the leverage of the settings is set on the symbol of <c>linear</c> before an entry, under the lock of the account and after the
///     checks, so that a refusal of the exchange leaves no trade. A manual entry and the entry of an auto both go this way.
/// </summary>
public static class EntryLeverage
{
    /// <returns>
    ///     <see cref="OrderOutcomeKind.Accepted" /> when the symbol has the leverage now or had it already, <see cref="OrderOutcomeKind.Permanent" />
    ///     when the exchange refused it (its message goes to the journal), <see cref="OrderOutcomeKind.Transient" /> when it did not answer.
    /// </returns>
    public static async Task<OrderOutcomeKind> SetAsync(EntryCalculation calculation, IOrderGateway gateway, ILiveTradingJournal journal, CancellationToken cancellationToken)
    {
        if (calculation.LeverageToSet is not { } leverage)
        {
            return OrderOutcomeKind.Accepted;
        }

        EntryPreparation preparation = calculation.Preparation;
        OrderOutcome outcome = await gateway.SetLeverageAsync(preparation.Key!, preparation.Idea.Instrument, leverage, cancellationToken);

        if (outcome.Kind == OrderOutcomeKind.Permanent)
        {
            journal.LeverageRefused(preparation.Account!.Id, preparation.Idea.Instrument, leverage, outcome.Reason!);
        }

        return outcome.Kind;
    }
}