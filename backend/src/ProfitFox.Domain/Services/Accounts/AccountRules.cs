using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Domain.Services.Accounts;

/// <summary>
///     The rules of the fields of an account (spec exchange-accounts «Добавление аккаунта», «Переименование аккаунта»): the
///     name, the API key and the secret are required, spaces at the edges do not count, and the name is at most
///     <see cref="MaxName" /> characters. Every problem is told at once, each at its field; none needs the exchange.
/// </summary>
public static class AccountRules
{
    public const int MaxName = 60;

    /// <summary>
    ///     The problems of a new account.
    /// </summary>
    public static List<AccountProblem> Check(string? name, string? apiKey, string? secret)
    {
        List<AccountProblem> problems = [];
        if (CheckName(name) is { } problem)
        {
            problems.Add(problem);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            problems.Add(new AccountProblem("Required", "нужен ключ API", "apiKey"));
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            problems.Add(new AccountProblem("Required", "нужен секрет", "secret"));
        }

        return problems;
    }

    /// <summary>
    ///     The problem of a name, null for a good one.
    /// </summary>
    public static AccountProblem? CheckName(string? name)
    {
        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return new AccountProblem("Required", "нужно имя", "name");
        }

        return trimmed.Length > MaxName ? new AccountProblem("InvalidName", $"имя — не длиннее {MaxName} символов", "name") : null;
    }

    /// <summary>
    ///     The refusal of a name another account has.
    /// </summary>
    public static AccountProblem TakenName(string name)
    {
        return new AccountProblem("TakenName", $"аккаунт «{name}» уже есть", "name");
    }

    /// <summary>
    ///     The refusal of a key of an account of Bybit already added (spec exchange-accounts «Один аккаунт на UID»), with the
    ///     name it was added under.
    /// </summary>
    public static AccountProblem TakenAccount(string name)
    {
        return new AccountProblem("TakenAccount", $"этот аккаунт Bybit уже добавлен под именем «{name}»", "apiKey");
    }
}
