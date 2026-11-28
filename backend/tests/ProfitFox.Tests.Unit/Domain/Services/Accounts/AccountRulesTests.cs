using ProfitFox.Domain.Services.Accounts;
using ProfitFox.Domain.ValueObjects.Accounts;

namespace ProfitFox.Tests.Unit.Domain.Services.Accounts;

/// <summary>
///     Change add-exchange-accounts, task 3.1: spec exchange-accounts «Добавление аккаунта» — the three fields are required,
///     spaces at the edges do not count, and the name is at most 60 characters; every problem at once, each at its field.
/// </summary>
public sealed class AccountRulesTests
{
    [Test]
    public void A_full_account_has_no_problem()
    {
        Assert.That(AccountRules.Check("Основной", "key", "secret"), Is.Empty);
    }

    // Spec «Пустой секрет».
    [Test]
    public void An_account_without_a_secret_is_refused_at_the_secret()
    {
        Assert.That(AccountRules.Check("Основной", "key", "   "), Is.EqualTo(new[] { new AccountProblem("Required", "нужен секрет", "secret") }));
    }

    [Test]
    public void Every_empty_field_is_told_at_once_in_the_order_of_the_dialog()
    {
        Assert.That(
            AccountRules.Check(" ", null, ""),
            Is.EqualTo(new[]
            {
                new AccountProblem("Required", "нужно имя", "name"),
                new AccountProblem("Required", "нужен ключ API", "apiKey"),
                new AccountProblem("Required", "нужен секрет", "secret")
            }));
    }

    [Test]
    public void A_name_is_at_most_60_characters_without_the_spaces_at_its_edges()
    {
        Assert.That(AccountRules.CheckName($"  {new string('а', 60)}  "), Is.Null);
        Assert.That(AccountRules.CheckName(new string('а', 61)), Is.EqualTo(new AccountProblem("InvalidName", "имя — не длиннее 60 символов", "name")));
    }
}
