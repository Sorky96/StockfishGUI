namespace MoveMentorChess.Presentation.Models;

internal sealed record PieceMoveOptionListItem(PieceMoveOption Option, string Label)
{
    public override string ToString() => Label;
}
