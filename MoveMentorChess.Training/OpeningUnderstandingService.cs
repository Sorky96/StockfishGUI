namespace MoveMentorChess.Training;

public sealed class OpeningUnderstandingService
{
    public IReadOnlyList<OpeningUnderstandingCard> BuildCards(
        OpeningTrainerOverview overview,
        OpeningLineCatalogItem line)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(line);

        List<OpeningUnderstandingCard> cards =
        [
            BuildOpeningPlanCard(overview, line),
            BuildPieceSetupCard(overview, line),
            BuildCommonTrapCard(overview, line)
        ];

        return cards
            .GroupBy(card => card.Kind)
            .Select(group => group.OrderByDescending(card => card.Priority).First())
            .OrderByDescending(card => card.Priority)
            .ThenBy(card => card.Kind)
            .ToList();
    }

    private static OpeningUnderstandingCard BuildOpeningPlanCard(OpeningTrainerOverview overview, OpeningLineCatalogItem line)
    {
        IReadOnlyList<string> ideas = overview.WhyTheseMovesMatter
            .Select(idea => idea.ShortExplanation)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        string mainMoves = FormatMainLine(overview.MainLine, 4);
        string body = ideas.Count > 0
            ? $"{line.DisplayName} starts with {mainMoves}. Main idea: {string.Join(" ", ideas)}"
            : $"{line.DisplayName} starts with {mainMoves}. Use the overview below as the reference plan before practicing the moves.";

        return new OpeningUnderstandingCard(
            OpeningUnderstandingCardKind.OpeningPlan,
            "Opening idea",
            body,
            100);
    }

    private static OpeningUnderstandingCard BuildPieceSetupCard(OpeningTrainerOverview overview, OpeningLineCatalogItem line)
    {
        IReadOnlyList<string> developmentMoves = overview.MainLine
            .Where(move => LooksLikePieceDevelopment(move.San))
            .Select(move => move.San)
            .Take(4)
            .ToList();
        string body = developmentMoves.Count > 0
            ? $"Your early setup usually includes {string.Join(", ", developmentMoves)}. Watch how those pieces support the center before committing to side plans."
            : $"No clear piece setup is available yet for {line.DisplayName}. Start by keeping development connected to the center and use the main line as the reference.";

        return new OpeningUnderstandingCard(
            OpeningUnderstandingCardKind.PieceSetup,
            "Typical setup",
            body,
            80);
    }

    private static OpeningUnderstandingCard BuildCommonTrapCard(OpeningTrainerOverview overview, OpeningLineCatalogItem line)
    {
        TrainingPriorityItem? repair = overview.Priorities.FirstOrDefault(priority =>
            priority.Action == TrainingPriorityAction.RepairThisPosition
            || priority.ReasonCode == TrainingPriorityReasonCode.RecentMistake);
        if (repair is not null)
        {
            return new OpeningUnderstandingCard(
                OpeningUnderstandingCardKind.CommonTrap,
                "What to watch for",
                $"Main risk: {repair.Title}. {repair.Summary}",
                70);
        }

        OpeningTrainingBranch? branch = overview.CommonBranches
            .OrderByDescending(candidate => candidate.Frequency)
            .FirstOrDefault();
        string body = branch is null
            ? $"No specific risk is known for {line.DisplayName} from local data yet. Treat forcing moves carefully and use hints when the plan is unclear."
            : $"No personal risk is recorded yet. The most common reply is {branch.OpponentMove}, so make sure you know the prepared answer before memorizing deeper moves.";

        return new OpeningUnderstandingCard(
            OpeningUnderstandingCardKind.CommonTrap,
            "What to watch for",
            $"Main risk: {body}",
            60);
    }

    private static OpeningUnderstandingCard BuildTheoryExitCard(OpeningTrainerOverview overview, OpeningLineCatalogItem line)
    {
        OpeningTrainingBranch? branch = overview.CommonBranches
            .OrderByDescending(candidate => candidate.Frequency)
            .FirstOrDefault();
        string body;
        int priority;
        if (branch?.RecommendedResponse is not null)
        {
            body = $"If the opponent chooses {branch.OpponentMove}, answer with {branch.RecommendedResponse.DisplayText} and then follow the resulting plan instead of forcing the main line.";
            priority = 75;
        }
        else if (overview.Coverage.UnseenCommonBranches > 0)
        {
            body = $"{overview.Coverage.UnseenCommonBranches} common branch(es) are still unseen. Leave memorized theory when the opponent leaves the main line and switch to the plan from the position.";
            priority = 65;
        }
        else
        {
            body = $"No clear theory-exit branch is available yet for {line.DisplayName}. If the opponent leaves the line, prioritize development, king safety, and central control.";
            priority = 50;
        }

        return new OpeningUnderstandingCard(
            OpeningUnderstandingCardKind.TheoryExit,
            "When To Leave Theory",
            body,
            priority);
    }

    private static string FormatMainLine(IReadOnlyList<OpeningLineMove> mainLine, int maxMoves)
    {
        IReadOnlyList<string> moves = mainLine
            .Take(maxMoves)
            .Select(move => move.San)
            .Where(move => !string.IsNullOrWhiteSpace(move))
            .ToList();

        return moves.Count == 0 ? "the imported main line" : string.Join(" ", moves);
    }

    private static bool LooksLikePieceDevelopment(string san)
    {
        if (string.IsNullOrWhiteSpace(san))
        {
            return false;
        }

        char first = san[0];
        return first is 'N' or 'B' or 'R' or 'Q' or 'K'
            || san.Contains("O-O", StringComparison.Ordinal);
    }
}
