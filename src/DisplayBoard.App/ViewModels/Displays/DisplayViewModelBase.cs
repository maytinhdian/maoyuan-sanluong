using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels.Displays;

/// <summary>ViewModel của một màn hiển thị. Dùng chung cho Preview và fullscreen.</summary>
public abstract partial class DisplayViewModelBase(ClockViewModel clock) : ObservableObject
{
    public ClockViewModel Clock { get; } = clock;

    public abstract string ViewId { get; }
    public abstract string Title { get; }
    public abstract string IconKind { get; }

    /// <summary>Ghi chú khi dữ liệu không phải của hôm nay.</summary>
    [ObservableProperty] private string? _dataDateNote;

    public void Update(DisplayDataSnapshot snapshot)
    {
        DataDateNote = snapshot.Summary.IsToday || snapshot.Summary.EmployeeCount == 0
            ? null
            : $"Dữ liệu ngày {snapshot.Summary.Date:dd/MM/yyyy}";
        OnUpdate(snapshot);
    }

    protected abstract void OnUpdate(DisplayDataSnapshot snapshot);

    /// <summary>Gọi khi view được đưa lên màn hình (vd để lật trang lại từ đầu).</summary>
    public virtual void OnActivated() { }

    public virtual void OnDeactivated() { }
}
