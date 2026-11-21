namespace ProfitFox.Application.Hypotheses;

/// <summary>
///     Where the hypotheses are: the texts of their versions, which are only ever added (spec hypothesis-bench
///     «Заморозка гипотезы»). A hypothesis is its versions, there is no record of it apart from them.
/// </summary>
public interface IHypothesisStore
{
    /// <summary>
    ///     The latest version of every hypothesis, in the order of the ids.
    /// </summary>
    Task<IReadOnlyList<StoredHypothesis>> Latest(CancellationToken cancellation);

    /// <summary>
    ///     The version <paramref name="version" /> of the hypothesis <paramref name="id" />, or its latest when the version is
    ///     null; null when there is none.
    /// </summary>
    Task<StoredHypothesis?> Find(string id, int? version, CancellationToken cancellation);

    /// <summary>
    ///     Every version of every hypothesis with its hash, without the texts.
    /// </summary>
    Task<IReadOnlyList<HypothesisKey>> Keys(CancellationToken cancellation);

    /// <summary>
    ///     Keeps the text of the hypothesis <paramref name="id" /> with the hash <paramref name="hash" />: the first version for
    ///     an id the store does not know, the next version for a hash its versions do not have, nothing for a hash that one of
    ///     them has. Gives the version the text has and whether it was made now.
    /// </summary>
    Task<(int Version, bool Created)> Add(string id, string hash, string text, CancellationToken cancellation);
}
