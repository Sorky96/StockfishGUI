using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MoveMentorChessServices;

public sealed class ScreenCaptureService
{
    public Bitmap Capture(Rectangle region)
    {
        Bitmap bitmap = new(region.Width, region.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.Location, Point.Empty, region.Size);
        return bitmap;
    }

    public string ComputeHash(Bitmap bitmap)
    {
        using Bitmap resized = new(16, 16);
        using (Graphics graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(bitmap, 0, 0, resized.Width, resized.Height);
        }

        HashCode hash = new();

        for (int y = 0; y < resized.Height; y++)
        {
            for (int x = 0; x < resized.Width; x++)
            {
                Color pixel = resized.GetPixel(x, y);
                hash.Add(pixel.R / 8);
                hash.Add(pixel.G / 8);
                hash.Add(pixel.B / 8);
            }
        }

        return hash.ToHashCode().ToString("X8");
    }
}
