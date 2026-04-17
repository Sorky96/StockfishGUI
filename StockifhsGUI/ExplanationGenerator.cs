namespace StockifhsGUI;

public sealed class ExplanationGenerator
{
    public MoveExplanation Generate(
        ReplayPly replay,
        MoveQualityBucket quality,
        MistakeTag? tag,
        string? bestMoveUci,
        int? centipawnLoss,
        ExplanationLevel level = ExplanationLevel.Intermediate)
    {
        ArgumentNullException.ThrowIfNull(replay);

        string label = tag?.Label ?? "general";
        string qualityText = quality.ToString().ToLowerInvariant();
        string lossText = centipawnLoss is int cp
            ? $"and lost about {cp} centipawns"
            : "and changed the evaluation sharply";
        string bestMoveText = FormatMoveFromFen(replay.FenBefore, bestMoveUci);
        string bestMoveSentence = string.IsNullOrWhiteSpace(bestMoveText)
            ? "A calmer alternative kept the position healthier."
            : $"A stronger option was {bestMoveText}.";

        string patternHint = label switch
        {
            "material_loss" => "Before committing, compare the material balance after the forcing reply.",
            "hanging_piece" => "After every move, check whether your moved piece is defended at least as many times as it is attacked.",
            "king_safety" => "When your king is castled, treat pawn moves in front of it as a concession that needs concrete justification.",
            "opening_principles" => "In the opening, prefer development, king safety and central control before side pawn moves or early queen adventures.",
            "piece_activity" => "In quieter middlegames, favor squares that increase your piece activity instead of moves that leave a piece sidelined or passive.",
            "endgame_technique" => "In endgames, calculate the simplest technical line and avoid moves that hand activity back to the opponent.",
            _ => "Pause on tactical turns and look for forcing replies before choosing a natural-looking move."
        };

        string shortText = BuildShortText(replay, qualityText, lossText, label, bestMoveSentence, level);
        string detailedText = BuildDetailedText(replay, qualityText, label, bestMoveSentence, centipawnLoss, level);
        string trainingHint = BuildTrainingHint(patternHint, label, level);

        return new MoveExplanation(shortText, trainingHint, detailedText);
    }

    private static string BuildShortText(
        ReplayPly replay,
        string qualityText,
        string lossText,
        string label,
        string bestMoveSentence,
        ExplanationLevel level)
    {
        return level switch
        {
            ExplanationLevel.Beginner => $"After {replay.San}, the position got worse {lossText}. This was mainly an example of '{label}'. {bestMoveSentence}",
            ExplanationLevel.Advanced => $"{replay.San} was a {qualityText} in the '{label}' family {lossText}. {bestMoveSentence}",
            _ => $"This {qualityText} came after {replay.San} {lossText}. It fits the pattern '{label}'. {bestMoveSentence}"
        };
    }

    private static string BuildDetailedText(
        ReplayPly replay,
        string qualityText,
        string label,
        string bestMoveSentence,
        int? centipawnLoss,
        ExplanationLevel level)
    {
        return level switch
        {
            ExplanationLevel.Beginner =>
                $"{BuildProblemSentenceBeginner(replay, qualityText, centipawnLoss)} " +
                $"{BuildWhySentenceBeginner(label)} " +
                $"{bestMoveSentence} " +
                $"{BuildRecognitionSentenceBeginner(label)}",
            ExplanationLevel.Advanced =>
                $"{BuildProblemSentenceAdvanced(replay, qualityText, centipawnLoss)} " +
                $"{BuildWhySentenceAdvanced(label)} " +
                $"{bestMoveSentence} " +
                $"{BuildRecognitionSentenceAdvanced(label)}",
            _ =>
                $"{BuildProblemSentence(replay, qualityText, centipawnLoss)} " +
                $"{BuildWhySentence(label)} " +
                $"{bestMoveSentence} " +
                $"{BuildRecognitionSentence(label)}"
        };
    }

