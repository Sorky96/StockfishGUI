namespace StockifhsGUI;

public sealed class ExplanationGenerator
{
    public MoveExplanation Generate(
        ReplayPly replay,
        MoveQualityBucket quality,
        MistakeTag? tag,
        string? bestMoveUci,
        int? centipawnLoss)
    {
        ArgumentNullException.ThrowIfNull(replay);

        string label = tag?.Label ?? "general";
        string qualityText = quality.ToString().ToLowerInvariant();
        string lossText = centipawnLoss is int cp
            ? $"and lost about {cp} centipawns"
            : "and changed the evaluation sharply";

        string bestMoveText = string.IsNullOrWhiteSpace(bestMoveUci)
            ? "A calmer alternative kept the position healthier."
            : $"A stronger option was {bestMoveUci}.";

        string patternHint = label switch
        {
            "material_loss" => "Before committing, compare the material balance after the forcing reply.",
            "hanging_piece" => "After every move, check whether your moved piece is defended at least as many times as it is attacked.",
            "king_safety" => "When your king is castled, treat pawn moves in front of it as a concession that needs concrete justification.",
            "opening_principles" => "In the opening, prefer development, king safety and central control before side pawn moves or early queen adventures.",
            "endgame_technique" => "In endgames, calculate the simplest technical line and avoid moves that hand activity back to the opponent.",
            _ => "Pause on tactical turns and look for forcing replies before choosing a natural-looking move."
        };

        string shortText = $"This {qualityText} came after {replay.San} {lossText}. It fits the pattern '{label}'. {bestMoveText}";
        return new MoveExplanation(shortText, patternHint);
    }
}
