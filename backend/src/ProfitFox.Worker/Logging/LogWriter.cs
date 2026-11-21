using System.Text;

namespace ProfitFox.Worker.Logging;

/// <summary>
///     A <see cref="TextWriter" /> that gives every line it is written to the log: the wait for the schema of the database
///     writes to a writer.
/// </summary>
internal sealed class LogWriter(ILogger logger) : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        // Only whole lines are logged.
    }

    public override void WriteLine(string? value)
    {
        logger.LogInformation("{Line}", value);
    }
}
