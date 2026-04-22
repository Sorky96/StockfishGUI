using Avalonia.Controls;
using Avalonia.Interactivity;
using StockifhsGUI.Avalonia.Controls;
using StockifhsGUI.Avalonia.ViewModels;

namespace StockifhsGUI.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SetPromotionMoveSelector(ShowPromotionDialogAsync);
            }
        };
        Closing += (_, _) =>
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        };
    }

    private async void BoardView_OnSquarePressed(object? sender, BoardSquarePressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.HandleSquareClickAsync(e.Square);
        }

        BoardView.InvalidateVisual();
    }

    private async void PgnImportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        PgnImportWindow dialog = new();
        bool? result = await dialog.ShowDialog<bool?>(this);
        if (result == true && !string.IsNullOrWhiteSpace(dialog.PgnText))
        {
            viewModel.ImportPgn(dialog.PgnText);
        }
    }

    private async void LoadSavedButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            return;
        }

        SavedGamesWindow dialog = new(store);
        bool? result = await dialog.ShowDialog<bool?>(this);
        if (result == true && dialog.SelectedGame is not null)
        {
            viewModel.LoadImportedGame(dialog.SelectedGame);
        }
    }

    private async void ProfilesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            return;
        }

        ProfilesWindow dialog = new(
            new PlayerProfileService(store),
            viewModel.NavigateToProfileExampleAsync,
            viewModel.NavigateToOpeningExampleAsync,
            viewModel.NavigateToOpeningPositionAsync);
        await dialog.ShowDialog(this);
    }

    private async void AnalyzeImportedButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        AnalysisWindow? dialog = viewModel.CreateAnalysisWindow();
        if (dialog is null)
        {
            return;
        }

        await dialog.ShowDialog(this);
    }

    private async void SavedAnalysesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            return;
        }

        SavedAnalysesWindow dialog = new(store, canOpenAnalysis: viewModel.HasAnalysisEngine());
        bool? result = await dialog.ShowDialog<bool?>(this);
        if (result != true || dialog.SelectedResult is null)
        {
            return;
        }

        viewModel.LoadImportedGame(dialog.SelectedResult.Game);
        viewModel.SelectedAnalysisSide = dialog.SelectedResult.AnalyzedSide;

        if (dialog.RequestedAction == SavedAnalysisAction.OpenAnalysis)
        {
            AnalysisWindow? analysisWindow = viewModel.CreateAnalysisWindow();
            if (analysisWindow is not null)
            {
                await analysisWindow.ShowDialog(this);
            }
        }
    }

    private async Task<LegalMoveInfo?> ShowPromotionDialogAsync(IReadOnlyList<LegalMoveInfo> moves)
    {
        PromotionWindow dialog = new(moves);
        bool? result = await dialog.ShowDialog<bool?>(this);
        return result == true ? dialog.SelectedMove : null;
    }
}
