namespace MoveMentorChess.Profiles;

public interface IPlayerProfileFormatter
{
    PlayerProfileFormattedOutput Format(
        PlayerProfileReport report,
        PlayerProfileAudienceLevel audienceLevel = PlayerProfileAudienceLevel.Intermediate,
        AdviceNarrationStyle trainerStyle = AdviceNarrationStyle.RegularTrainer);
}
