namespace StockifhsGUI.Avalonia.ViewModels;

public sealed class PlayerProfileSummaryItemViewModel
{
    public PlayerProfileSummaryItemViewModel(PlayerProfileSummary summary)
    {
        Summary = summary;
        Header = summary.DisplayName;
        string topLabels = summary.TopLabels.Count == 0 ? "no tags" : string.Join(", ", summary.TopLabels);
        Meta = $"Games {summary.GamesAnalyzed} | Highlighted mistakes {summary.HighlightedMistakes} | CPL {summary.AverageCentipawnLoss?.ToString() ?? "n/a"} | {topLabels}";
    }

    public PlayerProfileSummary Summary { get; }

    public string Header { get; }

    public string Meta { get; }
}
