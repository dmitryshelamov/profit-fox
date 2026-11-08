using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.ExchangeAccounts.Commands.CreateExchangeAccount;
using ProfitFox.Application.ExchangeAccounts.Commands.DeleteExchangeAccount;
using ProfitFox.Application.ExchangeAccounts.Commands.RenameExchangeAccount;
using ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccount;
using ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccountBalance;
using ProfitFox.Application.ExchangeAccounts.Queries.GetExchangeAccounts;
using ProfitFox.Application.ExchangeAccounts.Queries.ListBrokerAccounts;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;
using ProfitFox.Presentation.Contracts.ExchangeAccounts;
using ProfitFox.Presentation.Contracts.Strategies;

namespace ProfitFox.Presentation.Controllers;

[ApiController]
[Route("api/exchange-accounts")]
public sealed class ExchangeAccountsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ExchangeAccountResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<ExchangeAccount> accounts = await sender.Send(new GetExchangeAccountsQuery(), cancellationToken);

        return Ok(accounts.Select(ExchangeAccountResponse.Of));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ExchangeAccountDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        GetExchangeAccountResult? account = await sender.Send(new GetExchangeAccountQuery(id), cancellationToken);

        return account is null ? NotFound() : Ok(ExchangeAccountDetailsResponse.Of(account));
    }

    /// <summary>
    ///     Asks the exchange before saving: Bybit about the key (a key it does not accept, that cannot trade or can withdraw is a 400 on
    ///     <c>apiKey</c>), T-Invest about the accounts of the token (a token it does not accept is a 400 on <c>token</c>, an account that
    ///     cannot be added a 400 on <c>accountId</c>); another refusal of the exchange is a 502 with its reason, no answer a 503.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(ExchangeAccountBody body, CancellationToken cancellationToken)
    {
        try
        {
            if (!ExchangeCodes.TryParse(body.Exchange, out Exchange exchange))
            {
                throw new ValidationFailedException(new Dictionary<string, string> { ["exchange"] = "Exchange must be bybit or tinvest." });
            }

            Guid id = await sender.Send(new CreateExchangeAccountCommand(body.Name, exchange, body.ApiKey, body.ApiSecret, body.Token, body.AccountId), cancellationToken);

            return CreatedAtAction(nameof(Get), new { id }, new CreatedResponse(id));
        }
        catch (Exception exception) when (ExchangeAccountProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>The accounts a T-Invest token opens, each with whether it can be added; a token T-Invest does not accept is a 400 on <c>token</c>.</summary>
    [HttpPost("tinvest/accounts")]
    [ProducesResponseType<IReadOnlyList<BrokerAccountResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> BrokerAccounts(BrokerTokenBody body, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<BrokerAccountOption> accounts = await sender.Send(new ListBrokerAccountsQuery(body.Token), cancellationToken);

            return Ok(accounts.Select(BrokerAccountResponse.Of));
        }
        catch (Exception exception) when (ExchangeAccountProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rename(Guid id, RenameExchangeAccountBody body, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new RenameExchangeAccountCommand(id, body.Name), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (ExchangeAccountProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>An account bots trade with is a 409 whose <c>botNames</c> lists them.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new DeleteExchangeAccountCommand(id), cancellationToken);

            return NoContent();
        }
        catch (Exception exception) when (ExchangeAccountProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }

    /// <summary>Read from the exchange on each call and never stored, in <c>currency</c>; a key or a token the exchange no longer accepts is a 409.</summary>
    [HttpGet("{id:guid}/balance")]
    [ProducesResponseType<AccountBalanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Balance(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            AccountBalance? balance = await sender.Send(new GetExchangeAccountBalanceQuery(id), cancellationToken);

            return balance is null ? NotFound() : Ok(AccountBalanceResponse.Of(balance));
        }
        catch (Exception exception) when (ExchangeAccountProblems.Of(exception) is { } problem)
        {
            return problem;
        }
    }
}