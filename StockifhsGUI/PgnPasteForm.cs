using System.Drawing;
using System.Windows.Forms;

namespace StockifhsGUI;

public class PgnPasteForm : Form
{
    private readonly TextBox pgnTextBox;

    public string PgnText => pgnTextBox.Text;

    public PgnPasteForm()
    {
        Text = "Paste PGN";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(700, 500);
        MinimumSize = new Size(500, 350);

        Label helpLabel = new()
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 12, 12, 4),
            Text = "Paste PGN text below. Tags, comments, and move numbers are supported."
        };

        pgnTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10),
            Margin = new Padding(12)
        };

        FlowLayoutPanel buttonsPanel = new()
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 12),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        Button importButton = new()
        {
            Text = "Import",
            DialogResult = DialogResult.OK,
            Width = 100,
            Height = 30
        };

        Button cancelButton = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 100,
            Height = 30
        };

        buttonsPanel.Controls.Add(importButton);
        buttonsPanel.Controls.Add(cancelButton);

        Controls.Add(pgnTextBox);
        Controls.Add(helpLabel);
        Controls.Add(buttonsPanel);

        AcceptButton = importButton;
        CancelButton = cancelButton;
    }
}
