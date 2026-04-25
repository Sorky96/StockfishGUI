using System;
using System.Collections.Generic;

namespace MoveMentorChessServices;

internal sealed class ImportedGameSession
{
    private readonly List<ImportedMoveListItem> moves = new();
    private readonly List<ReplayPly> replay = new();

    public ImportedGame? Game { get; private set; }

    public IReadOnlyList<ImportedMoveListItem> Moves => moves;

    public IReadOnlyList<ReplayPly> Replay => replay;

    public int Cursor { get; set; }

    public bool HasImportedGame => Game is not null;

    public int HighlightIndex => Cursor == 0 ? -1 : Cursor - 1;

    public void Clear()
    {
        Game = null;
        moves.Clear();
        replay.Clear();
        Cursor = 0;
    }

    public void LoadImportedGame(ImportedGame game, IReadOnlyList<ReplayPly> replayPlies)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(replayPlies);

        Clear();
        Game = game;
        replay.AddRange(replayPlies);

        for (int i = 0; i < replayPlies.Count; i++)
        {
            ReplayPly replayPly = replayPlies[i];
            moves.Add(new ImportedMoveListItem(i + 1, replayPly.MoveNumber, replayPly.Side, replayPly.San));
        }
    }

    public void LoadTrackedMoves(IReadOnlyList<string> trackedMoves)
    {
        ArgumentNullException.ThrowIfNull(trackedMoves);

        Clear();
        Cursor = trackedMoves.Count;

        for (int i = 0; i < trackedMoves.Count; i++)
        {
            int ply = i + 1;
            PlayerSide side = ply % 2 == 1 ? PlayerSide.White : PlayerSide.Black;
            int moveNumber = (ply + 1) / 2;
            moves.Add(new ImportedMoveListItem(ply, moveNumber, side, trackedMoves[i]));
        }
    }

    public string BuildSummaryText(Func<ImportedGame, PlayerSide?> getSavedAnalysisSide)
    {
        ArgumentNullException.ThrowIfNull(getSavedAnalysisSide);

        if (moves.Count == 0 || Game is null)
        {
            return "Imported moves: none";
        }

        string players = $"{Game.WhitePlayer ?? "White"} vs {Game.BlackPlayer ?? "Black"}";
        string result = string.IsNullOrWhiteSpace(Game.Result) ? "Result: ?" : $"Result: {Game.Result}";
        string date = string.IsNullOrWhiteSpace(Game.DateText) ? string.Empty : $" | {Game.DateText}";
        string eco = string.IsNullOrWhiteSpace(Game.Eco) ? string.Empty : $" | {OpeningCatalog.Describe(Game.Eco)}";
        PlayerSide? savedSide = getSavedAnalysisSide(Game);
        string analysisStatus = savedSide is not null ? $" | saved analysis: {savedSide}" : string.Empty;

        return $"Imported moves: {Cursor}/{moves.Count} applied | {players}{Environment.NewLine}{result}{date}{eco}{analysisStatus}";
    }

    public string BuildAnalyzeButtonText(Func<ImportedGame, PlayerSide?> getSavedAnalysisSide)
    {
        ArgumentNullException.ThrowIfNull(getSavedAnalysisSide);

        if (Game is null)
        {
            return "Analyze Imported";
        }

        PlayerSide? savedSide = getSavedAnalysisSide(Game);
        return savedSide is not null
            ? $"Open Analysis ({savedSide})"
            : "Analyze Imported";
    }
}

internal readonly record struct ImportedMoveListItem(int Ply, int MoveNumber, PlayerSide Side, string San)
{
    public string DisplayText => Side == PlayerSide.White
        ? $"{MoveNumber,3}. {San}"
        : $"{MoveNumber,3}... {San}";

    public override string ToString() => DisplayText;
}
