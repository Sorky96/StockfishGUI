using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MoveMentorChess.App.Views;

public partial class PgnImportWindow : Window
{
    public PgnImportWindow()
    {
        InitializeComponent();
    }

    public string PgnText => PgnTextBox.Text ?? string.Empty;

    private void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
