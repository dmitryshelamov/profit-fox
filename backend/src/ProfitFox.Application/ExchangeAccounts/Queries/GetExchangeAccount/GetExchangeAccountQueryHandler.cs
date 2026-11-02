using MediatR;

using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccount;

public sealed class GetExchangeAccountQueryHandler(IExchangeAccountRepository accounts, IStrategyLibraryRepository library)
    : IRequestHandler<GetExchangeAccountQuery, GetExchangeAccountResult?>
{
    public async Task<GetExchangeAccountResult?> Handle(GetExchangeAccountQuery request, CancellationToken cancellationToken)
    {
        ExchangeAccount? account = await accounts.GetAsync(request.Id, cancellationToken);

        if (account is null)
        {
            return null;
        }

        IReadOnlyList<Bot> bots = await library.GetBotsAsync(cancellationToken);

        return new GetExchangeAccountResult(account, [.. bots.Where(bot => bot.ExchangeAccountId == account.Id)]);
    }
}