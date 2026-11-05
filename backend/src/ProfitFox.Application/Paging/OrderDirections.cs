namespace ProfitFox.Application.Paging;

/// <summary>Codes of the sort directions in the API: <c>asc</c> and <c>desc</c>.</summary>
public static class OrderDirections
{
    public const string Invalid = "Direction must be asc or desc.";

    public static bool TryParse(string code, out OrderDirection direction)
    {
        direction = code == "desc" ? OrderDirection.Descending : OrderDirection.Ascending;

        return code is "asc" or "desc";
    }
}