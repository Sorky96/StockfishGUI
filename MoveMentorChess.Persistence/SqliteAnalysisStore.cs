using System.Runtime.InteropServices;
using System.Text.Json;
using System.Globalization;

namespace MoveMentorChess.Persistence;

public sealed class SqliteAnalysisStore :
    IAnalysisStore,
    IImportedGameStore,
    IAnalysisResultStore,
    IStoredMoveAnalysisStore,
    IAdviceFeedbackStore,
    IAnalysisWindowStateStore,
    IOpeningTreeStore,
    IOpeningTheoryStore,
    IOpeningTrainingHistoryStore
{
    private const char CompositeKeySeparator = '|';
    private const string AppDataDirectoryName = "MoveMentorChessServices";
    private const string DatabaseFileName = "analysis-cache.db";
    private const string DerivedAnalysisDataVersionKey = "derived_analysis_data_version";
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteNull = 5;
    private const int NoMoveTimeMs = -1;
    public const string CurrentDerivedAnalysisDataVersion = "derived-analysis-v1";

    private static readonly IntPtr SqliteTransient = new(-1);
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly string databasePath;
    private readonly string derivedAnalysisDataVersion;
    private readonly bool applyDerivedAnalysisDataVersionPolicy;
    private readonly object sync = new();

    public SqliteAnalysisStore(
        string databasePath,
        string derivedAnalysisDataVersion = CurrentDerivedAnalysisDataVersion,
        bool applyDerivedAnalysisDataVersionPolicy = true)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(derivedAnalysisDataVersion);

        this.databasePath = databasePath;
        this.derivedAnalysisDataVersion = derivedAnalysisDataVersion;
        this.applyDerivedAnalysisDataVersionPolicy = applyDerivedAnalysisDataVersionPolicy;
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
            AppDataDirectoryName);
        return Path.Combine(baseDirectory, DatabaseFileName);
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

    public IReadOnlyList<OpeningLineCatalogItem> ListOpeningLines(string? filterText = null, RepertoireSide? repertoireSide = null, int limit = 100)
    {
        int safeLimit = Math.Clamp(limit, 1, 500);
        List<OpeningLineCatalogItem> items = [];

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare($"""
                SELECT
                    coalesce(tags.eco, ''),
                    coalesce(tags.opening_name, ''),
                    coalesce(tags.variation_name, ''),
                    nodes.position_key,
                    nodes.fen,
                    nodes.side_to_move,
                    nodes.distinct_game_count,
                    (
                        SELECT COUNT(*)
                        FROM opening_move_edges edges
                        WHERE edges.from_node_id = nodes.id
                    ) AS branch_count
                FROM opening_position_nodes nodes
                INNER JOIN opening_node_tags tags ON tags.node_id = nodes.id
                WHERE nodes.ply <= 12
                ORDER BY nodes.distinct_game_count DESC, tags.eco ASC, tags.opening_name ASC, tags.variation_name ASC
                LIMIT {safeLimit * 4};
                """);

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            while (statement.Step() == SqliteRow)
            {
                string eco = statement.GetText(0) ?? string.Empty;
                string openingName = statement.GetText(1) ?? string.Empty;
                string variationName = statement.GetText(2) ?? string.Empty;
                OpeningPositionKey rootPositionKey = new(statement.GetText(3) ?? string.Empty);
                string fen = statement.GetText(4) ?? string.Empty;
                RepertoireSide side = ParseRepertoireSide(statement.GetText(5));
                int gameCount = statement.GetInt(6);
                int branchCount = statement.GetInt(7);

                if (repertoireSide.HasValue
                    && repertoireSide.Value != RepertoireSide.Both
                    && side != repertoireSide.Value)
                {
                    continue;
                }

                string displayName = BuildDisplayName(eco, openingName, variationName);
                if (!string.IsNullOrWhiteSpace(filterText)
                    && displayName.Contains(filterText, StringComparison.OrdinalIgnoreCase) == false
                    && eco.Contains(filterText, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                string dedupeKey = $"{eco}|{openingName}|{variationName}|{side}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                OpeningKey openingKey = new(BuildOpeningKey(eco, openingName));
                OpeningLineKey lineKey = new(BuildOpeningLineKey(eco, openingName, variationName, side, rootPositionKey.Value));
                items.Add(new OpeningLineCatalogItem(
                    openingKey,
                    lineKey,
                    side,
                    eco,
                    openingName,
                    variationName,
                    displayName,
                    rootPositionKey,
                    fen,
                    gameCount,
                    branchCount));

                if (items.Count >= safeLimit)
                {
                    break;
                }
            }
        }

        return items;
    }

    public bool TryGetOpeningOverview(
        OpeningLineKey lineKey,
        RepertoireSide repertoireSide,
        int maxDepth,
        out OpeningTrainerOverview? overview)
    {
        if (!TryParseOpeningLineKey(lineKey.Value, out string eco, out string openingName, out string variationName, out RepertoireSide parsedSide, out OpeningPositionKey rootPositionKey))
        {
            overview = null;
            return false;
        }

        OpeningKey openingKey = new(BuildOpeningKey(eco, openingName));
        List<OpeningLineMove> mainLine = [];
        List<OpeningTrainingBranch> branches = [];
        List<OpeningMoveIdea> ideas = [];
        OpeningPositionKey currentPositionKey = rootPositionKey;
        string? currentFen = null;
        int maxPly = Math.Max(1, maxDepth);

        if (TryGetOpeningPositionByKey(currentPositionKey.Value, out OpeningTheoryPosition? rootPosition) && rootPosition is not null)
        {
            currentFen = rootPosition.Fen;
        }

        for (int ply = 0; ply < maxPly; ply++)
        {
            IReadOnlyList<OpeningTheoryMove> moves = GetOpeningMovesByPositionKey(currentPositionKey.Value, 6, playableOnly: false);
            if (moves.Count == 0)
            {
                break;
            }

            OpeningTheoryMove primary = moves[0];
            OpeningMoveIdea primaryIdea = primary.Idea ?? BuildOpeningMoveIdea(primary.MoveSan, primary.IsMainMove);
            ideas.Add(primaryIdea);

            if (TryGetOpeningPositionByKey(primary.ToPositionKey, out OpeningTheoryPosition? nextPosition) && nextPosition is not null)
            {
                mainLine.Add(new OpeningLineMove(
                    nextPosition.Ply,
                    nextPosition.MoveNumber,
                    ParsePlayerSide(nextPosition.SideToMove) == PlayerSide.White ? PlayerSide.Black : PlayerSide.White,
                    primary.MoveSan,
                    primary.MoveUci,
                    currentPositionKey,
                    primary.ToOpeningPositionKey,
                    primary.IsMainMove,
                    primaryIdea));
                currentPositionKey = primary.ToOpeningPositionKey;
                currentFen = nextPosition.Fen;
            }
            else
            {
                break;
            }
        }

        IReadOnlyList<OpeningTheoryMove> branchMoves = GetOpeningMovesByPositionKey(rootPositionKey.Value, 5, playableOnly: false);
        foreach (OpeningTheoryMove move in branchMoves)
        {
            OpeningMoveIdea idea = move.Idea ?? BuildOpeningMoveIdea(move.MoveSan, move.IsMainMove);
            OpeningTrainingMoveOption? recommended = null;
            IReadOnlyList<OpeningTheoryMove> replies = GetOpeningMovesByPositionKey(move.ToPositionKey, 1, playableOnly: false);
            OpeningTheoryMove? bestReply = replies.FirstOrDefault();
            if (bestReply is not null)
            {
                recommended = new OpeningTrainingMoveOption(
                    bestReply.MoveSan,
                    bestReply.MoveUci,
                    OpeningTrainingMoveRole.Expected,
                    bestReply.IsMainMove,
                    "Best local book response.",
                    OpeningLineRecallReferenceKind.BestMove,
                    OpeningTrainingMoveSourceKind.OpeningBook,
                    bestReply.Idea ?? BuildOpeningMoveIdea(bestReply.MoveSan, bestReply.IsMainMove),
                    bestReply.ToOpeningPositionKey);
            }

            branches.Add(new OpeningTrainingBranch(
                new OpeningBranchKey($"{lineKey.Value}|{move.MoveUci}"),
                move.MoveSan,
                move.MoveUci,
                Math.Max(1, move.DistinctGameCount),
                $"Book frequency: {move.OccurrenceCount} occurrence(s), {move.DistinctGameCount} game(s).",
                recommended,
                [],
                [],
                move.ToOpeningPositionKey));
        }

        OpeningCoverageSummary coverage = new(
            TotalBookBranches: Math.Max(branches.Count, 1),
            CoveredBranches: 0,
            WeakBranches: branches.Count,
            UnseenCommonBranches: branches.Count,
            CoveragePercent: 0,
            KnownPositions: mainLine.Count,
            StableBranches: 0,
            KnowledgeBoundaryPly: mainLine.LastOrDefault()?.Ply ?? 0);
        OpponentReplyProfile opponentProfile = new(
            lineKey,
            parsedSide == RepertoireSide.Both ? repertoireSide : parsedSide,
            branches.Select(branch => new OpponentMoveFrequency(
                branch.OpponentMove,
                branch.OpponentMoveUci,
                branch.Frequency,
                branch.Frequency,
                0,
                0,
                false,
                OpponentMoveFrequencySourceKind.BookFrequency,
                branch.SourceSummary)).ToList(),
            branches.Count == 0
                ? "No opponent branches were found in the local opening book."
                : $"Tracked {branches.Count} opponent branch(es) from the local opening book.");

        overview = new OpeningTrainerOverview(
            openingKey,
            lineKey,
            parsedSide,
            eco,
            openingName,
            variationName,
            mainLine,
            branches,
            opponentProfile,
            coverage,
            [],
            ideas);
        return true;
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
                DELETE FROM move_advice_feedbacks
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

    public void ClearImportedAnalysisData()
    {
        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery("BEGIN IMMEDIATE;");
            try
            {
                database.ExecuteNonQuery("DELETE FROM move_advice_feedbacks;");
                database.ExecuteNonQuery("DELETE FROM analysis_window_states;");
                database.ExecuteNonQuery("DELETE FROM analysis_moves;");
                database.ExecuteNonQuery("DELETE FROM analysis_results;");
                database.ExecuteNonQuery("DELETE FROM imported_games;");
                database.ExecuteNonQuery("COMMIT;");
            }
            catch
            {
                database.ExecuteNonQuery("ROLLBACK;");
                throw;
            }
        }
    }

    public void ClearDerivedAnalysisData()
    {
        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery("BEGIN IMMEDIATE;");
            try
            {
                ClearDerivedAnalysisData(database);
                SetMetadataValue(database, DerivedAnalysisDataVersionKey, derivedAnalysisDataVersion);
                database.ExecuteNonQuery("COMMIT;");
            }
            catch
            {
                database.ExecuteNonQuery("ROLLBACK;");
                throw;
            }
        }
    }

    public string? GetDerivedAnalysisDataVersion()
    {
        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            return GetMetadataValue(database, DerivedAnalysisDataVersionKey);
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
                SELECT game_fingerprint, white_player, black_player, date_text, result_text, eco, site,
                       white_elo, black_elo, time_control, time_control_category, updated_utc
                FROM imported_games
                {(string.IsNullOrWhiteSpace(normalizedFilter)
                    ? string.Empty
                    : "WHERE lower(coalesce(white_player, '')) LIKE ?1 OR lower(coalesce(black_player, '')) LIKE ?1 OR lower(coalesce(date_text, '')) LIKE ?1 OR lower(coalesce(result_text, '')) LIKE ?1 OR lower(coalesce(eco, '')) LIKE ?1 OR lower(coalesce(site, '')) LIKE ?1 OR lower(coalesce(time_control, '')) LIKE ?1")}
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
                int? whiteElo = statement.GetNullableInt(7);
                int? blackElo = statement.GetNullableInt(8);
                string? timeControl = statement.GetText(9);
                GameTimeControlCategory category = ParseTimeControlCategory(statement.GetNullableInt(10), timeControl);
                string? updatedUtcText = statement.GetText(11);
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
                    whiteElo,
                    blackElo,
                    timeControl,
                    category,
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
                SELECT
                    analysis_results.game_fingerprint,
                    analysis_results.analyzed_side,
                    analysis_results.depth,
                    analysis_results.multi_pv,
                    analysis_results.move_time_ms,
                    analysis_results.payload_json
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
                GameAnalysisCacheKey key = new(
                    statement.GetText(0) ?? string.Empty,
                    (PlayerSide)statement.GetInt(1),
                    statement.GetInt(2),
                    statement.GetInt(3),
                    ReadMoveTime(statement.GetInt(4)));
                string? payload = statement.GetText(5);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                GameAnalysisResult? item = JsonSerializer.Deserialize<GameAnalysisResult>(payload, JsonOptions);
                if (item is not null)
                {
                    items.Add(NormalizeLoadedResult(database, key, item));
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
                    imported_games.white_elo,
                    imported_games.black_elo,
                    imported_games.time_control,
                    imported_games.time_control_category,
                    imported_games.utc_date,
                    imported_games.utc_time,
                    imported_games.end_date,
                    imported_games.end_time,
                    imported_games.termination,
                    imported_games.link,
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
                    analysis_moves.is_highlighted,
                    latest_feedback.feedback_kind,
                    latest_feedback.corrected_label,
                    latest_feedback.comment,
                    latest_feedback.timestamp_utc
                FROM analysis_moves
                LEFT JOIN analysis_results ON analysis_results.game_fingerprint = analysis_moves.game_fingerprint
                    AND analysis_results.analyzed_side = analysis_moves.analyzed_side
                    AND analysis_results.depth = analysis_moves.depth
                    AND analysis_results.multi_pv = analysis_moves.multi_pv
                    AND analysis_results.move_time_ms = analysis_moves.move_time_ms
                LEFT JOIN imported_games ON imported_games.game_fingerprint = analysis_moves.game_fingerprint
                LEFT JOIN move_advice_feedbacks AS latest_feedback ON latest_feedback.feedback_id = (
                    SELECT feedback_id
                    FROM move_advice_feedbacks
                    WHERE move_advice_feedbacks.game_fingerprint = analysis_moves.game_fingerprint
                      AND move_advice_feedbacks.analyzed_side = analysis_moves.analyzed_side
                      AND move_advice_feedbacks.depth = analysis_moves.depth
                      AND move_advice_feedbacks.multi_pv = analysis_moves.multi_pv
                      AND move_advice_feedbacks.move_time_ms = analysis_moves.move_time_ms
                      AND move_advice_feedbacks.ply = analysis_moves.ply
                    ORDER BY timestamp_utc DESC, feedback_id DESC
                    LIMIT 1
                )
                {(string.IsNullOrWhiteSpace(normalizedFilter)
                    ? string.Empty
                    : "WHERE lower(coalesce(imported_games.white_player, '')) LIKE ?1 OR lower(coalesce(imported_games.black_player, '')) LIKE ?1 OR lower(coalesce(imported_games.date_text, '')) LIKE ?1 OR lower(coalesce(imported_games.result_text, '')) LIKE ?1 OR lower(coalesce(imported_games.eco, '')) LIKE ?1 OR lower(coalesce(imported_games.site, '')) LIKE ?1 OR lower(coalesce(latest_feedback.corrected_label, analysis_moves.mistake_label, '')) LIKE ?1 OR lower(coalesce(analysis_moves.mistake_label, '')) LIKE ?1 OR lower(coalesce(analysis_moves.san, '')) LIKE ?1 OR lower(coalesce(analysis_moves.move_uci, '')) LIKE ?1")}
                ORDER BY imported_games.updated_utc DESC, analysis_moves.ply ASC
                LIMIT {safeLimit};
                """);

            if (!string.IsNullOrWhiteSpace(normalizedFilter))
            {
                statement.BindText(1, $"%{normalizedFilter}%");
            }

            while (statement.Step() == SqliteRow)
            {
                string? timeControl = statement.GetText(14);
                string? originalLabel = statement.GetText(37);
                string? correctedLabel = statement.GetText(45);
                AdviceFeedbackKind? manualFeedbackKind = ParseNullableFeedbackKind(statement.GetText(44));
                DateTime? manualCorrectedUtc = ParseNullableUtc(statement.GetText(47));
                items.Add(StoredMoveAnalysisMapper.FromSqliteRow(
                    new StoredGameContext(
                        statement.GetText(0) ?? string.Empty,
                        statement.GetText(6),
                        statement.GetText(7),
                        statement.GetText(8),
                        statement.GetText(9),
                        statement.GetText(10),
                        statement.GetText(11),
                        statement.GetNullableInt(12),
                        statement.GetNullableInt(13),
                        timeControl,
                        ParseTimeControlCategory(statement.GetNullableInt(15), timeControl),
                        statement.GetText(16),
                        statement.GetText(17),
                        statement.GetText(18),
                        statement.GetText(19),
                        statement.GetText(20),
                        statement.GetText(21)),
                    new StoredAnalysisRunContext(
                        (PlayerSide)statement.GetInt(1),
                        statement.GetInt(2),
                        statement.GetInt(3),
                        ReadMoveTime(statement.GetInt(4)),
                        ParseUtc(statement.GetText(5))),
                    new StoredMoveContext(
                        statement.GetInt(22),
                        statement.GetInt(23),
                        statement.GetText(24) ?? string.Empty,
                        statement.GetText(25) ?? string.Empty,
                        statement.GetText(26) ?? string.Empty,
                        statement.GetText(27) ?? string.Empty,
                        (GamePhase)statement.GetInt(28),
                        statement.GetNullableInt(29),
                        statement.GetNullableInt(30),
                        statement.GetNullableInt(31),
                        statement.GetNullableInt(32),
                        statement.GetNullableInt(33),
                        (MoveQualityBucket)statement.GetInt(34),
                        statement.GetInt(35),
                        statement.GetText(36)),
                    new StoredMoveAdviceContext(
                        string.IsNullOrWhiteSpace(correctedLabel) ? originalLabel : correctedLabel,
                        ParseNullableDouble(statement.GetText(38)),
                        DeserializeEvidence(statement.GetText(39)),
                        statement.GetText(40),
                        statement.GetText(41),
                        statement.GetText(42),
                        statement.GetInt(43) != 0,
                        originalLabel),
                    new StoredManualFeedbackContext(
                        manualFeedbackKind,
                        correctedLabel,
                        statement.GetText(46),
                        manualCorrectedUtc)));
            }
        }

        return items;
    }

    public IReadOnlyList<MoveAdviceFeedback> ListMoveAdviceFeedback(string? filterText = null, int limit = 5000)
    {
        string normalizedFilter = filterText?.Trim().ToLowerInvariant() ?? string.Empty;
        int safeLimit = Math.Clamp(limit, 1, 20000);
        List<MoveAdviceFeedback> items = [];

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare($"""
                SELECT
                    feedback_id,
                    timestamp_utc,
                    game_fingerprint,
                    analyzed_side,
                    depth,
                    multi_pv,
                    move_time_ms,
                    ply,
                    move_number,
                    played_san,
                    played_uci,
                    fen_before,
                    fen_after,
                    eval_before_cp,
                    eval_after_cp,
                    best_move_uci,
                    original_label,
                    original_confidence,
                    original_evidence_json,
                    quality,
                    centipawn_loss,
                    feedback_kind,
                    corrected_label,
                    comment,
                    source
                FROM move_advice_feedbacks
                {(string.IsNullOrWhiteSpace(normalizedFilter)
                    ? string.Empty
                    : "WHERE lower(coalesce(original_label, '')) LIKE ?1 OR lower(coalesce(corrected_label, '')) LIKE ?1 OR lower(coalesce(comment, '')) LIKE ?1 OR lower(played_san) LIKE ?1 OR lower(played_uci) LIKE ?1")}
                ORDER BY timestamp_utc DESC, feedback_id DESC
                LIMIT {safeLimit};
                """);

            if (!string.IsNullOrWhiteSpace(normalizedFilter))
            {
                statement.BindText(1, $"%{normalizedFilter}%");
            }

            while (statement.Step() == SqliteRow)
            {
                items.Add(ReadMoveAdviceFeedback(statement));
            }
        }

        return items;
    }

    public void SaveMoveAdviceFeedback(MoveAdviceFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                INSERT INTO move_advice_feedbacks (
                    feedback_id,
                    timestamp_utc,
                    game_fingerprint,
                    analyzed_side,
                    depth,
                    multi_pv,
                    move_time_ms,
                    ply,
                    move_number,
                    played_san,
                    played_uci,
                    fen_before,
                    fen_after,
                    eval_before_cp,
                    eval_after_cp,
                    best_move_uci,
                    original_label,
                    original_confidence,
                    original_evidence_json,
                    quality,
                    centipawn_loss,
                    feedback_kind,
                    corrected_label,
                    comment,
                    source)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21, ?22, ?23, ?24, ?25);
                """);

            statement.BindText(1, string.IsNullOrWhiteSpace(feedback.FeedbackId) ? Guid.NewGuid().ToString("N") : feedback.FeedbackId);
            statement.BindText(2, feedback.TimestampUtc.ToUniversalTime().ToString("O"));
            statement.BindText(3, feedback.GameFingerprint);
            statement.BindInt(4, (int)feedback.AnalyzedSide);
            statement.BindInt(5, feedback.Depth);
            statement.BindInt(6, feedback.MultiPv);
            statement.BindInt(7, NormalizeMoveTime(feedback.MoveTimeMs));
            statement.BindInt(8, feedback.Ply);
            statement.BindInt(9, feedback.MoveNumber);
            statement.BindText(10, feedback.PlayedSan);
            statement.BindText(11, feedback.PlayedUci);
            statement.BindText(12, feedback.FenBefore);
            statement.BindText(13, feedback.FenAfter);
            BindNullableInt(statement, 14, feedback.EvalBeforeCp);
            BindNullableInt(statement, 15, feedback.EvalAfterCp);
            statement.BindNullableText(16, feedback.BestMoveUci);
            statement.BindNullableText(17, feedback.OriginalLabel);
            statement.BindNullableText(18, FormatNullableDouble(feedback.OriginalConfidence));
            statement.BindText(19, SerializeEvidence(feedback.OriginalEvidence));
            statement.BindInt(20, (int)feedback.Quality);
            BindNullableInt(statement, 21, feedback.CentipawnLoss);
            statement.BindText(22, feedback.FeedbackKind.ToString());
            statement.BindNullableText(23, feedback.CorrectedLabel);
            statement.BindNullableText(24, feedback.Comment);
            statement.BindText(25, feedback.Source);
            statement.StepUntilDone();
        }
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
            if (result is not null)
            {
                result = NormalizeLoadedResult(database, key, result);
            }

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

            ReplaceMoveAnalyses(database, key, result, ParseUtc(timestamp));
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

    public void SaveOpeningTrainingSessionResult(OpeningTrainingSessionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string payload = JsonSerializer.Serialize(result, JsonOptions);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare("""
                INSERT INTO opening_training_session_results (
                    session_id,
                    player_key,
                    display_name,
                    created_utc,
                    completed_utc,
                    outcome,
                    position_count,
                    attempt_count,
                    correct_count,
                    playable_count,
                    wrong_count,
                    related_openings_json,
                    theme_labels_json,
                    payload_json)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14)
                ON CONFLICT (session_id)
                DO UPDATE SET
                    player_key = excluded.player_key,
                    display_name = excluded.display_name,
                    created_utc = excluded.created_utc,
                    completed_utc = excluded.completed_utc,
                    outcome = excluded.outcome,
                    position_count = excluded.position_count,
                    attempt_count = excluded.attempt_count,
                    correct_count = excluded.correct_count,
                    playable_count = excluded.playable_count,
                    wrong_count = excluded.wrong_count,
                    related_openings_json = excluded.related_openings_json,
                    theme_labels_json = excluded.theme_labels_json,
                    payload_json = excluded.payload_json;
                """);

            statement.BindText(1, result.SessionId);
            statement.BindText(2, NormalizePlayerKey(result.PlayerKey));
            statement.BindText(3, result.DisplayName);
            statement.BindText(4, result.CreatedUtc.ToUniversalTime().ToString("O"));
            statement.BindText(5, result.CompletedUtc.ToUniversalTime().ToString("O"));
            statement.BindInt(6, (int)result.Outcome);
            statement.BindInt(7, result.PositionCount);
            statement.BindInt(8, result.AttemptCount);
            statement.BindInt(9, result.CorrectCount);
            statement.BindInt(10, result.PlayableCount);
            statement.BindInt(11, result.WrongCount);
            statement.BindText(12, JsonSerializer.Serialize(result.RelatedOpenings, JsonOptions));
            statement.BindText(13, JsonSerializer.Serialize(result.ThemeLabels, JsonOptions));
            statement.BindText(14, payload);
            statement.StepUntilDone();
        }
    }

    public IReadOnlyList<OpeningTrainingSessionResult> ListOpeningTrainingSessionResults(string? playerKey = null, int limit = 200)
    {
        string normalizedPlayerKey = NormalizePlayerKey(playerKey);
        int safeLimit = Math.Clamp(limit, 1, 1000);
        List<OpeningTrainingSessionResult> results = [];

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare($"""
                SELECT payload_json
                FROM opening_training_session_results
                {(string.IsNullOrWhiteSpace(normalizedPlayerKey) ? string.Empty : "WHERE player_key = ?1")}
                ORDER BY completed_utc DESC
                LIMIT {safeLimit};
                """);

            if (!string.IsNullOrWhiteSpace(normalizedPlayerKey))
            {
                statement.BindText(1, normalizedPlayerKey);
            }

            while (statement.Step() == SqliteRow)
            {
                string? payload = statement.GetText(0);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                OpeningTrainingSessionResult? result = JsonSerializer.Deserialize<OpeningTrainingSessionResult>(payload, JsonOptions);
                if (result is not null)
                {
                    results.Add(result);
                }
            }
        }

        return results;
    }

    public void SaveOpeningReviewItems(string playerKey, IReadOnlyList<OpeningReviewItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerKey);
        ArgumentNullException.ThrowIfNull(items);

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            database.ExecuteNonQuery("BEGIN IMMEDIATE;");
            try
            {
                database.ExecuteNonQuery(
                    "DELETE FROM opening_review_items WHERE player_key = ?1;",
                    statement => statement.BindText(1, NormalizePlayerKey(playerKey)));

                foreach (OpeningReviewItem item in items)
                {
                    string branchKey = item.BranchKey.Value;
                    string positionKey = item.PositionKey.Value;
                    database.ExecuteNonQuery(
                        """
                        INSERT INTO opening_review_items (
                            player_key,
                            branch_key,
                            position_key,
                            last_reviewed_utc,
                            next_review_utc,
                            ease,
                            correct_streak,
                            wrong_streak,
                            total_attempts)
                        VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9);
                        """,
                        statement =>
                        {
                            statement.BindText(1, NormalizePlayerKey(playerKey));
                            statement.BindText(2, branchKey);
                            statement.BindText(3, positionKey);
                            statement.BindNullableText(4, item.LastReviewedUtc?.ToString("O", CultureInfo.InvariantCulture));
                            statement.BindText(5, item.NextReviewUtc.ToString("O", CultureInfo.InvariantCulture));
                            statement.BindText(6, item.Ease.ToString(CultureInfo.InvariantCulture));
                            statement.BindInt(7, item.CorrectStreak);
                            statement.BindInt(8, item.WrongStreak);
                            statement.BindInt(9, item.TotalAttempts);
                        });
                }

                database.ExecuteNonQuery("COMMIT;");
            }
            catch
            {
                database.ExecuteNonQuery("ROLLBACK;");
                throw;
            }
        }
    }

    public IReadOnlyList<OpeningReviewItem> ListOpeningReviewItems(string? playerKey = null, int limit = 1000)
    {
        string normalizedPlayerKey = NormalizePlayerKey(playerKey);
        int safeLimit = Math.Clamp(limit, 1, 5000);
        List<OpeningReviewItem> items = [];

        lock (sync)
        {
            using SqliteDatabase database = OpenDatabase();
            using SqliteStatement statement = database.Prepare($"""
                SELECT branch_key, position_key, last_reviewed_utc, next_review_utc, ease, correct_streak, wrong_streak, total_attempts
                FROM opening_review_items
                {(string.IsNullOrWhiteSpace(normalizedPlayerKey) ? string.Empty : "WHERE player_key = ?1")}
                ORDER BY next_review_utc ASC
                LIMIT {safeLimit};
                """);

            if (!string.IsNullOrWhiteSpace(normalizedPlayerKey))
            {
                statement.BindText(1, normalizedPlayerKey);
            }

            while (statement.Step() == SqliteRow)
            {
                items.Add(new OpeningReviewItem(
                    new OpeningBranchKey(statement.GetText(0) ?? string.Empty),
                    new OpeningPositionKey(statement.GetText(1) ?? string.Empty),
                    ParseNullableUtc(statement.GetText(2)),
                    ParseUtc(statement.GetText(3)),
                    ParseDouble(statement.GetText(4)),
                    statement.GetInt(5),
                    statement.GetInt(6),
                    statement.GetInt(7)));
            }
        }

        return items;
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
                    white_elo INTEGER NULL,
                    black_elo INTEGER NULL,
                    date_text TEXT NULL,
                    result_text TEXT NULL,
                    eco TEXT NULL,
                    site TEXT NULL,
                    round_text TEXT NULL,
                    current_position TEXT NULL,
                    timezone TEXT NULL,
                    eco_url TEXT NULL,
                    utc_date TEXT NULL,
                    utc_time TEXT NULL,
                    time_control TEXT NULL,
                    time_control_category INTEGER NOT NULL DEFAULT 0,
                    termination TEXT NULL,
                    start_time TEXT NULL,
                    end_date TEXT NULL,
                    end_time TEXT NULL,
                    link TEXT NULL,
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
                CREATE TABLE IF NOT EXISTS move_advice_feedbacks (
                    feedback_id TEXT NOT NULL PRIMARY KEY,
                    timestamp_utc TEXT NOT NULL,
                    game_fingerprint TEXT NOT NULL,
                    analyzed_side INTEGER NOT NULL,
                    depth INTEGER NOT NULL,
                    multi_pv INTEGER NOT NULL,
                    move_time_ms INTEGER NOT NULL,
                    ply INTEGER NOT NULL,
                    move_number INTEGER NOT NULL,
                    played_san TEXT NOT NULL,
                    played_uci TEXT NOT NULL,
                    fen_before TEXT NOT NULL,
                    fen_after TEXT NOT NULL,
                    eval_before_cp INTEGER NULL,
                    eval_after_cp INTEGER NULL,
                    best_move_uci TEXT NULL,
                    original_label TEXT NULL,
                    original_confidence TEXT NULL,
                    original_evidence_json TEXT NULL,
                    quality INTEGER NOT NULL,
                    centipawn_loss INTEGER NULL,
                    feedback_kind TEXT NOT NULL,
                    corrected_label TEXT NULL,
                    comment TEXT NULL,
                    source TEXT NOT NULL
                );
                """);
            database.ExecuteNonQuery("""
                CREATE INDEX IF NOT EXISTS idx_move_advice_feedbacks_move_latest
                ON move_advice_feedbacks (game_fingerprint, analyzed_side, depth, multi_pv, move_time_ms, ply, timestamp_utc DESC);
                """);
            database.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS app_metadata (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """);
            database.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS opening_training_session_results (
                    session_id TEXT NOT NULL PRIMARY KEY,
                    player_key TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    completed_utc TEXT NOT NULL,
                    outcome INTEGER NOT NULL,
                    position_count INTEGER NOT NULL,
                    attempt_count INTEGER NOT NULL,
                    correct_count INTEGER NOT NULL,
                    playable_count INTEGER NOT NULL,
                    wrong_count INTEGER NOT NULL,
                    related_openings_json TEXT NOT NULL,
                    theme_labels_json TEXT NOT NULL,
                    payload_json TEXT NOT NULL
                );
                """);
            database.ExecuteNonQuery("""
                CREATE INDEX IF NOT EXISTS idx_opening_training_session_results_player_completed
                ON opening_training_session_results (player_key, completed_utc DESC);
                """);
            database.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS opening_review_items (
                    player_key TEXT NOT NULL,
                    branch_key TEXT NOT NULL,
                    position_key TEXT NOT NULL,
                    last_reviewed_utc TEXT NULL,
                    next_review_utc TEXT NOT NULL,
                    ease TEXT NOT NULL,
                    correct_streak INTEGER NOT NULL,
                    wrong_streak INTEGER NOT NULL,
                    total_attempts INTEGER NOT NULL,
                    PRIMARY KEY (player_key, branch_key, position_key)
                );
                """);
            database.ExecuteNonQuery("""
                CREATE INDEX IF NOT EXISTS idx_opening_review_items_player_next_review
                ON opening_review_items (player_key, next_review_utc ASC);
                """);
            EnsureColumnExists(
                database,
                "analysis_window_states",
                "explanation_level_index",
                "INTEGER NOT NULL DEFAULT 1");
            EnsureColumnExists(database, "imported_games", "white_elo", "INTEGER NULL");
            EnsureColumnExists(database, "imported_games", "black_elo", "INTEGER NULL");
            EnsureColumnExists(database, "imported_games", "round_text", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "current_position", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "timezone", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "eco_url", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "utc_date", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "utc_time", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "time_control", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "time_control_category", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumnExists(database, "imported_games", "termination", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "start_time", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "end_date", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "end_time", "TEXT NULL");
            EnsureColumnExists(database, "imported_games", "link", "TEXT NULL");
            EnsureOpeningTreeSchema(database);
            if (applyDerivedAnalysisDataVersionPolicy)
            {
                ApplyDerivedAnalysisDataVersionPolicy(database);
            }
        }
    }

    private void ApplyDerivedAnalysisDataVersionPolicy(SqliteDatabase database)
    {
        string? storedVersion = GetMetadataValue(database, DerivedAnalysisDataVersionKey);
        if (string.Equals(storedVersion, derivedAnalysisDataVersion, StringComparison.Ordinal))
        {
            return;
        }

        database.ExecuteNonQuery("BEGIN IMMEDIATE;");
        try
        {
            ClearDerivedAnalysisData(database);
            SetMetadataValue(database, DerivedAnalysisDataVersionKey, derivedAnalysisDataVersion);
            database.ExecuteNonQuery("COMMIT;");
        }
        catch
        {
            database.ExecuteNonQuery("ROLLBACK;");
            throw;
        }
    }

    private static void ClearDerivedAnalysisData(SqliteDatabase database)
    {
        database.ExecuteNonQuery("DELETE FROM analysis_window_states;");
        database.ExecuteNonQuery("DELETE FROM analysis_moves;");
        database.ExecuteNonQuery("DELETE FROM analysis_results;");
    }

    private static GameAnalysisResult NormalizeLoadedResult(
        SqliteDatabase database,
        GameAnalysisCacheKey key,
        GameAnalysisResult result)
    {
        IReadOnlyDictionary<int, StoredMoveAnnotation> annotations = LoadMoveAnnotations(database, key);
        Dictionary<int, MoveAnalysisResult> normalizedMoves = result.MoveAnalyses
            .Select(move => NormalizeMove(move, annotations))
            .ToDictionary(move => move.Replay.Ply);
        IReadOnlyList<MoveAnalysisResult> moveAnalyses = result.MoveAnalyses
            .Select(move => normalizedMoves[move.Replay.Ply])
            .ToList();
        IReadOnlyList<SelectedMistake> highlightedMistakes = result.HighlightedMistakes
            .Select(mistake => NormalizeMistake(mistake, normalizedMoves, annotations))
            .ToList();

        return result with
        {
            MoveAnalyses = moveAnalyses,
            HighlightedMistakes = highlightedMistakes
        };
    }

    private static MoveAnalysisResult NormalizeMove(
        MoveAnalysisResult move,
        IReadOnlyDictionary<int, StoredMoveAnnotation> annotations)
    {
        if (!annotations.TryGetValue(move.Replay.Ply, out StoredMoveAnnotation? annotation))
        {
            return move;
        }

        return move with
        {
            MistakeTag = move.MistakeTag ?? annotation.Tag,
            Explanation = move.Explanation ?? annotation.Explanation
        };
    }

    private static SelectedMistake NormalizeMistake(
        SelectedMistake mistake,
        IReadOnlyDictionary<int, MoveAnalysisResult> normalizedMoves,
        IReadOnlyDictionary<int, StoredMoveAnnotation> annotations)
    {
        IReadOnlyList<MoveAnalysisResult> moves = mistake.Moves
            .Select(move => normalizedMoves.TryGetValue(move.Replay.Ply, out MoveAnalysisResult? normalized)
                ? normalized
                : NormalizeMove(move, annotations))
            .ToList();
        MoveAnalysisResult? lead = moves
            .OrderByDescending(move => move.Quality)
            .ThenByDescending(move => move.CentipawnLoss ?? 0)
            .FirstOrDefault();

        return mistake with
        {
            Moves = moves,
            Tag = mistake.Tag ?? lead?.MistakeTag,
            Explanation = lead?.Explanation ?? mistake.Explanation
        };
    }

    private static IReadOnlyDictionary<int, StoredMoveAnnotation> LoadMoveAnnotations(
        SqliteDatabase database,
        GameAnalysisCacheKey key)
    {
        Dictionary<int, StoredMoveAnnotation> annotations = new();
        using SqliteStatement statement = database.Prepare("""
            SELECT
                analysis_moves.ply,
                coalesce(latest_feedback.corrected_label, analysis_moves.mistake_label),
                analysis_moves.mistake_confidence,
                analysis_moves.evidence_json,
                analysis_moves.short_explanation,
                analysis_moves.detailed_explanation,
                analysis_moves.training_hint
            FROM analysis_moves
            LEFT JOIN move_advice_feedbacks AS latest_feedback ON latest_feedback.feedback_id = (
                SELECT feedback_id
                FROM move_advice_feedbacks
                WHERE move_advice_feedbacks.game_fingerprint = analysis_moves.game_fingerprint
                  AND move_advice_feedbacks.analyzed_side = analysis_moves.analyzed_side
                  AND move_advice_feedbacks.depth = analysis_moves.depth
                  AND move_advice_feedbacks.multi_pv = analysis_moves.multi_pv
                  AND move_advice_feedbacks.move_time_ms = analysis_moves.move_time_ms
                  AND move_advice_feedbacks.ply = analysis_moves.ply
                ORDER BY timestamp_utc DESC, feedback_id DESC
                LIMIT 1
            )
            WHERE analysis_moves.game_fingerprint = ?1
              AND analysis_moves.analyzed_side = ?2
              AND analysis_moves.depth = ?3
              AND analysis_moves.multi_pv = ?4
              AND analysis_moves.move_time_ms = ?5;
            """);

        statement.BindText(1, key.GameFingerprint);
        statement.BindInt(2, (int)key.Side);
        statement.BindInt(3, key.Depth);
        statement.BindInt(4, key.MultiPv);
        statement.BindInt(5, NormalizeMoveTime(key.MoveTimeMs));

        while (statement.Step() == SqliteRow)
        {
            MistakeTag? tag = null;
            string? label = statement.GetText(1);
            if (!string.IsNullOrWhiteSpace(label))
            {
                tag = new MistakeTag(
                    label,
                    ParseNullableDouble(statement.GetText(2)) ?? 0,
                    DeserializeEvidence(statement.GetText(3)));
            }

            MoveExplanation? explanation = null;
            string? shortExplanation = statement.GetText(4);
            string? trainingHint = statement.GetText(6);
            if (!string.IsNullOrWhiteSpace(shortExplanation)
                || !string.IsNullOrWhiteSpace(trainingHint))
            {
                explanation = new MoveExplanation(
                    shortExplanation ?? string.Empty,
                    trainingHint ?? string.Empty,
                    statement.GetText(5) ?? string.Empty);
            }

            annotations[statement.GetInt(0)] = new StoredMoveAnnotation(tag, explanation);
        }

        return annotations;
    }

    private SqliteDatabase OpenDatabase() => new(databasePath);

    private static int NormalizeMoveTime(int? moveTimeMs) => moveTimeMs ?? NoMoveTimeMs;

    private static int? ReadMoveTime(int rawMoveTime) => rawMoveTime == NoMoveTimeMs ? null : rawMoveTime;

    private static GameTimeControlCategory ParseTimeControlCategory(int? storedValue, string? timeControl)
    {
        if (storedValue.HasValue
            && Enum.IsDefined(typeof(GameTimeControlCategory), storedValue.Value))
        {
            return (GameTimeControlCategory)storedValue.Value;
        }

        return PgnGameParser.ClassifyTimeControl(timeControl);
    }

    private static string NormalizePlayerKey(string? playerKey)
        => string.IsNullOrWhiteSpace(playerKey) ? string.Empty : playerKey.Trim().ToLowerInvariant();

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

    private static DateTime? ParseNullableUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseUtc(value);
    }

    private static AdviceFeedbackKind? ParseNullableFeedbackKind(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out AdviceFeedbackKind parsed)
            ? parsed
            : null;
    }

    private static MoveAdviceFeedback ReadMoveAdviceFeedback(SqliteStatement statement)
    {
        return new MoveAdviceFeedback(
            statement.GetText(0) ?? string.Empty,
            ParseUtc(statement.GetText(1)),
            statement.GetText(2) ?? string.Empty,
            (PlayerSide)statement.GetInt(3),
            statement.GetInt(4),
            statement.GetInt(5),
            ReadMoveTime(statement.GetInt(6)),
            statement.GetInt(7),
            statement.GetInt(8),
            statement.GetText(9) ?? string.Empty,
            statement.GetText(10) ?? string.Empty,
            statement.GetText(11) ?? string.Empty,
            statement.GetText(12) ?? string.Empty,
            statement.GetNullableInt(13),
            statement.GetNullableInt(14),
            statement.GetText(15),
            statement.GetText(16),
            ParseNullableDouble(statement.GetText(17)),
            DeserializeEvidence(statement.GetText(18)),
            (MoveQualityBucket)statement.GetInt(19),
            statement.GetNullableInt(20),
            ParseNullableFeedbackKind(statement.GetText(21)) ?? AdviceFeedbackKind.NotUseful,
            statement.GetText(22),
            statement.GetText(23),
            statement.GetText(24) ?? string.Empty);
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

    private static void BindNullableInt(SqliteStatement statement, int index, int? value)
    {
        if (value.HasValue)
        {
            statement.BindInt(index, value.Value);
            return;
        }

        statement.BindNull(index);
    }

    private static void ReplaceMoveAnalyses(
        SqliteDatabase database,
        GameAnalysisCacheKey key,
        GameAnalysisResult result,
        DateTime analysisUpdatedUtc)
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

        foreach (StoredMoveAnalysis move in StoredMoveAnalysisMapper.FromAnalysisResult(key, result, analysisUpdatedUtc))
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
                    statement.BindText(1, move.GameFingerprint);
                    statement.BindInt(2, (int)move.AnalyzedSide);
                    statement.BindInt(3, move.Depth);
                    statement.BindInt(4, move.MultiPv);
                    statement.BindInt(5, NormalizeMoveTime(move.MoveTimeMs));
                    statement.BindInt(6, move.Ply);
                    statement.BindInt(7, move.MoveNumber);
                    statement.BindText(8, move.San);
                    statement.BindText(9, move.Uci);
                    statement.BindText(10, move.FenBefore);
                    statement.BindText(11, move.FenAfter);
                    statement.BindInt(12, (int)move.Phase);
                    BindNullableInt(statement, 13, move.EvalBeforeCp);
                    BindNullableInt(statement, 14, move.EvalAfterCp);
                    BindNullableInt(statement, 15, move.BestMateIn);
                    BindNullableInt(statement, 16, move.PlayedMateIn);
                    BindNullableInt(statement, 17, move.CentipawnLoss);
                    statement.BindInt(18, (int)move.Quality);
                    statement.BindInt(19, move.MaterialDeltaCp);
                    statement.BindNullableText(20, move.BestMoveUci);
                    statement.BindNullableText(21, move.MistakeLabel);
                    statement.BindNullableText(22, FormatNullableDouble(move.MistakeConfidence));
                    statement.BindText(23, SerializeEvidence(move.Evidence));
                    statement.BindNullableText(24, move.ShortExplanation);
                    statement.BindNullableText(25, move.DetailedExplanation);
                    statement.BindNullableText(26, move.TrainingHint);
                    statement.BindInt(27, move.IsHighlighted ? 1 : 0);
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
                white_elo,
                black_elo,
                date_text,
                result_text,
                eco,
                site,
                round_text,
                current_position,
                timezone,
                eco_url,
                utc_date,
                utc_time,
                time_control,
                time_control_category,
                termination,
                start_time,
                end_date,
                end_time,
                link,
                updated_utc)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21, ?22, ?23, ?24)
            ON CONFLICT (game_fingerprint)
            DO UPDATE SET
                pgn_text = excluded.pgn_text,
                white_player = excluded.white_player,
                black_player = excluded.black_player,
                white_elo = excluded.white_elo,
                black_elo = excluded.black_elo,
                date_text = excluded.date_text,
                result_text = excluded.result_text,
                eco = excluded.eco,
                site = excluded.site,
                round_text = excluded.round_text,
                current_position = excluded.current_position,
                timezone = excluded.timezone,
                eco_url = excluded.eco_url,
                utc_date = excluded.utc_date,
                utc_time = excluded.utc_time,
                time_control = excluded.time_control,
                time_control_category = excluded.time_control_category,
                termination = excluded.termination,
                start_time = excluded.start_time,
                end_date = excluded.end_date,
                end_time = excluded.end_time,
                link = excluded.link,
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
            BindNullableInt(statement, 5, game.WhiteElo);
            BindNullableInt(statement, 6, game.BlackElo);
            statement.BindNullableText(7, game.DateText);
            statement.BindNullableText(8, game.Result);
            statement.BindNullableText(9, game.Eco);
            statement.BindNullableText(10, game.Site);
            statement.BindNullableText(11, game.Metadata?.Round);
            statement.BindNullableText(12, game.Metadata?.CurrentPosition);
            statement.BindNullableText(13, game.Metadata?.Timezone);
            statement.BindNullableText(14, game.Metadata?.EcoUrl);
            statement.BindNullableText(15, game.Metadata?.UtcDate);
            statement.BindNullableText(16, game.Metadata?.UtcTime);
            statement.BindNullableText(17, game.Metadata?.TimeControl);
            statement.BindInt(18, (int)(game.Metadata?.TimeControlCategory ?? GameTimeControlCategory.Unknown));
            statement.BindNullableText(19, game.Metadata?.Termination);
            statement.BindNullableText(20, game.Metadata?.StartTime);
            statement.BindNullableText(21, game.Metadata?.EndDate);
            statement.BindNullableText(22, game.Metadata?.EndTime);
            statement.BindNullableText(23, game.Metadata?.Link);
            statement.BindText(24, timestamp);
            statement.StepUntilDone();
        }
    }

    private static int CountRows(SqliteDatabase database, string tableName)
    {
        using SqliteStatement statement = database.Prepare($"SELECT COUNT(*) FROM {tableName};");
        return statement.Step() == SqliteRow ? statement.GetInt(0) : 0;
    }

    private static string? GetMetadataValue(SqliteDatabase database, string key)
    {
        using SqliteStatement statement = database.Prepare("""
            SELECT value
            FROM app_metadata
            WHERE key = ?1
            LIMIT 1;
            """);

        statement.BindText(1, key);
        return statement.Step() == SqliteRow ? statement.GetText(0) : null;
    }

    private static void SetMetadataValue(SqliteDatabase database, string key, string value)
    {
        database.ExecuteNonQuery(
            """
            INSERT INTO app_metadata (key, value)
            VALUES (?1, ?2)
            ON CONFLICT (key)
            DO UPDATE SET value = excluded.value;
            """,
            statement =>
            {
                statement.BindText(1, key);
                statement.BindText(2, value);
            });
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
            new OpeningPositionKey(statement.GetText(1) ?? string.Empty),
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
        string moveSan = statement.GetText(4) ?? string.Empty;
        bool isMainMove = statement.GetInt(7) != 0;
        return new OpeningTheoryMove(
            ParseGuid(statement.GetText(0)),
            ParseGuid(statement.GetText(1)),
            ParseGuid(statement.GetText(2)),
            statement.GetText(3) ?? string.Empty,
            moveSan,
            statement.GetInt(5),
            statement.GetInt(6),
            isMainMove,
            statement.GetInt(8) != 0,
            statement.GetInt(9),
            statement.GetText(10) ?? string.Empty,
            new OpeningPositionKey(statement.GetText(10) ?? string.Empty),
            statement.GetText(11) ?? string.Empty,
            new OpeningGameMetadata(
                statement.GetText(12) ?? string.Empty,
                statement.GetText(13) ?? string.Empty,
                statement.GetText(14) ?? string.Empty),
            "opening_book",
            BuildOpeningMoveIdea(moveSan, isMainMove));
    }

    private static Guid ParseGuid(string? value)
    {
        return Guid.TryParse(value, out Guid parsed) ? parsed : Guid.Empty;
    }

    private static double ParseDouble(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;
    }

    private static RepertoireSide ParseRepertoireSide(string? sideToMove)
    {
        return string.Equals(sideToMove, "Black", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sideToMove, "b", StringComparison.OrdinalIgnoreCase)
            ? RepertoireSide.Black
            : RepertoireSide.White;
    }

    private static PlayerSide ParsePlayerSide(string? sideToMove)
    {
        return string.Equals(sideToMove, "Black", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sideToMove, "b", StringComparison.OrdinalIgnoreCase)
            ? PlayerSide.Black
            : PlayerSide.White;
    }

    private static string BuildDisplayName(string eco, string openingName, string variationName)
    {
        string opening = string.IsNullOrWhiteSpace(openingName) ? OpeningCatalog.GetName(eco) : openingName;
        return string.IsNullOrWhiteSpace(variationName)
            ? $"{opening} ({eco})"
            : $"{opening}: {variationName} ({eco})";
    }

    private static string BuildOpeningKey(string eco, string openingName)
    {
        return $"{SanitizeKeyPart(eco)}{CompositeKeySeparator}{SanitizeKeyPart(openingName)}";
    }

    private static string BuildOpeningLineKey(string eco, string openingName, string variationName, RepertoireSide side, string positionKey)
    {
        return string.Join(
            CompositeKeySeparator,
            SanitizeKeyPart(eco),
            SanitizeKeyPart(openingName),
            SanitizeKeyPart(variationName),
            side.ToString(),
            SanitizeKeyPart(positionKey));
    }

    private static bool TryParseOpeningLineKey(
        string value,
        out string eco,
        out string openingName,
        out string variationName,
        out RepertoireSide side,
        out OpeningPositionKey positionKey)
    {
        eco = string.Empty;
        openingName = string.Empty;
        variationName = string.Empty;
        side = RepertoireSide.Both;
        positionKey = default;

        string[] parts = value.Split(CompositeKeySeparator);
        if (parts.Length < 5)
        {
            return false;
        }

        eco = RestoreKeyPart(parts[0]);
        openingName = RestoreKeyPart(parts[1]);
        variationName = RestoreKeyPart(parts[2]);
        _ = Enum.TryParse(parts[3], out side);
        positionKey = new OpeningPositionKey(RestoreKeyPart(parts[4]));
        return !positionKey.IsEmpty;
    }

    private static string SanitizeKeyPart(string? value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string RestoreKeyPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("\\|", "|", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static OpeningMoveIdea BuildOpeningMoveIdea(string moveSan, bool isMainMove)
    {
        List<OpeningMoveIdeaTag> tags = [];
        string explanation;
        string normalized = moveSan.Trim();

        if (normalized.StartsWith("O-O", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(OpeningMoveIdeaTag.KingSafety);
            explanation = "Castling improves king safety and activates the rooks.";
        }
        else if (normalized.StartsWith("N", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(OpeningMoveIdeaTag.DevelopPiece);
            explanation = "This move develops a piece toward active play.";
        }
        else if (normalized.StartsWith("c", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("d", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("e", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(OpeningMoveIdeaTag.ControlCenter);
            explanation = "This move fights for central space and influence.";
        }
        else
        {
            tags.Add(OpeningMoveIdeaTag.ImproveWorstPiece);
            explanation = "This move improves coordination without drifting from theory.";
        }

        if (normalized.Contains("+", StringComparison.Ordinal) || normalized.Contains("x", StringComparison.Ordinal))
        {
            tags.Add(OpeningMoveIdeaTag.TacticalResource);
        }

        if (isMainMove)
        {
            tags.Add(OpeningMoveIdeaTag.MainTheoreticalMove);
            explanation = $"{explanation} It is also the main theoretical move here.";
        }

        return new OpeningMoveIdea(normalized, tags.Distinct().ToList(), explanation);
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

    private sealed record StoredMoveAnnotation(MistakeTag? Tag, MoveExplanation? Explanation);

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
