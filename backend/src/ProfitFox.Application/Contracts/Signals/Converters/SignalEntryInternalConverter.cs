using ProfitFox.Domain.Entities.Signals;

namespace ProfitFox.Application.Contracts.Signals.Converters;

public static class SignalEntryInternalConverter
{
    /// <summary>
    ///     A signal that reads, as a line of the list.
    /// </summary>
    public static SignalEntryInternal ToEntryInternal(this SignalDocument signal)
    {
        return new SignalEntryInternal(signal.Name, signal.Description, signal.Version, Error: null) { Id = signal.Id };
    }
}
