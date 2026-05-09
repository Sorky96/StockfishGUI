using System.Text;
using MoveMentorChess.Domain;
using MoveMentorChess.Persistence;

namespace MoveMentorChess.Diagnostics;

public static class ManualReviewSetBuilder
{
    public static string BuildMarkdown(IReadOnlyList<StoredMoveAnalysis> moves, int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(moves);

        int safeLimit = Math.Clamp(limit, 10, 20);
        List<StoredMoveAnalysis> candidates = moves
            .Where(move => move.Advice.IsHighlighted || move.Move.Quality.IsProblem())
            .Where(move => !string.IsNullOrWhiteSpace(move.Advice.ShortExplanation)
                || !string.IsNullOrWhiteSpace(move.Advice.DetailedExplanation)
                || !string.IsNullOrWhiteSpace(move.Advice.TrainingHint))
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
            StoredMoveContext played = move.Move;
            StoredMoveAdviceContext advice = move.Advice;

            sb.AppendLine($"## {i + 1}. {played.MoveNumber}{(move.Analysis.AnalyzedSide == PlayerSide.White ? "." : "...")} {played.San}");
            sb.AppendLine($"- Game: `{move.Game.GameFingerprint}`");
            sb.AppendLine($"- Ply: {played.Ply}");
            sb.AppendLine($"- Phase: {played.Phase}");
            sb.AppendLine($"- Quality: {played.Quality}");
            sb.AppendLine($"- Label: {advice.MistakeLabel ?? "unclassified"}");
            sb.AppendLine($"- CPL: {played.CentipawnLoss?.ToString() ?? "n/a"}");
            sb.AppendLine($"- Played: {played.San} ({played.Uci})");
            sb.AppendLine($"- Best move: {played.BestMoveUci ?? "n/a"}");
            sb.AppendLine($"- FEN before: `{played.FenBefore}`");
            sb.AppendLine();
            sb.AppendLine("Advice:");
            AppendAdviceLine(sb, "Short", advice.ShortExplanation);
            AppendAdviceLine(sb, "Detailed", advice.DetailedExplanation);
            AppendAdviceLine(sb, "Training hint", advice.TrainingHint);
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
        => BuildDefaultMarkdown(AnalysisStoreProvider.GetStore(), limit);

    public static string BuildDefaultMarkdown(IStoredMoveAnalysisStore? store, int limit = 20)
    {
        IReadOnlyList<StoredMoveAnalysis> moves = store?.ListMoveAnalyses(limit: 5000) ?? [];
        return BuildMarkdown(moves, limit);
    }

    public static void RunReport(int limit = 20)
        => RunReport(AnalysisStoreProvider.GetStore(), limit);

    public static void RunReport(IStoredMoveAnalysisStore? store, int limit = 20)
    {
        string markdown = BuildDefaultMarkdown(store, limit);
        string outputPath = Path.Combine(AppContext.BaseDirectory, "manual-review-set.md");
        File.WriteAllText(outputPath, markdown);
        Console.WriteLine($"Manual review set saved to: {outputPath}");
    }

    private static List<StoredMoveAnalysis> SelectStratified(List<StoredMoveAnalysis> candidates, int limit)
    {
        List<StoredMoveAnalysis> selected = [];
        HashSet<string> selectedKeys = new(StringComparer.Ordinal);

        foreach (StoredMoveAnalysis move in candidates
            .OrderBy(move => move.Advice.MistakeLabel ?? "unclassified", StringComparer.Ordinal)
            .ThenBy(move => move.Move.Phase)
            .ThenByDescending(move => move.Move.CentipawnLoss ?? 0))
        {
            if (selected.Count >= limit)
            {
                break;
            }

            string label = move.Advice.MistakeLabel ?? "unclassified";
            string phaseKey = $"{label}:{move.Move.Phase}";
            if (selectedKeys.Add(phaseKey))
            {
                selected.Add(move);
            }
        }

        foreach (StoredMoveAnalysis move in candidates
            .OrderByDescending(move => move.Advice.IsHighlighted)
            .ThenByDescending(move => move.Move.CentipawnLoss ?? 0)
            .ThenBy(move => move.Move.Ply))
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (!selected.Any(existing => existing.Game.GameFingerprint == move.Game.GameFingerprint && existing.Move.Ply == move.Move.Ply))
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
