namespace ProfitFox.Bench.Application.Hypotheses;

/// <summary>
///     Where the hypotheses are, each in a folder named <c>H&lt;NNNN&gt;-&lt;name&gt;</c>.
/// </summary>
public interface IHypothesisStore
{
    /// <summary>
    ///     The folders of the hypotheses of the bench, in no particular order.
    /// </summary>
    IReadOnlyList<string> Folders();

    /// <summary>
    ///     The text of the hypothesis in <paramref name="folder" />, with <c>\n</c> line ends: the hash of the hypothesis is
    ///     the hash of this text.
    /// </summary>
    /// <exception cref="ProfitFox.Bench.Domain.Hypotheses.HypothesisException">There is no hypothesis there.</exception>
    string Text(string folder);
}
