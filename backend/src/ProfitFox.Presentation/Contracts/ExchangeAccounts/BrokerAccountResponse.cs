using ProfitFox.Application.ExchangeAccounts;
using ProfitFox.Application.ExchangeAccounts.Queries.ListBrokerAccounts;

namespace ProfitFox.Presentation.Contracts.ExchangeAccounts;

/// <summary>
///     An account a T-Invest token opens: <c>type</c> is <c>brokerage</c>, <c>iis</c> or <c>other</c>, <c>status</c> <c>new</c>, <c>open</c> or
///     <c>closed</c>, <c>access</c> <c>full</c>, <c>read_only</c> or <c>none</c>; <c>problem</c> is why it cannot be added (<c>not_open</c>,
///     <c>not_full_access</c>, <c>unsupported_type</c>), <c>null</c> when it can.
/// </summary>
public sealed record BrokerAccountResponse(string Id, string Name, string Type, string Status, string Access, string? Problem)
{
    public static BrokerAccountResponse Of(BrokerAccountOption option)
    {
        return new BrokerAccountResponse(
            option.Account.Id,
            option.Account.Name,
            option.Account.Type.ToCode(),
            option.Account.Status.ToCode(),
            option.Account.Access.ToCode(),
            option.Problem);
    }
}