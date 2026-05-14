namespace MoveMentorChess.Training;

public sealed class OpeningTrainingNextActionService
{
    public IReadOnlyList<TrainingNextAction> BuildNextActions(TrainingSessionOutcomeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        List<TrainingNextAction> actions = [];
        if (summary.WrongCount > 0)
        {
            bool heavyRepair = summary.WrongCount >= 3
                || (summary.CompletedCount > 0 && (double)summary.WrongCount / summary.CompletedCount >= 0.35);
            actions.Add(new TrainingNextAction(
                "repeat-now",
                TrainingNextActionKind.RepeatNow,
                heavyRepair ? "Repeat a smaller repair pass" : "Repeat this line now",
                BuildRepairRepeatReason(summary, heavyRepair),
                "Repeat now",
                100));
            actions.Add(new TrainingNextAction(
                "repair-weak-branches",
                TrainingNextActionKind.RepairWeakBranches,
                "Train weak branches",
                "Repair concrete deviations or opponent replies from this opening.",
                "Open priorities",
                90));
            actions.Add(CreatePracticeMainLineOnly(80));
            actions.Add(CreateReviewWithHintsAllowed(70));
            actions.Add(CreateStopForNow(50));
        }
        else if (summary.PlayableCount > 0 || summary.HintCount > 0)
        {
            bool mainLineDrift = summary.PlayableCount > 0 && summary.CorrectCount < summary.CompletedCount;
            if (mainLineDrift && summary.HintCount == 0)
            {
                actions.Add(CreatePracticeMainLineOnly(95));
                actions.Add(new TrainingNextAction(
                    "repeat-after-break",
                    TrainingNextActionKind.RepeatAfterBreak,
                    "Repeat after 10 min",
                    "Best for making this line automatic after useful alternatives appeared.",
                    "Repeat after 10 min",
                    90,
                    10));
            }
            else
            {
                actions.Add(new TrainingNextAction(
                    "repeat-after-break",
                    TrainingNextActionKind.RepeatAfterBreak,
                    "Repeat after 10 min",
                    "Best for making this line automatic after hints or useful alternatives appeared.",
                    "Repeat after 10 min",
                    95,
                    10));
                actions.Add(CreatePracticeMainLineOnly(85));
            }
            actions.Add(CreateReviewWithHintsAllowed(75));
            actions.Add(new TrainingNextAction(
                "train-another-opening",
                TrainingNextActionKind.BrowseAnotherOpening,
                "Train another recommended opening",
                "Let the system choose the next useful opening from your backlog.",
                "Train another opening",
                65));
            actions.Add(new TrainingNextAction(
                "browse-another-opening",
                TrainingNextActionKind.BrowseAnotherOpening,
                "Browse openings",
                "Choose a line yourself when you want exploration instead of the recommended next step.",
                "Browse openings",
                55));
            actions.Add(CreateStopForNow(50));
        }
        else
        {
            actions.Add(new TrainingNextAction(
                "train-another-opening",
                TrainingNextActionKind.BrowseAnotherOpening,
                "Train another recommended opening",
                "Clean session. Let the system choose the next useful opening from your backlog.",
                "Train another opening",
                90));
            actions.Add(CreateStopForNow(85));
            actions.Add(new TrainingNextAction(
                "return-tomorrow",
                TrainingNextActionKind.ReturnTomorrow,
                "Return tomorrow",
                "Let spacing do its work and revisit this line tomorrow.",
                "Back to selection",
                75,
                1440));
            actions.Add(new TrainingNextAction(
                "browse-another-opening",
                TrainingNextActionKind.BrowseAnotherOpening,
                "Browse openings",
                "Choose a different line manually.",
                "Browse openings",
                60));
        }

        return actions
            .OrderByDescending(action => action.Priority)
            .ThenBy(action => action.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildRepairRepeatReason(TrainingSessionOutcomeSummary summary, bool heavyRepair)
    {
        if (heavyRepair)
        {
            return "Useful diagnostic session. Repeat fewer positions and focus on the first repair target.";
        }

        return summary.WrongCount == 1
            ? "Good session overall. One quick repeat will lock in the weak move while it is fresh."
            : "Good session overall. A quick repeat will lock in the weak moves while they are fresh.";
    }

    private static TrainingNextAction CreatePracticeMainLineOnly(int priority)
        => new(
            "practice-main-line-only",
            TrainingNextActionKind.PracticeMainLineOnly,
            "Practice main line only",
            "Use this when you want to quickly lock in the exact repertoire move order.",
            "Practice main line only",
            priority);

    private static TrainingNextAction CreateReviewWithHintsAllowed(int priority)
        => new(
            "review-with-hints",
            TrainingNextActionKind.ReviewWithHintsAllowed,
            "Review with hints allowed",
            "A lower-pressure repeat for cautious review without pure memory pressure.",
            "Review with hints",
            priority);

    private static TrainingNextAction CreateStopForNow(int priority)
        => new(
            "stop-for-now",
            TrainingNextActionKind.StopForNow,
            "Stop for now",
            "It is OK to finish here. Spacing and rest are part of learning.",
            "Stop for now",
            priority);

    public IReadOnlyList<OpeningTrainingScheduledAction> BuildScheduledActions(
        string playerKey,
        OpeningTrainingSessionResult sessionResult,
        IReadOnlyList<TrainingNextAction> nextActions,
        DateTime createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerKey);
        ArgumentNullException.ThrowIfNull(sessionResult);
        ArgumentNullException.ThrowIfNull(nextActions);

        string normalizedPlayerKey = playerKey.Trim().ToLowerInvariant();
        DateTime created = createdUtc.ToUniversalTime();
        OpeningTrainingRecordedAttempt? focusAttempt = sessionResult.Attempts
            .OrderByDescending(attempt => attempt.Score == OpeningTrainingScore.Wrong)
            .ThenByDescending(attempt => attempt.RecordedUtc)
            .FirstOrDefault();
        OpeningLineKey? lineKey = focusAttempt?.OpeningLineKey
            ?? sessionResult.ReviewItems?.Select(item => item.OpeningLineKey).FirstOrDefault(key => key.HasValue);
        OpeningBranchKey? branchKey = focusAttempt?.BranchKey
            ?? sessionResult.ReviewItems?.Select(item => (OpeningBranchKey?)item.BranchKey).FirstOrDefault(key => key.HasValue);
        OpeningPositionKey? positionKey = focusAttempt?.PositionKey
            ?? sessionResult.ReviewItems?.Select(item => (OpeningPositionKey?)item.PositionKey).FirstOrDefault(key => key.HasValue);

        return nextActions
            .Where(action => action.DelayMinutes > 0)
            .Select(action => new OpeningTrainingScheduledAction(
                BuildScheduledActionId(sessionResult.SessionId, action.Id),
                normalizedPlayerKey,
                sessionResult.SessionId,
                action.Kind,
                lineKey,
                branchKey,
                positionKey,
                created,
                created.AddMinutes(Math.Max(0, action.DelayMinutes)),
                OpeningTrainingScheduledActionStatus.Pending,
                null,
                action.Priority,
                action.Id))
            .ToList();
    }

    public static string BuildScheduledActionId(string sessionId, string sourceActionId)
    {
        return $"{sessionId}:{sourceActionId}";
    }
}
