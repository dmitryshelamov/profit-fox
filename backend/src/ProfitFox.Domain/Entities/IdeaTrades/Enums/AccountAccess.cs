namespace ProfitFox.Domain.Entities.IdeaTrades.Enums;

/// <summary>What the exchange account of a trade lets it do in the market category of the instrument.</summary>
public enum AccountAccess
{
    Ready,
    NoKey,
    CategoryNotTraded,
    HedgeMode
}