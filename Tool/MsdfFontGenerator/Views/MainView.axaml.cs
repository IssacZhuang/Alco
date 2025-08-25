using Avalonia.Controls;
using MsdfFontGenerator.ViewModels;

namespace MsdfFontGenerator.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.StorageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        }
    }
}
