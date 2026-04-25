namespace MoveMentorChessServices;

public sealed class TrainingPlanService
{
    public TrainingPlanReport Build(PlayerProfileReport profileReport, OpeningWeaknessReport? openingReport = null)
    {
        ArgumentNullException.ThrowIfNull(profileReport);

        IReadOnlyList<TrainingPlanTopic> topics = BuildTopics(profileReport, openingReport);
        IReadOnlyList<TrainingRecommendation> recommendations = topics
            .Select(topic => new TrainingRecommendation(
                topic.Priority,
                topic.FocusArea,
                topic.Title,
                topic.Summary,
                topic.EmphasisPhase,
                topic.EmphasisSide,
                topic.RelatedOpenings,
                topic.Checklist,
                topic.SuggestedDrills,
                topic.Examples,
                topic.Blocks))
            .ToList();

        WeeklyTrainingPlan weeklyPlan = BuildWeeklyPlan(profileReport.DisplayName, topics);
        string summary = topics.Count == 0
            ? "Not enough stable profile data yet. Start with a light review loop and collect more analyzed games."
            : $"Built from deterministic priorities and block mapping: {string.Join(", ", topics.Select(topic => topic.Title))}.";

        return new TrainingPlanReport(
            profileReport.PlayerKey,
            profileReport.DisplayName,
            profileReport.ProgressSignal.Direction,
            summary,
            topics,
            recommendations,
            weeklyPlan);
    }

