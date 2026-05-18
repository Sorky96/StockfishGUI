using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MoveMentorChess.App.ViewModels;

namespace MoveMentorChess.App.Views;

public partial class OpeningCoverageWindow : Window
{
    public OpeningCoverageWindow()
        : this(CreateDefaultViewModel())
    {
    }

    public OpeningCoverageWindow(OpeningCoverageWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private static OpeningCoverageWindowViewModel CreateDefaultViewModel()
    {
        DefaultMainWindowDialogDataService dataService = new(() => null);
        return dataService.TryCreateOpeningCoverageViewModel(out OpeningCoverageWindowViewModel? viewModel) && viewModel is not null
            ? viewModel
            : throw new InvalidOperationException("Local analysis store is unavailable.");
    }

    public OpeningLineCatalogItem? SelectedLine { get; private set; }

    private void PracticeSelectedButton_Click(object? sender, RoutedEventArgs e)
    {
        PracticeSelectedLine();
    }

    private void CoverageListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        PracticeSelectedLine();
    }

    private void PracticeSelectedLine()
    {
        if (DataContext is not OpeningCoverageWindowViewModel viewModel || viewModel.SelectedLine is null)
        {
            return;
        }

        SelectedLine = viewModel.SelectedLine.Line;
        Close(true);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
