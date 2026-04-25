using System.Diagnostics;

namespace MoveMentorChessServices;

public static class LlamaCppProcessCleaner
{
    private static readonly string[] ManagedExecutableNames =
    [
        "llama-server",
        "llama-cli"
    ];

    public static void CleanupOrphanedProcesses()
    {
        HashSet<string> managedRoots = GetManagedRoots();
        if (managedRoots.Count == 0)
        {
            return;
        }

        foreach (string processName in ManagedExecutableNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (Process process in processes)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    string? executablePath = TryGetExecutablePath(process);
                    if (string.IsNullOrWhiteSpace(executablePath))
                    {
                        continue;
                    }

                    string normalizedPath = NormalizeDirectorySeparator(Path.GetFullPath(executablePath));
                    if (!IsUnderManagedRoot(normalizedPath, managedRoots))
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch
                {
                    // Best effort orphan cleanup only.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    private static HashSet<string> GetManagedRoots()
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);

        AddRoot(roots, AppContext.BaseDirectory);
        AddRoot(roots, Directory.GetCurrentDirectory());

        return roots;
    }

    private static void AddRoot(HashSet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            string normalized = NormalizeDirectorySeparator(Path.GetFullPath(path));
            roots.Add(normalized);
        }
        catch
        {
            // Ignore invalid paths.
        }
    }

    private static bool IsUnderManagedRoot(string executablePath, HashSet<string> managedRoots)
    {
        foreach (string root in managedRoots)
        {
            if (executablePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }
}
