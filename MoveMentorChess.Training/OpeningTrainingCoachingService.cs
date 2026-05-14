namespace MoveMentorChess.Training;

public sealed class OpeningTrainingCoachingService
{
    public IReadOnlyList<TrainingCoachHint> BuildHints(OpeningTrainingPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        OpeningTrainingMoveOption? preferred = position.CandidateMoves.FirstOrDefault(option => option.IsPreferred)
            ?? position.CandidateMoves.FirstOrDefault();
        string moveText = preferred?.DisplayText
            ?? position.BetterMove
            ?? "the prepared book move";
        string plan = BuildPlanHint(position, preferred);
        string structure = BuildStructureHint(position, preferred);
        string opponentIdea = BuildOpponentIdeaHint(position);
        string fullIdea = preferred?.Idea?.ShortExplanation
            ?? position.BetterMoveReason
            ?? "Use the move that keeps you inside the prepared opening structure.";

        return
        [
            new TrainingCoachHint(
                TrainingCoachHintLevel.Light,
                "Small nudge",
                SanitizeHintText(BuildLightHint(position), position, preferred)),
            new TrainingCoachHint(
                TrainingCoachHintLevel.Plan,
                "Plan",
                SanitizeHintText(plan, position, preferred)),
            new TrainingCoachHint(
                TrainingCoachHintLevel.Structure,
                "Structure",
                SanitizeHintText(structure, position, preferred)),
            new TrainingCoachHint(
                TrainingCoachHintLevel.OpponentIdea,
                "Opponent idea",
                SanitizeHintText(opponentIdea, position, preferred)),
            new TrainingCoachHint(
                TrainingCoachHintLevel.Full,
                "Full explanation",
                $"Look for {moveText}. {fullIdea}")
        ];
    }

