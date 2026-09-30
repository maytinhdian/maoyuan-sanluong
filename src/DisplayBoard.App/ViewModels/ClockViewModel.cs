using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DisplayBoard.App.ViewModels;

/// <summary>Đồng hồ dùng chung cho header của mọi view.</summary>
public sealed partial class ClockViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string _time = "";
    [ObservableProperty] private string _date = "";

    public ClockViewModel()
    {
        Tick();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        Time = now.ToString("HH:mm");
        Date = $"{Format.DayName(DateOnly.FromDateTime(now))}, {now:dd/MM/yyyy}";
    }
}
