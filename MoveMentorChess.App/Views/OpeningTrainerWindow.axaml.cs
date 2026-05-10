using Avalonia.Controls;
using MoveMentorChess.App.Controls;
using MoveMentorChess.App.ViewModels;

namespace MoveMentorChess.App.Views;

public partial class OpeningTrainerWindow : Window
{
    public OpeningTrainerWindow()
    {
        InitializeComponent();
        DataContext ??= new OpeningTrainerWindowViewModel();
    }

    public OpeningTrainerWindow(OpeningTrainerWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private void OnStudyBoardSquarePressed(object? sender, BoardSquarePressedEventArgs e)
    {
        if (DataContext is OpeningTrainerWindowViewModel viewModel)
        {
            viewModel.HandleStudyBoardSquarePressed(e.Square);
        }
    }
}
