using System.Drawing;
using System.Windows.Forms;

namespace StockifhsGUI;

internal static class UiTheme
{
    internal static Color AppBackground => Color.FromArgb(243, 245, 248);
    internal static Color CardBackground => Color.White;
    internal static Color BorderColor => Color.FromArgb(208, 214, 223);
    internal static Color TextColor => Color.FromArgb(32, 38, 46);
    internal static Color MutedTextColor => Color.FromArgb(95, 105, 118);
    internal static Color AccentColor => Color.FromArgb(34, 96, 168);
    internal static Color AccentHoverColor => Color.FromArgb(27, 80, 140);
    internal static Color AccentPressedColor => Color.FromArgb(20, 65, 116);
    internal static Color SecondaryButtonColor => Color.FromArgb(233, 237, 243);
    internal static Color SecondaryHoverColor => Color.FromArgb(221, 228, 237);
    internal static Color DangerButtonColor => Color.FromArgb(188, 76, 61);
    internal static Color DangerHoverColor => Color.FromArgb(165, 62, 48);
    internal static Color SuccessPanelColor => Color.FromArgb(236, 245, 255);

    public static void ApplyFormChrome(Form form)
    {
        form.BackColor = AppBackground;
        form.ForeColor = TextColor;
        form.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    }

    public static void StylePrimaryButton(Button button)
    {
        StyleButton(button, AccentColor, Color.White, AccentHoverColor, AccentPressedColor);
    }

    public static void StyleSecondaryButton(Button button)
    {
        StyleButton(button, SecondaryButtonColor, TextColor, SecondaryHoverColor, BorderColor);
    }

    public static void StyleDangerButton(Button button)
    {
        StyleButton(button, DangerButtonColor, Color.White, DangerHoverColor, DangerHoverColor);
    }

    public static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.BackColor = CardBackground;
        comboBox.ForeColor = TextColor;
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = textBox.ReadOnly ? CardBackground : Color.White;
        textBox.ForeColor = TextColor;
    }

    public static void StyleListBox(ListBox listBox)
    {
        listBox.BorderStyle = BorderStyle.FixedSingle;
        listBox.BackColor = CardBackground;
        listBox.ForeColor = TextColor;
    }

    public static void StyleInfoLabel(Label label)
    {
        label.BackColor = SuccessPanelColor;
        label.ForeColor = TextColor;
        label.BorderStyle = BorderStyle.FixedSingle;
        label.Padding = new Padding(10, 8, 10, 8);
    }

    public static void StyleMutedLabel(Label label)
    {
        label.ForeColor = MutedTextColor;
    }

    public static void StyleSectionLabel(Label label)
    {
        label.ForeColor = TextColor;
        label.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
    }

    public static void StyleCheckBox(CheckBox checkBox)
    {
        checkBox.ForeColor = TextColor;
        checkBox.BackColor = AppBackground;
    }

    public static void StyleCardPanel(Control control)
    {
        control.BackColor = CardBackground;
        control.ForeColor = TextColor;
    }

    private static void StyleButton(Button button, Color backColor, Color foreColor, Color hoverColor, Color pressedColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = hoverColor;
        button.FlatAppearance.MouseDownBackColor = pressedColor;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold, GraphicsUnit.Point);
    }
}
