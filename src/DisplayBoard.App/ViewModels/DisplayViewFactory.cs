using DisplayBoard.App.ViewModels.Displays;
using DisplayBoard.Core.Display;

namespace DisplayBoard.App.ViewModels;

/// <summary>Tạo ViewModel theo viewId. Thêm view mới: thêm một dòng ở đây + DataTemplate trong App.xaml.</summary>
public sealed class DisplayViewFactory(ClockViewModel clock)
{
    public DisplayViewModelBase Create(string viewId) => viewId switch
    {
        ViewIds.Ranking => new RankingViewModel(clock),
        ViewIds.DepartmentProgress => new DepartmentProgressViewModel(clock),
        ViewIds.TopPerformers => new TopPerformersViewModel(clock),
        ViewIds.NotMet => new NotMetViewModel(clock),
        ViewIds.Notice => new NoticeViewModel(clock),
        ViewIds.Detail => new DetailViewModel(clock),
        ViewIds.Trend => new TrendViewModel(clock),
        _ => new OverviewViewModel(clock)
    };
}
