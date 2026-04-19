using System.Text.Json;

namespace StockifhsGUI;

/// <summary>
/// Wraps <see cref="MistakeClassifier"/> and records an entry to JSONL
/// whenever the result has low confidence or falls back to the generic label.
/// Logging is always fire-and-forget — it never throws or blocks analysis.
/// </summary>
public sealed class DiagnosticMistakeClassifier
{
    private const double LowConfidenceThreshold = 0.70;
    private const string GenericFallbackLabel = "missed_tactic";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly MistakeClassifier inner;
    private readonly string logFilePath;
    private readonly object sync = new();

    public DiagnosticMistakeClassifier(MistakeClassifier? inner = null, string? logFilePath = null)
    {
        this.inner = inner ?? new MistakeClassifier();
        this.logFilePath = logFilePath ?? DefaultLogPath();
        string? dir = Path.GetDirectoryName(this.logFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public static DiagnosticMistakeClassifier CreateDefault() => new();

    public static string DefaultLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StockifhsGUI",
            "classifier-low-confidence.jsonl");
    }

    public MistakeTag? Classify(
        ReplayPly replay,
        string gameFingerprint,
        PlayerSide analyzedSide,
        MoveQualityBucket quality,
        int? centipawnLoss,
        int materialDeltaCp,
        MoveHeuristicContext? context = null)
    {
        MistakeTag? tag = inner.Classify(replay, analyzedSide, quality, centipawnLoss, materialDeltaCp, context);
        TryLog(replay, gameFingerprint, quality, centipawnLoss, materialDeltaCp, tag);
        return tag;
    }

    private void TryLog(
        ReplayPly replay,
        string gameFingerprint,
        MoveQualityBucket quality,
        int? centipawnLoss,
        int materialDeltaCp,
        MistakeTag? tag)
    {
        if (tag is null)
        {
            return;
        }

        string? diagnosticReason = GetDiagnosticReason(tag);
        if (diagnosticReason is null)
        {
            return;
        }

        ClassifierDiagnosticEntry entry = new(
            DateTime.UtcNow,
            gameFingerprint,
            replay.Ply,
            replay.MoveNumber,
            replay.Side,
            replay.Phase,
            replay.San,
            replay.Uci,
            null,
            quality,
            centipawnLoss,
            materialDeltaCp,
            tag.Label,
            tag.Confidence,
            tag.Evidence,
            diagnosticReason);

        try
        {
            string line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (sync)
            {
                File.AppendAllText(logFilePath, line + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? GetDiagnosticReason(MistakeTag tag)
    {
        if (tag.Label == GenericFallbackLabel
            && tag.Evidence.Any(e => string.Equals(e, "engine_prefers_tactical_alternative", StringComparison.Ordinal)))
        {
            return "generic_fallback_label";
        }

        if (tag.Confidence < LowConfidenceThreshold)
        {
            return $"low_confidence_{tag.Confidence:F2}";
        }

        return null;
    }
}
