using System.Runtime.InteropServices;
using System.Text.Json;
using System.Globalization;

namespace StockifhsGUI;

public sealed class SqliteAnalysisStore : IAnalysisStore
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteNull = 5;
    private const int NoMoveTimeMs = -1;

    private static readonly IntPtr SqliteTransient = new(-1);
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly string databasePath;
    private readonly object sync = new();

    public SqliteAnalysisStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        this.databasePath = databasePath;
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        InitializeSchema();
    }

    public static SqliteAnalysisStore CreateDefault()
    {
        string baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StockifhsGUI");
        string databasePath = Path.Combine(baseDirectory, "analysis-cache.db");
        return new SqliteAnalysisStore(databasePath);
    }

    public void SaveImportedGame(ImportedGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        string gameFingerprint = GameFingerprint.Compute(game.PgnText);
        string timestamp = DateTime.UtcNow.ToString("O");

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                INSERT INTO imported_games (
                    game_fingerprint,
                    pgn_text,
                    white_player,
                    black_player,
                    date_text,
                    result_text,
                    eco,
                    site,
                    updated_utc)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)
                ON CONFLICT (game_fingerprint)
                DO UPDATE SET
                    pgn_text = excluded.pgn_text,
                    white_player = excluded.white_player,
                    black_player = excluded.black_player,
                    date_text = excluded.date_text,
                    result_text = excluded.result_text,
                    eco = excluded.eco,
                    site = excluded.site,
                    updated_utc = excluded.updated_utc;
                """);

            statement.BindText(1, gameFingerprint);
            statement.BindText(2, game.PgnText);
            statement.BindNullableText(3, game.WhitePlayer);
            statement.BindNullableText(4, game.BlackPlayer);
            statement.BindNullableText(5, game.DateText);
            statement.BindNullableText(6, game.Result);
            statement.BindNullableText(7, game.Eco);
            statement.BindNullableText(8, game.Site);
            statement.BindText(9, timestamp);
            statement.StepUntilDone();
        }
    }

    public bool TryLoadImportedGame(string gameFingerprint, out ImportedGame? game)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFingerprint);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                SELECT pgn_text
                FROM imported_games
                WHERE game_fingerprint = ?1
                LIMIT 1;
                """);

            statement.BindText(1, gameFingerprint);
            int stepResult = statement.Step();
            if (stepResult != SqliteRow)
            {
                game = null;
                return false;
            }

            string? pgnText = statement.GetText(0);
            if (string.IsNullOrWhiteSpace(pgnText))
            {
                game = null;
                return false;
            }

            game = PgnGameParser.Parse(pgnText);
            return true;
        }
    }

    public bool DeleteImportedGame(string gameFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFingerprint);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            bool exists = database.Exists(
                """
                SELECT 1
                FROM imported_games
                WHERE game_fingerprint = ?1
                LIMIT 1;
                """,
                statement => statement.BindText(1, gameFingerprint));

            database.ExecuteNonQuery(
                """
                DELETE FROM analysis_moves
                WHERE game_fingerprint = ?1;
                """,
                statement => statement.BindText(1, gameFingerprint));
            database.ExecuteNonQuery(
                """
                DELETE FROM analysis_results
                WHERE game_fingerprint = ?1;
                """,
                statement => statement.BindText(1, gameFingerprint));
            database.ExecuteNonQuery(
                """
                DELETE FROM analysis_window_states
                WHERE game_fingerprint = ?1;
                """,
                statement => statement.BindText(1, gameFingerprint));
            database.ExecuteNonQuery(
                """
                DELETE FROM imported_games
                WHERE game_fingerprint = ?1;
                """,
                statement => statement.BindText(1, gameFingerprint));

            return exists;
        }
    }

    public IReadOnlyList<SavedImportedGameSummary> ListImportedGames(string? filterText = null, int limit = 200)
    {
        string normalizedFilter = filterText?.Trim().ToLowerInvariant() ?? string.Empty;
        int safeLimit = Math.Clamp(limit, 1, 1000);
        List<SavedImportedGameSummary> items = new();

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare($"""
                SELECT game_fingerprint, white_player, black_player, date_text, result_text, eco, site, updated_utc
                FROM imported_games
                {(string.IsNullOrWhiteSpace(normalizedFilter)
                    ? string.Empty
                    : "WHERE lower(coalesce(white_player, '')) LIKE ?1 OR lower(coalesce(black_player, '')) LIKE ?1 OR lower(coalesce(date_text, '')) LIKE ?1 OR lower(coalesce(result_text, '')) LIKE ?1 OR lower(coalesce(eco, '')) LIKE ?1 OR lower(coalesce(site, '')) LIKE ?1")}
                ORDER BY updated_utc DESC
                LIMIT {safeLimit};
                """);

            if (!string.IsNullOrWhiteSpace(normalizedFilter))
            {
                statement.BindText(1, $"%{normalizedFilter}%");
            }

            while (statement.Step() == SqliteRow)
            {
                string fingerprint = statement.GetText(0) ?? string.Empty;
                string? white = statement.GetText(1);
                string? black = statement.GetText(2);
                string? dateText = statement.GetText(3);
                string? result = statement.GetText(4);
                string? eco = statement.GetText(5);
                string? site = statement.GetText(6);
                string? updatedUtcText = statement.GetText(7);
                DateTime.TryParse(updatedUtcText, out DateTime updatedUtc);

                items.Add(new SavedImportedGameSummary(
                    fingerprint,
                    BuildDisplayTitle(white, black, dateText, result, eco),
                    white,
                    black,
                    dateText,
                    result,
                    eco,
                    site,
                    updatedUtc));
            }
        }

        return items;
    }

    public IReadOnlyList<GameAnalysisResult> ListResults(string? filterText = null, int limit = 500)
    {
        string normalizedFilter = filterText?.Trim().ToLowerInvariant() ?? string.Empty;
        int safeLimit = Math.Clamp(limit, 1, 5000);
        List<GameAnalysisResult> items = new();

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare($"""
                SELECT analysis_results.payload_json
                FROM analysis_results
                LEFT JOIN imported_games ON imported_games.game_fingerprint = analysis_results.game_fingerprint
                {(string.IsNullOrWhiteSpace(normalizedFilter)
                    ? string.Empty
                    : "WHERE lower(coalesce(imported_games.white_player, '')) LIKE ?1 OR lower(coalesce(imported_games.black_player, '')) LIKE ?1 OR lower(coalesce(imported_games.date_text, '')) LIKE ?1 OR lower(coalesce(imported_games.result_text, '')) LIKE ?1 OR lower(coalesce(imported_games.eco, '')) LIKE ?1 OR lower(coalesce(imported_games.site, '')) LIKE ?1")}
                ORDER BY analysis_results.updated_utc DESC
                LIMIT {safeLimit};
                """);

            if (!string.IsNullOrWhiteSpace(normalizedFilter))
            {
                statement.BindText(1, $"%{normalizedFilter}%");
            }

            while (statement.Step() == SqliteRow)
            {
                string? payload = statement.GetText(0);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                GameAnalysisResult? item = JsonSerializer.Deserialize<GameAnalysisResult>(payload, JsonOptions);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    public IReadOnlyList<StoredMoveAnalysis> ListMoveAnalyses(string? filterText = null, int limit = 5000)
    {
        string normalizedFilter = filterText?.Trim().ToLowerInvariant() ?? string.Empty;
        int safeLimit = Math.Clamp(limit, 1, 20000);
        List<StoredMoveAnalysis> items = new();

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare($"""
                SELECT
                    analysis_moves.game_fingerprint,
                    analysis_moves.analyzed_side,
                    analysis_moves.depth,
                    analysis_moves.multi_pv,
                    analysis_moves.move_time_ms,
                    analysis_results.updated_utc,
                    imported_games.white_player,
                    imported_games.black_player,
                    imported_games.date_text,
                    imported_games.result_text,
                    imported_games.eco,
                    imported_games.site,
                    analysis_moves.ply,
                    analysis_moves.move_number,
                    analysis_moves.san,
                    analysis_moves.move_uci,
                    analysis_moves.fen_before,
                    analysis_moves.fen_after,
                    analysis_moves.phase,
                    analysis_moves.eval_before_cp,
                    analysis_moves.eval_after_cp,
                    analysis_moves.best_mate_in,
                    analysis_moves.played_mate_in,
                    analysis_moves.centipawn_loss,
                    analysis_moves.quality,
                    analysis_moves.material_delta_cp,
                    analysis_moves.best_move_uci,
                    analysis_moves.mistake_label,
                    analysis_moves.mistake_confidence,
                    analysis_moves.evidence_json,
                    analysis_moves.short_explanation,
                    analysis_moves.detailed_explanation,
                    analysis_moves.training_hint,
                    analysis_moves.is_highlighted
                FROM analysis_moves
                LEFT JOIN analysis_results ON analysis_results.game_fingerprint = analysis_moves.game_fingerprint
                    AND analysis_results.analyzed_side = analysis_moves.analyzed_side
                    AND analysis_results.depth = analysis_moves.depth
                    AND analysis_results.multi_pv = analysis_moves.multi_pv
                    AND analysis_results.move_time_ms = analysis_moves.move_time_ms
                LEFT JOIN imported_games ON imported_games.game_fingerprint = analysis_moves.game_fingerprint
                {(string.IsNullOrWhiteSpace(normalizedFilter)
                    ? string.Empty
                    : "WHERE lower(coalesce(imported_games.white_player, '')) LIKE ?1 OR lower(coalesce(imported_games.black_player, '')) LIKE ?1 OR lower(coalesce(imported_games.date_text, '')) LIKE ?1 OR lower(coalesce(imported_games.result_text, '')) LIKE ?1 OR lower(coalesce(imported_games.eco, '')) LIKE ?1 OR lower(coalesce(imported_games.site, '')) LIKE ?1 OR lower(coalesce(analysis_moves.mistake_label, '')) LIKE ?1 OR lower(coalesce(analysis_moves.san, '')) LIKE ?1 OR lower(coalesce(analysis_moves.move_uci, '')) LIKE ?1")}
                ORDER BY imported_games.updated_utc DESC, analysis_moves.ply ASC
                LIMIT {safeLimit};
                """);

            if (!string.IsNullOrWhiteSpace(normalizedFilter))
            {
                statement.BindText(1, $"%{normalizedFilter}%");
            }

            while (statement.Step() == SqliteRow)
            {
                items.Add(new StoredMoveAnalysis(
                    statement.GetText(0) ?? string.Empty,
                    (PlayerSide)statement.GetInt(1),
                    statement.GetInt(2),
                    statement.GetInt(3),
                    ReadMoveTime(statement.GetInt(4)),
                    ParseUtc(statement.GetText(5)),
                    statement.GetText(6),
                    statement.GetText(7),
                    statement.GetText(8),
                    statement.GetText(9),
                    statement.GetText(10),
                    statement.GetText(11),
                    statement.GetInt(12),
                    statement.GetInt(13),
                    statement.GetText(14) ?? string.Empty,
                    statement.GetText(15) ?? string.Empty,
                    statement.GetText(16) ?? string.Empty,
                    statement.GetText(17) ?? string.Empty,
                    (GamePhase)statement.GetInt(18),
                    statement.GetNullableInt(19),
                    statement.GetNullableInt(20),
                    statement.GetNullableInt(21),
                    statement.GetNullableInt(22),
                    statement.GetNullableInt(23),
                    (MoveQualityBucket)statement.GetInt(24),
                    statement.GetInt(25),
                    statement.GetText(26),
                    statement.GetText(27),
                    ParseNullableDouble(statement.GetText(28)),
                    DeserializeEvidence(statement.GetText(29)),
                    statement.GetText(30),
                    statement.GetText(31),
                    statement.GetText(32),
                    statement.GetInt(33) != 0));
            }
        }

        return items;
    }

    public bool TryLoadResult(GameAnalysisCacheKey key, out GameAnalysisResult? result)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                SELECT payload_json
                FROM analysis_results
                WHERE game_fingerprint = ?1
                  AND analyzed_side = ?2
                  AND depth = ?3
                  AND multi_pv = ?4
                  AND move_time_ms = ?5
                LIMIT 1;
                """);

            statement.BindText(1, key.GameFingerprint);
            statement.BindInt(2, (int)key.Side);
            statement.BindInt(3, key.Depth);
            statement.BindInt(4, key.MultiPv);
            statement.BindInt(5, NormalizeMoveTime(key.MoveTimeMs));

            int stepResult = statement.Step();
            if (stepResult != SqliteRow)
            {
                result = null;
                return false;
            }

            string? payload = statement.GetText(0);
            if (string.IsNullOrWhiteSpace(payload))
            {
                result = null;
                return false;
            }

            result = JsonSerializer.Deserialize<GameAnalysisResult>(payload, JsonOptions);
            return result is not null;
        }
    }

    public void SaveResult(GameAnalysisCacheKey key, GameAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(result);

        SaveImportedGame(result.Game);

        string payload = JsonSerializer.Serialize(result, JsonOptions);
        string timestamp = DateTime.UtcNow.ToString("O");

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                INSERT INTO analysis_results (
                    game_fingerprint,
                    analyzed_side,
                    depth,
                    multi_pv,
                    move_time_ms,
                    payload_json,
                    created_utc,
                    updated_utc)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
                ON CONFLICT (game_fingerprint, analyzed_side, depth, multi_pv, move_time_ms)
                DO UPDATE SET
                    payload_json = excluded.payload_json,
                    updated_utc = excluded.updated_utc;
                """);

            statement.BindText(1, key.GameFingerprint);
            statement.BindInt(2, (int)key.Side);
            statement.BindInt(3, key.Depth);
            statement.BindInt(4, key.MultiPv);
            statement.BindInt(5, NormalizeMoveTime(key.MoveTimeMs));
            statement.BindText(6, payload);
            statement.BindText(7, timestamp);
            statement.BindText(8, timestamp);
            statement.StepUntilDone();

            ReplaceMoveAnalyses(database, key, result);
        }
    }

    public bool TryLoadWindowState(string gameFingerprint, out AnalysisWindowState? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFingerprint);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                SELECT selected_side, quality_filter_index, explanation_level_index
                FROM analysis_window_states
                WHERE game_fingerprint = ?1
                LIMIT 1;
                """);

            statement.BindText(1, gameFingerprint);
            int stepResult = statement.Step();
            if (stepResult != SqliteRow)
            {
                state = null;
                return false;
            }

            state = new AnalysisWindowState(
                (PlayerSide)statement.GetInt(0),
                statement.GetInt(1),
                statement.GetInt(2));
            return true;
        }
    }

    public void SaveWindowState(string gameFingerprint, AnalysisWindowState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFingerprint);
        ArgumentNullException.ThrowIfNull(state);

        string timestamp = DateTime.UtcNow.ToString("O");

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                INSERT INTO analysis_window_states (
                    game_fingerprint,
                    selected_side,
                    quality_filter_index,
                    explanation_level_index,
                    updated_utc)
                VALUES (?1, ?2, ?3, ?4, ?5)
                ON CONFLICT (game_fingerprint)
                DO UPDATE SET
                    selected_side = excluded.selected_side,
                    quality_filter_index = excluded.quality_filter_index,
                    explanation_level_index = excluded.explanation_level_index,
                    updated_utc = excluded.updated_utc;
                """);

            statement.BindText(1, gameFingerprint);
            statement.BindInt(2, (int)state.SelectedSide);
            statement.BindInt(3, state.QualityFilterIndex);
            statement.BindInt(4, state.ExplanationLevelIndex);
            statement.BindText(5, timestamp);
            statement.StepUntilDone();
        }
    }

    private void InitializeSchema()
    {
        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS imported_games (
                    game_fingerprint TEXT NOT NULL PRIMARY KEY,
                    pgn_text TEXT NOT NULL,
                    white_player TEXT NULL,
                    black_player TEXT NULL,
                    date_text TEXT NULL,
                    result_text TEXT NULL,
                    eco TEXT NULL,
                    site TEXT NULL,
                    updated_utc TEXT NOT NULL
                );
                """);
            database.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS analysis_results (
                    game_fingerprint TEXT NOT NULL,
                    analyzed_side INTEGER NOT NULL,
                    depth INTEGER NOT NULL,
                    multi_pv INTEGER NOT NULL,
                    move_time_ms INTEGER NOT NULL,
                    payload_json TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    updated_utc TEXT NOT NULL,
                    PRIMARY KEY (game_fingerprint, analyzed_side, depth, multi_pv, move_time_ms)
                );
                """);
            database.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS analysis_moves (
                    game_fingerprint TEXT NOT NULL,
                    analyzed_side INTEGER NOT NULL,
                    depth INTEGER NOT NULL,
                    multi_pv INTEGER NOT NULL,
                    move_time_ms INTEGER NOT NULL,
                    ply INTEGER NOT NULL,
                    move_number INTEGER NOT NULL,
                    san TEXT NOT NULL,
                    move_uci TEXT NOT NULL,
                    fen_before TEXT NOT NULL,
                    fen_after TEXT NOT NULL,
                    phase INTEGER NOT NULL,
                    eval_before_cp INTEGER NULL,
                    eval_after_cp INTEGER NULL,
                    best_mate_in INTEGER NULL,
                    played_mate_in INTEGER NULL,
                    centipawn_loss INTEGER NULL,
                    quality INTEGER NOT NULL,
                    material_delta_cp INTEGER NOT NULL,
                    best_move_uci TEXT NULL,
                    mistake_label TEXT NULL,
                    mistake_confidence TEXT NULL,
                    evidence_json TEXT NULL,
                    short_explanation TEXT NULL,
                    detailed_explanation TEXT NULL,
                    training_hint TEXT NULL,
                    is_highlighted INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (game_fingerprint, analyzed_side, depth, multi_pv, move_time_ms, ply)
                );
                """);
            database.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS analysis_window_states (
                    game_fingerprint TEXT NOT NULL PRIMARY KEY,
                    selected_side INTEGER NOT NULL,
                    quality_filter_index INTEGER NOT NULL,
                    explanation_level_index INTEGER NOT NULL DEFAULT 1,
                    updated_utc TEXT NOT NULL
                );
                """);
            EnsureColumnExists(
                database,
                "analysis_window_states",
                "explanation_level_index",
                "INTEGER NOT NULL DEFAULT 1");
        }
    }

    private SqliteDatabase OpenDatabase() => new(databasePath);

    private static int NormalizeMoveTime(int? moveTimeMs) => moveTimeMs ?? NoMoveTimeMs;

    private static int? ReadMoveTime(int rawMoveTime) => rawMoveTime == NoMoveTimeMs ? null : rawMoveTime;

    private static DateTime ParseUtc(string? value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out DateTime parsed)
            ? parsed
            : DateTime.MinValue;
    }

    private static IReadOnlyList<string> DeserializeEvidence(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(payload, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static double? ParseNullableDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }

    private static string? FormatNullableDouble(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.####", CultureInfo.InvariantCulture)
            : null;
    }

    private static string SerializeEvidence(IReadOnlyList<string>? evidence)
    {
        return JsonSerializer.Serialize(evidence ?? [], JsonOptions);
    }

    private static bool IsHighlightedMove(MoveAnalysisResult move, HashSet<int> highlightedPlys)
    {
        return highlightedPlys.Contains(move.Replay.Ply);
    }

    private static void BindNullableInt(SqliteStatement statement, int index, int? value)
    {
        if (value.HasValue)
        {
            statement.BindInt(index, value.Value);
            return;
        }

        statement.BindNull(index);
    }

    private static void ReplaceMoveAnalyses(SqliteDatabase database, GameAnalysisCacheKey key, GameAnalysisResult result)
    {
        database.ExecuteNonQuery(
            """
            DELETE FROM analysis_moves
            WHERE game_fingerprint = ?1
              AND analyzed_side = ?2
              AND depth = ?3
              AND multi_pv = ?4
              AND move_time_ms = ?5;
            """,
            statement =>
            {
                statement.BindText(1, key.GameFingerprint);
                statement.BindInt(2, (int)key.Side);
                statement.BindInt(3, key.Depth);
                statement.BindInt(4, key.MultiPv);
                statement.BindInt(5, NormalizeMoveTime(key.MoveTimeMs));
            });

        HashSet<int> highlightedPlys = result.HighlightedMistakes
            .SelectMany(mistake => mistake.Moves)
            .Select(move => move.Replay.Ply)
            .ToHashSet();

        foreach (MoveAnalysisResult move in result.MoveAnalyses)
        {
            database.ExecuteNonQuery(
                """
                INSERT INTO analysis_moves (
                    game_fingerprint,
                    analyzed_side,
                    depth,
                    multi_pv,
                    move_time_ms,
                    ply,
                    move_number,
                    san,
                    move_uci,
                    fen_before,
                    fen_after,
                    phase,
                    eval_before_cp,
                    eval_after_cp,
                    best_mate_in,
                    played_mate_in,
                    centipawn_loss,
                    quality,
                    material_delta_cp,
                    best_move_uci,
                    mistake_label,
                    mistake_confidence,
                    evidence_json,
                    short_explanation,
                    detailed_explanation,
                    training_hint,
                    is_highlighted)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21, ?22, ?23, ?24, ?25, ?26, ?27);
                """,
                statement =>
                {
                    statement.BindText(1, key.GameFingerprint);
                    statement.BindInt(2, (int)key.Side);
                    statement.BindInt(3, key.Depth);
                    statement.BindInt(4, key.MultiPv);
                    statement.BindInt(5, NormalizeMoveTime(key.MoveTimeMs));
                    statement.BindInt(6, move.Replay.Ply);
                    statement.BindInt(7, move.Replay.MoveNumber);
                    statement.BindText(8, move.Replay.San);
                    statement.BindText(9, move.Replay.Uci);
                    statement.BindText(10, move.Replay.FenBefore);
                    statement.BindText(11, move.Replay.FenAfter);
                    statement.BindInt(12, (int)move.Replay.Phase);
                    BindNullableInt(statement, 13, move.EvalBeforeCp);
                    BindNullableInt(statement, 14, move.EvalAfterCp);
                    BindNullableInt(statement, 15, move.BestMateIn);
                    BindNullableInt(statement, 16, move.PlayedMateIn);
                    BindNullableInt(statement, 17, move.CentipawnLoss);
                    statement.BindInt(18, (int)move.Quality);
                    statement.BindInt(19, move.MaterialDeltaCp);
                    statement.BindNullableText(20, move.BeforeAnalysis.BestMoveUci);
                    statement.BindNullableText(21, move.MistakeTag?.Label);
                    statement.BindNullableText(22, FormatNullableDouble(move.MistakeTag?.Confidence));
                    statement.BindText(23, SerializeEvidence(move.MistakeTag?.Evidence));
                    statement.BindNullableText(24, move.Explanation?.ShortText);
                    statement.BindNullableText(25, move.Explanation?.DetailedText);
                    statement.BindNullableText(26, move.Explanation?.TrainingHint);
                    statement.BindInt(27, IsHighlightedMove(move, highlightedPlys) ? 1 : 0);
                });
        }
    }

    private static void EnsureColumnExists(SqliteDatabase database, string tableName, string columnName, string definition)
    {
        using SqliteStatement statement = database.Prepare($"PRAGMA table_info({tableName});");
        while (statement.Step() == SqliteRow)
        {
            if (string.Equals(statement.GetText(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        database.ExecuteNonQuery($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};");
    }

    private sealed class SqliteDatabase : IDisposable
    {
        public SqliteDatabase(string path)
        {
            int result = sqlite3_open16(path, out IntPtr handle);
            if (result != SqliteOk)
            {
                string message = handle == IntPtr.Zero ? "unknown error" : GetErrorMessage(handle);
                if (handle != IntPtr.Zero)
                {
                    sqlite3_close(handle);
                }

                throw new InvalidOperationException($"Unable to open SQLite database '{path}': {message}");
            }

            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void ExecuteNonQuery(string sql)
        {
            using SqliteStatement statement = Prepare(sql);
            statement.StepUntilDone();
        }

        public void ExecuteNonQuery(string sql, Action<SqliteStatement> bind)
        {
            using SqliteStatement statement = Prepare(sql);
            bind(statement);
            statement.StepUntilDone();
        }

        public bool Exists(string sql, Action<SqliteStatement> bind)
        {
            using SqliteStatement statement = Prepare(sql);
            bind(statement);
            return statement.Step() == SqliteRow;
        }

        public SqliteStatement Prepare(string sql)
        {
            int result = sqlite3_prepare16_v2(Handle, sql, -1, out IntPtr statement, IntPtr.Zero);
            ThrowIfError(result, Handle, $"prepare SQL '{sql}'");
            return new SqliteStatement(this, statement);
        }

        public void Dispose()
        {
            sqlite3_close(Handle);
        }
    }

    private sealed class SqliteStatement : IDisposable
    {
        private readonly SqliteDatabase database;

        public SqliteStatement(SqliteDatabase database, IntPtr handle)
        {
            this.database = database;
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void BindText(int index, string value)
        {
            int result = sqlite3_bind_text16(Handle, index, value, -1, SqliteTransient);
            ThrowIfError(result, database.Handle, $"bind text parameter {index}");
        }

        public void BindNullableText(int index, string? value)
        {
            if (value is null)
            {
                BindNull(index);
                return;
            }

            BindText(index, value);
        }

        public void BindNull(int index)
        {
            int bindNullResult = sqlite3_bind_null(Handle, index);
            ThrowIfError(bindNullResult, database.Handle, $"bind null parameter {index}");
        }

        public void BindInt(int index, int value)
        {
            int result = sqlite3_bind_int(Handle, index, value);
            ThrowIfError(result, database.Handle, $"bind int parameter {index}");
        }

        public int Step()
        {
            int result = sqlite3_step(Handle);
            if (result is SqliteRow or SqliteDone)
            {
                return result;
            }

            ThrowIfError(result, database.Handle, "execute statement");
            return result;
        }

        public void StepUntilDone()
        {
            int result = Step();
            if (result != SqliteDone)
            {
                throw new InvalidOperationException("SQLite statement returned rows when no rows were expected.");
            }
        }

        public int GetInt(int columnIndex)
        {
            return sqlite3_column_int(Handle, columnIndex);
        }

        public int? GetNullableInt(int columnIndex)
        {
            return sqlite3_column_type(Handle, columnIndex) == SqliteNull
                ? null
                : sqlite3_column_int(Handle, columnIndex);
        }

        public string? GetText(int columnIndex)
        {
            if (sqlite3_column_type(Handle, columnIndex) == SqliteNull)
            {
                return null;
            }

            IntPtr textPointer = sqlite3_column_text16(Handle, columnIndex);
            return textPointer == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUni(textPointer);
        }

        public void Dispose()
        {
            sqlite3_finalize(Handle);
        }
    }

    private static void ThrowIfError(int result, IntPtr databaseHandle, string operation)
    {
        if (result == SqliteOk)
        {
            return;
        }

        throw new InvalidOperationException($"SQLite failed to {operation}: {GetErrorMessage(databaseHandle)}");
    }

    private static string GetErrorMessage(IntPtr databaseHandle)
    {
        IntPtr pointer = sqlite3_errmsg16(databaseHandle);
        return pointer == IntPtr.Zero
            ? "unknown error"
            : Marshal.PtrToStringUni(pointer) ?? "unknown error";
    }

    private static string BuildDisplayTitle(string? whitePlayer, string? blackPlayer, string? dateText, string? result, string? eco)
    {
        string players = $"{whitePlayer ?? "White"} vs {blackPlayer ?? "Black"}";
        string datePart = string.IsNullOrWhiteSpace(dateText) ? string.Empty : $" | {dateText}";
        string resultPart = string.IsNullOrWhiteSpace(result) ? string.Empty : $" | {result}";
        string ecoPart = string.IsNullOrWhiteSpace(eco) ? string.Empty : $" | {OpeningCatalog.Describe(eco)}";
        return players + datePart + resultPart + ecoPart;
    }

    [DllImport("winsqlite3", CharSet = CharSet.Unicode, EntryPoint = "sqlite3_open16")]
    private static extern int sqlite3_open16(string filename, out IntPtr db);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_close")]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("winsqlite3", CharSet = CharSet.Unicode, EntryPoint = "sqlite3_prepare16_v2")]
    private static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int numBytes, out IntPtr statement, IntPtr tail);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_step")]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_finalize")]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3", CharSet = CharSet.Unicode, EntryPoint = "sqlite3_bind_text16")]
    private static extern int sqlite3_bind_text16(IntPtr statement, int index, string value, int length, IntPtr destructor);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_bind_null")]
    private static extern int sqlite3_bind_null(IntPtr statement, int index);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_bind_int")]
    private static extern int sqlite3_bind_int(IntPtr statement, int index, int value);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_column_int")]
    private static extern int sqlite3_column_int(IntPtr statement, int columnIndex);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_column_type")]
    private static extern int sqlite3_column_type(IntPtr statement, int columnIndex);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_column_text16")]
    private static extern IntPtr sqlite3_column_text16(IntPtr statement, int columnIndex);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_errmsg16")]
    private static extern IntPtr sqlite3_errmsg16(IntPtr db);
}
