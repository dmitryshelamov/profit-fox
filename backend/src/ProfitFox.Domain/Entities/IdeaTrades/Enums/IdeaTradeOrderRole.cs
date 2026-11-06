namespace ProfitFox.Domain.Entities.IdeaTrades.Enums;

/// <summary>What an order of a trade by an idea is for. The stop and the target are made by the exchange and kept apart.</summary>
public enum IdeaTradeOrderRole
{
    Entry,

    /// <summary>The market close the owner asked for.</summary>
    Close
}