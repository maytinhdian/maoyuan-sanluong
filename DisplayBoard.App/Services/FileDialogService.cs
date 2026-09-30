using Microsoft.Win32;

namespace DisplayBoard.App.Services;

/// <summary>Keeps file dialogs out of view models.</summary>
public interface IFileDialogService
{
    string? PickExcelFile(string? initialPath);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickExcelFile(string? initialPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file Excel dữ liệu",
            Filter = "Excel (*.xlsx)|*.xlsx",
            CheckFileExists = true,
        };

        if (!string.IsNullOrEmpty(initialPath))
        {
            dialog.InitialDirectory = System.IO.Path.GetDirectoryName(initialPath);
            dialog.FileName = System.IO.Path.GetFileName(initialPath);
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
