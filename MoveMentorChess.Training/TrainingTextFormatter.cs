using System.Globalization;

namespace MoveMentorChess.Training;

internal static class TrainingTextFormatter
{
    public static string FormatMistakeLabel(string label)
    {
        return label switch
        {
            "hanging_piece" => "Loose pieces",
            "missed_tactic" => "Missed tactics",
            "opening_principles" => "Opening discipline",
            "king_safety" => "King safety",
            "endgame_technique" => "Endgame technique",
            "material_loss" => "Material losses",
            "piece_activity" => "Passive pieces",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase((label ?? string.Empty).Replace('_', ' ').ToLowerInvariant())
        };
    }

    public static string FormatOpening(string eco)
    {
        string description = OpeningCatalog.Describe(eco);
        return string.IsNullOrWhiteSpace(description)
            ? "Mixed openings"
            : description;
    }

    public static string FormatTrendHeadline(ProfileProgressDirection direction)
    {
        return direction switch
        {
            ProfileProgressDirection.Improving => "Improving lately",
            ProfileProgressDirection.Stable => "Mostly stable",
            ProfileProgressDirection.Regressing => "Results slipped recently",
            _ => "Need more games"
        };
    }

    public static string FormatTrainingBlockKind(TrainingBlockKind kind)
    {
        return kind switch
        {
            TrainingBlockKind.Tactics => "Tactics",
            TrainingBlockKind.OpeningReview => "Opening review",
            TrainingBlockKind.EndgameDrill => "Endgame drill",
            TrainingBlockKind.GameReview => "Game review",
            TrainingBlockKind.SlowPlayFocus => "Slow play focus",
            _ => kind.ToString()
        };
    }

    public static string FormatTrainingBlockPurpose(TrainingBlockPurpose purpose)
    {
        return purpose switch
        {
            TrainingBlockPurpose.Repair => "Repair",
            TrainingBlockPurpose.Maintain => "Maintain",
            TrainingBlockPurpose.Checklist => "Checklist",
            _ => purpose.ToString()
        };
    }

    public static string BuildAudienceDescription(PlayerProfileAudienceLevel level)
    {
        return level switch
        {
            PlayerProfileAudienceLevel.Beginner => "Beginner player; use simple chess language and short concrete actions.",
            PlayerProfileAudienceLevel.Advanced => "Advanced player; use precise chess vocabulary and compact explanations.",
            _ => "Intermediate player; balance clear explanation with useful chess detail."
        };
    }

    public static string BuildTrainerDescription(AdviceNarrationStyle trainerStyle)
    {
        return trainerStyle switch
        {
            AdviceNarrationStyle.LevyRozman => "Direct, practical chess coach voice.",
            AdviceNarrationStyle.HikaruNakamura => "Fast candidate-move focused coach voice.",
            AdviceNarrationStyle.BotezLive => "Upbeat, encouraging coach voice.",
            AdviceNarrationStyle.WittyAlien => "Playful alien-coach voice while staying accurate.",
            _ => "Regular practical chess trainer voice."
        };
    }
}
