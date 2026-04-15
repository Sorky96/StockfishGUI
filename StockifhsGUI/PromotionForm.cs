using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace StockifhsGUI;

public class PromotionForm : Form
{
    public string? SelectedPiece { get; private set; }

    public PromotionForm(bool isWhite, IReadOnlyDictionary<string, Image>? pieceImages = null)
    {
        Text = "Choose Promotion";
        Size = new Size(320, 130);

        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        string[] pieceCodes = { "q", "r", "b", "n" };
        string[] labels = { "Queen", "Rook", "Bishop", "Knight" };

        for (int i = 0; i < pieceCodes.Length; i++)
        {
            string symbol = isWhite ? pieceCodes[i].ToUpperInvariant() : pieceCodes[i];
            Button button = new()
            {
                Tag = symbol,
                Width = 64,
                Height = 64,
                Text = labels[i],
                BackgroundImageLayout = ImageLayout.Stretch
            };

            if (pieceImages is not null && pieceImages.TryGetValue(symbol, out Image? image))
            {
                button.BackgroundImage = image;
                button.Text = string.Empty;
            }

            button.Click += (_, _) =>
            {
                SelectedPiece = symbol;
                DialogResult = DialogResult.OK;
                Close();
            };

            panel.Controls.Add(button);
        }

        Controls.Add(panel);
    }
}
