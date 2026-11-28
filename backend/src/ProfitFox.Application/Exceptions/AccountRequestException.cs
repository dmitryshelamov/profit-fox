using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Application.Exceptions;

/// <summary>
///     A request about an account was refused with every problem, each at its field (spec exchange-accounts «Добавление
///     аккаунта»); nothing was written.
/// </summary>
public sealed class AccountRequestException(IReadOnlyList<AccountProblem> problems)
    : Exception(string.Join("; ", problems.Select(problem => problem.Reason)))
{
    public IReadOnlyList<AccountProblem> Problems { get; } = problems;
}
