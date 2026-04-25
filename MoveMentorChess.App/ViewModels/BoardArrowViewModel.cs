using Avalonia.Media;

namespace MoveMentorChess.App.ViewModels;

public sealed record BoardArrowViewModel(string FromSquare, string ToSquare, Color Color);
