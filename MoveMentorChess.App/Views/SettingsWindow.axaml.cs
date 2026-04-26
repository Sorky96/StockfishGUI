using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MoveMentorChess.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
        : this(LlamaGpuSettingsStore.Load(), StockfishSettingsStore.Load())
    {
    }

    public SettingsWindow(LlamaGpuSettings settings, StockfishSettings stockfishSettings)
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
        StockfishThreadsNumeric.Value = stockfishSettings.Threads;
        StockfishHashNumeric.Value = stockfishSettings.HashMb;
        BulkDepthNumeric.Value = stockfishSettings.BulkAnalysisDepth;
        BulkMultiPvNumeric.Value = stockfishSettings.BulkAnalysisMultiPv;
        BulkMoveTimeNumeric.Value = stockfishSettings.BulkAnalysisMoveTimeMs;
        ExplanationLevelComboBox.SelectedItem = ExplanationLevelComboBox.Items
            .OfType<ExplanationLevelOption>()
            .FirstOrDefault(option => option.Level == settings.DefaultExplanationLevel);
        NarrationStyleComboBox.SelectedItem = NarrationStyleComboBox.Items
            .OfType<NarrationStyleOption>()
            .FirstOrDefault(option => option.Style == settings.NarrationStyle);
        FullGpuPowerCheckBox.IsCheckedChanged += (_, _) => RefreshModeDescription();
        StockfishThreadsNumeric.ValueChanged += (_, _) => RefreshStockfishDescription();
        StockfishHashNumeric.ValueChanged += (_, _) => RefreshStockfishDescription();
        BulkDepthNumeric.ValueChanged += (_, _) => RefreshStockfishDescription();
        BulkMultiPvNumeric.ValueChanged += (_, _) => RefreshStockfishDescription();
        BulkMoveTimeNumeric.ValueChanged += (_, _) => RefreshStockfishDescription();
        RefreshModeDescription();
        RefreshStockfishDescription();
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

    public StockfishSettings SelectedStockfishSettings =>
        new(
            ReadInt(StockfishThreadsNumeric, StockfishSettings.Default.Threads),
            ReadInt(StockfishHashNumeric, StockfishSettings.Default.HashMb),
            ReadInt(BulkDepthNumeric, StockfishSettings.Default.BulkAnalysisDepth),
            ReadInt(BulkMultiPvNumeric, StockfishSettings.Default.BulkAnalysisMultiPv),
            ReadInt(BulkMoveTimeNumeric, StockfishSettings.Default.BulkAnalysisMoveTimeMs));

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        LlamaGpuSettingsStore.Save(SelectedSettings);
        StockfishSettingsStore.Save(SelectedStockfishSettings);
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

    private void RefreshStockfishDescription()
    {
        StockfishSettings settings = SelectedStockfishSettings;
        StockfishDescriptionTextBlock.Text =
            $"Stockfish will use {settings.Threads} thread(s), {settings.HashMb} MB hash, and bulk PGN analysis will run at depth {settings.BulkAnalysisDepth}, MultiPV {settings.BulkAnalysisMultiPv}, {settings.BulkAnalysisMoveTimeMs} ms per position.";
    }

    private static int ReadInt(NumericUpDown numeric, int fallback)
    {
        return numeric.Value.HasValue
            ? Convert.ToInt32(numeric.Value.Value)
            : fallback;
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