    public OpeningTrainingAttemptResult AddCoaching(OpeningTrainingPosition position, OpeningTrainingAttemptResult result)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Score != OpeningTrainingScore.Wrong)
        {
            TrainingMistakeCategory successCategory = result.Status == OpeningTrainingAttemptStatus.TransposedToKnownPosition
                ? TrainingMistakeCategory.Transposition
                : TrainingMistakeCategory.Unknown;
            return result with
            {
                MistakeCategory = successCategory,
                ShouldRepeatImmediately = false
            };
        }

        TrainingMistakeCategory category = DetermineMistakeCategory(position, result);
        string recovery = BuildRecoverySuggestion(position, result, category);

        return result with
        {
            RecoverySuggestion = recovery,
            NextHintLevel = TrainingCoachHintLevel.Plan,
            MistakeCategory = category,
            ShouldRepeatImmediately = true
        };
    }

    private static string BuildLightHint(OpeningTrainingPosition position)
    {
        return position.Mode switch
        {
            OpeningTrainingMode.MistakeRepair => "Pause at the old mistake and look for the move that fixes development or piece activity.",
            OpeningTrainingMode.BranchAwareness => "Name the opponent reply first, then choose the prepared answer from the book branch.",
            _ => "Find the move that keeps the main line connected before checking side ideas."
        };
    }

    private static string BuildPlanHint(OpeningTrainingPosition position, OpeningTrainingMoveOption? preferred)
    {
        OpeningMoveIdea? idea = preferred?.Idea;
        if (idea is not null)
        {
            if (idea.IdeaTags.Contains(OpeningMoveIdeaTag.DevelopPiece))
            {
                return "Improve piece activity before making extra pawn moves.";
            }

            if (idea.IdeaTags.Contains(OpeningMoveIdeaTag.ControlCenter))
            {
                return "Fight for central control and keep your main setup coordinated.";
            }

            if (idea.IdeaTags.Contains(OpeningMoveIdeaTag.KingSafety))
            {
                return "Prioritize king safety and connect your pieces before expanding.";
            }

            if (idea.IdeaTags.Contains(OpeningMoveIdeaTag.TacticalResource))
            {
                return "Check whether the position contains a forcing resource before choosing a quiet move.";
            }
        }

        if (!string.IsNullOrWhiteSpace(position.BetterMoveReason))
        {
            return position.BetterMoveReason!;
        }

        return "Choose the move that keeps your repertoire plan intact.";
    }

    private static string BuildStructureHint(OpeningTrainingPosition position, OpeningTrainingMoveOption? preferred)
    {
        OpeningMoveIdea? idea = preferred?.Idea;
        if (idea?.IdeaTags.Contains(OpeningMoveIdeaTag.ControlCenter) == true)
        {
            return "Look at the central pawn tension and the squares your pieces should influence next.";
        }

        if (idea?.IdeaTags.Contains(OpeningMoveIdeaTag.DevelopPiece) == true)
        {
            return "Find the least active undeveloped piece and improve its access to central squares.";
        }

        if (position.CoverageSummary is { WeakBranches: > 0 } coverage)
        {
            return $"This line still has {coverage.WeakBranches} weak branch(es), so keep the structure simple and book-connected.";
        }

        if (!string.IsNullOrWhiteSpace(position.ThemeLabel))
        {
            return $"Use the theme '{position.ThemeLabel}' as the structural clue.";
        }

        return "Keep the pawn structure stable and improve coordination.";
    }

    private static string BuildOpponentIdeaHint(OpeningTrainingPosition position)
    {
        OpeningTrainingBranch? branch = position.Branches?
            .OrderByDescending(item => item.Frequency)
            .ThenBy(item => item.OpponentMove, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (branch is not null)
        {
            string response = branch.RecommendedResponse is null
                ? "your setup should stay flexible enough to meet it."
                : "your prepared response should address that reply directly.";
            return $"The opponent's common reply challenges your setup; {response}";
        }

        OpponentMoveFrequency? frequency = position.OpponentReplyProfile?.Frequencies
            .OrderByDescending(item => item.Weight)
            .ThenBy(item => item.MoveSan, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (frequency is not null)
        {
            return "Expect the most common reply in this line and choose a move that keeps your next step clear.";
        }

        return position.Mode == OpeningTrainingMode.BranchAwareness
            ? "First identify what the opponent is trying to ask with this branch."
            : "Consider what reply the opponent wants before committing your move.";
    }

    private static TrainingMistakeCategory DetermineMistakeCategory(OpeningTrainingPosition position, OpeningTrainingAttemptResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ResolvedUci) && string.IsNullOrWhiteSpace(result.ResolvedSan))
        {
            return TrainingMistakeCategory.IllegalMove;
        }

        return position.Mode switch
        {
            OpeningTrainingMode.MistakeRepair => TrainingMistakeCategory.NeedsRepair,
            OpeningTrainingMode.BranchAwareness => TrainingMistakeCategory.WrongBranch,
            _ => TrainingMistakeCategory.MissedBookMove
        };
    }

    private static string BuildRecoverySuggestion(
        OpeningTrainingPosition position,
        OpeningTrainingAttemptResult result,
        TrainingMistakeCategory category)
    {
        OpeningTrainingMoveOption? preferred = result.PreferredReferences.FirstOrDefault()
            ?? position.CandidateMoves.FirstOrDefault(option => option.IsPreferred)
            ?? position.CandidateMoves.FirstOrDefault();
        string categoryText = category switch
        {
            TrainingMistakeCategory.IllegalMove => "First make sure the move is legal in the current board position.",
            TrainingMistakeCategory.WrongBranch => "You picked a move outside the tracked opponent reply set.",
            TrainingMistakeCategory.NeedsRepair => "This is the position the session wants you to repair.",
            _ => "The move missed the prepared book continuation."
        };

        return $"{categoryText} {BuildConceptualRecoveryCue(position, preferred)}";
    }

    private static string BuildConceptualRecoveryCue(
        OpeningTrainingPosition position,
        OpeningTrainingMoveOption? preferred)
    {
        OpeningMoveIdea? idea = preferred?.Idea;
        if (idea?.IdeaTags.Contains(OpeningMoveIdeaTag.ControlCenter) == true)
        {
            return "Think about the move that supports central control.";
        }

        if (idea?.IdeaTags.Contains(OpeningMoveIdeaTag.DevelopPiece) == true)
        {
            return "Look for the move that improves piece activity without losing the thread of the line.";
        }

        if (idea?.IdeaTags.Contains(OpeningMoveIdeaTag.KingSafety) == true)
        {
            return "Look for the move that keeps king safety and coordination first.";
        }

        if (idea?.IdeaTags.Contains(OpeningMoveIdeaTag.TacticalResource) == true)
        {
            return "Check for the forcing resource before choosing a quiet-looking move.";
        }

        return position.Mode switch
        {
            OpeningTrainingMode.BranchAwareness => "Name the opponent's idea first, then choose the prepared response.",
            OpeningTrainingMode.MistakeRepair => "Recall what the old mistake changed, then find the move that repairs it.",
            _ => "Try again from the plan, not from the move notation."
        };
    }

    private static string SanitizeHintText(
        string text,
        OpeningTrainingPosition position,
        OpeningTrainingMoveOption? preferred)
    {
        string sanitized = text;
        foreach (string? token in BuildMoveRevealTokens(position, preferred))
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            sanitized = sanitized.Replace(token, "the prepared move", StringComparison.OrdinalIgnoreCase);
        }

        return sanitized;
    }

    private static IEnumerable<string?> BuildMoveRevealTokens(
        OpeningTrainingPosition position,
        OpeningTrainingMoveOption? preferred)
    {
        yield return preferred?.DisplayText;
        yield return preferred?.Uci;
        yield return preferred?.Idea?.Move;
        yield return position.BetterMove;
        foreach (OpeningTrainingMoveOption option in position.CandidateMoves)
        {
            if (option.IsPreferred)
            {
                yield return option.DisplayText;
                yield return option.Uci;
                yield return option.Idea?.Move;
            }
        }
    }
}
