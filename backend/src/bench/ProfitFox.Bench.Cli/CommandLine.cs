namespace ProfitFox.Bench.Cli;

/// <summary>
///     A command line split into the words of the command, its arguments and its options. <c>--wait</c> is the only flag; every
///     other option takes a value.
/// </summary>
internal sealed class CommandLine
{
    private static readonly HashSet<string> Flags = ["--wait"];
    private static readonly HashSet<string> Valued = ["--bench", "--version", "--period"];

    private readonly Dictionary<string, string> _options = [];

    private CommandLine(IReadOnlyList<string> words)
    {
        Words = words;
    }

    /// <summary>
    ///     The words that are not options, in order: the command and its arguments.
    /// </summary>
    public IReadOnlyList<string> Words { get; }

    public bool Has(string option)
    {
        return _options.ContainsKey(option);
    }

    public string? Value(string option)
    {
        return _options.GetValueOrDefault(option);
    }

    /// <summary>
    ///     The value of <c>--version</c>, null when it is missing.
    /// </summary>
    public int? Version()
    {
        string? text = Value("--version");
        if (text is null)
        {
            return null;
        }

        return int.TryParse(text, out int version) && version > 0 ? version : throw new UsageException($"--version wants a positive number, not '{text}'.");
    }

    /// <summary>
    ///     Refuses every option the command does not take.
    /// </summary>
    public void Allow(params string[] options)
    {
        foreach (string option in _options.Keys.Where(option => option != "--bench" && !options.Contains(option)))
        {
            throw new UsageException($"{Words[0]} does not take {option}.");
        }
    }

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        List<string> words = [];
        var line = new CommandLine(words);
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (Flags.Contains(arg))
            {
                line._options[arg] = "";
            }
            else if (Valued.Contains(arg))
            {
                line._options[arg] = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : throw new UsageException($"{arg} wants a value.");
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"unknown option {arg}.");
            }
            else
            {
                words.Add(arg);
            }
        }

        return line.Words.Count == 0 ? throw new UsageException("no command.") : line;
    }
}
