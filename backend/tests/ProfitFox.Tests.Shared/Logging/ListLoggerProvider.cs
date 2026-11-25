using Microsoft.Extensions.Logging;

namespace ProfitFox.Tests.Shared.Logging;

/// <summary>
///     Gives every category the one <see cref="ListLogger" />: a test of a host reads its journal there.
/// </summary>
public sealed class ListLoggerProvider(ListLogger logger) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return logger;
    }

    public void Dispose()
    {
    }
}
