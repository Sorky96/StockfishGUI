using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record StoredMoveAnalysis(
    StoredGameContext Game,
    StoredAnalysisRunContext Analysis,
    StoredMoveContext Move,
    StoredMoveAdviceContext Advice,
    StoredManualFeedbackContext? ManualFeedback = null)
{
    // Compatibility accessors for older UI/report call sites. New service code should prefer Game, Analysis, Move, Advice and ManualFeedback.
    public string GameFingerprint => Game.GameFingerprint;
    public string? WhitePlayer => Game.WhitePlayer;
    public string? BlackPlayer => Game.BlackPlayer;
    public string? DateText => Game.DateText;
    public string? Result => Game.Result;
    public string? Eco => Game.Eco;
    public string? Site => Game.Site;
    public int? WhiteElo => Game.WhiteElo;
    public int? BlackElo => Game.BlackElo;
    public string? TimeControl => Game.TimeControl;
    public GameTimeControlCategory TimeControlCategory => Game.TimeControlCategory;
    public string? UtcDate => Game.UtcDate;
    public string? UtcTime => Game.UtcTime;
    public string? EndDate => Game.EndDate;
    public string? EndTime => Game.EndTime;
    public string? Termination => Game.Termination;
    public string? Link => Game.Link;

    public PlayerSide AnalyzedSide => Analysis.AnalyzedSide;
    public int Depth => Analysis.Depth;
    public int MultiPv => Analysis.MultiPv;
    public int? MoveTimeMs => Analysis.MoveTimeMs;
    public DateTime AnalysisUpdatedUtc => Analysis.AnalysisUpdatedUtc;

    public int Ply => Move.Ply;
    public int MoveNumber => Move.MoveNumber;
    public string San => Move.San;
    public string Uci => Move.Uci;
    public string FenBefore => Move.FenBefore;
    public string FenAfter => Move.FenAfter;
    public GamePhase Phase => Move.Phase;
    public int? EvalBeforeCp => Move.EvalBeforeCp;
    public int? EvalAfterCp => Move.EvalAfterCp;
    public int? BestMateIn => Move.BestMateIn;
    public int? PlayedMateIn => Move.PlayedMateIn;
    public int? CentipawnLoss => Move.CentipawnLoss;
    public MoveQualityBucket Quality => Move.Quality;
    public int MaterialDeltaCp => Move.MaterialDeltaCp;
    public string? BestMoveUci => Move.BestMoveUci;

    public string? MistakeLabel => Advice.MistakeLabel;
    public double? MistakeConfidence => Advice.MistakeConfidence;
    public IReadOnlyList<string> Evidence => Advice.Evidence;
    public string? ShortExplanation => Advice.ShortExplanation;
    public string? DetailedExplanation => Advice.DetailedExplanation;
    public string? TrainingHint => Advice.TrainingHint;
    public bool IsHighlighted => Advice.IsHighlighted;
    public string? OriginalMistakeLabel => Advice.OriginalMistakeLabel;

    public AdviceFeedbackKind? ManualFeedbackKind => ManualFeedback?.ManualFeedbackKind;
    public string? ManualCorrectedLabel => ManualFeedback?.ManualCorrectedLabel;
    public string? ManualComment => ManualFeedback?.ManualComment;
    public DateTime? ManualCorrectedUtc => ManualFeedback?.ManualCorrectedUtc;
}
