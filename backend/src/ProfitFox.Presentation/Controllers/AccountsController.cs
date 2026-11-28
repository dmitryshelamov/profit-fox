using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.Application.Commands.Accounts.AddAccount;
using ProfitFox.Application.Commands.Accounts.DeleteAccount;
using ProfitFox.Application.Commands.Accounts.RenameAccount;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Accounts;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Presentation.Contracts.Accounts;
using ProfitFox.Presentation.Contracts.Errors;

namespace ProfitFox.Presentation.Controllers;

/// <summary>
///     <c>/api/accounts</c> (change add-exchange-accounts, spec exchange-accounts; design.md, decisions 6 and 8): the
///     accounts of Bybit, their addition with the check of the key at Bybit, a new name, the removal and the free money.
///     A refused field or key answers 422 at its field, a key Bybit does not take 422 at <c>apiKey</c> with the reason of
///     Bybit, an exchange that did not answer 503, another refusal of Bybit 502, a secret that does not decrypt 409.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AccountsController(ISender sender) : ControllerBase
{
    [HttpGet(Name = "ListAccounts")]
    [ProducesResponseType<AccountResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountResponse[]>> List(CancellationToken cancellation)
    {
        return (await sender.Send(new GetAccountsQuery(), cancellation)).Select(Of).ToArray();
    }

    [HttpPost(Name = "AddAccount")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AccountResponse>> Add(AddAccountRequest request, CancellationToken cancellation)
    {
        return await Answer<AccountResponse>(async () =>
        {
            ExchangeAccount account = await sender.Send(new AddAccountCommand(request.Name, request.ApiKey, request.Secret), cancellation);
            return CreatedAtRoute("ListAccounts", null, Of(account));
        });
    }

    [HttpPut("{id:guid}", Name = "RenameAccount")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AccountResponse>> Rename(Guid id, RenameAccountRequest request, CancellationToken cancellation)
    {
        return await Answer<AccountResponse>(async () => Of(await sender.Send(new RenameAccountCommand(id, request.Name), cancellation)));
    }

    [HttpDelete("{id:guid}", Name = "DeleteAccount")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellation)
    {
        return await sender.Send(new DeleteAccountCommand(id), cancellation) ? NoContent() : Problem($"There is no account {id}.", statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    ///     The free money of the account, read at Bybit now; nothing is kept.
    /// </summary>
    [HttpGet("{id:guid}/balance", Name = "GetAccountBalance")]
    [ProducesResponseType<AccountBalanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AccountBalanceResponse>> Balance(Guid id, CancellationToken cancellation)
    {
        return await Answer<AccountBalanceResponse>(async () => new AccountBalanceResponse(await sender.Send(new GetAccountBalanceQuery(id), cancellation)));
    }

    private static AccountResponse Of(ExchangeAccount account)
    {
        return new AccountResponse(account.Id, account.Name, account.Uid, account.ApiKey, account.CreatedAt);
    }

    private async Task<ActionResult<T>> Answer<T>(Func<Task<ActionResult<T>>> act)
    {
        try
        {
            return await act();
        }
        catch (AccountRequestException exception)
        {
            return UnprocessableEntity(new ErrorResponse([.. exception.Problems.Select(problem => new RequestError(problem.Code, problem.Reason) { Field = problem.Field })]));
        }
        catch (ExchangeKeyRejectedException exception)
        {
            return UnprocessableEntity(new ErrorResponse([new RequestError("KeyRejected", exception.Message) { Field = "apiKey" }]));
        }
        catch (ExchangeUnavailableException)
        {
            return Problem("Bybit не отвечает: попробуйте ещё раз позже.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (ExchangeException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (SecretUnreadableException)
        {
            return Problem("Секрет аккаунта не читается: удалите аккаунт и добавьте его заново.", statusCode: StatusCodes.Status409Conflict);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }
}
