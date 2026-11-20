using System.Net;

namespace ProfitFox.Bench.Cli;

/// <summary>
///     What the bench API answered: <see cref="Value" /> when the answer is a success, else the <see cref="Reason" /> of the
///     refusal.
/// </summary>
internal sealed record Answer<T>(HttpStatusCode Status, T? Value, string Reason)
    where T : class
{
    public bool Refused => Value is null;
}
