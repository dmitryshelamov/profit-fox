using MediatR;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;

namespace ProfitFox.Application.ExchangeAccounts.Queries.ListBrokerAccounts;

public sealed class ListBrokerAccountsQueryHandler(IBrokerAccountReader reader) : IRequestHandler<ListBrokerAccountsQuery, IReadOnlyList<BrokerAccountOption>>
{
    /// <exception cref="ValidationFailedException">The token is empty or the broker does not accept it.</exception>
    /// <exception cref="ExchangeUnavailableException">The broker did not answer.</exception>
    public async Task<IReadOnlyList<BrokerAccountOption>> Handle(ListBrokerAccountsQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw TokenError("Token must not be empty.");
        }

        try
        {
            IReadOnlyList<BrokerAccount> accounts = await reader.ListAccountsAsync(request.Token, cancellationToken);

            return [.. accounts.Select(account => new BrokerAccountOption(account, BrokerAccountRules.Problem(account)))];
        }
        catch (ExchangeKeyRejectedException exception)
        {
            throw TokenError(exception.Message);
        }
    }

    private static ValidationFailedException TokenError(string reason)
    {
        return new ValidationFailedException(new Dictionary<string, string> { ["token"] = reason });
    }
}