using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockifhsGUI.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
        : this(LlamaGpuSettingsStore.Load())
    {
    }

    public SettingsWindow(LlamaGpuSettings settings)
    {
        InitializeComponent();
        ExplanationLevelComboBox.ItemsSource = new[]
        {
            new ExplanationLevelOption(ExplanationLevel.Beginner, "Beginner"),
            new ExplanationLevelOption(ExplanationLevel.Intermediate, "Intermediate"),
            new ExplanationLevelOption(ExplanationLevel.Advanced, "Advanced")
        };
        NarrationStyleComboBox.ItemsSource = new[]
        {
            new NarrationStyleOption(AdviceNarrationStyle.RegularTrainer, "Regular Trainer"),
            new NarrationStyleOption(AdviceNarrationStyle.LevyRozman, "Levy Rozman"),
            new NarrationStyleOption(AdviceNarrationStyle.HikaruNakamura, "Hikaru Nakamura"),
            new NarrationStyleOption(AdviceNarrationStyle.BotezLive, "BotezLive"),
            new NarrationStyleOption(AdviceNarrationStyle.WittyAlien, "Witty Alien")
        };

        FullGpuPowerCheckBox.IsChecked = settings.UseFullGpuPower;
        ExplanationLevelComboBox.SelectedItem = ExplanationLevelComboBox.Items
            .OfType<ExplanationLevelOption>()
            .FirstOrDefault(option => option.Level == settings.DefaultExplanationLevel);
        NarrationStyleComboBox.SelectedItem = NarrationStyleComboBox.Items
            .OfType<NarrationStyleOption>()
            .FirstOrDefault(option => option.Style == settings.NarrationStyle);
        FullGpuPowerCheckBox.IsCheckedChanged += (_, _) => RefreshModeDescription();
        RefreshModeDescription();
    }

    public LlamaGpuSettings SelectedSettings =>
        new(
            FullGpuPowerCheckBox.IsChecked == true,
            ExplanationLevelComboBox.SelectedItem is ExplanationLevelOption levelOption
                ? levelOption.Level
                : ExplanationLevel.Intermediate,
            NarrationStyleComboBox.SelectedItem is NarrationStyleOption narrationOption
                ? narrationOption.Style
                : AdviceNarrationStyle.RegularTrainer);

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        LlamaGpuSettingsStore.Save(SelectedSettings);
        LlamaCppServerManager.Instance.Shutdown();
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void RefreshModeDescription()
    {
        bool useFullGpuPower = FullGpuPowerCheckBox.IsChecked == true;
        ModeDescriptionTextBlock.Text = useFullGpuPower
            ? $"Full GPU mode is enabled. llama.cpp will request '-ngl {LlamaGpuSettingsResolver.FullGpuLayersArgument}', which means pushing all possible model layers onto the GPU."
            : $"Balanced mode is enabled. llama.cpp will request '-ngl {LlamaGpuSettingsResolver.BalancedGpuLayersArgument}', which is safer on smaller cards and remains the default.";
    }

    private sealed record ExplanationLevelOption(ExplanationLevel Level, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record NarrationStyleOption(AdviceNarrationStyle Style, string Label)
    {
        public override string ToString() => Label;
    }
}
