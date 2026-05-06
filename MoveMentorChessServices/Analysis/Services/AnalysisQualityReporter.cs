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
        string feedbackLogPath = AdviceFeedbackLogger.DefaultLogPath();
        string qualityGateLogPath = QualityGateDiagnosticsLogger.DefaultLogPath();

        List<ClassifierDiagnosticEntry> classifierEntries = LoadClassifierEntries(classifierLogPath);
        List<AdviceGenerationTrace> adviceTraces = LoadAdviceTraces(adviceLogPath);
        List<AdviceFeedbackEntry> feedbackEntries = LoadJsonl<AdviceFeedbackEntry>(feedbackLogPath);
        List<QualityGateReport> qualityGateReports = LoadJsonl<QualityGateReport>(qualityGateLogPath);
        IReadOnlyList<MoveAdviceFeedback> manualFeedback = AnalysisStoreProvider.GetStore()?.ListMoveAdviceFeedback(limit: 200_000) ?? [];

        return BuildReport(classifierEntries, adviceTraces, feedbackEntries, qualityGateReports, manualFeedback);
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
        => LoadJsonl<AdviceGenerationTrace>(path);

    private static List<T> LoadJsonl<T>(string path)
    {
        List<T> entries = [];
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
                T? entry = JsonSerializer.Deserialize<T>(line, JsonOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException) { }
        }

        return entries;
    }

    private static AnalysisQualityReport BuildReport(
        List<ClassifierDiagnosticEntry> classifierEntries,
        List<AdviceGenerationTrace> adviceTraces,
        List<AdviceFeedbackEntry> feedbackEntries,
        List<QualityGateReport> qualityGateReports,
        IReadOnlyList<MoveAdviceFeedback> manualFeedback)
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

        IReadOnlyList<LabelFeedbackStat> labelFeedbackStats = feedbackEntries
            .GroupBy(e => e.Label, StringComparer.Ordinal)
            .Select(g =>
            {
                int total = g.Count();
                int negative = g.Count(IsNegativeFeedback);
                return new LabelFeedbackStat(
                    g.Key,
                    total,
                    negative,
                    total == 0 ? 0.0 : Math.Round((double)negative / total, 4));
            })
            .OrderByDescending(s => s.NegativeRate)
            .ThenByDescending(s => s.Total)
            .ToList();

        List<QualityGateFinding> qualityGateFindings = qualityGateReports
            .SelectMany(r => r.Findings)
            .ToList();
        IReadOnlyDictionary<string, int> qualityGateCodeBreakdown = qualityGateFindings
            .GroupBy(f => f.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        IReadOnlyDictionary<string, int> manualFeedbackKindBreakdown = manualFeedback
            .GroupBy(item => item.FeedbackKind.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        IReadOnlyList<ManualLabelCorrectionStat> manualLabelCorrectionStats = manualFeedback
            .Where(item => item.FeedbackKind == AdviceFeedbackKind.WrongLabel
                && !string.IsNullOrWhiteSpace(item.CorrectedLabel))
            .GroupBy(
                item => new ManualCorrectionKey(item.OriginalLabel ?? "unclassified", item.CorrectedLabel!),
                ManualCorrectionKeyComparer.Instance)
            .Select(group => new ManualLabelCorrectionStat(
                group.Key.OriginalLabel,
                group.Key.CorrectedLabel,
                group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.OriginalLabel, StringComparer.Ordinal)
            .ThenBy(item => item.CorrectedLabel, StringComparer.Ordinal)
            .Take(20)
            .ToList();

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
            FallbackReasonBreakdown: fallbackReasons,
            HelpfulFeedbackCount: feedbackEntries.Count(e => e.FeedbackKind is AdviceFeedbackKind.Correct or AdviceFeedbackKind.GoodExplanation),
            TooVagueFeedbackCount: feedbackEntries.Count(e => e.FeedbackKind == AdviceFeedbackKind.TooGeneric),
            DoNotUnderstandFeedbackCount: feedbackEntries.Count(e => e.FeedbackKind == AdviceFeedbackKind.NotUseful),
            LooksWrongFeedbackCount: feedbackEntries.Count(e => e.FeedbackKind == AdviceFeedbackKind.WrongLabel),
            GoodTrainingTipFeedbackCount: feedbackEntries.Count(e => e.FeedbackKind == AdviceFeedbackKind.GoodExplanation),
            LabelFeedbackStats: labelFeedbackStats,
            QualityGateFindingCount: qualityGateFindings.Count,
            QualityGateFailureCount: qualityGateFindings.Count(f => f.Severity == QualityGateSeverity.Failure),
            QualityGateCorrectedCount: qualityGateReports.Sum(r => r.CorrectedCount),
            QualityGateFallbackCount: qualityGateReports.Sum(r => r.FallbackCount),
            QualityGateCodeBreakdown: qualityGateCodeBreakdown,
            ManualFeedbackCount: manualFeedback.Count,
            ManualFeedbackKindBreakdown: manualFeedbackKindBreakdown,
            ManualLabelCorrectionStats: manualLabelCorrectionStats,
            ManualDiagnosticCaseCount: manualFeedback.Count(item => item.FeedbackKind is AdviceFeedbackKind.WrongLabel or AdviceFeedbackKind.NotUseful or AdviceFeedbackKind.TooGeneric));
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
        sb.AppendLine("## User feedback");
        sb.AppendLine($"- Manual feedback events: **{r.ManualFeedbackCount}**");
        sb.AppendLine($"- Diagnostic cases (`WrongLabel`, `NotUseful`, `TooGeneric`): **{r.ManualDiagnosticCaseCount}**");

        if (r.ManualFeedbackKindBreakdown is { Count: > 0 })
        {
            foreach (KeyValuePair<string, int> pair in r.ManualFeedbackKindBreakdown.OrderByDescending(item => item.Value))
            {
                sb.AppendLine($"- `{pair.Key}`: {pair.Value}");
            }
        }

        if (r.LabelFeedbackStats is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("### Labels with weakest feedback");
            sb.AppendLine("| Label | Total | Negative | Negative Rate |");
            sb.AppendLine("|-------|------:|---------:|--------------:|");
            foreach (LabelFeedbackStat stat in r.LabelFeedbackStats.Take(10))
            {
                sb.AppendLine($"| {stat.Label} | {stat.Total} | {stat.NegativeCount} | {stat.NegativeRate:P1} |");
            }
        }

        if (r.ManualLabelCorrectionStats is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("### Manual label corrections");
            sb.AppendLine("| Original | Corrected | Count |");
            sb.AppendLine("|----------|-----------|------:|");
            foreach (ManualLabelCorrectionStat stat in r.ManualLabelCorrectionStats)
            {
                sb.AppendLine($"| {stat.OriginalLabel} | {stat.CorrectedLabel} | {stat.Count} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Quality Gate");
        sb.AppendLine($"- Findings: **{r.QualityGateFindingCount}**");
        sb.AppendLine($"- Failures: **{r.QualityGateFailureCount}**");
        sb.AppendLine($"- Corrected: **{r.QualityGateCorrectedCount}**");
        sb.AppendLine($"- Advice fallbacks: **{r.QualityGateFallbackCount}**");

        if (r.QualityGateCodeBreakdown is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("### Finding code breakdown");
            foreach (KeyValuePair<string, int> pair in r.QualityGateCodeBreakdown.OrderByDescending(p => p.Value))
            {
                sb.AppendLine($"- `{pair.Key}`: {pair.Value}");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static bool IsNegativeFeedback(AdviceFeedbackEntry entry)
    {
        return entry.FeedbackKind is AdviceFeedbackKind.NotUseful
            or AdviceFeedbackKind.TooGeneric
            or AdviceFeedbackKind.WrongLabel;
    }

    private sealed record ManualCorrectionKey(string OriginalLabel, string CorrectedLabel);

    private sealed class ManualCorrectionKeyComparer : IEqualityComparer<ManualCorrectionKey>
    {
        public static ManualCorrectionKeyComparer Instance { get; } = new();

        public bool Equals(ManualCorrectionKey? x, ManualCorrectionKey? y)
            => x is not null
                && y is not null
                && string.Equals(x.OriginalLabel, y.OriginalLabel, StringComparison.Ordinal)
                && string.Equals(x.CorrectedLabel, y.CorrectedLabel, StringComparison.Ordinal);

        public int GetHashCode(ManualCorrectionKey obj)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.OriginalLabel),
                StringComparer.Ordinal.GetHashCode(obj.CorrectedLabel));
    }
}
