using MediatR;

using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Repositories;

namespace ProfitFox.Application.Bots.Queries.GetBot;

public sealed class GetBotQueryHandler(IStrategyLibraryRepository repository, IExchangeAccountRepository accounts, ILiveBotRepository live) : IRequestHandler<GetBotQuery, GetBotResult?>
{
    public async Task<GetBotResult?> Handle(GetBotQuery request, CancellationToken cancellationToken)
    {
        Bot? bot = await repository.GetBotAsync(request.Id, cancellationToken);

        if (bot is null)
        {
            return null;
        }

        return new GetBotResult(
            bot,
            bot.ExchangeAccountId is { } account ? await accounts.GetAsync(account, cancellationToken) : null,
            await live.GetAsync(bot.Id, cancellationToken));
    }
}