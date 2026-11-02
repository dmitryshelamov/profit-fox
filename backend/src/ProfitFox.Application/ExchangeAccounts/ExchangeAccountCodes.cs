using ProfitFox.Domain.Entities.ExchangeAccounts.Enums;

namespace ProfitFox.Application.ExchangeAccounts;

/// <summary>Codes of exchange environments in the API and in stored rows: <c>mainnet</c>, <c>testnet</c>.</summary>
public static class ExchangeAccountCodes
{
    private static readonly Dictionary<string, ExchangeEnvironment> EnvironmentsByCode = new Dictionary<string, ExchangeEnvironment>(StringComparer.Ordinal)
    {
        ["mainnet"] = ExchangeEnvironment.Mainnet,
        ["testnet"] = ExchangeEnvironment.Testnet
    };

    public static bool TryParseEnvironment(string? code, out ExchangeEnvironment environment)
    {
        return EnvironmentsByCode.TryGetValue(code ?? string.Empty, out environment);
    }

    public static string ToCode(this ExchangeEnvironment environment)
    {
        return environment == ExchangeEnvironment.Mainnet ? "mainnet" : "testnet";
    }
}