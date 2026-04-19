using System;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string[] files = Directory.GetFiles(@"..\StockifhsGUI\UI", "*.cs", SearchOption.AllDirectories);

        foreach (string file in files)
        {
            if (file.EndsWith("UiTheme.cs")) continue;

            string content = File.ReadAllText(file);
            bool modified = false;

            // Remove method calls like UiTheme.ApplyFormChrome(this); or UiTheme.StyleListBox(listBox);
            string newContent = Regex.Replace(content, @"^\s*UiTheme\.[A-Za-z0-9_]+\(.*?\);\r?\n?", "", RegexOptions.Multiline);
            
            // Replace properties
            newContent = newContent.Replace("UiTheme.AppBackground", "System.Drawing.Color.Transparent");
            newContent = newContent.Replace("UiTheme.CardBackground", "System.Drawing.Color.Transparent");
            newContent = newContent.Replace("UiTheme.BorderColor", "System.Drawing.Color.Transparent");
            newContent = newContent.Replace("UiTheme.TextColor", "System.Drawing.Color.Black");
            newContent = newContent.Replace("UiTheme.MutedTextColor", "System.Drawing.Color.Gray");
            newContent = newContent.Replace("UiTheme.SuccessPanelColor", "System.Drawing.Color.Transparent");

            // Also remove ApplyUiTheme() from MainForm.cs
            newContent = Regex.Replace(newContent, @"^\s*ApplyUiTheme\(\);\r?\n?", "", RegexOptions.Multiline);
            newContent = Regex.Replace(newContent, @"\s*private void ApplyUiTheme\(\)\s*\{[^}]*\}\r?\n?", "\n", RegexOptions.Multiline);
            
            // Remove UiTheme.ApplyFormChrome(this); just in case it didn't match the first regex
            newContent = newContent.Replace("UiTheme.ApplyFormChrome(this);", "");

            if (content != newContent)
            {
                File.WriteAllText(file, newContent);
                Console.WriteLine("Updated " + Path.GetFileName(file));
            }
        }
    }
}
