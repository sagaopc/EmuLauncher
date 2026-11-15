using System.IO;

namespace EmuLauncher.Services;

/// <summary>Finds the program to launch inside a freshly installed emulator folder.</summary>
public static class ExeFinder
{
    private static readonly string[] NotTheEmulator =
    {
        "unins", "setup", "install", "update", "crash", "report", "redist", "vcredist", "vc_redist",
        "dxsetup", "helper", "webengine", "7z", "cli", "tool", "test"
    };

    /// <param name="hints">File names of the executable, best first. Wildcards are allowed.</param>
    public static string? Find(string installDir, IEnumerable<string>? hints)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return null;

            foreach (var hint in hints ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(hint)) continue;
                var match = Enumerate(installDir, hint.Trim(), 3).FirstOrDefault();
                if (match is not null) return match;
            }

            // No hint matched: the biggest program near the top of the folder is almost always the emulator.
            for (var depth = 0; depth <= 2; depth++)
            {
                var best = Enumerate(installDir, "*.exe", depth)
                    .Where(path => !LooksLikeATool(path))
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.Length)
                    .FirstOrDefault();

                if (best is not null) return best.FullName;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Matching files, the ones closest to the top of the folder first.</summary>
    private static IEnumerable<string> Enumerate(string folder, string pattern, int maxDepth)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = maxDepth > 0,
            MaxRecursionDepth = maxDepth,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        return Directory.EnumerateFiles(folder, pattern, options)
            .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool LooksLikeATool(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return NotTheEmulator.Any(word => name.Contains(word, StringComparison.Ordinal));
    }
}
