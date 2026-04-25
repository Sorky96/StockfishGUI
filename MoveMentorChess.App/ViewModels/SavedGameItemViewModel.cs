namespace MoveMentorChess.App.ViewModels;

public sealed class SavedGameItemViewModel
{
    public SavedGameItemViewModel(SavedImportedGameSummary summary)
    {
        Summary = summary;
        DisplayTitle = summary.DisplayTitle;
        string whitePlayer = string.IsNullOrWhiteSpace(summary.WhitePlayer) ? "White" : summary.WhitePlayer;
        string blackPlayer = string.IsNullOrWhiteSpace(summary.BlackPlayer) ? "Black" : summary.BlackPlayer;
        Player = $"{whitePlayer} vs {blackPlayer}";
        Date = string.IsNullOrWhiteSpace(summary.DateText) ? "(unknown)" : summary.DateText;
    }

    public SavedImportedGameSummary Summary { get; }

    public string DisplayTitle { get; }

    public string Player { get; }

    public string Date { get; }
}
