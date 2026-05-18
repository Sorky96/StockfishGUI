using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

internal sealed class DefaultMainWindowDialogDataService : IMainWindowDialogDataService
{
    private readonly Func<IAnalysisStore?> analysisStoreProvider;

    public DefaultMainWindowDialogDataService(Func<IAnalysisStore?> analysisStoreProvider)
    {
        this.analysisStoreProvider = analysisStoreProvider ?? throw new ArgumentNullException(nameof(analysisStoreProvider));
        SavedLibrary = new DefaultSavedLibraryDataService(analysisStoreProvider);
    }

    public ISavedLibraryDataService SavedLibrary { get; }

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

    public bool TryCreateOpeningCoverageViewModel(out OpeningCoverageWindowViewModel? viewModel)
    {
        IAnalysisStore? store = analysisStoreProvider();
        if (store is null)
        {
            viewModel = null;
            return false;
        }

        viewModel = new OpeningCoverageWindowViewModel(new OpeningTrainerWorkspaceService(store));
        return true;
    }
}
