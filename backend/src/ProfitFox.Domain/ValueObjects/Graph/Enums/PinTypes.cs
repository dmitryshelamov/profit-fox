namespace ProfitFox.Domain.ValueObjects.Graph.Enums;

/// <summary>
///     The names of pin types in the JSON format.
/// </summary>
public static class PinTypes
{
    public static string Name(PinType type)
    {
        return type switch
        {
            PinType.Number => "number",
            PinType.Boolean => "boolean",
            PinType.Duration => "duration",
            PinType.Event => "event",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null)
        };
    }

    public static bool TryParse(string name, out PinType type)
    {
        switch (name)
        {
            case "number":
                type = PinType.Number;

                return true;
            case "boolean":
                type = PinType.Boolean;

                return true;
            case "duration":
                type = PinType.Duration;

                return true;
            case "event":
                type = PinType.Event;

                return true;
            default:
                type = default;

                return false;
        }
    }
}