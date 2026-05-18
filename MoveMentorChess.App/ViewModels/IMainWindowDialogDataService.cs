namespace MoveMentorChess.App.ViewModels;

internal interface IMainWindowDialogDataService
{
    ISavedLibraryDataService SavedLibrary { get; }

    bool TryCreateOpeningTrainerViewModel(out OpeningTrainerWindowViewModel? viewModel);

    bool TryCreateOpeningCoverageViewModel(out OpeningCoverageWindowViewModel? viewModel);
}
