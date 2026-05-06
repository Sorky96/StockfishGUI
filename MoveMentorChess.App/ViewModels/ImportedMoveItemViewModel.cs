namespace MoveMentorChess.App.ViewModels;

public sealed class ImportedMoveItemViewModel : ViewModelBase
{
    private bool hasAnalysisLabel;
    private string qualityLabel = string.Empty;
    private string evalDeltaText = string.Empty;
    private string qualityLabelBrush = "#657386";
    private string qualityLabelForeground = "White";
    private string evalDeltaBrush = "#657386";

    public ImportedMoveItemViewModel(int index, ReplayPly replayPly)
    {
        Index = index;
        ReplayPly = replayPly;
        DisplayText = replayPly.Side == PlayerSide.White
            ? $"{replayPly.MoveNumber,3}. {replayPly.San}"
            : $"{replayPly.MoveNumber,3}... {replayPly.San}";
    }

    public int Index { get; }

    public ReplayPly ReplayPly { get; }

    public string DisplayText { get; }

    public string QualityLabel
    {
        get => qualityLabel;
        private set => SetProperty(ref qualityLabel, value);
    }

    public bool HasAnalysisLabel
    {
        get => hasAnalysisLabel;
        private set => SetProperty(ref hasAnalysisLabel, value);
    }

    public string EvalDeltaText
    {
        get => evalDeltaText;
        private set => SetProperty(ref evalDeltaText, value);
    }

    public string QualityLabelBrush
    {
        get => qualityLabelBrush;
        private set => SetProperty(ref qualityLabelBrush, value);
    }

    public string QualityLabelForeground
    {
        get => qualityLabelForeground;
        private set => SetProperty(ref qualityLabelForeground, value);
    }

    public string EvalDeltaBrush
    {
        get => evalDeltaBrush;
        private set => SetProperty(ref evalDeltaBrush, value);
    }

    public void ClearAnalysis()
    {
        HasAnalysisLabel = false;
        QualityLabel = string.Empty;
        EvalDeltaText = string.Empty;
        QualityLabelBrush = "#657386";
        QualityLabelForeground = "White";
        EvalDeltaBrush = "#657386";
    }

    public void ApplyAnalysis(MoveAnalysisResult analysis)
    {
        HasAnalysisLabel = true;
        QualityLabel = analysis.Quality.ToString();
        QualityLabelBrush = GetQualityBrush(analysis.Quality);
        QualityLabelForeground = GetQualityForeground(analysis.Quality);
        EvalDeltaText = FormatEvalDelta(analysis.EvalBeforeCp, analysis.EvalAfterCp, analysis.BestMateIn, analysis.PlayedMateIn);
        EvalDeltaBrush = GetEvalDeltaBrush(analysis.EvalBeforeCp, analysis.EvalAfterCp);
    }

    public override string ToString() => DisplayText;

    private static string FormatEvalDelta(int? beforeCp, int? afterCp, int? bestMateIn, int? playedMateIn)
    {
        if (bestMateIn.HasValue || playedMateIn.HasValue)
        {
            return $"mate {FormatMateChange(bestMateIn, playedMateIn)}";
        }

        if (beforeCp is not int before || afterCp is not int after)
        {
            return string.Empty;
        }

        int delta = after - before;
        return delta == 0
            ? "+0.00"
            : $"{(delta > 0 ? "+" : "-")}{Math.Abs(delta) / 100.0:0.00}";
    }

    private static string FormatMateChange(int? beforeMate, int? afterMate)
    {
        string before = beforeMate?.ToString() ?? "?";
        string after = afterMate?.ToString() ?? "?";
        return $"{before}->{after}";
    }

    private static string GetQualityBrush(MoveQualityBucket quality)
    {
        return quality switch
        {
            MoveQualityBucket.Book => "#5B6875",
            MoveQualityBucket.Brilliant => "#22B8CF",
            MoveQualityBucket.Great => "#1D7ED0",
            MoveQualityBucket.Best => "#1F7A55",
            MoveQualityBucket.Excellent => "#2F6FB3",
            MoveQualityBucket.Good => "#4D6B2E",
            MoveQualityBucket.Inaccuracy => "#B88A10",
            MoveQualityBucket.Mistake => "#C56A19",
            MoveQualityBucket.Blunder => "#B93838",
            _ => "#657386"
        };
    }

    private static string GetQualityForeground(MoveQualityBucket quality)
    {
        return quality switch
        {
            MoveQualityBucket.Inaccuracy => "#111827",
            MoveQualityBucket.Brilliant => "#082F38",
            _ => "White"
        };
    }

    private static string GetEvalDeltaBrush(int? beforeCp, int? afterCp)
    {
        if (beforeCp is not int before || afterCp is not int after)
        {
            return "#657386";
        }

        int delta = after - before;
        if (delta > 0)
        {
            return "#1F7A55";
        }

        return delta < 0 ? "#B93838" : "#657386";
    }
}
