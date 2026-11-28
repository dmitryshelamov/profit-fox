namespace ProfitFox.Presentation.Contracts.Accounts;

/// <summary>
///     An account of Bybit as the API gives it (change add-exchange-accounts, design.md, decision 8): its id, name, the UID
///     of Bybit, the API key in full and when it was added. There is no field of the secret.
/// </summary>
public sealed record AccountResponse(Guid Id, string Name, long Uid, string ApiKey, DateTimeOffset CreatedAt);

/// <summary>
///     What an account is added with: its name, the API key and its secret.
/// </summary>
public sealed record AddAccountRequest(string? Name, string? ApiKey, string? Secret);

/// <summary>
///     The new name of an account.
/// </summary>
public sealed record RenameAccountRequest(string? Name);

/// <summary>
///     The money of the unified account free for a new position, in USDT, read at Bybit now (design.md, decision 7).
/// </summary>
public sealed record AccountBalanceResponse(decimal Free);
