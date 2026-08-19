using System.Collections.Generic;

namespace Spacevors.Game;

public static class LitSpriteMatcher
{
    public const string TextureSuffix = "-texture";
    public const string NormalsSuffix = "-normals";
    // Some assets use the singular form.
    public const string NormalAliasSuffix = "-normal";
    public const string DepthSuffix = "-depth";

    public sealed record Set(string Prefix, string BaseStem, string NormalsStem, string DepthStem);

    public static List<Set> Match(IEnumerable<string> stems)
    {
        var present = new HashSet<string>(stems);
        var prefixes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var stem in stems)
        {
            string? stripped = StripSuffix(stem);
            if (stripped == null) prefixes.Add(stem);          // not a map file: candidate plain base
            else if (stripped.Length > 0) prefixes.Add(stripped);
        }

        var result = new List<Set>();
        foreach (var prefix in prefixes)
        {
            string? baseStem = FindBaseStem(present, prefix);
            if (baseStem == null) continue;
            string? normalsStem = FindNormalsStem(present, prefix);
            if (normalsStem == null || !present.Contains(prefix + DepthSuffix)) continue;

            result.Add(new Set(prefix, baseStem, normalsStem, prefix + DepthSuffix));
        }
        return result;
    }

    // Base is the explicit "-texture" file when present, otherwise a plain same-named image.
    private static string? FindBaseStem(HashSet<string> present, string prefix)
    {
        if (present.Contains(prefix + TextureSuffix)) return prefix + TextureSuffix;
        if (present.Contains(prefix)) return prefix;
        return null;
    }

    private static string? FindNormalsStem(HashSet<string> present, string prefix)
    {
        foreach (var suffix in new[] { NormalsSuffix, NormalAliasSuffix })
            if (present.Contains(prefix + suffix)) return prefix + suffix;
        return null;
    }

    // Returns the stem without its map suffix, or null when the stem is not a map file.
    private static string? StripSuffix(string stem)
    {
        foreach (var suffix in new[] { TextureSuffix, NormalsSuffix, NormalAliasSuffix, DepthSuffix })
            if (stem.EndsWith(suffix, StringComparison.Ordinal)) return stem[..^suffix.Length];
        return null;
    }
}
