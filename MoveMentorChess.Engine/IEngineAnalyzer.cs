namespace MoveMentorChess.Engine;

public interface IEngineAnalyzer
{
    EngineAnalysis AnalyzePosition(string fen, EngineAnalysisOptions options);
}
