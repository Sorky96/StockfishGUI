using System.Runtime.InteropServices;
using System.Text.Json;

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
                int bindNullResult = sqlite3_bind_null(Handle, index);
                ThrowIfError(bindNullResult, database.Handle, $"bind null parameter {index}");
                return;
            }

            BindText(index, value);
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
