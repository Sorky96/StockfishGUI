namespace MoveMentorChess.Domain;

public sealed record AnalysisWindowState(
    PlayerSide SelectedSide,
    int QualityFilterIndex,
    int ExplanationLevelIndex = 1);
