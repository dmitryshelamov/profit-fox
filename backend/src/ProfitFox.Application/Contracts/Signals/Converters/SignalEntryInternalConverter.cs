using ProfitFox.Domain.Entities.Signals;
using ProfitFox.Domain.ValueObjects.Graph.Enums;

namespace ProfitFox.Application.Contracts.Signals.Converters;

public static class SignalEntryInternalConverter
{
    /// <summary>
    ///     A signal that reads, as a line of the list.
    /// </summary>
    public static SignalEntryInternal ToEntryInternal(this SignalDocument signal)
    {
        return new SignalEntryInternal(signal.Name, signal.Description, signal.Version, Error: null) { Id = signal.Id, Output = PinTypes.Name(signal.Graph.Outputs[0].Type) };
    }
}
