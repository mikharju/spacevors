using System.Collections.Generic;
using System.Linq;

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
        var result = new List<Set>();
        foreach (var stem in stems.OrderBy(s => s, StringComparer.Ordinal))
        {
            if (!stem.EndsWith(TextureSuffix, StringComparison.Ordinal)) continue;
            string prefix = stem[..^TextureSuffix.Length];
            if (prefix.Length == 0) continue;

            string? normalsStem = FindNormalsStem(present, prefix);
            if (normalsStem == null || !present.Contains(prefix + DepthSuffix)) continue;

            result.Add(new Set(prefix, stem, normalsStem, prefix + DepthSuffix));
        }
        return result;
    }

    private static string? FindNormalsStem(HashSet<string> present, string prefix)
    {
        foreach (var suffix in new[] { NormalsSuffix, NormalAliasSuffix })
            if (present.Contains(prefix + suffix)) return prefix + suffix;
        return null;
    }
}
