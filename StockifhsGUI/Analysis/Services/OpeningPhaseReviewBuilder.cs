using System.Text;

namespace StockifhsGUI;

public static class OpeningPhaseReviewBuilder
{
    private const int TheoryExitThresholdCp = 70;
    private const int SignificantMistakeThresholdCp = 90;

    private static readonly HashSet<string> FallbackLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "opening_principles",
        "king_safety",
        "piece_activity",
        "material_loss"
    };

    public static OpeningPhaseReview? Build(
        ImportedGame game,
        PlayerSide analyzedSide,
        IReadOnlyList<ReplayPly> replay,
        IReadOnlyList<MoveAnalysisResult> moveAnalyses)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(moveAnalyses);

        List<ReplayPly> openingReplay = replay
            .Where(item => item.Phase == GamePhase.Opening)
            .OrderBy(item => item.Ply)
            .ToList();
        if (openingReplay.Count == 0)
        {
            return null;
        }

        List<MoveAnalysisResult> openingAnalyses = moveAnalyses
            .Where(item => item.Replay.Phase == GamePhase.Opening)
            .OrderBy(item => item.Replay.Ply)
            .ToList();

        MoveAnalysisResult? firstSignificantMistake = openingAnalyses.FirstOrDefault(IsSignificantOpeningMistake);
        MoveAnalysisResult? theoryExit = openingAnalyses.FirstOrDefault(item => IsTheoryExit(item, firstSignificantMistake));
        int branchPly = theoryExit?.Replay.Ply
            ?? firstSignificantMistake?.Replay.Ply
            ?? openingReplay[^1].Ply;

        OpeningBranchReference branch = BuildBranch(game, openingReplay, branchPly, firstSignificantMistake);
        return new OpeningPhaseReview(
            branch,
            ToCriticalMoment(theoryExit, branch.BranchLabel),
            ToCriticalMoment(firstSignificantMistake, branch.BranchLabel));
    }

    private static bool IsTheoryExit(MoveAnalysisResult analysis, MoveAnalysisResult? firstSignificantMistake)
    {
        if (firstSignificantMistake is not null && analysis.Replay.Ply == firstSignificantMistake.Replay.Ply)
        {
            return true;
        }

        int loss = analysis.CentipawnLoss ?? 0;
        if (analysis.Quality is MoveQualityBucket.Blunder or MoveQualityBucket.Mistake)
        {
            return loss >= TheoryExitThresholdCp;
        }

        string? label = analysis.MistakeTag?.Label;
        if (label is null || !FallbackLabels.Contains(label))
        {
            return false;
        }

        if (label.Equals("opening_principles", StringComparison.OrdinalIgnoreCase))
        {
            return loss >= TheoryExitThresholdCp;
        }

        return loss >= SignificantMistakeThresholdCp;
    }

    private static bool IsSignificantOpeningMistake(MoveAnalysisResult analysis)
    {
        string? label = analysis.MistakeTag?.Label;
        if (label is null || !FallbackLabels.Contains(label))
        {
            return analysis.Quality is MoveQualityBucket.Blunder or MoveQualityBucket.Mistake;
        }

        return analysis.Quality is MoveQualityBucket.Blunder or MoveQualityBucket.Mistake
            || (analysis.CentipawnLoss ?? 0) >= SignificantMistakeThresholdCp;
    }

    private static OpeningBranchReference BuildBranch(
        ImportedGame game,
        IReadOnlyList<ReplayPly> openingReplay,
        int branchPly,
        MoveAnalysisResult? firstSignificantMistake)
    {
        string openingName = OpeningCatalog.GetName(game.Eco);
        string? ecoText = string.IsNullOrWhiteSpace(game.Eco) ? null : game.Eco!.Trim().ToUpperInvariant();
        string lineText = BuildLineText(openingReplay.Where(item => item.Ply <= branchPly).ToList());

        if (OpeningCatalog.TryGetExactName(ecoText, out string? exactName) && !string.IsNullOrWhiteSpace(exactName))
        {
            string exactBranch = string.IsNullOrWhiteSpace(lineText)
                ? $"{exactName} ({ecoText})"
                : $"{exactName} ({ecoText}) - {lineText}";
            return new OpeningBranchReference(ecoText, exactName, exactBranch, "eco_exact", UsedFallback: false);
        }

        string fallbackLabel = firstSignificantMistake?.MistakeTag?.Label ?? "opening_phase";
        StringBuilder builder = new();
        builder.Append(OpeningCatalog.Describe(ecoText));
        builder.Append(" | opening phase");
        if (!string.IsNullOrWhiteSpace(lineText))
        {
            builder.Append(" | ");
            builder.Append(lineText);
        }

        builder.Append(" | first issue: ");
        builder.Append(fallbackLabel);

        return new OpeningBranchReference(ecoText, openingName, builder.ToString(), $"fallback_{fallbackLabel}", UsedFallback: true);
    }

    private static OpeningCriticalMoment? ToCriticalMoment(MoveAnalysisResult? analysis, string branchLabel)
    {
        if (analysis is null)
        {
            return null;
        }

        return new OpeningCriticalMoment(
            analysis.Replay.Ply,
            analysis.Replay.MoveNumber,
            analysis.Replay.Side,
            analysis.Replay.San,
            analysis.Replay.Uci,
            analysis.Quality,
            analysis.CentipawnLoss,
            analysis.MistakeTag?.Label,
            BuildTrigger(analysis),
            branchLabel);
    }

    private static string BuildTrigger(MoveAnalysisResult analysis)
    {
        if (!string.IsNullOrWhiteSpace(analysis.MistakeTag?.Label))
        {
            return $"{analysis.MistakeTag!.Label} at {(analysis.CentipawnLoss?.ToString() ?? "n/a")} cp";
        }

        return $"{analysis.Quality} at {(analysis.CentipawnLoss?.ToString() ?? "n/a")} cp";
    }

    private static string BuildLineText(IReadOnlyList<ReplayPly> replay)
    {
        if (replay.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (ReplayPly ply in replay.Take(8))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(ply.MoveNumber);
            builder.Append(ply.Side == PlayerSide.White ? ". " : "... ");
            builder.Append(ply.San);
        }

        if (replay.Count > 8)
        {
            builder.Append(" ...");
        }

        return builder.ToString();
    }
}
