using DisplayBoard.App.ViewModels.Displays;
using DisplayBoard.Core.Display;

namespace DisplayBoard.App.ViewModels;

/// <summary>Tạo ViewModel theo viewId. Thêm view mới: thêm một dòng ở đây + DataTemplate trong App.xaml.</summary>
public sealed class DisplayViewFactory(ClockViewModel clock)
{
    public DisplayViewModelBase Create(string viewId) => viewId switch
    {
        ViewIds.Ranking => new RankingViewModel(clock),
        ViewIds.ProductProgress => new ProductProgressViewModel(clock),
        ViewIds.TopProducts => new TopProductsViewModel(clock),
        ViewIds.NotMet => new NotMetViewModel(clock),
        ViewIds.Notice => new NoticeViewModel(clock),
        ViewIds.Detail => new DetailViewModel(clock),
        ViewIds.MonthProgress => new MonthProgressViewModel(clock),
        ViewIds.Lines => new LinesViewModel(clock),
        ViewIds.Hourly => new HourlyViewModel(clock),
        ViewIds.Defects or ViewIds.Defects4 or ViewIds.Defects2 => new DefectsViewModel(clock, viewId),
        _ => new OverviewViewModel(clock)
    };
}
