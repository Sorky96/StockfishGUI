using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MoveMentorChess.App.ViewModels;
using MoveMentorChess.App.Views;

namespace MoveMentorChess.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            LlamaCppProcessCleaner.CleanupOrphanedProcesses();
            desktop.Exit += (_, _) => LlamaCppServerManager.Instance.Shutdown();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
