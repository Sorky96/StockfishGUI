using MoveMentorChess.Persistence;

namespace MoveMentorChess.Diagnostics;

public static class OpeningTheoryDiagnosticsReporter
{
    public static OpeningTheoryDiagnosticsReport Build()
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is not SqliteAnalysisStore sqliteStore)
        {
            return new OpeningTheoryDiagnosticsReport(DateTime.UtcNow, 0, 0, 0, 0, 0, 0);
        }

        IReadOnlyList<OpeningLineCatalogItem> lines = sqliteStore.ListOpeningLines(limit: 5000);
        int importedLines = lines.Count;
        int withoutSide = lines.Count(line => line.RepertoireSide == RepertoireSide.Both);
        int positionsWithCandidateMoves = lines.Count(line => line.BookBranchCount > 0);
        int positionsWithoutEco = lines.Count(line => string.IsNullOrWhiteSpace(line.Eco));
        int zeroFrequencyBranches = lines.Count(line => line.BookBranchCount == 0);
        int duplicateKeys = lines
            .GroupBy(line => line.RootPositionKey.Value, StringComparer.Ordinal)
            .Count(group => group.Count() > 1);

        return new OpeningTheoryDiagnosticsReport(
            DateTime.UtcNow,
            importedLines,
            positionsWithCandidateMoves,
            positionsWithoutEco,
            zeroFrequencyBranches,
            duplicateKeys,
            withoutSide);
    }

    public static string FormatReport(OpeningTheoryDiagnosticsReport report)
    {
        return string.Join(
            Environment.NewLine,
            [
                "# Opening Theory Diagnostics",
                $"Generated: {report.GeneratedUtc:yyyy-MM-dd HH:mm} UTC",
                $"- Imported openings: {report.ImportedOpeningLines}",
                $"- Positions with candidate moves: {report.PositionsWithCandidateMoves}",
                $"- Positions without ECO: {report.PositionsWithoutEco}",
                $"- Branches with zero frequency: {report.BranchesWithZeroFrequency}",
                $"- Duplicate position keys: {report.DuplicatePositionKeys}",
                $"- Lines without explicit side: {report.LinesWithoutExplicitSide}"
            ]);
    }
}