    private static string BuildTrainingHint(string baseHint, string label, ExplanationLevel level)
    {
        return level switch
        {
            ExplanationLevel.Beginner => $"Focus on one simple habit: {baseHint}",
            ExplanationLevel.Advanced => label switch
            {
                "material_loss" => "Train this by checking forcing continuations until the resulting material balance is completely clear.",
                "hanging_piece" => "Train board scanning with attacker-defender counts before and after every candidate move.",
                "missed_tactic" => "Train this with short tactical calculation drills that force you to enumerate checks, captures and threats first.",
                "king_safety" => "Review positions where a single pawn move changed diagonal or file access toward the king.",
                "opening_principles" => "Review your first 10 moves and justify each one in terms of development, king safety and central influence.",
                "piece_activity" => "Compare candidate moves by mobility gain, square quality and coordination rather than by surface-level safety alone.",
                "endgame_technique" => "Drill conversion and defensive endgames with emphasis on king activity and zugzwang awareness.",
                _ => baseHint
            },
            _ => baseHint
        };
    }

    private static string BuildProblemSentence(ReplayPly replay, string qualityText, int? centipawnLoss)
    {
        return centipawnLoss is int cp
            ? $"The move {replay.San} was a {qualityText} because it worsened the position by roughly {cp} centipawns."
            : $"The move {replay.San} was a {qualityText} because the position became significantly less healthy afterwards.";
    }

    private static string BuildProblemSentenceBeginner(ReplayPly replay, string qualityText, int? centipawnLoss)
    {
        return centipawnLoss is int cp
            ? $"{replay.San} was a {qualityText} because it gave away roughly {cp} centipawns of value."
            : $"{replay.San} was a {qualityText} because it made your position much harder to handle.";
    }

    private static string BuildProblemSentenceAdvanced(ReplayPly replay, string qualityText, int? centipawnLoss)
    {
        return centipawnLoss is int cp
            ? $"{replay.San} qualifies as a {qualityText} because the engine swing is about {cp} centipawns and the position loses practical stability."
            : $"{replay.San} qualifies as a {qualityText} because the evaluation shifts sharply even without a clean centipawn comparison.";
    }

    private static string BuildWhySentence(string label)
    {
        return label switch
        {
            "material_loss" => "The main issue was concrete material damage or a line that allowed the opponent to win material cleanly.",
            "hanging_piece" => "The move left a piece insufficiently defended, so the opponent could challenge it immediately.",
            "missed_tactic" => "The move missed a forcing tactical resource, either for you or for your opponent in reply.",
            "king_safety" => "The move loosened king safety and gave the opponent easier attacking targets or entry lines.",
            "opening_principles" => "The move ignored core opening priorities such as development, king safety or central control.",
            "piece_activity" => "The move made one of your pieces less active and reduced your ability to fight for key squares.",
            "endgame_technique" => "The move missed a cleaner technical plan and gave away useful activity in the endgame.",
            _ => "The move created a practical problem that the opponent could exploit with more active play."
        };
    }

    private static string BuildWhySentenceBeginner(string label)
    {
        return label switch
        {
            "material_loss" => "The biggest problem is that you let the opponent win material.",
            "hanging_piece" => "The moved piece did not have enough protection after the move.",
            "missed_tactic" => "There was a forcing tactical idea in the position that was missed.",
            "king_safety" => "The move made your king easier to attack.",
            "opening_principles" => "The move spent time on something less important than development or king safety.",
            "piece_activity" => "The move placed your piece on a less useful square.",
            "endgame_technique" => "The move missed a simpler endgame plan.",
            _ => "The move allowed the opponent to improve too easily."
        };
    }

