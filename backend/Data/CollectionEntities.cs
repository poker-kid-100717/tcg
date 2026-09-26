using Microsoft.AspNetCore.Identity;

namespace PokemonTCG.API.Data;

/// <summary>A set in the local catalog, refreshed by each price snapshot so set pages don't wait on the card database.</summary>
public class CardSet
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Series { get; set; } = "";
    public int PrintedTotal { get; set; }
    public int Total { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? LogoUrl { get; set; }
    public string? SymbolUrl { get; set; }
    /// <summary>The matching TCGplayer group, once the TCGplayer catalog has been matched.</summary>
    public int? TcgplayerGroupId { get; set; }
}

/// <summary>
/// The newest known price of every printing, including cheap ones that price_snapshots skips to stay small.
/// One row per card and printing, overwritten by each snapshot.
/// </summary>
public class LatestPrice
{
    public string CardId { get; set; } = "";
    public string Variant { get; set; } = "";
    public decimal? Market { get; set; }
    public decimal? Low { get; set; }
    public decimal? Mid { get; set; }
    public decimal? High { get; set; }
    public DateOnly UpdatedOn { get; set; }
}

/// <summary>
/// A collector. Accounts start as guests (no email or password, kept by a long-lived cookie) so anyone can start
/// a collection immediately; adding an email and password later keeps the same account and everything in it.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public bool IsGuest { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>TCGplayer's condition grades.</summary>
public enum CardCondition
{
    NearMint,
    LightlyPlayed,
    ModeratelyPlayed,
    HeavilyPlayed,
    Damaged,
}

/// <summary>Copies of one printing of one card in one condition that a collector owns.</summary>
public class CollectionItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CardId { get; set; } = "";
    public string Variant { get; set; } = "";
    public CardCondition Condition { get; set; }
    public int Quantity { get; set; }
    /// <summary>What the collector paid per copy, if they recorded it (average when copies were added at different prices).</summary>
    public decimal? CostEach { get; set; }
    public DateOnly? AcquiredOn { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A card the collector wants, with the price they'd buy at.</summary>
/// <summary>Which version of a set a collector is chasing.</summary>
public enum SetGoalKind
{
    /// <summary>One of every card up to the printed total ("1/165" to "165/165").</summary>
    MainSet,
    /// <summary>Every card, secret rares included.</summary>
    FullSet,
    /// <summary>Every card in every printing it comes in (normal, reverse holo, holo…), secret rares included.</summary>
    MasterSet,
}

/// <summary>A set a collector has decided to complete, and to what standard.</summary>
public class SetGoal
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string SetId { get; set; } = "";
    public SetGoalKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class WishlistItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CardId { get; set; } = "";
    /// <summary>A specific printing, or null for whichever is cheapest.</summary>
    public string? Variant { get; set; }
    public decimal? TargetPrice { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
