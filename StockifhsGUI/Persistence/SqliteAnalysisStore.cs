using System.Runtime.InteropServices;
using System.Text.Json;
using System.Globalization;

namespace StockifhsGUI;

public sealed class SqliteAnalysisStore : IAnalysisStore, IOpeningTreeStore, IOpeningTheoryStore
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
        return new SqliteAnalysisStore(GetDefaultDatabasePath());
    }

    public static string GetDefaultDatabasePath()
    {
        string baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StockifhsGUI");
        return Path.Combine(baseDirectory, "analysis-cache.db");
    }

    public void SaveOpeningTree(OpeningTreeBuildResult tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery("BEGIN IMMEDIATE;");
            try
            {
                SaveOpeningTree(database, tree);
                database.ExecuteNonQuery("COMMIT;");
            }
            catch
            {
                database.ExecuteNonQuery("ROLLBACK;");
                throw;
            }
        }
    }

    public void ReplaceOpeningTree(OpeningTreeBuildResult tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery("BEGIN IMMEDIATE;");
            try
            {
                database.ExecuteNonQuery("DELETE FROM opening_node_tags;");
                database.ExecuteNonQuery("DELETE FROM opening_move_edges;");
                database.ExecuteNonQuery("DELETE FROM opening_position_nodes;");
                SaveOpeningTree(database, tree);
                database.ExecuteNonQuery("COMMIT;");
            }
            catch
            {
                database.ExecuteNonQuery("ROLLBACK;");
                throw;
            }
        }
    }

    public OpeningTreeBuildResult LoadOpeningTree()
    {
        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            Dictionary<string, Guid> nodeIdMap = new(StringComparer.OrdinalIgnoreCase);
            List<OpeningPositionNode> nodes = new();
            using (SqliteStatement statement = database.Prepare("""
                SELECT id, position_key, fen, ply, move_number, side_to_move, occurrence_count, distinct_game_count
                FROM opening_position_nodes
                ORDER BY ply ASC, position_key ASC;
                """))
            {
                while (statement.Step() == SqliteRow)
                {
                    OpeningPositionNode node = new()
                    {
                        Id = ParseGuid(statement.GetText(0)),
                        PositionKey = statement.GetText(1) ?? string.Empty,
                        Fen = statement.GetText(2) ?? string.Empty,
                        Ply = statement.GetInt(3),
                        MoveNumber = statement.GetInt(4),
                        SideToMove = statement.GetText(5) ?? string.Empty,
                        OccurrenceCount = statement.GetInt(6),
                        DistinctGameCount = statement.GetInt(7)
                    };
                    nodes.Add(node);
                    nodeIdMap[statement.GetText(0) ?? string.Empty] = node.Id;
                }
            }

            List<OpeningMoveEdge> edges = new();
            using (SqliteStatement statement = database.Prepare("""
                SELECT id, from_node_id, to_node_id, move_uci, move_san, occurrence_count, distinct_game_count, is_main_move, is_playable_move, rank_within_position
                FROM opening_move_edges
                ORDER BY rank_within_position ASC, occurrence_count DESC, move_san ASC;
                """))
            {
                while (statement.Step() == SqliteRow)
                {
                    string fromNodeId = statement.GetText(1) ?? string.Empty;
                    string toNodeId = statement.GetText(2) ?? string.Empty;
                    edges.Add(new OpeningMoveEdge
                    {
                        Id = ParseGuid(statement.GetText(0)),
                        FromNodeId = nodeIdMap.TryGetValue(fromNodeId, out Guid fromGuid) ? fromGuid : Guid.Empty,
                        ToNodeId = nodeIdMap.TryGetValue(toNodeId, out Guid toGuid) ? toGuid : Guid.Empty,
                        MoveUci = statement.GetText(3) ?? string.Empty,
                        MoveSan = statement.GetText(4) ?? string.Empty,
                        OccurrenceCount = statement.GetInt(5),
                        DistinctGameCount = statement.GetInt(6),
                        IsMainMove = statement.GetInt(7) != 0,
                        IsPlayableMove = statement.GetInt(8) != 0,
                        RankWithinPosition = statement.GetInt(9)
                    });
                }
            }

            List<OpeningNodeTag> tags = new();
            using (SqliteStatement statement = database.Prepare("""
                SELECT id, node_id, eco, opening_name, variation_name, source_kind
                FROM opening_node_tags
                ORDER BY node_id ASC, eco ASC, opening_name ASC, variation_name ASC;
                """))
            {
                while (statement.Step() == SqliteRow)
                {
                    string nodeId = statement.GetText(1) ?? string.Empty;
                    tags.Add(new OpeningNodeTag
                    {
                        Id = ParseGuid(statement.GetText(0)),
                        NodeId = nodeIdMap.TryGetValue(nodeId, out Guid nodeGuid) ? nodeGuid : Guid.Empty,
                        Eco = statement.GetText(2) ?? string.Empty,
                        OpeningName = statement.GetText(3) ?? string.Empty,
                        VariationName = statement.GetText(4) ?? string.Empty,
                        SourceKind = statement.GetText(5) ?? string.Empty
                    });
                }
            }

            return new OpeningTreeBuildResult(nodes, edges, tags);
        }
    }

    public string? GetOpeningSeedVersion()
    {
        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                SELECT value
                FROM app_metadata
                WHERE key = 'opening_tree_seed_version'
                LIMIT 1;
                """);

            return statement.Step() == SqliteRow ? statement.GetText(0) : null;
        }
    }

    public void SetOpeningSeedVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery(
                """
                INSERT INTO app_metadata (key, value)
                VALUES ('opening_tree_seed_version', ?1)
                ON CONFLICT (key)
                DO UPDATE SET value = excluded.value;
                """,
                statement => statement.BindText(1, version));
        }
    }

    public OpeningTreeStoreSummary GetOpeningTreeSummary()
    {
        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            return new OpeningTreeStoreSummary(
                CountRows(database, "opening_position_nodes"),
                CountRows(database, "opening_move_edges"),
                CountRows(database, "opening_node_tags"));
        }
    }

    public bool TryGetOpeningPositionByKey(string positionKey, out OpeningTheoryPosition? position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionKey);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                SELECT
                    opening_position_nodes.id,
                    opening_position_nodes.position_key,
                    opening_position_nodes.fen,
                    opening_position_nodes.ply,
                    opening_position_nodes.move_number,
                    opening_position_nodes.side_to_move,
                    opening_position_nodes.occurrence_count,
                    opening_position_nodes.distinct_game_count,
                    coalesce(opening_node_tags.eco, ''),
                    coalesce(opening_node_tags.opening_name, ''),
                    coalesce(opening_node_tags.variation_name, '')
                FROM opening_position_nodes
                LEFT JOIN opening_node_tags ON opening_node_tags.node_id = opening_position_nodes.id
                WHERE opening_position_nodes.position_key = ?1
                ORDER BY opening_node_tags.source_kind = 'pgn' DESC
                LIMIT 1;
                """);

            statement.BindText(1, positionKey);
            if (statement.Step() != SqliteRow)
            {
                position = null;
                return false;
            }

            position = ReadOpeningTheoryPosition(statement);
            return true;
        }
    }

    public IReadOnlyList<OpeningTheoryMove> GetOpeningMovesByPositionKey(
        string positionKey,
        int limit = 10,
        bool playableOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionKey);

        int safeLimit = Math.Clamp(limit, 1, 100);
        List<OpeningTheoryMove> moves = new();

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare($"""
                SELECT
                    opening_move_edges.id,
                    opening_move_edges.from_node_id,
                    opening_move_edges.to_node_id,
                    opening_move_edges.move_uci,
                    opening_move_edges.move_san,
                    opening_move_edges.occurrence_count,
                    opening_move_edges.distinct_game_count,
                    opening_move_edges.is_main_move,
                    opening_move_edges.is_playable_move,
                    opening_move_edges.rank_within_position,
                    to_nodes.position_key,
                    to_nodes.fen,
                    coalesce(opening_node_tags.eco, ''),
                    coalesce(opening_node_tags.opening_name, ''),
                    coalesce(opening_node_tags.variation_name, '')
                FROM opening_move_edges
                INNER JOIN opening_position_nodes AS from_nodes
                    ON from_nodes.id = opening_move_edges.from_node_id
                INNER JOIN opening_position_nodes AS to_nodes
                    ON to_nodes.id = opening_move_edges.to_node_id
                LEFT JOIN opening_node_tags
                    ON opening_node_tags.node_id = to_nodes.id
                WHERE from_nodes.position_key = ?1
                  {(playableOnly ? "AND opening_move_edges.is_playable_move = 1" : string.Empty)}
                ORDER BY
                    opening_move_edges.rank_within_position = 0 ASC,
                    opening_move_edges.rank_within_position ASC,
                    opening_move_edges.occurrence_count DESC,
                    opening_move_edges.move_san ASC
                LIMIT {safeLimit};
                """);

            statement.BindText(1, positionKey);
            while (statement.Step() == SqliteRow)
            {
                moves.Add(ReadOpeningTheoryMove(statement));
            }
        }

        return moves;
    }

    public void SaveImportedGame(ImportedGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery("BEGIN IMMEDIATE;");
            try
            {
                SaveImportedGames(database, [game]);
                database.ExecuteNonQuery("COMMIT;");
            }
            catch
            {
                database.ExecuteNonQuery("ROLLBACK;");
                throw;
            }
        }
    }

    public void SaveImportedGames(IReadOnlyList<ImportedGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        if (games.Count == 0)
        {
            return;
        }

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery("BEGIN IMMEDIATE;");
            try
            {
                SaveImportedGames(database, games);
                database.ExecuteNonQuery("COMMIT;");
            }
            catch
            {
                database.ExecuteNonQuery("ROLLBACK;");
                throw;
            }
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
            database.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS app_metadata (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """);
            EnsureColumnExists(
                database,
                "analysis_window_states",
                "explanation_level_index",
                "INTEGER NOT NULL DEFAULT 1");
            EnsureOpeningTreeSchema(database);
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

    private static void EnsureOpeningTreeSchema(SqliteDatabase database)
    {
        database.ExecuteNonQuery("""
            CREATE TABLE IF NOT EXISTS opening_position_nodes (
                id TEXT NOT NULL PRIMARY KEY,
                position_key TEXT NOT NULL UNIQUE,
                fen TEXT NOT NULL,
                ply INTEGER NOT NULL,
                move_number INTEGER NOT NULL,
                side_to_move TEXT NOT NULL,
                occurrence_count INTEGER NOT NULL,
                distinct_game_count INTEGER NOT NULL
            );
            """);
        database.ExecuteNonQuery("""
            CREATE TABLE IF NOT EXISTS opening_move_edges (
                id TEXT NOT NULL PRIMARY KEY,
                from_node_id TEXT NOT NULL,
                to_node_id TEXT NOT NULL,
                move_uci TEXT NOT NULL,
                move_san TEXT NOT NULL,
                occurrence_count INTEGER NOT NULL,
                distinct_game_count INTEGER NOT NULL,
                is_main_move INTEGER NOT NULL,
                is_playable_move INTEGER NOT NULL,
                rank_within_position INTEGER NOT NULL,
                UNIQUE (from_node_id, move_uci, to_node_id)
            );
            """);
        database.ExecuteNonQuery("""
            CREATE INDEX IF NOT EXISTS idx_opening_position_nodes_position_key
            ON opening_position_nodes (position_key);
            """);
        database.ExecuteNonQuery("""
            CREATE INDEX IF NOT EXISTS idx_opening_move_edges_from_node_id
            ON opening_move_edges (from_node_id);
            """);
        database.ExecuteNonQuery("""
            CREATE TABLE IF NOT EXISTS opening_node_tags (
                id TEXT NOT NULL PRIMARY KEY,
                node_id TEXT NOT NULL,
                eco TEXT NOT NULL,
                opening_name TEXT NOT NULL,
                variation_name TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                UNIQUE (node_id, eco, opening_name, variation_name, source_kind)
            );
            """);
        database.ExecuteNonQuery("""
            CREATE INDEX IF NOT EXISTS idx_opening_node_tags_node_id
            ON opening_node_tags (node_id);
            """);
    }

    private static string? LoadOpeningNodeId(SqliteDatabase database, string positionKey)
    {
        using SqliteStatement statement = database.Prepare("""
            SELECT id
            FROM opening_position_nodes
            WHERE position_key = ?1
            LIMIT 1;
            """);

        statement.BindText(1, positionKey);
        return statement.Step() == SqliteRow ? statement.GetText(0) : null;
    }

    private static string? LoadOpeningEdgeId(SqliteDatabase database, string fromNodeId, string moveUci, string toNodeId)
    {
        using SqliteStatement statement = database.Prepare("""
            SELECT id
            FROM opening_move_edges
            WHERE from_node_id = ?1
              AND move_uci = ?2
              AND to_node_id = ?3
            LIMIT 1;
            """);

        statement.BindText(1, fromNodeId);
        statement.BindText(2, moveUci);
        statement.BindText(3, toNodeId);
        return statement.Step() == SqliteRow ? statement.GetText(0) : null;
    }

    private static void UpsertOpeningNode(SqliteDatabase database, OpeningPositionNode node, string nodeId)
    {
        database.ExecuteNonQuery(
            """
            INSERT INTO opening_position_nodes (
                id,
                position_key,
                fen,
                ply,
                move_number,
                side_to_move,
                occurrence_count,
                distinct_game_count)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
            ON CONFLICT (position_key)
            DO UPDATE SET
                fen = excluded.fen,
                ply = excluded.ply,
                move_number = excluded.move_number,
                side_to_move = excluded.side_to_move,
                occurrence_count = excluded.occurrence_count,
                distinct_game_count = excluded.distinct_game_count;
            """,
            statement =>
            {
                statement.BindText(1, nodeId);
                statement.BindText(2, node.PositionKey);
                statement.BindText(3, node.Fen);
                statement.BindInt(4, node.Ply);
                statement.BindInt(5, node.MoveNumber);
                statement.BindText(6, node.SideToMove);
                statement.BindInt(7, node.OccurrenceCount);
                statement.BindInt(8, node.DistinctGameCount);
            });
    }

    private static void UpsertOpeningEdge(
        SqliteDatabase database,
        OpeningMoveEdge edge,
        string edgeId,
        string fromNodeId,
        string toNodeId)
    {
        database.ExecuteNonQuery(
            """
            INSERT INTO opening_move_edges (
                id,
                from_node_id,
                to_node_id,
                move_uci,
                move_san,
                occurrence_count,
                distinct_game_count,
                is_main_move,
                is_playable_move,
                rank_within_position)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)
            ON CONFLICT (from_node_id, move_uci, to_node_id)
            DO UPDATE SET
                move_san = excluded.move_san,
                occurrence_count = excluded.occurrence_count,
                distinct_game_count = excluded.distinct_game_count,
                is_main_move = excluded.is_main_move,
                is_playable_move = excluded.is_playable_move,
                rank_within_position = excluded.rank_within_position;
            """,
            statement =>
            {
                statement.BindText(1, edgeId);
                statement.BindText(2, fromNodeId);
                statement.BindText(3, toNodeId);
                statement.BindText(4, edge.MoveUci);
                statement.BindText(5, edge.MoveSan);
                statement.BindInt(6, edge.OccurrenceCount);
                statement.BindInt(7, edge.DistinctGameCount);
                statement.BindInt(8, edge.IsMainMove ? 1 : 0);
                statement.BindInt(9, edge.IsPlayableMove ? 1 : 0);
                statement.BindInt(10, edge.RankWithinPosition);
            });
    }

    private static void DeleteOpeningNodeTags(SqliteDatabase database, string nodeId)
    {
        database.ExecuteNonQuery(
            """
            DELETE FROM opening_node_tags
            WHERE node_id = ?1;
            """,
            statement => statement.BindText(1, nodeId));
    }

    private static void UpsertOpeningNodeTag(SqliteDatabase database, OpeningNodeTag tag, string nodeId)
    {
        database.ExecuteNonQuery(
            """
            INSERT INTO opening_node_tags (
                id,
                node_id,
                eco,
                opening_name,
                variation_name,
                source_kind)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6)
            ON CONFLICT (node_id, eco, opening_name, variation_name, source_kind)
            DO UPDATE SET
                eco = excluded.eco,
                opening_name = excluded.opening_name,
                variation_name = excluded.variation_name,
                source_kind = excluded.source_kind;
            """,
            statement =>
            {
                statement.BindText(1, tag.Id.ToString("D"));
                statement.BindText(2, nodeId);
                statement.BindText(3, tag.Eco);
                statement.BindText(4, tag.OpeningName);
                statement.BindText(5, tag.VariationName);
                statement.BindText(6, tag.SourceKind);
            });
    }

    private static void SaveImportedGames(SqliteDatabase database, IReadOnlyList<ImportedGame> games)
    {
        string timestamp = DateTime.UtcNow.ToString("O");
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

        foreach (ImportedGame game in games)
        {
            string gameFingerprint = GameFingerprint.Compute(game.PgnText);
            statement.Reset();
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

    private static int CountRows(SqliteDatabase database, string tableName)
    {
        using SqliteStatement statement = database.Prepare($"SELECT COUNT(*) FROM {tableName};");
        return statement.Step() == SqliteRow ? statement.GetInt(0) : 0;
    }

    private static void SaveOpeningTree(SqliteDatabase database, OpeningTreeBuildResult tree)
    {
        Dictionary<Guid, string> persistedNodeIds = new();

        foreach (OpeningPositionNode node in tree.Nodes)
        {
            string nodeId = LoadOpeningNodeId(database, node.PositionKey) ?? node.Id.ToString("D");
            UpsertOpeningNode(database, node, nodeId);
            persistedNodeIds[node.Id] = nodeId;
        }

        foreach (OpeningMoveEdge edge in tree.Edges)
        {
            if (!persistedNodeIds.TryGetValue(edge.FromNodeId, out string? fromNodeId)
                || !persistedNodeIds.TryGetValue(edge.ToNodeId, out string? toNodeId))
            {
                throw new InvalidOperationException("Opening edge references a node that was not saved.");
            }

            string edgeId = LoadOpeningEdgeId(database, fromNodeId, edge.MoveUci, toNodeId)
                ?? edge.Id.ToString("D");
            UpsertOpeningEdge(database, edge, edgeId, fromNodeId, toNodeId);
        }

        foreach (string persistedNodeId in persistedNodeIds.Values)
        {
            DeleteOpeningNodeTags(database, persistedNodeId);
        }

        foreach (OpeningNodeTag tag in tree.Tags)
        {
            if (!persistedNodeIds.TryGetValue(tag.NodeId, out string? nodeId))
            {
                throw new InvalidOperationException("Opening tag references a node that was not saved.");
            }

            UpsertOpeningNodeTag(database, tag, nodeId);
        }
    }

    private static OpeningTheoryPosition ReadOpeningTheoryPosition(SqliteStatement statement)
    {
        return new OpeningTheoryPosition(
            ParseGuid(statement.GetText(0)),
            statement.GetText(1) ?? string.Empty,
            statement.GetText(2) ?? string.Empty,
            statement.GetInt(3),
            statement.GetInt(4),
            statement.GetText(5) ?? string.Empty,
            statement.GetInt(6),
            statement.GetInt(7),
            new OpeningGameMetadata(
                statement.GetText(8) ?? string.Empty,
                statement.GetText(9) ?? string.Empty,
                statement.GetText(10) ?? string.Empty));
    }

    private static OpeningTheoryMove ReadOpeningTheoryMove(SqliteStatement statement)
    {
        return new OpeningTheoryMove(
            ParseGuid(statement.GetText(0)),
            ParseGuid(statement.GetText(1)),
            ParseGuid(statement.GetText(2)),
            statement.GetText(3) ?? string.Empty,
            statement.GetText(4) ?? string.Empty,
            statement.GetInt(5),
            statement.GetInt(6),
            statement.GetInt(7) != 0,
            statement.GetInt(8) != 0,
            statement.GetInt(9),
            statement.GetText(10) ?? string.Empty,
            statement.GetText(11) ?? string.Empty,
            new OpeningGameMetadata(
                statement.GetText(12) ?? string.Empty,
                statement.GetText(13) ?? string.Empty,
                statement.GetText(14) ?? string.Empty));
    }

    private static Guid ParseGuid(string? value)
    {
        return Guid.TryParse(value, out Guid parsed) ? parsed : Guid.Empty;
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

        public void Reset()
        {
            int resetResult = sqlite3_reset(Handle);
            ThrowIfError(resetResult, database.Handle, "reset statement");

            int clearResult = sqlite3_clear_bindings(Handle);
            ThrowIfError(clearResult, database.Handle, "clear statement bindings");
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

    [DllImport("winsqlite3", EntryPoint = "sqlite3_reset")]
    private static extern int sqlite3_reset(IntPtr statement);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_clear_bindings")]
    private static extern int sqlite3_clear_bindings(IntPtr statement);

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
