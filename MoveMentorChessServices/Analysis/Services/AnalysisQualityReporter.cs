using System.Text.Json;

namespace MoveMentorChessServices;

/// <summary>
/// Reads the diagnostic JSONL files and produces an <see cref="AnalysisQualityReport"/>
/// summarising classifier confidence, unclassified rate and advice fallback rate.
/// All I/O is local; no external dependencies.
/// </summary>
public static class AnalysisQualityReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AnalysisQualityReport Build()
    {
        string classifierLogPath = DiagnosticMistakeClassifier.DefaultLogPath();
        string adviceLogPath = FileAdviceGenerationLogger.CreateDefault().FilePath;

        List<ClassifierDiagnosticEntry> classifierEntries = LoadClassifierEntries(classifierLogPath);
        List<AdviceGenerationTrace> adviceTraces = LoadAdviceTraces(adviceLogPath);

        return BuildReport(classifierEntries, adviceTraces);
    }

    private static List<ClassifierDiagnosticEntry> LoadClassifierEntries(string path)
    {
        List<ClassifierDiagnosticEntry> entries = [];
        if (!File.Exists(path))
        {
            return entries;
        }

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                ClassifierDiagnosticEntry? entry = JsonSerializer.Deserialize<ClassifierDiagnosticEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException) { }
        }

        return entries;
    }

    private static List<AdviceGenerationTrace> LoadAdviceTraces(string path)
    {
        List<AdviceGenerationTrace> traces = [];
        if (!File.Exists(path))
        {
            return traces;
        }

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                AdviceGenerationTrace? trace = JsonSerializer.Deserialize<AdviceGenerationTrace>(line, JsonOptions);
                if (trace is not null)
                {
                    traces.Add(trace);
                }
            }
            catch (JsonException) { }
        }

        return traces;
    }

    private static AnalysisQualityReport BuildReport(
        List<ClassifierDiagnosticEntry> classifierEntries,
        List<AdviceGenerationTrace> adviceTraces)
    {
        int totalClassified = classifierEntries.Count;
        int lowConfidence = classifierEntries.Count(e => e.DiagnosticReason.StartsWith("low_confidence", StringComparison.Ordinal));
        int unclassified = classifierEntries.Count(e => string.Equals(e.AssignedLabel, "missed_tactic", StringComparison.Ordinal)
            && e.Evidence.Any(ev => string.Equals(ev, "engine_prefers_tactical_alternative", StringComparison.Ordinal)));
        int genericFallback = classifierEntries.Count(e => string.Equals(e.DiagnosticReason, "generic_fallback_label", StringComparison.Ordinal));

        IReadOnlyList<LabelQualityStat> labelStats = classifierEntries
            .GroupBy(e => e.AssignedLabel, StringComparer.Ordinal)
            .Select(g => new LabelQualityStat(
                g.Key,
                g.Count(),
                g.Average(e => e.Confidence),
                g.Count(e => e.DiagnosticReason.StartsWith("low_confidence", StringComparison.Ordinal))))
            .OrderByDescending(s => s.Count)
            .ToList();

        int totalAdvice = adviceTraces.Count;
        int fallbackAdvice = adviceTraces.Count(t => t.UsedFallback);
        IReadOnlyDictionary<string, int> fallbackReasons = adviceTraces
            .Where(t => t.UsedFallback && !string.IsNullOrWhiteSpace(t.FallbackReason))
            .GroupBy(t => t.FallbackReason!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new AnalysisQualityReport(
            DateTime.UtcNow,
            TotalClassifiedMoves: totalClassified,
            LowConfidenceMoves: lowConfidence,
            LowConfidenceRate: totalClassified == 0 ? 0.0 : Math.Round((double)lowConfidence / totalClassified, 4),
            UnclassifiedMoves: unclassified,
            UnclassifiedRate: totalClassified == 0 ? 0.0 : Math.Round((double)unclassified / totalClassified, 4),
            GenericFallbackMoves: genericFallback,
            GenericFallbackRate: totalClassified == 0 ? 0.0 : Math.Round((double)genericFallback / totalClassified, 4),
            LabelStats: labelStats,
            TotalAdviceTraces: totalAdvice,
            FallbackAdviceCount: fallbackAdvice,
            FallbackAdviceRate: totalAdvice == 0 ? 0.0 : Math.Round((double)fallbackAdvice / totalAdvice, 4),
            FallbackReasonBreakdown: fallbackReasons);
    }

    public static void RunReport()
    {
        Console.WriteLine("=== Analysis Quality Report ===");
        Console.WriteLine();

        AnalysisQualityReport report = Build();

        string reportText = FormatReport(report);
        Console.Write(reportText);

        string outputPath = Path.Combine(AppContext.BaseDirectory, "analysis-quality-report.md");
        File.WriteAllText(outputPath, reportText);
        Console.WriteLine($"Report saved to: {outputPath}");
    }

    private static string FormatReport(AnalysisQualityReport r)
    {
        System.Text.StringBuilder sb = new();
        sb.AppendLine("# Analysis Quality Report");
        sb.AppendLine($"Generated: {r.GeneratedUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("## Classifier");
        sb.AppendLine($"- Logged entries (low-confidence + generic fallback): **{r.TotalClassifiedMoves}**");
        sb.AppendLine($"- Low confidence (< 0.70): **{r.LowConfidenceMoves}** ({r.LowConfidenceRate:P1})");
        sb.AppendLine($"- Generic fallback `missed_tactic`: **{r.GenericFallbackMoves}** ({r.GenericFallbackRate:P1})");
        sb.AppendLine($"- Unclassified pattern: **{r.UnclassifiedMoves}** ({r.UnclassifiedRate:P1})");
        sb.AppendLine();

        if (r.LabelStats.Count > 0)
        {
            sb.AppendLine("### Label breakdown (low-confidence log)");
            sb.AppendLine("| Label | Count | Avg Confidence | Low Conf |");
            sb.AppendLine("|-------|------:|---------------:|---------:|");
            foreach (LabelQualityStat stat in r.LabelStats)
            {
                sb.AppendLine($"| {stat.Label} | {stat.Count} | {stat.AverageConfidence:F2} | {stat.LowConfidenceCount} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Advice Generator");
        sb.AppendLine($"- Total advice traces: **{r.TotalAdviceTraces}**");
        sb.AppendLine($"- Fallback advice: **{r.FallbackAdviceCount}** ({r.FallbackAdviceRate:P1})");

        if (r.FallbackReasonBreakdown.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Fallback reason breakdown");
            foreach (KeyValuePair<string, int> pair in r.FallbackReasonBreakdown.OrderByDescending(p => p.Value))
            {
                sb.AppendLine($"- `{pair.Key}`: {pair.Value}");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
