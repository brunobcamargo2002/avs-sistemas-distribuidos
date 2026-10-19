namespace orders.Models;

public sealed record PromotionInterestEvent(
    Guid EventId,
    string Email,
    IReadOnlyList<string> Categories,
    DateTimeOffset OccurredAt);