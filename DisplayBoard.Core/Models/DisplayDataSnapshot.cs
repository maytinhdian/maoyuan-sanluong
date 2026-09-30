namespace DisplayBoard.Core.Models;

/// <summary>Immutable data shared by every view. Replaced atomically on reload.</summary>
public sealed record DisplayDataSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProductionRecord> Records,
    ProductionSummary Summary);
