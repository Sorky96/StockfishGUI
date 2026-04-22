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
        FullGpuPowerCheckBox.IsChecked = settings.UseFullGpuPower;
        FullGpuPowerCheckBox.IsCheckedChanged += (_, _) => RefreshModeDescription();
        RefreshModeDescription();
    }

    public LlamaGpuSettings SelectedSettings =>
        new(FullGpuPowerCheckBox.IsChecked == true);

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
}
