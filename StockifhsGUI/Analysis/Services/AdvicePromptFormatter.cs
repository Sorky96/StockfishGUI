using System.Text;

namespace StockifhsGUI;

public static class AdvicePromptFormatter
{
    public static string BuildPrompt(LocalModelAdviceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        StringBuilder builder = new();

        // System instruction — role and tone.
        builder.AppendLine(BuildSystemBlock(request.ExplanationLevel));
        builder.AppendLine();

        // Position context.
        builder.AppendLine("Position:");
        builder.AppendLine($"  FEN: {request.Replay.FenBefore}");
        builder.AppendLine($"  Move number: {request.Replay.MoveNumber}");
        builder.AppendLine($"  Side to move: {request.Replay.Side}");
        builder.AppendLine($"  Phase: {request.Replay.Phase}");
        builder.AppendLine();

        // What happened.
        builder.AppendLine("Played move:");
        builder.AppendLine($"  SAN: {request.Replay.San}");
        builder.AppendLine($"  UCI: {request.Replay.Uci}");

        if (request.Replay.IsCapture)
        {
            builder.AppendLine("  (capture)");
        }

        builder.AppendLine();

        // Engine verdict.
        builder.AppendLine("Engine verdict:");
        builder.AppendLine($"  Quality: {request.Quality}");
        builder.AppendLine($"  Pattern: {request.Tag?.Label ?? "general"}");
        builder.AppendLine($"  Centipawn loss: {(request.CentipawnLoss?.ToString() ?? "unknown")}");

        string bestMove = request.Context?.PromptContext?.BestMoveSan
            ?? request.BestMoveUci
            ?? "unknown";
        builder.AppendLine($"  Best move: {bestMove}");
        builder.AppendLine();

        // Game context (optional fields).
        bool hasGameContext = false;

        if (!string.IsNullOrWhiteSpace(request.Context?.PromptContext?.OpeningName))
        {
            if (!hasGameContext)
            {
                builder.AppendLine("Game context:");
                hasGameContext = true;
            }

            builder.AppendLine($"  Opening: {request.Context!.PromptContext!.OpeningName}");
        }

        if (!string.IsNullOrWhiteSpace(request.Context?.PromptContext?.AnalyzedPlayer))
        {
            if (!hasGameContext)
            {
                builder.AppendLine("Game context:");
                hasGameContext = true;
            }

            builder.AppendLine($"  Player: {request.Context!.PromptContext!.AnalyzedPlayer}");
        }

        if (request.Context?.PromptContext?.Evidence is { Count: > 0 } evidence)
        {
            if (!hasGameContext)
            {
                builder.AppendLine("Game context:");
                hasGameContext = true;
            }

            foreach (string item in evidence)
            {
                builder.AppendLine($"  - {item}");
            }
        }

        if (request.Context?.PromptContext?.HeuristicNotes is { Count: > 0 } notes)
        {
            if (!hasGameContext)
            {
                builder.AppendLine("Game context:");
                hasGameContext = true;
            }

            foreach (string item in notes)
            {
                builder.AppendLine($"  - {item}");
            }
        }

        if (hasGameContext)
        {
            builder.AppendLine();
        }

        // Player history (personalization from past analyses).
        if (request.Context?.PromptContext?.PlayerProfile is { } profile)
        {
            builder.AppendLine("Player history (from past analyses):");
            builder.AppendLine($"  Games analyzed: {profile.GamesAnalyzed}");

            if (profile.AverageCentipawnLoss.HasValue)
            {
                builder.AppendLine($"  Average centipawn loss: {profile.AverageCentipawnLoss.Value}");
            }

            if (profile.WeakestPhase.HasValue)
            {
                builder.AppendLine($"  Weakest phase: {profile.WeakestPhase.Value}");
            }

            if (profile.TopPatterns.Count > 0)
            {
                builder.AppendLine("  Recurring mistakes:");
                foreach (PlayerMistakePatternEntry pattern in profile.TopPatterns)
                {
                    builder.AppendLine($"    - {pattern.Label.Replace('_', ' ')}: {pattern.Count} times");
                }
            }

            builder.AppendLine("  → If this mistake matches a recurring pattern, mention it briefly in detailed_text.");
            builder.AppendLine();
        }

        // Output format instruction + one-shot example.
        builder.AppendLine("Reply with ONLY a JSON object. No markdown, no explanation outside the JSON.");
        builder.AppendLine("Keys: short_text, detailed_text, training_hint.");
        builder.AppendLine(BuildLengthInstruction(request.ExplanationLevel));
        builder.AppendLine("For detailed_text, use exactly four short parts in this order: What:, Why:, Better:, Watch next time:.");
        builder.AppendLine();
        builder.AppendLine("IMPORTANT: You must write about THIS specific position. Generate NEW text.");
        builder.AppendLine($"Refer to the actual move ({request.Replay.San}), the actual best move ({request.Context?.PromptContext?.BestMoveSan ?? request.BestMoveUci ?? "unknown"}), and the actual pattern ({request.Tag?.Label ?? "general"}).");
        builder.AppendLine();
        builder.AppendLine("Example format (DO NOT copy these values, write your own analysis):");
        builder.AppendLine("""
            {
              "short_text": "Write a brief summary of the mistake here.",
              "detailed_text": "What: Briefly state what went wrong. Why: Briefly explain the core tactical or positional reason. Better: Name the better move and why it helped. Watch next time: Give one pattern to notice earlier.",
              "training_hint": "Provide a short, actionable rule for the player to remember."
            }
            """);
        return builder.ToString().Trim();
    }

    private static string BuildSystemBlock(ExplanationLevel level)
    {
        return level switch
        {
            ExplanationLevel.Beginner =>
                """
                You are a friendly chess coach for a beginner student.
                Use simple language. Avoid jargon. Explain the idea behind the mistake in one or two plain sentences.
                The training hint should be a single easy habit the student can practice.
                """,
            ExplanationLevel.Advanced =>
                """
                You are a precise chess coach for an experienced player.
                Use technical language: name tactical motifs, structural themes, and concrete lines.
                Reference the position specifics: squares, pieces, pawn structure.
                The training hint should be an analytical drill or thinking process.
                """,
            _ =>
                """
                You are a practical chess coach for an intermediate player.
                Be concrete: name pieces, squares and the key idea behind the mistake.
                Keep the tone instructive but not overly technical.
                The training hint should be a specific check or habit to prevent this mistake.
                """
        };
    }

    private static string BuildLengthInstruction(ExplanationLevel level)
    {
        return level switch
        {
            ExplanationLevel.Beginner =>
                "short_text: 1-2 sentences, max 30 words. detailed_text: 2-3 sentences, max 50 words. training_hint: 1 sentence, max 20 words.",
            ExplanationLevel.Advanced =>
                "short_text: 1-2 sentences, max 40 words. detailed_text: 3-4 sentences, max 80 words. training_hint: 1-2 sentences, max 30 words.",
            _ =>
                "short_text: 1-2 sentences, max 35 words. detailed_text: 2-3 sentences, max 60 words. training_hint: 1 sentence, max 25 words."
        };
    }


}
