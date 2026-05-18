using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.ViewModels;

internal sealed record AnalysisSideOption(PlayerSide Side, string Label)
{
    public override string ToString() => Label;
}

internal static class AnalysisWindowSetupService
{
    public static IReadOnlyList<AnalysisSideOption> SideOptions { get; } =
    [
        new(PlayerSide.White, "Analyze White"),
        new(PlayerSide.Black, "Analyze Black")
    ];

    public static IReadOnlyList<AnalysisFilterOption> FilterOptions { get; } =
    [
        new("All highlights", null),
        new("Not reviewed", null, AnalysisReviewFilter.NotReviewed),
        new("Reviewed", null, AnalysisReviewFilter.Reviewed),
        new("Blunders only", MoveQualityBucket.Blunder),
        new("Mistakes only", MoveQualityBucket.Mistake),
        new("Inaccuracies only", MoveQualityBucket.Inaccuracy)
    ];

    public static int GetSideIndex(PlayerSide side)
        => side == PlayerSide.Black ? 1 : 0;

    public static AnalysisSideOption GetSideOption(PlayerSide side)
        => SideOptions[GetSideIndex(side)];

    public static AnalysisWindowState CreateWindowState(PlayerSide side, int qualityFilterIndex)
        => new(side, qualityFilterIndex, 1);

    public static int ClampFilterIndex(int index, int itemCount)
        => Math.Clamp(index, 0, Math.Max(0, itemCount - 1));

    public static AnalysisFilterOption GetFilterOption(int index)
        => FilterOptions[ClampFilterIndex(index, FilterOptions.Count)];

    public static int GetFilterIndex(AnalysisFilterOption? option)
    {
        if (option is null)
        {
            return 0;
        }

        for (int i = 0; i < FilterOptions.Count; i++)
        {
            if (FilterOptions[i] == option)
            {
                return i;
            }
        }

        return 0;
    }
}
