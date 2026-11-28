namespace ProfitFox.Presentation.Contracts.Accounts;

/// <summary>
///     An account of Bybit as the API gives it (change add-exchange-accounts, design.md, decisions 8 and 11): its id, name, the
///     UID of Bybit, the API key in full, the margin mode last read — as Bybit names it, null when none — and when it was
///     added. There is no field of the secret.
/// </summary>
public sealed record AccountResponse(Guid Id, string Name, long Uid, string ApiKey, string? MarginMode, DateTimeOffset CreatedAt);

/// <summary>
///     What an account is added with: its name, the API key and its secret.
/// </summary>
public sealed record AddAccountRequest(string? Name, string? ApiKey, string? Secret);

/// <summary>
///     The new name of an account.
/// </summary>
public sealed record RenameAccountRequest(string? Name);

/// <summary>
///     The money of the unified account free for a new position, in USDT, and its margin mode, both read at Bybit now
///     (design.md, decisions 7 and 11).
/// </summary>
public sealed record AccountBalanceResponse(decimal Free, string? MarginMode);
