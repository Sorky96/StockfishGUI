using MoveMentorChess.Persistence;
using MoveMentorChess.Profiles;

namespace MoveMentorChess.App.ViewModels;

internal sealed class DefaultProfilesWindowDataService(Func<IAnalysisStore?> analysisStoreProvider) : IProfilesWindowDataService
{
    private PlayerProfileService? profileService;

    public PlayerProfileService ProfileService
        => profileService ??= new PlayerProfileService(GetRequiredStore());

    public ProfileCoachSessionTracker CreateSessionTracker()
    {
        IClock clock = SystemClock.Instance;
        return new(
            new OpeningTrainingTelemetryService(analysisStoreProvider() as IOpeningTrainingTelemetryStore, clock),
            clock);
    }

    public bool TryCreateOpeningTrainerViewModel(out OpeningTrainerWindowViewModel? viewModel)
    {
        IAnalysisStore? store = analysisStoreProvider();
        if (store is null)
        {
            viewModel = null;
            return false;
        }

        viewModel = new OpeningTrainerWindowViewModel(new OpeningTrainerWorkspaceService(store));
        return true;
    }

    private IAnalysisStore GetRequiredStore()
        => analysisStoreProvider() ?? throw new InvalidOperationException("Local analysis store is unavailable.");
}
