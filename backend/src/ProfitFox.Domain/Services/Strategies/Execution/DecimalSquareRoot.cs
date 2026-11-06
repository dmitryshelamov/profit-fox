namespace ProfitFox.Domain.Services.Strategies.Execution;

/// <summary>Square root in <see cref="decimal" />: Newton's iterations from an upper estimate, for as long as the value decreases.</summary>
public static class DecimalSquareRoot
{
    public static decimal Of(decimal value)
    {
        if (value == 0m)
        {
            return 0m;
        }

        decimal estimate = Math.Max(value, 1m);

        while (true)
        {
            decimal next = (estimate + value / estimate) / 2m;

            if (next >= estimate)
            {
                return estimate;
            }

            estimate = next;
        }
    }
}