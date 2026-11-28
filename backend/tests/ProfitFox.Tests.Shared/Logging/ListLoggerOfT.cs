using Microsoft.Extensions.Logging;

namespace ProfitFox.Tests.Shared.Logging;

/// <summary>
///     The journal of a class that asks for <see cref="ILogger{TCategoryName}" />, kept in <paramref name="logger" />.
/// </summary>
public sealed class ListLogger<T>(ListLogger logger) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return logger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
