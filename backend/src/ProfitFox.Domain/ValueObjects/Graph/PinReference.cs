namespace ProfitFox.Domain.ValueObjects.Graph;

/// <summary>
///     A pin of a node, written <c>node.pin</c>.
/// </summary>
public sealed record PinReference(string Node, string Pin)
{
    public static bool TryParse(string text, out PinReference? reference)
    {
        int dot = text.IndexOf(value: '.', StringComparison.Ordinal);

        if (dot <= 0 || dot == text.Length - 1 || text.IndexOf(value: '.', dot + 1) >= 0)
        {
            reference = null;

            return false;
        }

        reference = new PinReference(text[..dot], text[(dot + 1)..]);

        return true;
    }

    public override string ToString()
    {
        return $"{Node}.{Pin}";
    }
}