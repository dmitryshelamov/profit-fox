using ProfitFox.Bench.Application.Hypotheses;
using ProfitFox.Bench.Domain.Hypotheses;

namespace ProfitFox.Bench.Infrastructure.Hypotheses;

/// <summary>
///     The hypotheses as files (spec hypothesis-bench): <c>hypothesis.json</c> in a folder of <paramref name="root" />,
///     usually <c>bench/hypotheses/</c>. A run may name a folder anywhere.
/// </summary>
public sealed class HypothesisFiles(string root) : IHypothesisStore
{
    public const string FileName = "hypothesis.json";

    public IReadOnlyList<string> Folders()
    {
        return Directory.Exists(root) ? Directory.GetDirectories(root) : [];
    }

    public string Text(string folder)
    {
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            throw new HypothesisException(folder, [$"there is no {FileName}"]);
        }

        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
