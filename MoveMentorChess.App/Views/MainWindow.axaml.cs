using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MoveMentorChess.App.Controls;
using MoveMentorChess.App.ViewModels;

namespace MoveMentorChess.App.Views;

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

    private async void LoadPgnFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load PGN file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PGN files")
                {
                    Patterns = ["*.pgn"],
                    MimeTypes = ["application/x-chess-pgn", "text/plain"]
                },
                FilePickerFileTypes.TextPlain,
                FilePickerFileTypes.All
            ]
        });

        IStorageFile? file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        string pgnText;
        await using (Stream stream = await file.OpenReadAsync())
        using (StreamReader reader = new(stream))
        {
            pgnText = await reader.ReadToEndAsync();
        }

        PgnBatchParseResult parseResult;
        try
        {
            parseResult = await Task.Run(() => PgnGameParser.ParseMany(pgnText));
        }
        catch (Exception ex)
        {
            await ShowInfoDialogAsync("Load PGN file", $"Could not read PGN file.\n{ex.Message}");
            return;
        }

        PgnFileImportResult importResult = viewModel.ImportPgnGames(parseResult);
        if (importResult.ImportedGames == 0)
        {
            await ShowInfoDialogAsync("Load PGN file", "No replayable games were found in the selected PGN file.");
            return;
        }

        if (!viewModel.HasAnalysisEngine())
        {
            await ShowInfoDialogAsync(
                "Load PGN file",
                $"Loaded {importResult.ImportedGames} games. The analysis engine is unavailable, so bulk analysis cannot start now.");
            return;
        }

        string skippedText = importResult.SkippedGames > 0
            ? $" Skipped {importResult.SkippedGames} games that could not be parsed or replayed."
            : string.Empty;
        string? primaryPlayer = MainWindowViewModel.DetectPrimaryPlayer(importResult.Games);
        string analysisTargetText = string.IsNullOrWhiteSpace(primaryPlayer)
            ? "No recurring player could be detected, so analysis will use the currently selected side."
            : $"Detected player: {primaryPlayer}. Only that player's moves will be analyzed, as White or Black depending on each game.";
        bool analyze = await ShowConfirmationDialogAsync(
            "Analyze imported games?",
            $"Loaded {importResult.ImportedGames} games from the PGN file.{skippedText}\n\n{analysisTargetText}\n\nAnalyze now? This can take a long time.",
            "Analyze",
            "Later");

        if (analyze)
        {
            BulkPgnAnalysisResult analysisResult = await viewModel.AnalyzeImportedGamesAsync(importResult.Games);
            await ShowInfoDialogAsync("PGN analysis finished", BuildBulkAnalysisSummary(analysisResult));
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

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        SettingsWindow dialog = new();
        await dialog.ShowDialog<bool?>(this);
    }

    private async Task<LegalMoveInfo?> ShowPromotionDialogAsync(IReadOnlyList<LegalMoveInfo> moves)
    {
        PromotionWindow dialog = new(moves);
        bool? result = await dialog.ShowDialog<bool?>(this);
        return result == true ? dialog.SelectedMove : null;
    }

    private async Task ShowInfoDialogAsync(string title, string message)
    {
        await ShowConfirmationDialogAsync(title, message, "OK", null);
    }

    private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string acceptText, string? cancelText)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 520,
            Height = 300,
            MinWidth = 460,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brush.Parse("#101820")
        };

        TextBlock messageBlock = new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse("#D7E2EA"),
            FontSize = 15
        };
        ScrollViewer messageScroller = new()
        {
            Content = messageBlock,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            Button cancelButton = new()
            {
                Content = cancelText,
                Width = 100
            };
            cancelButton.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(cancelButton);
        }

        Button acceptButton = new()
        {
            Content = acceptText,
            Width = 100
        };
        acceptButton.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(acceptButton);

        Border messagePanel = new()
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(18),
            Background = Brush.Parse("#182733"),
            Child = messageScroller
        };

        Grid content = new()
        {
            Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                messagePanel,
                buttons
            }
        };

        buttons.Margin = new Thickness(0, 14, 0, 0);
        Grid.SetRow(buttons, 1);
        dialog.Content = content;

        bool? result = await dialog.ShowDialog<bool?>(this);
        return result == true;
    }

    private static string BuildBulkAnalysisSummary(BulkPgnAnalysisResult result)
    {
        string player = string.IsNullOrWhiteSpace(result.PrimaryPlayer)
            ? "Detected player: none"
            : $"Analyzed player: {result.PrimaryPlayer}";
        string summary =
            $"{player}\n\nNew analyses: {result.AnalyzedGames}\nLoaded from cache: {result.CachedGames}\nSkipped: {result.SkippedGames}\nFailed: {result.FailedGames}";

        if (result.FailureMessages.Count == 0)
        {
            return summary;
        }

        return summary + "\n\nFirst failures:\n" + string.Join("\n", result.FailureMessages);
    }
}
