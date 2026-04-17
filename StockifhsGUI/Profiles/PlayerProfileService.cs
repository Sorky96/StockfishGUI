using System.Globalization;

namespace StockifhsGUI;

public sealed class PlayerProfileService
{
    private readonly IAnalysisStore analysisStore;

    public PlayerProfileService(IAnalysisStore analysisStore)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
    }

    public IReadOnlyList<PlayerProfileSummary> ListProfiles(string? filterText = null, int limit = 100)
    {
        List<PlayerProfileRecord> records = LoadProfileRecords(filterText, Math.Max(limit * 8, 200));
        return records
            .GroupBy(record => record.PlayerKey)
            .Select(BuildSummary)
            .OrderByDescending(summary => summary.GamesAnalyzed)
            .ThenByDescending(summary => summary.HighlightedMistakes)
            .ThenBy(summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    public bool TryBuildProfile(string playerKeyOrName, out PlayerProfileReport? report)
    {
        if (string.IsNullOrWhiteSpace(playerKeyOrName))
        {
            report = null;
            return false;
        }

        string normalized = NormalizePlayerKey(playerKeyOrName);
        List<PlayerProfileRecord> records = LoadProfileRecords(null, 2000)
            .Where(record => record.PlayerKey == normalized
                || string.Equals(record.DisplayName, playerKeyOrName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (records.Count == 0)
        {
            report = null;
            return false;
        }

        report = BuildReport(records);
        return true;
    }

    private List<PlayerProfileRecord> LoadProfileRecords(string? filterText, int limit)
    {
        IReadOnlyList<GameAnalysisResult> results = analysisStore.ListResults(filterText, limit);
        return results
            .Select(CreateProfileRecord)
            .Where(record => record is not null)
            .Select(record => record!)
            .GroupBy(record => $"{record.GameFingerprint}|{record.Side}")
            .Select(group => group.First())
            .ToList();
    }

    private static PlayerProfileSummary BuildSummary(IGrouping<string, PlayerProfileRecord> group)
    {
        string displayName = group
            .GroupBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key)
            .First();

        IReadOnlyList<string> topLabels = group
            .SelectMany(record => record.HighlightedMistakes)
            .GroupBy(item => item.Tag?.Label ?? "unclassified")
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(3)
            .Select(item => item.Key)
            .ToList();

        int? averageCpl = TryAverage(group.SelectMany(record => record.Result.MoveAnalyses).Select(move => move.CentipawnLoss));

        return new PlayerProfileSummary(
            group.Key,
            displayName,
            group.Count(),
            group.Sum(record => record.HighlightedMistakes.Count),
            averageCpl,
            topLabels);
    }

    private static PlayerProfileReport BuildReport(IReadOnlyList<PlayerProfileRecord> records)
    {
        string playerKey = records[0].PlayerKey;
        string displayName = records
            .GroupBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Count())
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key)
            .First();

        IReadOnlyList<ProfileLabelStat> topLabels = records
            .SelectMany(record => record.HighlightedMistakes)
            .GroupBy(item => item.Tag?.Label ?? "unclassified")
            .Select(group => new ProfileLabelStat(
                group.Key,
                group.Count(),
                group.Average(item => item.Tag?.Confidence ?? 0.0)))
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.AverageConfidence)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        IReadOnlyList<ProfilePhaseStat> mistakesByPhase = records
            .SelectMany(record => record.Result.MoveAnalyses)
            .Where(move => move.Quality != MoveQualityBucket.Good)
            .GroupBy(move => move.Replay.Phase)
            .Select(group => new ProfilePhaseStat(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Phase)
            .ToList();

        IReadOnlyList<ProfileOpeningStat> mistakesByOpening = records
            .SelectMany(record => record.Result.MoveAnalyses
                .Where(move => move.Quality != MoveQualityBucket.Good)
                .Select(_ => string.IsNullOrWhiteSpace(record.Result.Game.Eco) ? "Unknown" : record.Result.Game.Eco!))
            .GroupBy(eco => eco, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProfileOpeningStat(group.First(), group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Eco, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        IReadOnlyList<ProfileSideStat> gamesBySide = records
            .GroupBy(record => record.Side)
            .Select(group => new ProfileSideStat(
                group.Key,
                group.Count(),
                group.Sum(item => item.HighlightedMistakes.Count)))
            .OrderBy(item => item.Side)
            .ToList();

        IReadOnlyList<ProfileMonthlyTrend> monthlyTrend = records
            .GroupBy(record => record.MonthKey ?? "Unknown")
            .Select(group => new ProfileMonthlyTrend(
                group.Key,
                group.Count(),
                group.Sum(item => item.HighlightedMistakes.Count),
                TryAverage(group.SelectMany(item => item.Result.MoveAnalyses).Select(move => move.CentipawnLoss))))
            .OrderBy(item => item.MonthKey, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<TrainingRecommendation> recommendations = topLabels
            .Take(3)
            .Select((labelStat, index) => CreateRecommendation(labelStat, BuildRecommendationContext(records, labelStat.Label), index + 1))
            .ToList();
        WeeklyTrainingPlan weeklyPlan = BuildWeeklyPlan(displayName, recommendations);

        return new PlayerProfileReport(
            playerKey,
            displayName,
            records.Count,
            records.Sum(record => record.Result.MoveAnalyses.Count),
            records.Sum(record => record.HighlightedMistakes.Count),
            TryAverage(records.SelectMany(record => record.Result.MoveAnalyses).Select(move => move.CentipawnLoss)),
            topLabels,
            mistakesByPhase,
            mistakesByOpening,
            gamesBySide,
            monthlyTrend,
            recommendations,
            weeklyPlan);
    }

    private static TrainingRecommendation CreateRecommendation(ProfileLabelStat labelStat, RecommendationContext context, int priority)
    {
        string contextSummary = BuildContextSummary(context);
        string openingSummary = BuildOpeningSummary(context.TopOpenings);

        return labelStat.Label switch
        {
            "hanging_piece" => new TrainingRecommendation(
                priority,
                "Board safety",
                "Protect Loose Pieces",
                $"Your profile shows repeated piece losses after moves that leave a square underprotected. {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Count attackers and defenders on the destination square before every move.",
                    "After moving a piece, ask whether the opponent can capture it for free or with tempo.",
                    "Prefer moves that keep your most valuable piece defended at least once.",
                    $"Pay extra attention in {FormatPhaseText(context.DominantPhase, fallback: "the phases where this happens most")}."
                ],
                [
                    "10-15 quick 'undefended pieces' puzzles.",
                    "Slow review of your own blunders with attacker-defender counting.",
                    $"Mini checklist game: say 'safe or loose?' before each move{BuildSideSuffix(context.DominantSide)}.",
                    openingSummary
                ]),
            "missed_tactic" => new TrainingRecommendation(
                priority,
                "Tactics",
                "Checks, Captures, Threats",
                $"You are missing forcing resources often enough that tactical scanning should become the first step of your thought process in sharp positions. {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "List checks, captures and threats for both sides before selecting a move.",
                    "When the position opens, calculate forcing lines before quiet improvements.",
                    "Double-check whether the opponent has one tactical reply that changes everything.",
                    $"Be especially strict in {FormatPhaseText(context.DominantPhase, fallback: "the phase where this keeps recurring")}."
                ],
                [
                    "Short CCT puzzle sets with a clock.",
                    "Two-move tactical calculation drills from your own analyzed games.",
                    "Flashcards with forks, skewers and discovered attacks.",
                    openingSummary
                ]),
            "opening_principles" => new TrainingRecommendation(
                priority,
                "Opening discipline",
                "Clean Up The Opening",
                $"The profile shows that you give away quality early by spending tempi on side ideas before finishing development and king safety. {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "In the first 10 moves, ask whether each move develops, castles or fights for the center.",
                    "Delay repeated queen or rook moves unless there is a concrete tactical reason.",
                    "Avoid wing pawn moves before your minor pieces are meaningfully developed.",
                    $"Review your typical setups{BuildSideSuffix(context.DominantSide)} in {FormatOpeningsList(context.TopOpenings, "the openings where this appears most")}."
                ],
                [
                    "Review three model openings you actually play.",
                    "Annotate the first 10 moves of your own games with 'develop / center / king safety'.",
                    "Play a few training games with a rule: no wing pawn moves before minor piece development.",
                    openingSummary
                ]),
            "king_safety" => new TrainingRecommendation(
                priority,
                "King safety",
                "Safer King Decisions",
                $"Your mistakes suggest that king shelter breaks down too easily after pawn pushes or slow reactions to attacking chances. {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Before pushing a pawn near your king, identify which file, diagonal or square complex gets weaker.",
                    "When castled, treat every pawn move in front of the king as a concession that needs justification.",
                    "Check the opponent's forcing moves before grabbing material on the wing.",
                    $"Recheck king shelter{BuildSideSuffix(context.DominantSide)} in {FormatPhaseText(context.DominantPhase, fallback: "the critical phase")}."
                ],
                [
                    "Model-game review focused on attacking patterns against castled kings.",
                    "Puzzle set on mating nets and defensive resources.",
                    "Post-game note: which move first weakened your king?",
                    openingSummary
                ]),
            "endgame_technique" => new TrainingRecommendation(
                priority,
                "Endgames",
                "Sharpen Endgame Technique",
                $"The profile points to technical slips in reduced material, especially around king activity and the simplest conversion path. {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "In endgames, compare moves by king activity before anything else.",
                    "Prefer the cleanest line with the least counterplay, not the fanciest one.",
                    "Check whether a pawn ending or piece trade helps or hurts your winning chances.",
                    $"Pay special attention when converting{BuildSideSuffix(context.DominantSide)}."
                ],
                [
                    "Basic king-and-pawn endgame drills.",
                    "Winning rook or minor-piece endgame conversion exercises.",
                    "Replay your own endgames and mark where the king should have improved first.",
                    "Create a mini set from your own late-game mistakes and replay the conversion plan."
                ]),
            "material_loss" => new TrainingRecommendation(
                priority,
                "Material discipline",
                "Material Discipline",
                $"A recurring theme is losing material in lines where the forcing continuation was not checked deeply enough. {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Before moving, calculate the most forcing exchange sequence to the end.",
                    "Ask which side wins material if the board is simplified immediately.",
                    "Treat every tactical capture as suspicious until the final balance is clear.",
                    $"Use extra caution{BuildSideSuffix(context.DominantSide)} in {FormatOpeningsList(context.TopOpenings, "the recurring structures")}."
                ],
                [
                    "Capture-sequence exercises focused on material balance.",
                    "Blunder-check drill: write the final material count after each forcing line.",
                    "Review games where one missed recapture changed the result.",
                    openingSummary
                ]),
            "piece_activity" => new TrainingRecommendation(
                priority,
                "Piece coordination",
                "Improve Piece Activity",
                $"You are giving away too many useful tempi on moves that reduce mobility or coordination instead of improving the worst-placed piece. {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Before moving, ask which of your pieces is doing the least work.",
                    "Prefer squares that improve mobility, central control or coordination with other pieces.",
                    "Avoid edge retreats unless they solve a concrete tactical problem.",
                    $"Review this especially in {FormatPhaseText(context.DominantPhase, fallback: "the phase where these drifts appear most")}."
                ],
                [
                    "Find-the-best-square exercises for knights and bishops.",
                    "Annotate middlegame plans by identifying your worst piece each turn.",
                    "Review losses for passive regrouping moves that handed over initiative.",
                    openingSummary
                ]),
            _ => new TrainingRecommendation(
                priority,
                "Critical review",
                "Review Critical Moments",
                $"Your profile still has a mixed error picture, so the best next step is disciplined review of the positions where the evaluation turned fastest. {contextSummary}",
                context.DominantPhase,
                context.DominantSide,
                context.TopOpenings,
                [
                    "Stop at every big swing and identify the first missed forcing reply.",
                    "Write one sentence about what should have been checked before the move.",
                    "Group similar mistakes together instead of reviewing games one by one.",
                    $"Use {FormatOpeningsList(context.TopOpenings, "your most relevant openings")} as the first review set."
                ],
                [
                    "Mistake notebook built from your own analysis list.",
                    "Short review sessions of only the largest evaluation swings.",
                    "Theme tagging of recent blunders to find a dominant pattern.",
                    openingSummary
                ])
        };
    }

    private static RecommendationContext BuildRecommendationContext(IReadOnlyList<PlayerProfileRecord> records, string label)
    {
        List<RecommendationOccurrence> occurrences = records
            .SelectMany(record => BuildRecommendationOccurrences(record, label))
            .ToList();

        if (occurrences.Count == 0)
        {
            return new RecommendationContext(null, null, []);
        }

        GamePhase? dominantPhase = occurrences
            .Where(item => item.Phase.HasValue)
            .GroupBy(item => item.Phase!.Value)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => (GamePhase?)group.Key)
            .FirstOrDefault();

        PlayerSide? dominantSide = occurrences
            .GroupBy(item => item.Side)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => (PlayerSide?)group.Key)
            .FirstOrDefault();

        IReadOnlyList<string> topOpenings = occurrences
            .GroupBy(item => item.Eco, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(group => group.Key)
            .ToList();

        return new RecommendationContext(dominantPhase, dominantSide, topOpenings);
    }

    private static WeeklyTrainingPlan BuildWeeklyPlan(string displayName, IReadOnlyList<TrainingRecommendation> recommendations)
    {
        List<TrainingRecommendation> planRecommendations = recommendations.Count == 0
            ? [CreateFallbackRecommendation()]
            : recommendations.ToList();

        TrainingRecommendation primary = GetRecommendation(planRecommendations, 0);
        TrainingRecommendation secondary = GetRecommendation(planRecommendations, 1);
        TrainingRecommendation tertiary = GetRecommendation(planRecommendations, 2);

        string summary = recommendations.Count == 0
            ? "Start with a simple review rhythm: one focused theme, one practical game session and one end-of-week recap."
            : $"Built from your top priorities: {string.Join(", ", planRecommendations.Take(3).Select(item => item.Title))}.";

        List<WeeklyTrainingDay> days =
        [
            new WeeklyTrainingDay(
                1,
                "Baseline scan",
                primary.FocusArea,
                35,
                [
                    $"Read the priority theme aloud: {primary.Title}.",
                    PickOrFallback(primary.Checklist, 0, "Write down the one board-scan question you want to repeat before every move."),
                    PickOrFallback(primary.SuggestedDrills, 0, "Solve a short puzzle set linked to your main recurring mistake."),
                    $"Save one example position that captures the idea behind {primary.FocusArea.ToLowerInvariant()}."
                ],
                $"You finish with one concrete trigger phrase for {primary.FocusArea.ToLowerInvariant()}."),
            new WeeklyTrainingDay(
                2,
                "Deep work",
                primary.FocusArea,
                45,
                [
                    PickOrFallback(primary.Checklist, 1, "Repeat the same scan after every candidate move."),
                    PickOrFallback(primary.Checklist, 2, "Compare your move against the safest practical alternative."),
                    PickOrFallback(primary.SuggestedDrills, 1, "Review two of your own mistakes in slow motion."),
                    $"Close with 5 minutes of verbal recap: what usually goes wrong in {FormatRecommendationContext(primary)}?"
                ],
                $"You can explain why {primary.Title.ToLowerInvariant()} matters before you make the move, not after."),
            new WeeklyTrainingDay(
                3,
                "Secondary theme",
                secondary.FocusArea,
                40,
                [
                    $"Switch focus to: {secondary.Title}.",
                    PickOrFallback(secondary.Checklist, 0, "List the first thing you should verify in this type of position."),
                    PickOrFallback(secondary.SuggestedDrills, 0, "Do one drill block dedicated to the secondary pattern."),
                    $"Note how {secondary.FocusArea.ToLowerInvariant()} connects with your main theme from day 1."
                ],
                $"You identify at least one recurring pattern in {secondary.FocusArea.ToLowerInvariant()} positions."),
            new WeeklyTrainingDay(
                4,
                "Review and reset",
                "Integration",
                25,
                [
                    $"Revisit one saved mistake from {primary.Title} and one from {secondary.Title}.",
                    $"Use this checklist pair: {PickOrFallback(primary.Checklist, 0, "scan safety")} + {PickOrFallback(secondary.Checklist, 0, "scan forcing ideas")}.",
                    "Stop after each critical move and say which theme should have guided the decision.",
                    "Keep the session light: the goal is clean recall, not volume."
                ],
                "You can name the right training theme for both reviewed positions within a few seconds."),
            new WeeklyTrainingDay(
                5,
                "Applied game",
                $"{primary.FocusArea} + {secondary.FocusArea}",
                50,
                [
                    $"Play one slow training game with extra attention on {primary.Title}.",
                    $"Before every move, repeat the top checks from {primary.Title} and {secondary.Title}.",
                    "Mark 3 positions where you nearly defaulted to your old habit.",
                    PickOrFallback(secondary.SuggestedDrills, 1, "After the game, review the sharpest decision and write one better candidate move.")
                ],
                "You complete one full game where your process stays visible from opening to endgame."),
            new WeeklyTrainingDay(
                6,
                "Third theme and structures",
                tertiary.FocusArea,
                35,
                [
                    $"Work on the supporting theme: {tertiary.Title}.",
                    PickOrFallback(tertiary.Checklist, 0, "Write a one-line reminder for this theme."),
                    PickOrFallback(tertiary.SuggestedDrills, 0, "Review positions from your own games that match this theme."),
                    BuildOpeningTask(tertiary)
                ],
                $"You finish with one reusable rule for {tertiary.FocusArea.ToLowerInvariant()} positions."),
            new WeeklyTrainingDay(
                7,
                "Weekly assessment",
                "Reflection",
                20,
                [
                    $"Rank your confidence in these themes: {primary.Title}, {secondary.Title}, {tertiary.Title}.",
                    "List the easiest improvement and the habit that still feels unstable.",
                    "Choose one position to revisit next week as a checkpoint.",
                    "Prepare the next week around the theme that still breaks first under pressure."
                ],
                "You end the week with one clear priority for the next training cycle.")
        ];

        return new WeeklyTrainingPlan(
            $"{displayName} Weekly Training Plan",
            summary,
            days);
    }

    private static TrainingRecommendation CreateFallbackRecommendation()
    {
        return new TrainingRecommendation(
            1,
            "General review",
            "Review Critical Moments",
            "No dominant error pattern has been identified yet, so the next best step is a short weekly cycle built around your biggest evaluation swings.",
            null,
            null,
            [],
            [
                "Stop at every large evaluation swing and explain what should have been checked first.",
                "Keep the review focused on one simple question per move."
            ],
            [
                "Replay one recent game and pause before every critical decision.",
                "Collect 5 positions that felt unclear and review them slowly."
            ]);
    }

    private static GamePhase? GuessMistakePhase(SelectedMistake mistake)
    {
        return mistake.Moves.Count == 0
            ? null
            : mistake.Moves
                .GroupBy(move => move.Replay.Phase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => (GamePhase?)group.Key)
                .FirstOrDefault();
    }

    private static IReadOnlyList<RecommendationOccurrence> BuildRecommendationOccurrences(PlayerProfileRecord record, string label)
    {
        string eco = string.IsNullOrWhiteSpace(record.Result.Game.Eco) ? "Unknown" : record.Result.Game.Eco!;

        List<RecommendationOccurrence> fromHighlightedMistakes = record.HighlightedMistakes
            .Where(mistake => string.Equals(mistake.Tag?.Label ?? "unclassified", label, StringComparison.Ordinal))
            .Select(mistake => new RecommendationOccurrence(
                record.Side,
                GuessMistakePhase(mistake),
                eco))
            .ToList();

        if (fromHighlightedMistakes.Any(item => item.Phase.HasValue))
        {
            return fromHighlightedMistakes;
        }

        List<RecommendationOccurrence> fromMoveAnalyses = record.Result.MoveAnalyses
            .Where(move => string.Equals(move.MistakeTag?.Label ?? "unclassified", label, StringComparison.Ordinal))
            .Select(move => new RecommendationOccurrence(
                record.Side,
                move.Replay.Phase,
                eco))
            .ToList();

        return fromMoveAnalyses.Count > 0
            ? fromMoveAnalyses
            : fromHighlightedMistakes;
    }

    private static string BuildContextSummary(RecommendationContext context)
    {
        List<string> parts = [];

        if (context.DominantPhase.HasValue)
        {
            parts.Add($"It shows up most often in {FormatPhaseText(context.DominantPhase.Value, fallback: "that phase")}");
        }

        if (context.DominantSide.HasValue)
        {
            parts.Add($"mostly when you analyze {context.DominantSide.Value}");
        }

        if (context.TopOpenings.Count > 0)
        {
            parts.Add($"and especially in {FormatOpeningsList(context.TopOpenings, "these openings")}");
        }

        return parts.Count == 0
            ? "This is one of the most repeated patterns in your saved analyses."
            : string.Join(" ", parts) + ".";
    }

    private static string BuildOpeningSummary(IReadOnlyList<string> openings)
    {
        return openings.Count == 0
            ? "Build a small custom drill set from the openings where your own mistakes recur most."
            : $"Build a mini review pack from positions coming out of {FormatOpeningsList(openings, "your most relevant openings")}.";
    }

    private static string FormatOpeningsList(IReadOnlyList<string> openings, string fallback)
    {
        if (openings.Count == 0)
        {
            return fallback;
        }

        IReadOnlyList<string> formattedOpenings = openings
            .Select(OpeningCatalog.Describe)
            .ToList();

        return formattedOpenings.Count == 1
            ? formattedOpenings[0]
            : string.Join(" and ", formattedOpenings);
    }

    private static string FormatPhaseText(GamePhase? phase, string fallback)
    {
        return phase switch
        {
            GamePhase.Opening => "the opening",
            GamePhase.Middlegame => "the middlegame",
            GamePhase.Endgame => "the endgame",
            _ => fallback
        };
    }

    private static string BuildSideSuffix(PlayerSide? side)
    {
        return side switch
        {
            PlayerSide.White => " as White",
            PlayerSide.Black => " as Black",
            _ => string.Empty
        };
    }

    private static TrainingRecommendation GetRecommendation(IReadOnlyList<TrainingRecommendation> recommendations, int index)
    {
        return recommendations[Math.Min(index, recommendations.Count - 1)];
    }

    private static string PickOrFallback(IReadOnlyList<string> values, int index, string fallback)
    {
        return values.Count > index && !string.IsNullOrWhiteSpace(values[index])
            ? values[index]
            : fallback;
    }

    private static string BuildOpeningTask(TrainingRecommendation recommendation)
    {
        return recommendation.RelatedOpenings.Count == 0
            ? "Review one structure from your own recent games where this theme appeared."
            : $"Review two example positions from {string.Join(" / ", recommendation.RelatedOpenings.Select(OpeningCatalog.Describe))} and connect them to this theme.";
    }

    private static string FormatRecommendationContext(TrainingRecommendation recommendation)
    {
        List<string> parts = [];

        if (recommendation.EmphasisPhase.HasValue)
        {
            parts.Add(FormatPhaseText(recommendation.EmphasisPhase.Value, recommendation.EmphasisPhase.Value.ToString()));
        }

        if (recommendation.EmphasisSide.HasValue)
        {
            parts.Add(recommendation.EmphasisSide.Value.ToString());
        }

        if (recommendation.RelatedOpenings.Count > 0)
        {
            parts.Add(string.Join(", ", recommendation.RelatedOpenings.Select(OpeningCatalog.Describe)));
        }

        return parts.Count == 0
            ? "your current profile context"
            : string.Join(" | ", parts);
    }

    private static PlayerProfileRecord? CreateProfileRecord(GameAnalysisResult result)
    {
        string? playerName = result.AnalyzedSide == PlayerSide.White
            ? result.Game.WhitePlayer
            : result.Game.BlackPlayer;

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        return new PlayerProfileRecord(
            GameFingerprint.Compute(result.Game.PgnText),
            NormalizePlayerKey(playerName),
            playerName.Trim(),
            result.AnalyzedSide,
            result.HighlightedMistakes,
            ParseMonthKey(result.Game.DateText),
            result);
    }

    private static int? TryAverage(IEnumerable<int?> values)
    {
        List<int> knownValues = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return knownValues.Count == 0
            ? null
            : (int)Math.Round(knownValues.Average());
    }

    private static string NormalizePlayerKey(string playerName)
    {
        return playerName.Trim().ToLowerInvariant();
    }

    private static string? ParseMonthKey(string? dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText))
        {
            return null;
        }

        string[] formats =
        [
            "yyyy.MM.dd",
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyy.MM",
            "yyyy-MM",
            "yyyy/MM"
        ];

        if (DateTime.TryParseExact(dateText, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private sealed record PlayerProfileRecord(
        string GameFingerprint,
        string PlayerKey,
        string DisplayName,
        PlayerSide Side,
        IReadOnlyList<SelectedMistake> HighlightedMistakes,
        string? MonthKey,
        GameAnalysisResult Result);

    private sealed record RecommendationContext(
        GamePhase? DominantPhase,
        PlayerSide? DominantSide,
        IReadOnlyList<string> TopOpenings);

    private sealed record RecommendationOccurrence(
        PlayerSide Side,
        GamePhase? Phase,
        string Eco);
}
