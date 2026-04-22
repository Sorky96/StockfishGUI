namespace StockifhsGUI.Avalonia.ViewModels;

public sealed class OpeningLineRecallAttemptViewModel : ViewModelBase
{
    public OpeningLineRecallAttemptViewModel(OpeningLineRecallAttemptResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        SubmittedMove = string.IsNullOrWhiteSpace(result.ResolvedSan)
            ? result.SubmittedMoveText
            : string.IsNullOrWhiteSpace(result.ResolvedUci)
                ? result.ResolvedSan
                : $"{result.ResolvedSan} ({result.ResolvedUci})";
        Grade = result.Grade.ToString();
        Summary = result.Summary;
        PreferredMoves = result.PreferredReferences.Count == 0
            ? "No preferred local references"
            : string.Join(", ", result.PreferredReferences.Select(option => option.DisplayText));
        PlayableMoves = result.PlayableReferences.Count == 0
            ? "No extra playable local references"
            : string.Join(", ", result.PlayableReferences.Select(option => option.DisplayText));
    }

    public string SubmittedMove { get; }

    public string Grade { get; }

    public string Summary { get; }

    public string PreferredMoves { get; }

    public string PlayableMoves { get; }
}
