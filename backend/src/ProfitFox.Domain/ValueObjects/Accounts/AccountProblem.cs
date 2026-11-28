namespace ProfitFox.Domain.ValueObjects.Accounts;

/// <summary>
///     Why an account was refused and at which field of the request (spec exchange-accounts «Добавление аккаунта»):
///     <c>name</c>, <c>apiKey</c> or <c>secret</c>.
/// </summary>
public sealed record AccountProblem(string Code, string Reason, string Field);
