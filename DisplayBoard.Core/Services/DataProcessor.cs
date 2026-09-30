using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;

namespace DisplayBoard.Core.Services;

/// <summary>Computes totals, completion rates and rankings from raw records.</summary>
public sealed class DataProcessor(TimeProvider timeProvider) : IDataProcessor
{
    public DataProcessor()
        : this(TimeProvider.System)
    {
    }

    public DisplayDataSnapshot BuildSnapshot(IReadOnlyList<ProductionRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return new DisplayDataSnapshot(timeProvider.GetLocalNow(), records, Summarize(records));
    }

    public static ProductionSummary Summarize(IReadOnlyList<ProductionRecord> records)
    {
        if (records.Count == 0)
        {
            return ProductionSummary.Empty;
        }

        // An employee can appear on several rows (several days); aggregate per employee code.
        var employees = records
            .GroupBy(r => r.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var latest = g.MaxBy(r => r.Date)!;
                var quantity = g.Sum(r => r.Quantity);
                decimal? target = g.Any(r => r.Target.HasValue) ? g.Sum(r => r.Target ?? 0) : null;
                return (latest.EmployeeCode, latest.EmployeeName, latest.Department, Quantity: quantity, Target: target);
            })
            .OrderByDescending(e => e.Quantity)
            .ThenBy(e => e.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranking = employees
            .Select((e, i) => new EmployeeResult(
                i + 1,
                e.EmployeeCode,
                e.EmployeeName,
                e.Department,
                e.Quantity,
                e.Target,
                e.Target is > 0 ? CompletionRate(e.Quantity, e.Target.Value) : null))
            .ToList();

        var departments = employees
            .GroupBy(e => e.Department, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var quantity = g.Sum(e => e.Quantity);
                var target = g.Sum(e => e.Target ?? 0);
                return new DepartmentSummary(g.First().Department, g.Count(), quantity, target, CompletionRate(quantity, target));
            })
            .OrderByDescending(d => d.TotalQuantity)
            .ToList();

        var totalQuantity = employees.Sum(e => e.Quantity);
        var totalTarget = employees.Sum(e => e.Target ?? 0);

        return new ProductionSummary(
            TotalEmployees: employees.Count,
            TotalQuantity: totalQuantity,
            TotalTarget: totalTarget,
            CompletionRate: CompletionRate(totalQuantity, totalTarget),
            EmployeesMetTarget: ranking.Count(r => r.MetTarget == true),
            EmployeesBelowTarget: ranking.Count(r => r.MetTarget == false),
            Departments: departments,
            Ranking: ranking);
    }

    /// <summary>Spec: <c>TotalTarget &gt; 0 ? TotalQuantity / TotalTarget * 100 : 0</c>.</summary>
    public static decimal CompletionRate(decimal quantity, decimal target) =>
        target > 0 ? Math.Round(quantity / target * 100, 1) : 0;
}
