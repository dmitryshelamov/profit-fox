using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the one set of settings of trading by ideas.</summary>
public interface IIdeaTradingSettingsRepository
{
    /// <summary>Settings never saved read as <see cref="IdeaTradingSettings.Default" />.</summary>
    Task<IdeaTradingSettings> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(IdeaTradingSettings settings, CancellationToken cancellationToken);
}