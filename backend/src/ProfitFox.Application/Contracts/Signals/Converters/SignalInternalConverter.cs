using ProfitFox.Domain.Entities.Signals;

namespace ProfitFox.Application.Contracts.Signals.Converters;

public static class SignalInternalConverter
{
    public static SignalInternal ToInternal(this SignalDocument signal)
    {
        return new SignalInternal(signal.Name, signal.Description, signal.Graph, signal.Layout, signal.Version);
    }
}
