namespace ProfitFox.Domain.ValueObjects.IdeaAutos;

/// <summary>Who holds a pair when an auto is refused it: the auto itself, another auto, or a manual trade.</summary>
public enum PairHolderKind
{
    SameAuto,
    OtherAuto,
    Manual
}

/// <summary>
///     The open trade that holds the pair: of the auto itself, of another auto (named, and <paramref name="Above" /> when it stands higher in the list)
///     or a manual one on the account (named by <paramref name="AccountName" />), a paper trade or a real one, and since when.
/// </summary>
public sealed record PairHolder(PairHolderKind Kind, string? AutoName, bool Above, bool Paper, string? AccountName, DateTimeOffset Since);