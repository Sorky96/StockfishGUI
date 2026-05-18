using MoveMentorChess.Profiles;

namespace MoveMentorChess.App.ViewModels;

internal interface IProfilesWindowDataService
{
    PlayerProfileService ProfileService { get; }

    ProfileCoachSessionTracker CreateSessionTracker();

    bool TryCreateOpeningTrainerViewModel(out OpeningTrainerWindowViewModel? viewModel);
}
