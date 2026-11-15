using System.Text.RegularExpressions;

namespace EmuLauncher.Services;

public static class VersionUtil
{
    /// <summary>True when <paramref name="candidate"/> is a newer release than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? candidate, string? current)
        => Compare(candidate, current) > 0;

    /// <summary>Compares two version tags. Empty values sort lowest.</summary>
    public static int Compare(string? left, string? right)
    {
        var aEmpty = string.IsNullOrWhiteSpace(left);
        var bEmpty = string.IsNullOrWhiteSpace(right);
        if (aEmpty && bEmpty) return 0;
        if (aEmpty) return -1;
        if (bEmpty) return 1;

        var (aNums, aPre) = Parse(left!);
        var (bNums, bPre) = Parse(right!);

        for (var i = 0; i < Math.Max(aNums.Length, bNums.Length); i++)
        {
            var a = i < aNums.Length ? aNums[i] : 0;
            var b = i < bNums.Length ? bNums[i] : 0;
            if (a != b) return a > b ? 1 : -1;
        }

        // Same numbers: a stable release sorts above a pre-release of the same version.
        if (aPre.Length == 0 && bPre.Length > 0) return 1;
        if (aPre.Length > 0 && bPre.Length == 0) return -1;

        // "0.1-9384" against "0.1-10012": build numbers compare as numbers, not as text.
        if (long.TryParse(aPre, out var aBuild) && long.TryParse(bPre, out var bBuild))
            return aBuild.CompareTo(bBuild);

        return Math.Sign(string.Compare(aPre, bPre, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly Regex Numbered = new(@"^\d+(\.\d+)*([\-+_].*)?$", RegexOptions.Compiled);

    /// <summary>
    /// True for tags that carry a comparable number ("2.3.1", "v0.10", "build-178", "fritchii-v5.5").
    /// False for names such as "latest", "continuous" or "build-0a1b2c3", which say nothing about order.
    /// </summary>
    public static bool IsNumbered(string? version)
        => !string.IsNullOrWhiteSpace(version) && Numbered.IsMatch(StripPrefix(version!));

    /// <summary>Drops a leading "v" and any word before the first digit ("fritchii-v5.5" → "5.5").</summary>
    private static string StripPrefix(string version)
    {
        var v = version.Trim();
        if (v.Length > 0 && char.IsDigit(v[0])) return v;

        for (var i = 0; i < v.Length; i++)
        {
            if (char.IsDigit(v[i])) return v[i..];
        }

        return v;
    }

    private static (int[] Nums, string Pre) Parse(string version)
    {
        var v = StripPrefix(version);

        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];

        var pre = "";
        var dash = v.IndexOf('-');
        if (dash >= 0)
        {
            pre = v[(dash + 1)..];
            v = v[..dash];
        }

        var numbers = new List<int>();
        foreach (var part in v.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = new string(part.TakeWhile(char.IsDigit).ToArray());
            numbers.Add(int.TryParse(digits, out var n) ? n : 0);
        }

        return (numbers.ToArray(), pre);
    }
}
