using MoveMentorChess.Analysis;

namespace MoveMentorChess.Presentation.Models;

public static class AnalysisTimelinePresentation
{
    public static IReadOnlyList<SimilarMistakeLink> BuildSimilarMistakeLinks(
        IEnumerable<SelectedMistakeViewItem> visibleItems,
        MoveAnalysisResult lead,
        string label)
    {
        return visibleItems
            .Where(item => !ReferenceEquals(item.LeadMove, lead)
                && string.Equals(item.RawLabel, label, StringComparison.Ordinal))
            .OrderByDescending(item => item.LeadMove.CentipawnLoss ?? 0)
            .ThenBy(item => item.LeadMove.Replay.Ply)
            .Take(4)
            .Select(item => new SimilarMistakeLink(
                item,
                BuildSimilarMistakeRole(item, lead, label)))
            .ToList();
    }

    public static string BuildSimilarMistakesHint(int count, string label)
        => count == 0
            ? "No other visible highlights share this diagnosis."
            : $"Other {AnalysisMistakePresentation.FormatMistakeLabel(label).ToLowerInvariant()} moments. Click one to jump there.";

    public static string BuildSimilarMistakeRole(SelectedMistakeViewItem item, MoveAnalysisResult currentLead, string currentLabel)
    {
        if ((item.LeadMove.CentipawnLoss ?? 0) > (currentLead.CentipawnLoss ?? 0))
        {
            return "More costly";
        }

        if (item.LeadMove.Replay.Phase != currentLead.Replay.Phase)
        {
            return $"{AnalysisMistakePresentation.FormatPhase(item.LeadMove.Replay.Phase)} version";
        }

        if (item.LeadMove.Replay.Ply > currentLead.Replay.Ply)
        {
            return "Later example";
        }

        return string.Equals(item.RawLabel, currentLabel, StringComparison.Ordinal)
            ? "Same motif"
            : "Related";
    }

    public static List<PhaseSegment> BuildPhaseSegments(IReadOnlyList<ReplayPly> replay)
    {
        List<PhaseSegment> segments = [];
        foreach (ReplayPly ply in replay)
        {
            if (segments.Count == 0 || segments[^1].Phase != ply.Phase)
            {
                segments.Add(new PhaseSegment(ply.Phase, 1));
            }
            else
            {
                PhaseSegment last = segments[^1];
                segments[^1] = last with { PlyCount = last.PlyCount + 1 };
            }
        }

        return segments;
    }

    public static string BuildSummaryDiagnosis(GameAnalysisResult result)
    {
        if (result.HighlightedMistakes.Count == 0)
        {
            return "No recurring problem pattern found.";
        }

        var dominant = result.HighlightedMistakes
            .Select(mistake => new
            {
                Label = mistake.Tag?.Label ?? AnalysisMistakePresentation.GetLeadMove(mistake).MistakeTag?.Label ?? "unclassified",
                Lead = AnalysisMistakePresentation.GetLeadMove(mistake)
            })
            .GroupBy(item => item.Label)
            .Select(group => new
            {
                Label = group.Key,
                Count = group.Count(),
                AverageLoss = group.Average(item => item.Lead.CentipawnLoss ?? 0)
            })
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.AverageLoss)
            .First();

        MoveAnalysisResult mostExpensive = result.HighlightedMistakes
            .Select(AnalysisMistakePresentation.GetLeadMove)
            .OrderByDescending(move => move.CentipawnLoss ?? 0)
            .First();

        string moveLabel = $"{mostExpensive.Replay.MoveNumber}{(mostExpensive.Replay.Side == PlayerSide.White ? "." : "...")} {mostExpensive.Replay.San}";
        return $"Biggest pattern: {AnalysisMistakePresentation.FormatMistakeLabel(dominant.Label)}, {dominant.Count} times, average loss {dominant.AverageLoss:0} cp. Costliest moment: {moveLabel}.";
    }

    public static int CountReviewedHighlights(GameAnalysisResult result, IReadOnlySet<int> reviewedPlies)
        => result.HighlightedMistakes.Count(mistake => reviewedPlies.Contains(AnalysisMistakePresentation.GetLeadMove(mistake).Replay.Ply));

    public static string BuildPhaseSummary(IEnumerable<PhaseSegment> segments)
        => string.Join(", ", segments.Select(segment => $"{AnalysisMistakePresentation.FormatPhase(segment.Phase)} {segment.PlyCount} ply"));

    public static string GetPhaseBrush(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Opening => "#1F7A55",
            GamePhase.Middlegame => "#2F6FB3",
            GamePhase.Endgame => "#8F3F9F",
            _ => "#657386"
        };
    }

    public static string GetQualityBrush(MoveQualityBucket quality)
    {
        return quality switch
        {
            MoveQualityBucket.Blunder => "#D84A4A",
            MoveQualityBucket.Mistake => "#D9822B",
            MoveQualityBucket.Inaccuracy => "#D7B338",
            _ => "#657386"
        };
    }
}

public sealed record SimilarMistakeLink(SelectedMistakeViewItem Item, string RoleText)
{
    public string MoveRange => Item.MoveRange;

    public string MetaText => Item.MetaText;
}

public sealed record PhaseSegment(GamePhase Phase, int PlyCount);
