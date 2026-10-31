using ProfitFox.Application.Strategies;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Presentation.Contracts.Strategies;

public sealed record PinResponse(string Name, string Type)
{
    public static PinResponse Of(PinDeclaration pin)
    {
        return new PinResponse(pin.Name, StrategyCodes.ToCode(pin.Type));
    }
}