using MoveMentorChess.App.ViewModels;
using MoveMentorChess.App.Views;

namespace MoveMentorChess.App.Composition;

public sealed class AnalysisWindowFactory : IAnalysisWindowFactory
{
    private readonly IAnalysisWindowDataService dataService;

    public AnalysisWindowFactory()
        : this(new DefaultAnalysisWindowDataService())
    {
    }

    internal AnalysisWindowFactory(IAnalysisWindowDataService dataService)
    {
        this.dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public AnalysisWindow Create(AnalysisWindowRequest request)
    {
        return new AnalysisWindow(
            request.ImportedGame,
            request.EngineAnalyzer,
            request.NavigateToMoveAsync,
            request.AnalysisProgress,
            request.InitialSide,
            request.InitialResultsBySide,
            dataService);
    }
}
