using System.Linq;
using System.Text;

namespace MoveMentorChess.App.ViewModels;

public sealed class AnalysisMistakeItemViewModel
{
    public AnalysisMistakeItemViewModel(SelectedMistake mistake)
    {
        Mistake = mistake;
        LeadMove = mistake.Moves
            .OrderByDescending(move => move.Quality)
            .ThenByDescending(move => move.CentipawnLoss ?? 0)
            .First();

        string firstMove = $"{mistake.Moves.First().Replay.MoveNumber}{(mistake.Moves.First().Replay.Side == PlayerSide.White ? "." : "...")} {mistake.Moves.First().Replay.San}";
        string lastMove = $"{mistake.Moves.Last().Replay.MoveNumber}{(mistake.Moves.Last().Replay.Side == PlayerSide.White ? "." : "...")} {mistake.Moves.Last().Replay.San}";
        string moveRange = mistake.Moves.Count == 1 ? firstMove : $"{firstMove} -> {lastMove}";
        string label = mistake.Tag?.Label ?? "unclassified";
        string cpl = LeadMove.CentipawnLoss?.ToString() ?? "n/a";
        DisplayText = $"{moveRange} | {mistake.Quality} | {label} | CPL {cpl}";
        Details = BuildDetailsText(mistake, LeadMove);
    }

    public SelectedMistake Mistake { get; }

    public MoveAnalysisResult LeadMove { get; }

    public string DisplayText { get; }

    public string Details { get; }

    private static string BuildDetailsText(SelectedMistake mistake, MoveAnalysisResult lead)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Moves: {string.Join(", ", mistake.Moves.Select(m => $"{m.Replay.MoveNumber}{(m.Replay.Side == PlayerSide.White ? "." : "...")} {m.Replay.San}"))}");
        builder.AppendLine($"Quality: {mistake.Quality}");
        builder.AppendLine($"Label: {mistake.Tag?.Label ?? "unclassified"}");
        builder.AppendLine($"Confidence: {(mistake.Tag?.Confidence ?? 0):0.00}");
        builder.AppendLine($"Played move: {lead.Replay.San} ({lead.Replay.Uci})");
        builder.AppendLine($"Best move: {lead.BeforeAnalysis.BestMoveUci ?? "n/a"}");
        builder.AppendLine($"Eval before: {FormatScore(lead.EvalBeforeCp, lead.BestMateIn)}");
        builder.AppendLine($"Eval after: {FormatScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
        builder.AppendLine($"Centipawn loss: {lead.CentipawnLoss?.ToString() ?? "n/a"}");
        builder.AppendLine($"Material delta: {lead.MaterialDeltaCp}");
        builder.AppendLine();
        builder.AppendLine("Advice:");
        builder.AppendLine(lead.Explanation?.ShortText ?? mistake.Explanation.ShortText);

        if (!string.IsNullOrWhiteSpace(lead.Explanation?.DetailedText))
        {
            builder.AppendLine();
            builder.AppendLine("Detailed explanation:");
            builder.AppendLine(lead.Explanation!.DetailedText);
        }

        builder.AppendLine();
        builder.AppendLine("Training hint:");
        builder.AppendLine(lead.Explanation?.TrainingHint ?? mistake.Explanation.TrainingHint);

        if (mistake.Tag?.Evidence.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Evidence:");
            foreach (string evidence in mistake.Tag.Evidence)
            {
                builder.AppendLine($"- {evidence}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatScore(int? centipawns, int? mateIn)
    {
        if (mateIn is int mate)
        {
            return $"mate {mate}";
        }

        return centipawns is int cp ? $"{cp} cp" : "n/a";
    }
}
