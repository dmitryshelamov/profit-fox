using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Domain.Services.LiveTrading;

/// <summary>Whether the free money of an account pays for a bot that starts next to the other working bots of the account.</summary>
public static class StartFunds
{
    /// <param name="free">The free money of the account in the quote asset of the instrument.</param>
    /// <param name="balance"></param>
    /// <param name="others">The working bots of the account: what they have invested is already off the free money.</param>
    /// <param name="pendingEntries">The margins of the entries of trades by ideas that wait to be filled: the money is not off the free money yet.</param>
    public static bool Enough(decimal free, decimal balance, IReadOnlyCollection<BotFunds> others, IReadOnlyCollection<decimal>? pendingEntries = null)
    {
        return free >= balance + others.Sum(bot => Math.Max(bot.Balance - bot.Invested, 0m)) + (pendingEntries?.Sum() ?? 0m);
    }
}