using ProfitFox.Application.Exceptions;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Hypotheses;
using ProfitFox.Domain.ValueObjects.Hypotheses;

namespace ProfitFox.Application.Commands.Runs.Base;

/// <summary>
///     The version of a hypothesis a run is of, read and checked before anything else.
/// </summary>
internal static class RunHypotheses
{
    /// <summary>
    ///     The version <paramref name="version" /> of the hypothesis <paramref name="id" />, or its latest.
    /// </summary>
    /// <exception cref="HypothesisNotFoundException">The hypothesis or its version is not there.</exception>
    /// <exception cref="HypothesisException">The text of the version cannot be run.</exception>
    public static async Task<Hypothesis> ReadAsync(IHypothesisRepository hypotheses, string id, int? version, CancellationToken cancellationToken)
    {
        StoredHypothesis stored = await hypotheses.FindAsync(id, version, cancellationToken)
                                  ?? throw new HypothesisNotFoundException(id, version);
        return HypothesisReader.Parse(stored);
    }
}
