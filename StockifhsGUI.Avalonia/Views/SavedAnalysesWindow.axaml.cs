using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StockifhsGUI.Avalonia.Views;

public partial class SavedAnalysesWindow : Window
{
    private readonly IAnalysisStore analysisStore;
    private readonly bool canOpenAnalysis;

    public SavedAnalysesWindow()
    {
        analysisStore = AnalysisStoreProvider.GetStore() ?? throw new InvalidOperationException("Local analysis store is unavailable.");
        canOpenAnalysis = true;
        InitializeComponent();
    }

    public SavedAnalysesWindow(IAnalysisStore analysisStore, bool canOpenAnalysis)
    {
        this.analysisStore = analysisStore;
        this.canOpenAnalysis = canOpenAnalysis;
        InitializeComponent();
        RefreshList();
    }

    public GameAnalysisResult? SelectedResult { get; private set; }

    public SavedAnalysisAction RequestedAction { get; private set; }

    private void FilterTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void AnalysesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateDetails();
    }

    private void AnalysesListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        ConfirmSelection(canOpenAnalysis ? SavedAnalysisAction.OpenAnalysis : SavedAnalysisAction.LoadGame);
    }

    private void LoadGameButton_Click(object? sender, RoutedEventArgs e)
    {
        ConfirmSelection(SavedAnalysisAction.LoadGame);
    }

    private void OpenAnalysisButton_Click(object? sender, RoutedEventArgs e)
    {
        ConfirmSelection(SavedAnalysisAction.OpenAnalysis);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void RefreshList()
    {
        SelectedResult = null;
        RequestedAction = SavedAnalysisAction.None;
        LoadGameButton.IsEnabled = false;
        OpenAnalysisButton.IsEnabled = false;

        IReadOnlyList<GameAnalysisResult> items = analysisStore.ListResults(FilterTextBox.Text, limit: 1000);
        string normalizedFilter = FilterTextBox.Text?.Trim() ?? string.Empty;
        IEnumerable<GameAnalysisResult> filtered = string.IsNullOrWhiteSpace(normalizedFilter)
            ? items
            : items.Where(result => MatchesExtendedFilter(result, normalizedFilter));

        AnalysesListBox.ItemsSource = filtered
            .Select(result => new SavedAnalysisListItem(result, BuildListLabel(result)))
            .ToList();

        if (AnalysesListBox.ItemCount > 0)
        {
            AnalysesListBox.SelectedIndex = 0;
        }
        else
        {
            DetailsTextBlock.Text = "No saved analyses match the current filter.";
        }
    }

    private void UpdateDetails()
    {
        if (AnalysesListBox.SelectedItem is not SavedAnalysisListItem item)
        {
            DetailsTextBlock.Text = "Select a saved analysis to inspect the cached result.";
            LoadGameButton.IsEnabled = false;
            OpenAnalysisButton.IsEnabled = false;
            return;
        }

        SelectedResult = item.Result;
        LoadGameButton.IsEnabled = true;
        OpenAnalysisButton.IsEnabled = canOpenAnalysis;

        GameAnalysisResult result = item.Result;
        int blunders = result.HighlightedMistakes.Count(mistake => mistake.Quality == MoveQualityBucket.Blunder);
        int mistakes = result.HighlightedMistakes.Count(mistake => mistake.Quality == MoveQualityBucket.Mistake);
        int inaccuracies = result.HighlightedMistakes.Count(mistake => mistake.Quality == MoveQualityBucket.Inaccuracy);
        IReadOnlyList<string> topLabels = result.HighlightedMistakes
            .Select(mistake => mistake.Tag?.Label ?? "unclassified")
            .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(group => $"{FormatMistakeLabel(group.Key)} ({group.Count()})")
            .ToList();

        StringBuilder builder = new();
        builder.AppendLine($"{result.Game.WhitePlayer ?? "White"} vs {result.Game.BlackPlayer ?? "Black"}");
        builder.AppendLine($"Side: {result.AnalyzedSide}");
        builder.AppendLine($"Date: {result.Game.DateText ?? "?"}");
        builder.AppendLine($"Result: {result.Game.Result ?? "?"}");
        builder.AppendLine($"Opening: {OpeningCatalog.GetName(result.Game.Eco)}");
        builder.AppendLine($"Highlights: {result.HighlightedMistakes.Count} total");
        builder.AppendLine($"Breakdown: {blunders} blunders, {mistakes} mistakes, {inaccuracies} inaccuracies");

        if (topLabels.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Top labels:");
            foreach (string label in topLabels)
            {
                builder.AppendLine($"- {label}");
            }
        }

        if (result.HighlightedMistakes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Top highlighted mistakes:");
            foreach (SelectedMistake mistake in result.HighlightedMistakes.Take(5))
            {
                MoveAnalysisResult? lead = mistake.Moves
                    .OrderByDescending(move => move.Quality)
                    .ThenByDescending(move => move.CentipawnLoss ?? 0)
                    .FirstOrDefault();

                if (lead is null)
                {
                    continue;
                }

                string moveLabel = $"{lead.Replay.MoveNumber}{(lead.Replay.Side == PlayerSide.White ? "." : "...")} {lead.Replay.San}";
                builder.AppendLine($"- {moveLabel} | {mistake.Quality} | {FormatMistakeLabel(mistake.Tag?.Label ?? "unclassified")} | CPL {lead.CentipawnLoss?.ToString() ?? "n/a"}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(canOpenAnalysis
            ? "Use 'Open Analysis' to reopen the full analyzer on cached data, or 'Load Game' to bring the PGN back to the main board."
            : "Use 'Load Game' to bring the PGN back to the main board. Open Analysis is unavailable because Stockfish is not loaded.");
        DetailsTextBlock.Text = builder.ToString().TrimEnd();
    }

    private void ConfirmSelection(SavedAnalysisAction action)
    {
        if (AnalysesListBox.SelectedItem is not SavedAnalysisListItem item)
        {
            return;
        }

        if (action == SavedAnalysisAction.OpenAnalysis && !canOpenAnalysis)
        {
            return;
        }

        SelectedResult = item.Result;
        RequestedAction = action;
        Close(true);
    }

    private static bool MatchesExtendedFilter(GameAnalysisResult result, string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return (result.Game.WhitePlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.Game.BlackPlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.Game.DateText?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.Game.Result?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.Game.Eco?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || OpeningCatalog.Describe(result.Game.Eco).Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || (result.Game.Site?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || result.AnalyzedSide.ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || result.HighlightedMistakes.Any(mistake =>
                (mistake.Tag?.Label?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
                || mistake.Quality.ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildListLabel(GameAnalysisResult result)
    {
        string players = $"{result.Game.WhitePlayer ?? "White"} vs {result.Game.BlackPlayer ?? "Black"}";
        string date = string.IsNullOrWhiteSpace(result.Game.DateText) ? "date ?" : result.Game.DateText!;
        string opening = OpeningCatalog.GetName(result.Game.Eco);
        string side = result.AnalyzedSide == PlayerSide.White ? "White" : "Black";
        return $"{players,-28} | {side,-5} | {date,-10} | {opening,-22} | {result.HighlightedMistakes.Count,2} highlights";
    }

    private static string FormatMistakeLabel(string label)
    {
        return label switch
        {
            "hanging_piece" => "Loose pieces",
            "missed_tactic" => "Missed tactics",
            "opening_principles" => "Opening discipline",
            "king_safety" => "King safety",
            "endgame_technique" => "Endgame technique",
            "material_loss" => "Material losses",
            "piece_activity" => "Passive pieces",
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label.Replace('_', ' ').ToLowerInvariant())
        };
    }

    private sealed record SavedAnalysisListItem(GameAnalysisResult Result, string Label)
    {
        public override string ToString() => Label;
    }
}

public enum SavedAnalysisAction
{
    None,
    LoadGame,
    OpenAnalysis
}