    private static string BuildWhySentenceAdvanced(string label)
    {
        return label switch
        {
            "material_loss" => "The critical defect is a concrete forcing line in which your position fails the material balance test.",
            "hanging_piece" => "The move leaves the relocated piece tactically underprotected relative to the available attacking resources.",
            "missed_tactic" => "The position contains a forcing tactical resource that should dominate candidate-move selection.",
            "king_safety" => "The move concedes king shelter, opening tactical access on files, diagonals or weakened colour complexes.",
            "opening_principles" => "The move loses opening efficiency by neglecting development tempo, central influence or timely king safety.",
            "piece_activity" => "The move lowers activity and coordination, so your piece contributes less to critical squares and plans.",
            "endgame_technique" => "The move yields technical control, often by underestimating king activity or the simplest conversion route.",
            _ => "The move creates an exploitable positional or tactical concession that improves the opponent's options."
        };
    }

    private static string BuildRecognitionSentence(string label)
    {
        return label switch
        {
            "material_loss" => "Before you release the move, scan the forcing captures and ask what material remains after the sequence ends.",
            "hanging_piece" => "A good trigger is to count attackers and defenders on the destination square before moving on.",
            "missed_tactic" => "In sharp positions, pause for checks, captures and threats before trusting the first natural move.",
            "king_safety" => "Whenever your king shelter changes, double-check opened files, weakened diagonals and loose dark or light squares.",
            "opening_principles" => "In the opening, ask whether the move develops a piece, improves king safety or meaningfully fights for the center.",
            "piece_activity" => "When no tactic is forcing the game, compare whether your move improves or reduces the mobility of the moved piece.",
            "endgame_technique" => "In endgames, prefer the move that improves king activity or fixes the opponent's weaknesses with the least counterplay.",
            _ => "Use the next similar position as a cue to slow down and verify the opponent's strongest forcing reply."
        };
    }

    private static string BuildRecognitionSentenceBeginner(string label)
    {
        return label switch
        {
            "material_loss" => "A simple habit is to ask: after the obvious exchanges, who is up material?",
            "hanging_piece" => "Before moving on, count how many times the destination square is attacked and defended.",
            "missed_tactic" => "Look for checks, captures and threats before trusting a natural move.",
            "king_safety" => "Any move near your king should make you ask what attacking lines just opened.",
            "opening_principles" => "In the opening, first try to develop pieces and get your king safe.",
            "piece_activity" => "Ask whether the piece will be more active or less active after the move.",
            "endgame_technique" => "In the endgame, look for the move that activates your king or improves your simplest plan.",
            _ => "Use similar moments to slow down and check the opponent's best reply."
        };
    }

    private static string BuildRecognitionSentenceAdvanced(string label)
    {
        return label switch
        {
            "material_loss" => "Treat the position as a forcing-tree problem and verify whether the terminal material count still works for you.",
            "hanging_piece" => "Use attacker-defender accounting plus tactical motifs like overload or zwischenzug before accepting the destination square.",
            "missed_tactic" => "Candidate selection should begin with forcing resources, not with the most natural-looking improving move.",
            "king_safety" => "Re-evaluate access routes to the king immediately after any shelter concession, especially files, diagonals and dark/light square complexes.",
            "opening_principles" => "Measure opening moves by tempo efficiency, development quality and whether they support a coherent central setup.",
            "piece_activity" => "Compare candidate moves through activity metrics: mobility, square quality, coordination and restriction of counterplay.",
            "endgame_technique" => "In technical endgames, prioritize king activity and the line with the lowest counterplay, even if several moves keep the edge.",
            _ => "Use the position as a cue to run a stricter candidate-move and forcing-line verification pass."
        };
    }

    private static string FormatMoveFromFen(string fenBefore, string? uciMove)
    {
        if (string.IsNullOrWhiteSpace(uciMove))
        {
            return string.Empty;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return uciMove;
        }

        return FormatSanAndUci(appliedMove.San, appliedMove.Uci);
    }

    private static string FormatSanAndUci(string san, string uci)
    {
        return string.Equals(san, uci, StringComparison.OrdinalIgnoreCase)
            ? san
            : $"{san} ({uci})";
    }
}
