namespace DisplayBoard.Core.Models;

/// <summary>Immutable data every view renders from. Replaced atomically on each reload.</summary>
public sealed record DisplayDataSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProductionRecord> Records,
    ProductionSummary Summary)
{
    public static DisplayDataSnapshot Empty { get; } = new(DateTimeOffset.MinValue, [], ProductionSummary.Empty);
}
