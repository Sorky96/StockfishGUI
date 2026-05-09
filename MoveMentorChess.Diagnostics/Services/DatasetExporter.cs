using System.Globalization;
using System.Text;
using System.Text.Json;
using MoveMentorChess.Domain;

namespace MoveMentorChess.Diagnostics;

/// <summary>
/// Exports stored move analyses to a JSONL or CSV dataset for offline
/// experiments with local models.  All I/O is local; no external calls.
/// </summary>
public static class DatasetExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Builds the dataset from the SQLite analysis store and saves it.
    /// Returns the number of rows written.
    /// </summary>
    public static int ExportJsonl(IStoredMoveAnalysisStore store, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        IReadOnlyList<DatasetRow> rows = BuildRows(store);
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using StreamWriter writer = new(outputPath, append: false, Encoding.UTF8);
        foreach (DatasetRow row in rows)
        {
            writer.WriteLine(JsonSerializer.Serialize(row, JsonOptions));
        }

        return rows.Count;
    }

    /// <summary>
    /// Builds the dataset and saves it as CSV (header row + data rows).
    /// Returns the number of data rows written.
    /// </summary>
    public static int ExportCsv(IStoredMoveAnalysisStore store, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        IReadOnlyList<DatasetRow> rows = BuildRows(store);
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using StreamWriter writer = new(outputPath, append: false, Encoding.UTF8);
        writer.WriteLine(CsvHeader());
        foreach (DatasetRow row in rows)
        {
            writer.WriteLine(ToCsvLine(row));
        }

        return rows.Count;
    }

    public static void RunExport(IStoredMoveAnalysisStore store)
    {
        Console.WriteLine("=== Dataset Export ===");
        Console.WriteLine();

        string baseDir = AppContext.BaseDirectory;
        string jsonlPath = Path.Combine(baseDir, "dataset-export.jsonl");
        string csvPath = Path.Combine(baseDir, "dataset-export.csv");

        int jsonlCount = ExportJsonl(store, jsonlPath);
        Console.WriteLine($"JSONL: {jsonlCount} rows -> {jsonlPath}");

        int csvCount = ExportCsv(store, csvPath);
        Console.WriteLine($"CSV:   {csvCount} rows -> {csvPath}");
        Console.WriteLine();
        Console.WriteLine("Export complete. Use these files for offline model experiments.");
    }

    private static IReadOnlyList<DatasetRow> BuildRows(IStoredMoveAnalysisStore store)
    {
        IReadOnlyList<StoredMoveAnalysis> storedMoves = store.ListMoveAnalyses(null, 200_000);
        return storedMoves
            .Where(move => !string.IsNullOrWhiteSpace(move.Move.FenBefore))
            .Select(ToDatasetRow)
            .OrderBy(row => row.GameFingerprint, StringComparer.Ordinal)
            .ThenBy(row => row.Ply)
            .ToList();
    }

    private static DatasetRow ToDatasetRow(StoredMoveAnalysis move)
    {
        StoredGameContext game = move.Game;
        StoredAnalysisRunContext analysis = move.Analysis;
        StoredMoveContext played = move.Move;
        StoredMoveAdviceContext advice = move.Advice;
        StoredManualFeedbackContext? manualFeedback = move.ManualFeedback;

        return new DatasetRow(
            game.GameFingerprint,
            played.Ply,
            played.MoveNumber,
            analysis.AnalyzedSide.ToString(),
            played.Phase.ToString(),
            played.San,
            played.Uci,
            played.BestMoveUci,
            played.Quality.ToString(),
            played.CentipawnLoss,
            played.MaterialDeltaCp,
            advice.OriginalMistakeLabel ?? advice.MistakeLabel,
            advice.MistakeLabel,
            advice.MistakeConfidence,
            string.Join("|", advice.Evidence),
            manualFeedback?.ManualFeedbackKind?.ToString(),
            manualFeedback?.ManualCorrectedLabel,
            manualFeedback?.ManualComment,
            manualFeedback?.ManualCorrectedUtc,
            advice.ShortExplanation,
            advice.DetailedExplanation,
            advice.TrainingHint,
            advice.IsHighlighted,
            played.FenBefore,
            played.FenAfter);
    }

    private static string CsvHeader()
    {
        return "game_fingerprint,ply,move_number,side,phase,played_san,played_uci," +
               "best_move_uci,quality,centipawn_loss,material_delta_cp," +
               "original_mistake_label,effective_mistake_label,mistake_confidence,evidence," +
               "manual_feedback_kind,manual_corrected_label,manual_comment,manual_corrected_utc," +
               "short_explanation,detailed_explanation,training_hint," +
               "is_highlighted,fen_before,fen_after";
    }

    private static string ToCsvLine(DatasetRow row)
    {
        return string.Join(",", [
            CsvEscape(row.GameFingerprint),
            row.Ply.ToString(CultureInfo.InvariantCulture),
            row.MoveNumber.ToString(CultureInfo.InvariantCulture),
            CsvEscape(row.Side),
            CsvEscape(row.Phase),
            CsvEscape(row.PlayedSan),
            CsvEscape(row.PlayedUci),
            CsvEscape(row.BestMoveUci ?? string.Empty),
            CsvEscape(row.Quality),
            row.CentipawnLoss.HasValue ? row.CentipawnLoss.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            row.MaterialDeltaCp.ToString(CultureInfo.InvariantCulture),
            CsvEscape(row.OriginalMistakeLabel ?? string.Empty),
            CsvEscape(row.EffectiveMistakeLabel ?? string.Empty),
            row.MistakeConfidence.HasValue ? row.MistakeConfidence.Value.ToString("F4", CultureInfo.InvariantCulture) : string.Empty,
            CsvEscape(row.Evidence),
            CsvEscape(row.ManualFeedbackKind ?? string.Empty),
            CsvEscape(row.ManualCorrectedLabel ?? string.Empty),
            CsvEscape(row.ManualComment ?? string.Empty),
            row.ManualCorrectedUtc.HasValue ? row.ManualCorrectedUtc.Value.ToString("O", CultureInfo.InvariantCulture) : string.Empty,
            CsvEscape(row.ShortExplanation ?? string.Empty),
            CsvEscape(row.DetailedExplanation ?? string.Empty),
            CsvEscape(row.TrainingHint ?? string.Empty),
            row.IsHighlighted ? "1" : "0",
            CsvEscape(row.FenBefore),
            CsvEscape(row.FenAfter)
        ]);
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
