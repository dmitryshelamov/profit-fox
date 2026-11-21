using ProfitFox.Domain.ValueObjects.Ledger;

namespace ProfitFox.Application.Contracts.Runs.Converters;

public static class FinishedRunInternalConverter
{
    public static FinishedRunInternal ToInternal(this FinishedRun finished)
    {
        return new FinishedRunInternal(finished.Entry.ToInternal(), finished.Parity);
    }
}
