using System.Collections.Generic;
using System.Linq;

namespace Spacevors.Game;

public static class LitSpriteMatcher
{
    public const string TextureSuffix = "-texture";
    public const string NormalsSuffix = "-normals";
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

            string normalsStem = prefix + NormalsSuffix;
            string depthStem = prefix + DepthSuffix;
            if (present.Contains(normalsStem) && present.Contains(depthStem))
                result.Add(new Set(prefix, stem, normalsStem, depthStem));
        }
        return result;
    }
}
