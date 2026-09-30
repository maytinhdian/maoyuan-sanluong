using System.Windows;
using DisplayBoard.App.ViewModels;

namespace DisplayBoard.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
