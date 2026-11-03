using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.ValueObjects.ExchangeAccounts;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores exchange accounts; only <see cref="GetSecretAsync" /> reads the encrypted secret.</summary>
public interface IExchangeAccountRepository
{
    /// <summary>Returns <c>false</c> and saves nothing when an account of the same uid is stored.</summary>
    Task<bool> AddAsync(ExchangeAccount account, EncryptedSecret secret, CancellationToken cancellationToken);

    Task<ExchangeAccount?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every account, by name.</summary>
    Task<IReadOnlyList<ExchangeAccount>> GetAllAsync(CancellationToken cancellationToken);

    Task<EncryptedSecret?> GetSecretAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Returns <c>false</c> when there is no such account.</summary>
    Task<bool> RenameAsync(Guid id, string name, CancellationToken cancellationToken);

    /// <summary>Returns <c>false</c> when there is no such account; an account a bot refers to stays.</summary>
    /// <exception cref="Exceptions.ExchangeAccountInUseException">A bot refers to the account.</exception>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}