namespace ProfitFox.Application.Paging;

/// <summary>A page of a list asked for: its number from 1 and its size, 50, 100 or 200.</summary>
public sealed record PageRequest(int Number, int Size)
{
    public const string InvalidPage = "Page must be 1 or greater.";
    public const string InvalidPageSize = "Page size must be 50, 100 or 200.";

    private static readonly int[] Sizes = [50, 100, 200];

    /// <summary>The page of the rows; a number past the end gives the last page, an empty list the first.</summary>
    public Page<T> Slice<T>(IReadOnlyList<T> rows)
    {
        int last = Math.Max(1, (rows.Count + Size - 1) / Size);
        int number = Math.Min(Number, last);

        return new Page<T>([.. rows.Skip((number - 1) * Size).Take(Size)], number, Size, rows.Count);
    }

    /// <summary>Reads the number and the size a client sent, the first page of 50 for what is left out; a wrong one goes to <paramref name="errors" /> by its parameter name.</summary>
    public static PageRequest Parse(int? number, int? size, IDictionary<string, string> errors)
    {
        if (number < 1)
        {
            errors["page"] = InvalidPage;
        }

        if (size is not null && !Sizes.Contains(size.Value))
        {
            errors["pageSize"] = InvalidPageSize;
        }

        return new PageRequest(number ?? 1, size ?? Sizes[0]);
    }
}