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
    // The depth map is folded into the normal map's alpha before upload (see WriteDepthIntoAlpha).
    private static LitSprite LoadLitSet(LitSpriteMatcher.Set set, Dictionary<string, string> byStem, string directory)
    {
        var baseImg = Raylib.LoadImage(byStem[set.BaseStem]);
        var normalsImg = Raylib.LoadImage(byStem[set.NormalsStem]);
        var depthImg = Raylib.LoadImage(byStem[set.DepthStem]);

        if (normalsImg.Width != baseImg.Width || normalsImg.Height != baseImg.Height ||
            depthImg.Width != baseImg.Width || depthImg.Height != baseImg.Height)
            DiagnosticLogger.LogWarning(
                $"lit set '{set.Prefix}' in {Path.GetFileName(directory)}: maps " +
                $"{normalsImg.Width}x{normalsImg.Height}/{depthImg.Width}x{depthImg.Height}, base {baseImg.Width}x{baseImg.Height}; " +
                "regenerate maps at the base dimensions");

        // Byte-level packing needs one known layout, and every map upload goes through the same path.
        Raylib.ImageFormat(ref baseImg, PixelFormat.UncompressedR8G8B8A8);
        Raylib.ImageFormat(ref normalsImg, PixelFormat.UncompressedR8G8B8A8);
        Raylib.ImageFormat(ref depthImg, PixelFormat.UncompressedR8G8B8A8);

        // A differently sized depth map is resampled to the normal map: the shader samples both with
        // the same UVs, so the two channels must refer to the same texel.
        if (depthImg.Width != normalsImg.Width || depthImg.Height != normalsImg.Height)
            Raylib.ImageResize(ref depthImg, normalsImg.Width, normalsImg.Height);

        WriteDepthIntoAlpha(normalsImg, depthImg);

        var baseTex = Raylib.LoadTextureFromImage(baseImg);
        var normalDepthTex = Raylib.LoadTextureFromImage(normalsImg);
        Raylib.UnloadImage(baseImg);
        Raylib.UnloadImage(normalsImg);
        Raylib.UnloadImage(depthImg);

        return new LitSprite(baseTex, normalDepthTex);
    }

    // Copies the depth map's red channel into the normal map's alpha. Both are plain 8-bit PNGs whose
    // alpha carries nothing, and a lit fragment needs the normal and the depth at overlapping UVs, so
    // packing them saves one texture fetch per fragment and a third of the lit maps' VRAM.
    // Unsafe because raylib holds image pixels behind a raw pointer (Image.Data); the loop stays
    // inside the two buffers the caller already owns.
    static unsafe void WriteDepthIntoAlpha(Image normals, Image depth)
    {
        const int BytesPerTexel = 4;
        const int AlphaOffset = 3;

        int byteCount = normals.Width * normals.Height * BytesPerTexel;
        byte* surface = (byte*)normals.Data;
        byte* heights = (byte*)depth.Data;

        for (int alpha = AlphaOffset; alpha < byteCount; alpha += BytesPerTexel)
            surface[alpha] = heights[alpha - AlphaOffset];
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
            if (sprite.NormalDepth.Id != 0) Raylib.UnloadTexture(sprite.NormalDepth);
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
                if (lit.NormalDepth.Id != 0) Raylib.UnloadTexture(lit.NormalDepth);
            }
            if (sprite.Flat.HasValue && sprite.Flat.Value.Id != 0)
                Raylib.UnloadTexture(sprite.Flat.Value);
        }
    }
}
