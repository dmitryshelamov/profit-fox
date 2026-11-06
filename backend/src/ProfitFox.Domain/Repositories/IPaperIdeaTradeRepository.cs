using ProfitFox.Domain.Entities.IdeaAutos;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Reads the paper trades of the autos. The scanner writes them with the ideas of its minute (<see cref="IIdeaRepository" />); nothing else does.
/// </summary>
public interface IPaperIdeaTradeRepository
{
    /// <summary>The paper trades that wait for their entry or are open.</summary>
    Task<IReadOnlyList<PaperIdeaTrade>> GetOpenAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PaperIdeaTrade>> GetAllAsync(CancellationToken cancellationToken);
}