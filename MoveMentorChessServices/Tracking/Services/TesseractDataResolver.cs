using System;
using System.IO;

namespace MoveMentorChessServices;

internal static class TesseractDataResolver
{
    public static string GetExpectedDataPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "tessdata");
    }

    public static bool TryGetReadyDataPath(out string dataPath, out string? error)
    {
        dataPath = GetExpectedDataPath();
        string englishDataFile = Path.Combine(dataPath, "eng.traineddata");

        if (File.Exists(englishDataFile))
        {
            error = null;
            return true;
        }

        error = $"Missing OCR language data: '{englishDataFile}'. Download 'eng.traineddata' from the official Tesseract tessdata repository and place it in a 'tessdata' folder next to the app.";
        return false;
    }
}
