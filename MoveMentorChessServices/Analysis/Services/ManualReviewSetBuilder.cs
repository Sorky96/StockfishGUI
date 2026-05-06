using System.Text;

namespace MoveMentorChessServices;

public static class ManualReviewSetBuilder
{
    public static string BuildMarkdown(IReadOnlyList<StoredMoveAnalysis> moves, int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(moves);

        int safeLimit = Math.Clamp(limit, 10, 20);
        List<StoredMoveAnalysis> candidates = moves
            .Where(move => move.IsHighlighted || move.Quality.IsProblem())
            .Where(move => !string.IsNullOrWhiteSpace(move.ShortExplanation)
                || !string.IsNullOrWhiteSpace(move.DetailedExplanation)
                || !string.IsNullOrWhiteSpace(move.TrainingHint))
            .ToList();

        List<StoredMoveAnalysis> selected = SelectStratified(candidates, safeLimit);

        StringBuilder sb = new();
        sb.AppendLine("# Manual Advice Review Set");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Positions: {selected.Count}");
        sb.AppendLine();
        sb.AppendLine("Review questions for every position:");
        sb.AppendLine("- Czy rozumiem blad?");
        sb.AppendLine("- Czy lepszy ruch jest pokazany jasno?");
        sb.AppendLine("- Czy wskazowka jest praktyczna?");
        sb.AppendLine("- Czy opis brzmi wiarygodnie?");
        sb.AppendLine();

        for (int i = 0; i < selected.Count; i++)
        {
            StoredMoveAnalysis move = selected[i];
            sb.AppendLine($"## {i + 1}. {move.MoveNumber}{(move.AnalyzedSide == PlayerSide.White ? "." : "...")} {move.San}");
            sb.AppendLine($"- Game: `{move.GameFingerprint}`");
            sb.AppendLine($"- Ply: {move.Ply}");
            sb.AppendLine($"- Phase: {move.Phase}");
            sb.AppendLine($"- Quality: {move.Quality}");
            sb.AppendLine($"- Label: {move.MistakeLabel ?? "unclassified"}");
            sb.AppendLine($"- CPL: {move.CentipawnLoss?.ToString() ?? "n/a"}");
            sb.AppendLine($"- Played: {move.San} ({move.Uci})");
            sb.AppendLine($"- Best move: {move.BestMoveUci ?? "n/a"}");
            sb.AppendLine($"- FEN before: `{move.FenBefore}`");
            sb.AppendLine();
            sb.AppendLine("Advice:");
            AppendAdviceLine(sb, "Short", move.ShortExplanation);
            AppendAdviceLine(sb, "Detailed", move.DetailedExplanation);
            AppendAdviceLine(sb, "Training hint", move.TrainingHint);
            sb.AppendLine();
            sb.AppendLine("Manual answers:");
            sb.AppendLine("- Czy rozumiem blad? [ ] yes [ ] no [ ] unsure");
            sb.AppendLine("- Czy lepszy ruch jest pokazany jasno? [ ] yes [ ] no [ ] unsure");
            sb.AppendLine("- Czy wskazowka jest praktyczna? [ ] yes [ ] no [ ] unsure");
            sb.AppendLine("- Czy opis brzmi wiarygodnie? [ ] yes [ ] no [ ] unsure");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildDefaultMarkdown(int limit = 20)
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        IReadOnlyList<StoredMoveAnalysis> moves = store?.ListMoveAnalyses(limit: 5000) ?? [];
        return BuildMarkdown(moves, limit);
    }

    public static void RunReport(int limit = 20)
    {
        string markdown = BuildDefaultMarkdown(limit);
        string outputPath = Path.Combine(AppContext.BaseDirectory, "manual-review-set.md");
        File.WriteAllText(outputPath, markdown);
        Console.WriteLine($"Manual review set saved to: {outputPath}");
    }

    private static List<StoredMoveAnalysis> SelectStratified(List<StoredMoveAnalysis> candidates, int limit)
    {
        List<StoredMoveAnalysis> selected = [];
        HashSet<string> selectedKeys = new(StringComparer.Ordinal);

        foreach (StoredMoveAnalysis move in candidates
            .OrderBy(move => move.MistakeLabel ?? "unclassified", StringComparer.Ordinal)
            .ThenBy(move => move.Phase)
            .ThenByDescending(move => move.CentipawnLoss ?? 0))
        {
            if (selected.Count >= limit)
            {
                break;
            }

            string label = move.MistakeLabel ?? "unclassified";
            string phaseKey = $"{label}:{move.Phase}";
            if (selectedKeys.Add(phaseKey))
            {
                selected.Add(move);
            }
        }

        foreach (StoredMoveAnalysis move in candidates
            .OrderByDescending(move => move.IsHighlighted)
            .ThenByDescending(move => move.CentipawnLoss ?? 0)
            .ThenBy(move => move.Ply))
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (!selected.Any(existing => existing.GameFingerprint == move.GameFingerprint && existing.Ply == move.Ply))
            {
                selected.Add(move);
            }
        }

        return selected;
    }

    private static void AppendAdviceLine(StringBuilder sb, string label, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            sb.AppendLine($"- {label}: {text.Trim()}");
        }
    }
}
