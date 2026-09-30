using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

public enum StatusKind
{
    None,
    Met,
    Near,
    NotMet
}

/// <summary>Một dòng nhân viên đã định dạng sẵn để hiển thị.</summary>
public sealed class EmployeeRow(EmployeeDaily e, int index, string departmentColor)
{
    public int Index { get; } = index;
    public bool IsAlternate => Index % 2 == 0;
    public string Rank { get; } = e.Rank.ToString();
    public bool IsTop3 => e.Rank <= 3;
    public string Code { get; } = e.EmployeeCode;
    public string Name { get; } = e.EmployeeName;
    public string Department { get; } = e.Department;
    public string DepartmentColor { get; } = departmentColor;
    public string Shift { get; } = e.Shift ?? "";
    public string Quantity { get; } = Format.Number(e.Quantity);
    public string Target { get; } = Format.Number(e.Target);
    public string Rate { get; } = Format.Percent(e.CompletionRate);
    public string Shortfall { get; } = Format.Signed(e.Shortfall);
    public string? PhotoPath { get; } = e.PhotoPath;
    public string Initials { get; } = GetInitials(e.EmployeeName);

    public StatusKind Status { get; } = e.IsMet switch
    {
        true => StatusKind.Met,
        false when e.CompletionRate >= 90 => StatusKind.Near,
        false => StatusKind.NotMet,
        null => StatusKind.None
    };

    public string StatusText => Status switch
    {
        StatusKind.Met => "Đạt",
        StatusKind.Near or StatusKind.NotMet => "Chậm",
        _ => "—"
    };

    private static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[^2][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }

    public static IReadOnlyList<EmployeeRow> From(IEnumerable<EmployeeDaily> employees, DisplayDataSnapshot snapshot)
    {
        var colors = snapshot.Departments.ToDictionary(d => d.Name, d => d.Color, StringComparer.OrdinalIgnoreCase);
        return employees.Select((e, i) => new EmployeeRow(e, i + 1, colors.GetValueOrDefault(e.Department) ?? "#2E86DE")).ToList();
    }
}
