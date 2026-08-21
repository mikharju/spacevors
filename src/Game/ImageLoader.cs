using System.Collections.Generic;
using System.IO;
using System.Linq;
using Raylib_cs;
using Spacevors.Domain;

namespace Spacevors.Game;

public static class ImageLoader
{
    // Indexed by Asteroid.Variant, ordered by base name.
    public static AsteroidSprite[]? AsteroidSmallSprites { get; private set; }
    public static AsteroidSprite[]? AsteroidLargeSprites { get; private set; }
    public static Texture2D? MineTexture { get; private set; }

    // Enemy ship textures keyed by filename stem (e.g. "enemy-1", "interceptor", "heavy-cannon")
    public static Dictionary<string, Texture2D>? EnemyShipTextures { get; private set; }

    // Player ship textures keyed by filename stem (e.g. "scout", "fighter", "heavy")
    public static Dictionary<string, Texture2D>? PlayerShipTextures { get; private set; }

    // Lit sprites keyed by name prefix (e.g. "shadow" from shadow-texture/-normals/-depth.png)
    public static Dictionary<string, LitSprite>? EnemyShipLitSprites { get; private set; }
    public static Dictionary<string, LitSprite>? PlayerShipLitSprites { get; private set; }

    public static void LoadAssets()
    {
        AsteroidSmallSprites = LoadAsteroidSprites(AssetPath("assets/asteroids/small"));
        AsteroidLargeSprites = LoadAsteroidSprites(AssetPath("assets/asteroids/large"));

        string minesDir = AssetPath("assets/mines");
        if (Directory.Exists(minesDir))
        {
            var mineFiles = Directory.GetFiles(minesDir, "*.png").OrderBy(f => f).ToArray();
            if (mineFiles.Length > 0)
                MineTexture = Raylib.LoadTexture(mineFiles[0]);
        }

        var enemyShips = LoadSpriteSets(AssetPath("assets/enemy-ships"));
        EnemyShipTextures = enemyShips.Flat;
        EnemyShipLitSprites = enemyShips.Lit;

        var playerShips = LoadSpriteSets(AssetPath("assets/player-ships"));
        PlayerShipTextures = playerShips.Flat;
        PlayerShipLitSprites = playerShips.Lit;
    }

    // Resolves against the app directory so assets load regardless of the working directory.
    static string AssetPath(string relative) => Path.Combine(AppContext.BaseDirectory, relative);

