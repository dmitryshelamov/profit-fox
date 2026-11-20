using System.Text;

namespace ProfitFox.Bench.Worker;

/// <summary>
///     A <see cref="TextWriter" /> that gives every line it is written to the log: the runner and the executor of the
///     application write their progress to a writer.
/// </summary>
internal sealed class LogWriter(ILogger logger) : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public static LogWriter Of<T>(IServiceProvider services)
    {
        return new LogWriter(services.GetRequiredService<ILoggerFactory>().CreateLogger<T>());
    }

    public override void Write(char value)
    {
        // Only whole lines are logged.
    }

    public override void WriteLine(string? value)
    {
        logger.LogInformation("{Line}", value);
    }
}
