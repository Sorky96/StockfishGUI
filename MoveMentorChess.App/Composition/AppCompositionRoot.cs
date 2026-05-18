using MoveMentorChess.App.ViewModels;
using MoveMentorChess.App.Views;
using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.Composition;

internal static class AppCompositionRoot
{
    public static MainWindow CreateMainWindow()
    {
        IAnalysisWindowFactory analysisWindowFactory = new AnalysisWindowFactory(AnalysisStoreProvider.GetStore);
        IProfilesWindowFactory profilesWindowFactory = new ProfilesWindowFactory(AnalysisStoreProvider.GetStore);
        IStockfishPathResolver stockfishPathResolver = new DefaultStockfishPathResolver();
        IMainWindowAnalysisDataService mainWindowAnalysisDataService = new DefaultMainWindowAnalysisDataService(AnalysisStoreProvider.GetStore);

        return new MainWindow(analysisWindowFactory, profilesWindowFactory, AnalysisStoreProvider.GetStore)
        {
            DataContext = new MainWindowViewModel(
                stockfishPathResolver,
                mainWindowAnalysisDataService)
        };
    }
}
