using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels;

/// <summary>Một người được nhập liệu qua trình duyệt (tab Nhập liệu).</summary>
public sealed partial class EntryUserViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _pin = "";

    /// <summary>Tên chuyền cách nhau bởi dấu chấm phẩy. Trống = tất cả chuyền.</summary>
    [ObservableProperty] private string _linesText = "";

    /// <summary>Quản lý: vào được trang /quan-ly (sửa ngày cũ, kế hoạch, mục tiêu, danh mục, xuất/nhập Excel).</summary>
    [ObservableProperty] private bool _manager;

    public static EntryUserViewModel From(EntryUser user) => new()
    {
        Name = user.Name,
        Pin = user.Pin,
        LinesText = string.Join("; ", user.Lines),
        Manager = user.Manager
    };

    public EntryUser ToUser() => new()
    {
        Name = Name.Trim(),
        Pin = Pin.Trim(),
        Lines = LinesText.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        Manager = Manager
    };
}
