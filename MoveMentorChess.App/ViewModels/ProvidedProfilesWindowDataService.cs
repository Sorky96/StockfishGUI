using MoveMentorChess.Profiles;

namespace MoveMentorChess.App.ViewModels;

internal sealed class ProvidedProfilesWindowDataService(PlayerProfileService profileService) : IProfilesWindowDataService
{
    public PlayerProfileService ProfileService { get; } = profileService ?? throw new ArgumentNullException(nameof(profileService));

    public ProfileCoachSessionTracker CreateSessionTracker()
        => new(new OpeningTrainingTelemetryService());

    public bool TryCreateOpeningTrainerViewModel(out OpeningTrainerWindowViewModel? viewModel)
    {
        viewModel = null;
        return false;
    }
}
