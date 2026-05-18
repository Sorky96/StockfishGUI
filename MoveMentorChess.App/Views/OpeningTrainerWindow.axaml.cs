using Avalonia.Controls;
using Avalonia.Interactivity;
using MoveMentorChess.App.Controls;
using MoveMentorChess.App.ViewModels;

namespace MoveMentorChess.App.Views;

public partial class OpeningTrainerWindow : Window
{
    public OpeningTrainerWindow()
        : this(CreateDefaultViewModel())
    {
    }

    public OpeningTrainerWindow(OpeningTrainerWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private static OpeningTrainerWindowViewModel CreateDefaultViewModel()
    {
        DefaultMainWindowDialogDataService dataService = new(() => null);
        return dataService.TryCreateOpeningTrainerViewModel(out OpeningTrainerWindowViewModel? viewModel) && viewModel is not null
            ? viewModel
            : throw new InvalidOperationException("Local analysis store is unavailable.");
    }

    private void OnStudyBoardSquarePressed(object? sender, BoardSquarePressedEventArgs e)
    {
        if (DataContext is OpeningTrainerWindowViewModel viewModel)
        {
            viewModel.HandleStudyBoardSquarePressed(e.Square);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
