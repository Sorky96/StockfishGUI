namespace StockifhsGUI;

public interface IEngineAnalyzer
{
    EngineAnalysis AnalyzePosition(string fen, EngineAnalysisOptions options);
}