    private static IReadOnlyList<TrainingPlanTopic> BuildTopics(PlayerProfileReport profileReport, OpeningWeaknessReport? openingReport)
    {
        List<string> candidateLabels = profileReport.TopMistakeLabels
            .Select(item => item.Label)
            .Concat(profileReport.CostliestMistakeLabels.Select(item => item.Label))
            .Concat(profileReport.MistakeExamples.Select(item => item.Label))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (HasActionableOpeningWeakness(openingReport)
            && !candidateLabels.Contains("opening_principles", StringComparer.Ordinal))
        {
            candidateLabels.Add("opening_principles");
        }

        if (candidateLabels.Count == 0)
        {
            return [CreateFallbackTopic(profileReport)];
        }

        List<TrainingPlanTopic> rankedTopics = candidateLabels
            .Select(label => BuildTopic(profileReport, openingReport, label))
            .OrderByDescending(topic => topic.PriorityBreakdown.TotalScore)
            .ThenByDescending(topic => topic.PriorityBreakdown.CostScore)
            .ThenByDescending(topic => topic.PriorityBreakdown.FrequencyScore)
            .ThenBy(topic => topic.Label, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        int nonImprovingRank = 0;
        return rankedTopics
            .Select((topic, index) => topic with
            {
                Priority = index + 1,
                Category = DetermineCategory(topic.TrendDirection, nonImprovingRank += topic.TrendDirection == ProfileProgressDirection.Improving ? 0 : 1)
            })
            .ToList();
    }

    private static TrainingPlanTopic BuildTopic(PlayerProfileReport profileReport, OpeningWeaknessReport? openingReport, string label)
    {
        ProfileLabelStat? frequent = profileReport.TopMistakeLabels
            .FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));
        ProfileCostlyLabelStat? costly = profileReport.CostliestMistakeLabels
            .FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal));
        IReadOnlyList<ProfileMistakeExample> examples = profileReport.MistakeExamples
            .Where(item => string.Equals(item.Label, label, StringComparison.Ordinal))
            .Take(3)
            .ToList();

        GamePhase? emphasisPhase = DetermineEmphasisPhase(profileReport, examples);
        PlayerSide? emphasisSide = examples.Count == 0
            ? null
            : examples
                .GroupBy(item => item.Side)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => (PlayerSide?)group.Key)
                .FirstOrDefault();
        IReadOnlyList<string> relatedOpenings = BuildRelatedOpenings(label, examples, openingReport);
        ProfileProgressDirection labelTrend = profileReport.LabelTrends
            .FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.Ordinal))
            ?.Direction
            ?? ProfileProgressDirection.InsufficientData;

        int frequencyScore = (frequent?.Count ?? 0) * 100;
        int costScore = costly is null
            ? 0
            : (costly.TotalCentipawnLoss * 2) + ((costly.AverageCentipawnLoss ?? 0) * 3);
        int trendScore = GetTrendScore(labelTrend);
        int phaseScore = GetPhaseScore(profileReport, emphasisPhase);
        int openingWeaknessScore = GetOpeningWeaknessScore(label, openingReport);
        int totalScore = frequencyScore + costScore + trendScore + phaseScore + openingWeaknessScore;
        TopicTemplate template = GetTemplate(label);
        IReadOnlyList<TrainingBlock> blocks = BuildBlocks(label, template, emphasisPhase, emphasisSide, relatedOpenings);

        string phaseSummary = emphasisPhase.HasValue
            ? $" Most often it appears in {FormatPhase(emphasisPhase.Value).ToLowerInvariant()}."
            : string.Empty;
        string openingSummary = relatedOpenings.Count == 0
            ? string.Empty
            : $" It also clusters around {string.Join(" / ", relatedOpenings.Select(PlayerProfileTextFormatter.FormatOpening))}.";
        string blockSummary = blocks.Count == 0
            ? string.Empty
            : $" Training blocks: {string.Join(", ", blocks.Select(block => $"{PlayerProfileTextFormatter.FormatTrainingBlockPurpose(block.Purpose).ToLowerInvariant()} {PlayerProfileTextFormatter.FormatTrainingBlockKind(block.Kind).ToLowerInvariant()}"))}.";
        string whyThisTopicNow = BuildWhyThisTopicNow(
            frequent?.Count ?? 0,
            costly?.TotalCentipawnLoss ?? 0,
            costly?.AverageCentipawnLoss,
            labelTrend,
            profileReport.MistakesByPhase.FirstOrDefault()?.Phase,
            emphasisPhase,
            relatedOpenings,
            openingReport,
            label);
        string rationale = BuildChessRationale(
            frequent?.Count ?? 0,
            costly?.TotalCentipawnLoss ?? 0,
            costly?.AverageCentipawnLoss,
            labelTrend,
            emphasisPhase);
        string summary = $"{template.Description} {BuildTrendSummary(labelTrend)}{phaseSummary}{openingSummary}{blockSummary}".Trim();

        return new TrainingPlanTopic(
            0,
            TrainingPlanTopicCategory.CoreWeakness,
            label,
            template.FocusArea,
            template.Title,
            summary,
            whyThisTopicNow,
            rationale,
            labelTrend,
            emphasisPhase,
            emphasisSide,
            relatedOpenings,
            ExtractChecklist(blocks),
            ExtractSuggestedDrills(blocks),
            blocks,
            examples,
            new TrainingPlanPriorityBreakdown(
                frequencyScore,
                costScore,
                trendScore,
                phaseScore,
                totalScore));
    }

    private static TrainingPlanTopic CreateFallbackTopic(PlayerProfileReport profileReport)
    {
        IReadOnlyList<TrainingBlock> blocks =
        [
            CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.GameReview, "Review recent critical moments", "Replay one recent game and stop before every large evaluation swing.", 30, profileReport.MistakesByPhase.FirstOrDefault()?.Phase, null, []),
            CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.SlowPlayFocus, "Slow down before committing", "Play one calmer practice block and name the first thing that had to be checked before moving.", 20, profileReport.MistakesByPhase.FirstOrDefault()?.Phase, null, []),
            CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.SlowPlayFocus, "Default board-scan checklist", "Keep one short board-scan phrase visible and use it before every critical move.", 15, profileReport.MistakesByPhase.FirstOrDefault()?.Phase, null, [])
        ];

        return new TrainingPlanTopic(
            1,
            TrainingPlanTopicCategory.CoreWeakness,
            "general_review",
            "General review",
            "Review critical moments",
            "No single tactical or strategic pattern dominates yet, so keep a stable review rhythm and collect more analyzed games.",
            "There is not enough topic-specific data yet, so this stays as a general review anchor until stronger patterns emerge.",
            "Fallback topic created because the profile does not yet contain enough labeled mistakes.",
            ProfileProgressDirection.InsufficientData,
            profileReport.MistakesByPhase.FirstOrDefault()?.Phase,
            null,
            [],
            ExtractChecklist(blocks),
            ExtractSuggestedDrills(blocks),
            blocks,
            [],
            new TrainingPlanPriorityBreakdown(0, 0, 0, 0, 0));
    }

    private static WeeklyTrainingPlan BuildWeeklyPlan(string displayName, IReadOnlyList<TrainingPlanTopic> topics)
    {
        List<TrainingPlanTopic> planTopics = topics.ToList();
        TrainingPlanTopic core = planTopics[0];
        TrainingPlanTopic secondary = planTopics[Math.Min(1, planTopics.Count - 1)];
        TrainingPlanTopic maintenance = planTopics[Math.Min(2, planTopics.Count - 1)];

        List<WeeklyTrainingDay> days =
        [
            CreateDay(1, core, GetBlock(core, TrainingBlockPurpose.Repair)),
            CreateDay(2, core, GetBlock(core, TrainingBlockPurpose.Maintain)),
            CreateDay(3, secondary, GetBlock(secondary, TrainingBlockPurpose.Repair)),
            CreateDay(4, secondary, GetBlock(secondary, TrainingBlockPurpose.Checklist)),
            CreateDay(5, core, GetBlock(core, TrainingBlockPurpose.Checklist)),
            CreateDay(6, maintenance, GetBlock(maintenance, TrainingBlockPurpose.Maintain)),
            CreateDay(7, maintenance, GetBlock(maintenance, TrainingBlockPurpose.Checklist))
        ];

        WeeklyTrainingBudget budget = BuildWeeklyBudget(core, secondary, maintenance, days);

        return new WeeklyTrainingPlan(
            $"{displayName} Weekly Training Plan",
            $"Deterministic weekly cycle built from the core weakness ({core.Title}), the secondary weakness ({secondary.Title}) and the maintenance topic ({maintenance.Title}).",
            budget,
            days);
    }

    private static WeeklyTrainingDay CreateDay(int dayNumber, TrainingPlanTopic topic, TrainingBlock block)
    {
        return new WeeklyTrainingDay(
            dayNumber,
            topic.Title,
            $"{PlayerProfileTextFormatter.FormatTrainingBlockPurpose(block.Purpose)} • {PlayerProfileTextFormatter.FormatTrainingBlockKind(block.Kind)}",
            block.Description,
            block.EstimatedMinutes,
            topic.Category,
            block.Purpose,
            block.Kind,
            topic.RelatedOpenings,
            block.Kind == TrainingBlockKind.OpeningReview
                ? DetermineOpeningTrainingMode(block.Purpose)
                : null);
    }

    private static WeeklyTrainingBudget BuildWeeklyBudget(TrainingPlanTopic core, TrainingPlanTopic secondary, TrainingPlanTopic maintenance, IReadOnlyList<WeeklyTrainingDay> days)
    {
        int coreMinutes = days.Where(day => day.Category == TrainingPlanTopicCategory.CoreWeakness).Sum(day => day.EstimatedMinutes);
        int secondaryMinutes = days.Where(day => day.Category == TrainingPlanTopicCategory.SecondaryWeakness).Sum(day => day.EstimatedMinutes);
        int maintenanceMinutes = days.Where(day => day.Category == TrainingPlanTopicCategory.MaintenanceTopic).Sum(day => day.EstimatedMinutes);
        int integrationMinutes = 0;
        int totalMinutes = days.Sum(day => day.EstimatedMinutes);

        return new WeeklyTrainingBudget(
            totalMinutes,
            coreMinutes,
            secondaryMinutes,
            maintenanceMinutes,
            integrationMinutes,
            $"About {totalMinutes} minutes for the week: {coreMinutes} on {core.Title}, {secondaryMinutes} on {secondary.Title}, {maintenanceMinutes} on {maintenance.Title}. Each slot is selected deterministically from repair, maintain and checklist blocks.");
    }

    private static TrainingBlock GetBlock(TrainingPlanTopic topic, TrainingBlockPurpose purpose)
    {
        return topic.Blocks.FirstOrDefault(block => block.Purpose == purpose)
            ?? topic.Blocks.First();
    }

    private static string BuildChessRationale(int occurrences, int totalCentipawnLoss, int? averageCentipawnLoss, ProfileProgressDirection trendDirection, GamePhase? emphasisPhase)
    {
        string frequencyText = occurrences <= 0
            ? "This theme has not appeared often yet, but it still deserves monitoring."
            : occurrences == 1
                ? "This theme already showed up in one analyzed mistake."
                : $"This theme keeps coming back: {occurrences} analyzed mistakes point to the same habit.";

        string costText = totalCentipawnLoss <= 0
            ? "The current sample does not show a large material or evaluation cost yet."
            : averageCentipawnLoss.HasValue
                ? $"When it appears, it is expensive: it has cost about {totalCentipawnLoss} centipawns in total, around {averageCentipawnLoss.Value} on average."
                : $"When it appears, it is expensive: it has cost about {totalCentipawnLoss} centipawns in total.";

        string trendText = trendDirection switch
        {
            ProfileProgressDirection.Regressing => "Recent games suggest this problem is becoming more urgent, not less.",
            ProfileProgressDirection.Improving => "Recent games look cleaner, so this can be trained without panic.",
            ProfileProgressDirection.Stable => "Recent games show that this habit is still stable enough to justify focused work.",
            _ => "There is not enough recent data yet, so the plan leans more on repeated mistakes than on form."
        };

        string phaseText = emphasisPhase.HasValue
            ? $"It shows up most often in the {FormatPhase(emphasisPhase.Value).ToLowerInvariant()}, so that phase gets extra training time."
            : "It is not tied strongly to one phase yet, so the plan keeps the work general.";

        return $"{frequencyText} {costText} {trendText} {phaseText}";
    }

    private static string BuildWhyThisTopicNow(
        int occurrences,
        int totalCentipawnLoss,
        int? averageCentipawnLoss,
        ProfileProgressDirection trendDirection,
        GamePhase? weakestPhase,
        GamePhase? emphasisPhase,
        IReadOnlyList<string> relatedOpenings,
        OpeningWeaknessReport? openingReport,
        string label)
    {
        List<string> parts = [];

        parts.Add(occurrences switch
        {
            <= 0 => "Frequency: the sample is still small, so this topic is tracked conservatively.",
            1 => "Frequency: this theme already appeared in one analyzed mistake.",
            _ => $"Frequency: this theme appeared {occurrences} times in analyzed mistakes."
        });

        parts.Add(totalCentipawnLoss <= 0
            ? "CPL cost: the current sample does not show a large centipawn penalty yet."
            : averageCentipawnLoss.HasValue
                ? $"CPL cost: it has already cost {totalCentipawnLoss} centipawns in total, about {averageCentipawnLoss.Value} on average."
                : $"CPL cost: it has already cost {totalCentipawnLoss} centipawns in total.");

        parts.Add($"Trend: {DescribeTrend(trendDirection)}");

        if (emphasisPhase.HasValue)
        {
            string phaseText = FormatPhase(emphasisPhase.Value);
            if (weakestPhase.HasValue && weakestPhase.Value == emphasisPhase.Value)
            {
                parts.Add($"Weakest phase: it lines up with the current weakest phase, {phaseText}.");
            }
            else
            {
                parts.Add($"Weakest phase: it shows up most in {phaseText}, so the plan leans there now.");
            }
        }

        if (relatedOpenings.Count > 0)
        {
            parts.Add($"Openings: it clusters around {string.Join(" / ", relatedOpenings.Select(PlayerProfileTextFormatter.FormatOpening))}.");
        }

        if (string.Equals(label, "opening_principles", StringComparison.Ordinal)
            && HasActionableOpeningWeakness(openingReport))
        {
            IReadOnlyList<OpeningWeaknessEntry> urgentOpenings = GetActionableOpenings(openingReport)
                .Take(2)
                .ToList();
            parts.Add(
                $"Opening trainer: add focused sessions for {string.Join(" / ", urgentOpenings.Select(item => PlayerProfileTextFormatter.FormatOpening(item.Eco)))} because the opening report marks them as unstable or costly.");
        }

        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> BuildRelatedOpenings(
        string label,
        IReadOnlyList<ProfileMistakeExample> examples,
        OpeningWeaknessReport? openingReport)
    {
        IEnumerable<string> values = examples
            .Select(item => item.Eco)
            .Where(item => !string.IsNullOrWhiteSpace(item));

        if (string.Equals(label, "opening_principles", StringComparison.Ordinal)
            && HasActionableOpeningWeakness(openingReport))
        {
            values = values.Concat(GetActionableOpenings(openingReport).Select(item => item.Eco));
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static int GetOpeningWeaknessScore(string label, OpeningWeaknessReport? openingReport)
    {
        if (!string.Equals(label, "opening_principles", StringComparison.Ordinal)
            || !HasActionableOpeningWeakness(openingReport))
        {
            return 0;
        }

        return GetActionableOpenings(openingReport)
            .Take(3)
            .Sum(opening => opening.Category == OpeningWeaknessCategory.FixNow ? 220 : 110);
    }

    private static bool HasActionableOpeningWeakness(OpeningWeaknessReport? openingReport)
    {
        return openingReport is not null
            && openingReport.WeakOpenings.Any(opening =>
                opening.Category is OpeningWeaknessCategory.FixNow or OpeningWeaknessCategory.ReviewLater);
    }

    private static IEnumerable<OpeningWeaknessEntry> GetActionableOpenings(OpeningWeaknessReport? openingReport)
    {
        if (openingReport is null)
        {
            return [];
        }

        return openingReport.WeakOpenings
            .Where(opening => opening.Category is OpeningWeaknessCategory.FixNow or OpeningWeaknessCategory.ReviewLater)
            .OrderBy(opening => opening.Category == OpeningWeaknessCategory.FixNow ? 0 : 1)
            .ThenByDescending(opening => opening.AverageOpeningCentipawnLoss ?? 0)
            .ThenByDescending(opening => opening.Count);
    }

    private static OpeningTrainingMode DetermineOpeningTrainingMode(TrainingBlockPurpose purpose)
    {
        return purpose switch
        {
            TrainingBlockPurpose.Repair => OpeningTrainingMode.MistakeRepair,
            TrainingBlockPurpose.Maintain => OpeningTrainingMode.LineRecall,
            TrainingBlockPurpose.Checklist => OpeningTrainingMode.BranchAwareness,
            _ => OpeningTrainingMode.LineRecall
        };
    }

    private static int GetTrendScore(ProfileProgressDirection direction)
    {
        return direction switch
        {
            ProfileProgressDirection.Regressing => 180,
            ProfileProgressDirection.Stable => 60,
            ProfileProgressDirection.Improving => -120,
            _ => 0
        };
    }

    private static TrainingPlanTopicCategory DetermineCategory(ProfileProgressDirection direction, int nonImprovingRank)
    {
        if (direction == ProfileProgressDirection.Improving)
        {
            return TrainingPlanTopicCategory.MaintenanceTopic;
        }

        return nonImprovingRank switch
        {
            1 => TrainingPlanTopicCategory.CoreWeakness,
            2 => TrainingPlanTopicCategory.SecondaryWeakness,
            _ => TrainingPlanTopicCategory.MaintenanceTopic
        };
    }

    private static int GetPhaseScore(PlayerProfileReport profileReport, GamePhase? emphasisPhase)
    {
        if (!emphasisPhase.HasValue)
        {
            return 0;
        }

        int index = profileReport.MistakesByPhase
            .Select((item, itemIndex) => new { item.Phase, itemIndex })
            .Where(item => item.Phase == emphasisPhase.Value)
            .Select(item => item.itemIndex)
            .DefaultIfEmpty(profileReport.MistakesByPhase.Count)
            .First();

        return index switch
        {
            0 => 90,
            1 => 55,
            2 => 25,
            _ => 10
        };
    }

    private static GamePhase? DetermineEmphasisPhase(PlayerProfileReport profileReport, IReadOnlyList<ProfileMistakeExample> examples)
    {
        if (examples.Count > 0)
        {
            return examples
                .GroupBy(item => item.Phase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => (GamePhase?)group.Key)
                .FirstOrDefault();
        }

        return profileReport.MistakesByPhase.FirstOrDefault()?.Phase;
    }

    private static string BuildTrendSummary(ProfileProgressDirection direction)
    {
        return direction switch
        {
            ProfileProgressDirection.Regressing => "The recent trend makes this theme more urgent.",
            ProfileProgressDirection.Improving => "Recent form is improving, so this becomes a lighter maintenance target.",
            ProfileProgressDirection.Stable => "The trend is stable, so this remains a reliable training target.",
            _ => "There is not enough trend data yet, so the ranking relies on mistake volume and cost."
        };
    }

    private static string DescribeTrend(ProfileProgressDirection direction)
    {
        return direction switch
        {
            ProfileProgressDirection.Regressing => "regressing, so the topic gets extra urgency.",
            ProfileProgressDirection.Improving => "improving, so the topic is shifted toward maintenance/review.",
            ProfileProgressDirection.Stable => "stable, so it remains a consistent training target.",
            _ => "insufficient data, so priority stays anchored to frequency and CPL cost."
        };
    }

    private static TopicTemplate GetTemplate(string label)
    {
        return label switch
        {
            "hanging_piece" => new TopicTemplate("Board safety", "Protect loose pieces", "Repeated material losses come from leaving pieces underdefended after otherwise playable moves."),
            "missed_tactic" => new TopicTemplate("Tactics", "Checks, captures, threats", "Forcing resources are being missed often enough that tactical scanning should stay at the front of the move process."),
            "opening_principles" => new TopicTemplate("Opening discipline", "Clean up the opening", "Too many early inaccuracies come from development delays, side moves and king-safety shortcuts."),
            "king_safety" => new TopicTemplate("King safety", "Safer king decisions", "King shelter is breaking down too easily after pawn pushes or slow defensive reactions."),
            "endgame_technique" => new TopicTemplate("Endgames", "Sharpen endgame technique", "Reduced-material positions still leak points through technical slips and king-activity mistakes."),
            "material_loss" => new TopicTemplate("Material discipline", "Calculate the full exchange", "Material is being dropped in forcing lines that are not being checked to the end."),
            "piece_activity" => new TopicTemplate("Piece coordination", "Improve piece activity", "Useful tempi are being spent on moves that reduce coordination instead of improving the worst-placed piece."),
            _ => new TopicTemplate("Pattern review", PlayerProfileTextFormatter.FormatMistakeLabel(label), "This recurring label keeps surfacing in the profile and deserves a focused training block.")
        };
    }

    private static IReadOnlyList<string> ExtractChecklist(IReadOnlyList<TrainingBlock> blocks)
    {
        return blocks
            .Where(block => block.Purpose == TrainingBlockPurpose.Checklist)
            .Select(block => block.Description)
            .DefaultIfEmpty("Use one simple board-scan checklist before every critical move.")
            .ToList();
    }

    private static IReadOnlyList<string> ExtractSuggestedDrills(IReadOnlyList<TrainingBlock> blocks)
    {
        return blocks
            .Where(block => block.Purpose != TrainingBlockPurpose.Checklist)
            .Select(block => block.Description)
            .ToList();
    }

    private static TrainingBlock CreateBlock(TrainingBlockPurpose purpose, TrainingBlockKind kind, string title, string description, int estimatedMinutes, GamePhase? emphasisPhase, PlayerSide? emphasisSide, IReadOnlyList<string> relatedOpenings)
    {
        return new TrainingBlock(purpose, kind, title, description, estimatedMinutes, emphasisPhase, emphasisSide, relatedOpenings);
    }

    private static string FormatPhase(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Opening => "Opening",
            GamePhase.Middlegame => "Middlegame",
            GamePhase.Endgame => "Endgame",
            _ => phase.ToString()
        };
    }

    private static IReadOnlyList<TrainingBlock> BuildBlocks(string label, TopicTemplate template, GamePhase? emphasisPhase, PlayerSide? emphasisSide, IReadOnlyList<string> relatedOpenings)
    {
        string phaseText = emphasisPhase.HasValue
            ? FormatPhase(emphasisPhase.Value).ToLowerInvariant()
            : "critical positions";
        string sideText = emphasisSide.HasValue
            ? emphasisSide.Value == PlayerSide.White ? " as White" : " as Black"
            : string.Empty;
        string openingText = relatedOpenings.Count == 0
            ? "your own recurring structures"
            : string.Join(" / ", relatedOpenings.Select(PlayerProfileTextFormatter.FormatOpening));

        return label switch
        {
            "hanging_piece" =>
            [
                CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.Tactics, "Loose-piece repair", $"Solve a short attacker-defender counting set and keep checking what becomes loose after each move{sideText}.", 25, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.GameReview, "Loose-piece review", $"Replay two recent mistakes from {openingText} and mark the first move where a piece stopped being defended.", 20, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.SlowPlayFocus, "Loose-piece checklist", $"In {phaseText}, ask after every candidate move: what did I leave loose{sideText}?", 15, emphasisPhase, emphasisSide, relatedOpenings)
            ],
            "missed_tactic" =>
            [
                CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.Tactics, "Forcing-line repair", $"Run a tactics block built around checks, captures and threats, especially in {phaseText}.", 25, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.GameReview, "Forcing-moment review", $"Review two of your own sharp positions from {openingText} and compare your move with the first forcing move you missed.", 20, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.SlowPlayFocus, "CCT checklist", $"Before every critical move{sideText}, list checks, captures and threats for both sides.", 15, emphasisPhase, emphasisSide, relatedOpenings)
            ],
            "opening_principles" =>
            [
                CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.OpeningReview, "Opening repair", $"Review the first 10 moves from {openingText} and replace drifting moves with development, center control or castling choices.", 25, emphasisPhase ?? GamePhase.Opening, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.GameReview, "Opening phase review", "Annotate one recent game and mark the first move where opening discipline broke down.", 20, emphasisPhase ?? GamePhase.Opening, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.SlowPlayFocus, "Opening checklist", "In the first 10 moves, ask whether the move develops, castles or fights for the center before anything else.", 15, emphasisPhase ?? GamePhase.Opening, emphasisSide, relatedOpenings)
            ],
            "king_safety" =>
            [
                CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.Tactics, "King-safety repair", $"Do a short mating-net and forcing-move set, then identify the first unsafe concession in your own {phaseText} positions.", 25, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.GameReview, "King-shelter review", $"Review one recent game from {openingText} and mark when your king shelter became harder to defend{sideText}.", 20, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.SlowPlayFocus, "King-safety checklist", "Before pawn pushes near your king, name the weakened square or file and the opponent's forcing reply.", 15, emphasisPhase, emphasisSide, relatedOpenings)
            ],
            "endgame_technique" =>
            [
                CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.EndgameDrill, "Endgame repair", "Run one king-and-pawn or rook-endgame drill block and compare candidate moves by king activity first.", 25, emphasisPhase ?? GamePhase.Endgame, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.GameReview, "Technical ending review", "Replay one of your own reduced-material games slowly and mark the moment the clean conversion plan disappeared.", 20, emphasisPhase ?? GamePhase.Endgame, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.SlowPlayFocus, "Endgame checklist", "Before every endgame move, compare king activity, passed pawns and counterplay in that order.", 15, emphasisPhase ?? GamePhase.Endgame, emphasisSide, relatedOpenings)
            ],
            "material_loss" =>
            [
                CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.Tactics, "Exchange calculation repair", "Run a capture-sequence exercise block and say the final material balance before accepting any forcing line.", 25, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.GameReview, "Exchange review", $"Review two costly lines from {openingText} and stop where the calculation was cut short.", 20, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.SlowPlayFocus, "Material checklist", "Before every tactical capture, calculate the full exchange to the end and name the final count.", 15, emphasisPhase, emphasisSide, relatedOpenings)
            ],
            "piece_activity" =>
            [
                CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.SlowPlayFocus, "Worst-piece repair", "Play through one slow block and identify the worst-placed piece before every move.", 25, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.GameReview, "Coordination review", "Replay one drifting middlegame and mark the first moment you improved the wrong piece.", 20, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.OpeningReview, "Piece-activity checklist", $"Review {openingText} and keep asking which move improves the worst-placed piece instead of adding a side move.", 15, emphasisPhase, emphasisSide, relatedOpenings)
            ],
            _ =>
            [
                CreateBlock(TrainingBlockPurpose.Repair, TrainingBlockKind.GameReview, $"{template.Title} repair", "Review two of your own positions with this label and write down the recurring decision error.", 25, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Maintain, TrainingBlockKind.SlowPlayFocus, $"{template.Title} maintenance", $"Play one slower block in {phaseText} and keep one practical reminder visible for this pattern.", 20, emphasisPhase, emphasisSide, relatedOpenings),
                CreateBlock(TrainingBlockPurpose.Checklist, TrainingBlockKind.GameReview, $"{template.Title} checklist", $"Before every critical move, repeat one short review question linked to {template.FocusArea.ToLowerInvariant()}.", 15, emphasisPhase, emphasisSide, relatedOpenings)
            ]
        };
    }

    private sealed record TopicTemplate(string FocusArea, string Title, string Description);
}
