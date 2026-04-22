using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StockifhsGUI.Avalonia.ViewModels;

namespace StockifhsGUI.Avalonia.Views;

public partial class SavedGamesWindow : Window
{
    private readonly IAnalysisStore analysisStore;
    private List<SavedGameItemViewModel> items = [];
    private SavedGamesSortColumn sortColumn = SavedGamesSortColumn.Player;
    private bool sortAscending = true;

    public SavedGamesWindow()
        : this(AnalysisStoreProvider.GetStore() ?? throw new InvalidOperationException("Local analysis store is unavailable."))
    {
    }

    public SavedGamesWindow(IAnalysisStore analysisStore)
    {
        this.analysisStore = analysisStore;
        InitializeComponent();
        RefreshList();
    }

    public ImportedGame? SelectedGame { get; private set; }

    private void FilterTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void ColumnFilterTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void GamesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Selection is enough; row details are intentionally replaced by a denser table-like layout.
    }

    private void GamesListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        LoadSelectedGame();
    }

    private void SortPlayerButton_Click(object? sender, RoutedEventArgs e)
    {
        ToggleSort(SavedGamesSortColumn.Player);
    }

    private void SortDateButton_Click(object? sender, RoutedEventArgs e)
    {
        ToggleSort(SavedGamesSortColumn.Date);
    }

    private void LoadSelectedButton_Click(object? sender, RoutedEventArgs e)
    {
        LoadSelectedGame();
    }

    private void DeleteSelectedButton_Click(object? sender, RoutedEventArgs e)
    {
        if (GamesListBox.SelectedItem is not SavedGameItemViewModel item)
        {
            return;
        }

        if (!analysisStore.DeleteImportedGame(item.Summary.GameFingerprint))
        {
            return;
        }

        GameAnalysisCache.RemoveGame(item.Summary.GameFingerprint);
        RefreshList();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void RefreshList()
    {
        IEnumerable<SavedGameItemViewModel> query = analysisStore.ListImportedGames(null)
            .Select(summary => new SavedGameItemViewModel(summary));

        string filter = FilterTextBox.Text?.Trim() ?? string.Empty;
        string playerFilter = PlayerFilterTextBox.Text?.Trim() ?? string.Empty;
        string dateFilter = DateFilterTextBox.Text?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(item =>
                item.Player.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.Date.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.DisplayTitle.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(playerFilter))
        {
            query = query.Where(item => item.Player.Contains(playerFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(dateFilter))
        {
            query = query.Where(item => item.Date.Contains(dateFilter, StringComparison.OrdinalIgnoreCase));
        }

        query = sortColumn switch
        {
            SavedGamesSortColumn.Player => sortAscending
                ? query.OrderBy(item => item.Player, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(item => item.Player, StringComparer.OrdinalIgnoreCase),
            SavedGamesSortColumn.Date => sortAscending
                ? query.OrderBy(item => item.Date, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(item => item.Date, StringComparer.OrdinalIgnoreCase),
            _ => query
        };

        items = query.ToList();
        GamesListBox.ItemsSource = items;
        if (items.Count > 0)
        {
            GamesListBox.SelectedIndex = 0;
        }
    }

    private void ToggleSort(SavedGamesSortColumn column)
    {
        if (sortColumn == column)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = column;
            sortAscending = true;
        }

        RefreshList();
    }

    private void LoadSelectedGame()
    {
        if (GamesListBox.SelectedItem is not SavedGameItemViewModel item)
        {
            return;
        }

        if (!analysisStore.TryLoadImportedGame(item.Summary.GameFingerprint, out ImportedGame? game) || game is null)
        {
            return;
        }

        SelectedGame = game;
        Close(true);
    }

    private enum SavedGamesSortColumn
    {
        Player,
        Date
    }
}