    // One sprite per asteroid variant, ordered by base name so the Variant index is stable.
    private static AsteroidSprite[]? LoadAsteroidSprites(string directory)
    {
        var (flat, lit) = LoadSpriteSets(directory);
        if (lit.Count == 0 && flat.Count == 0) return null;

        var keys = lit.Keys.Concat(flat.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        var sprites = new AsteroidSprite[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            sprites[i] = lit.TryGetValue(key, out var litSprite)
                ? new AsteroidSprite(litSprite, null)
                : new AsteroidSprite(null, flat[key]);
        }
        return sprites;
    }

    // Discovers lit sets (base + normals + depth) and remaining flat textures in a directory.
    private static (Dictionary<string, Texture2D> Flat, Dictionary<string, LitSprite> Lit) LoadSpriteSets(string directory)
    {
        var flat = new Dictionary<string, Texture2D>();
        var lit = new Dictionary<string, LitSprite>();
        if (!Directory.Exists(directory)) return (flat, lit);

        var byStem = new Dictionary<string, string>();
        foreach (var file in Directory.GetFiles(directory, "*.png").OrderBy(f => f))
            byStem[Path.GetFileNameWithoutExtension(file)] = file;

        var consumed = new HashSet<string>();
        foreach (var set in LitSpriteMatcher.Match(byStem.Keys))
        {
            lit[set.Prefix] = LoadLitSet(set, byStem, directory);
            // Alternate variants of a matched prefix (e.g. foo.png next to foo-texture.png) stay consumed.
            foreach (string stem in LitSpriteMatcher.VariantStems(set.Prefix))
                if (byStem.ContainsKey(stem))
                    consumed.Add(stem);
        }

        foreach (var pair in byStem)
        {
            if (consumed.Contains(pair.Key)) continue;
            // Map files without a complete set would otherwise be drawn as ordinary textures.
            if (LitSpriteMatcher.IsMapFile(pair.Key))
            {
                DiagnosticLogger.LogWarning($"skipped map file '{pair.Key}.png' in {Path.GetFileName(directory)}: no complete lit set");
                continue;
            }
            flat[pair.Key] = Raylib.LoadTexture(pair.Value);
        }

        return (flat, lit);
    }

    // Maps are sampled with the base texture's normalized UVs, so a pure resolution difference is fine;
    // a size mismatch still warns because differently-framed maps would misregister silently.
    private static LitSprite LoadLitSet(LitSpriteMatcher.Set set, Dictionary<string, string> byStem, string directory)
    {
        var baseTex = Raylib.LoadTexture(byStem[set.BaseStem]);
        var normalsTex = Raylib.LoadTexture(byStem[set.NormalsStem]);
        var depthTex = Raylib.LoadTexture(byStem[set.DepthStem]);

        if (normalsTex.Width != baseTex.Width || normalsTex.Height != baseTex.Height ||
            depthTex.Width != baseTex.Width || depthTex.Height != baseTex.Height)
            DiagnosticLogger.LogWarning(
                $"lit set '{set.Prefix}' in {Path.GetFileName(directory)}: maps " +
                $"{normalsTex.Width}x{normalsTex.Height}/{depthTex.Width}x{depthTex.Height}, base {baseTex.Width}x{baseTex.Height}; " +
                "regenerate maps at the base dimensions");

        return new LitSprite(baseTex, normalsTex, depthTex);
    }

    public static void UnloadAssets()
    {
        UnloadAsteroidSprites(AsteroidSmallSprites);
        AsteroidSmallSprites = null;

        UnloadAsteroidSprites(AsteroidLargeSprites);
        AsteroidLargeSprites = null;

        if (MineTexture.HasValue && MineTexture.Value.Id != 0)
        {
            Raylib.UnloadTexture(MineTexture.Value);
            MineTexture = null;
        }

        if (EnemyShipTextures != null)
        {
            foreach (var tex in EnemyShipTextures.Values)
            {
                if (tex.Id != 0)
                    Raylib.UnloadTexture(tex);
            }
            EnemyShipTextures = null;
        }

        UnloadLitSprites(EnemyShipLitSprites);
        EnemyShipLitSprites = null;

        if (PlayerShipTextures != null)
        {
            foreach (var tex in PlayerShipTextures.Values)
            {
                if (tex.Id != 0)
                    Raylib.UnloadTexture(tex);
            }
            PlayerShipTextures = null;
        }

        UnloadLitSprites(PlayerShipLitSprites);
        PlayerShipLitSprites = null;
    }

    private static void UnloadLitSprites(Dictionary<string, LitSprite>? litSprites)
    {
        if (litSprites == null) return;

        foreach (var sprite in litSprites.Values)
        {
            if (sprite.Base.Id != 0) Raylib.UnloadTexture(sprite.Base);
            if (sprite.Normals.Id != 0) Raylib.UnloadTexture(sprite.Normals);
            if (sprite.Depth.Id != 0) Raylib.UnloadTexture(sprite.Depth);
        }
    }

    private static void UnloadAsteroidSprites(AsteroidSprite[]? sprites)
    {
        if (sprites == null) return;

        foreach (var sprite in sprites)
        {
            LitSprite? lit = sprite.Lit;
            if (lit != null)
            {
                if (lit.Base.Id != 0) Raylib.UnloadTexture(lit.Base);
                if (lit.Normals.Id != 0) Raylib.UnloadTexture(lit.Normals);
                if (lit.Depth.Id != 0) Raylib.UnloadTexture(lit.Depth);
            }
            if (sprite.Flat.HasValue && sprite.Flat.Value.Id != 0)
                Raylib.UnloadTexture(sprite.Flat.Value);
        }
    }
}
