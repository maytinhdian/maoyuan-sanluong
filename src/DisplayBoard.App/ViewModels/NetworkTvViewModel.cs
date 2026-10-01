using CommunityToolkit.Mvvm.ComponentModel;
using DisplayBoard.Core.Display;
using DisplayBoard.Core.Interfaces;
using DisplayBoard.Core.Models;

namespace DisplayBoard.App.ViewModels;

/// <summary>Một TV xem qua mạng LAN: tên, địa chỉ để mở trên TV và nội dung chiếu.</summary>
public sealed partial class NetworkTvViewModel : ObservableObject
{
    public NetworkTvViewModel(int number, IEnumerable<IDisplayViewDefinition> views)
    {
        Number = number;
        _name = $"TV{number}";
        Content = new ScreenSettingsViewModel($"TV{number}", views);
    }

    /// <summary>Số trong địa chỉ /tv/{Number}; không đổi khi xoá TV khác nên TV đã mở vẫn đúng.</summary>
    public int Number { get; }

    /// <summary>Danh sách nội dung (dùng chung trình chỉnh với chiếu nối dây).</summary>
    public ScreenSettingsViewModel Content { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string? _url;
    [ObservableProperty] private int _viewers;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"TV{Number}" : Name.Trim();

    public string ViewersText => Viewers switch
    {
        0 => "Chưa có TV mở",
        1 => "Đang chiếu",
        _ => $"Đang chiếu trên {Viewers} màn hình"
    };

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnViewersChanged(int value) => OnPropertyChanged(nameof(ViewersText));

    public void Load(NetworkScreen screen)
    {
        Name = string.IsNullOrWhiteSpace(screen.Name) ? $"TV{Number}" : screen.Name;
        Content.Load(new ScreenAssignment { Playlist = screen.Playlist }, [], ViewIds.Overview);
    }

    public NetworkScreen ToScreen() => new()
    {
        Number = Number,
        Name = DisplayName,
        Playlist = Content.ToAssignment().Playlist
    };
}
