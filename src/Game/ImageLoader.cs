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

    // The largest a lit sprite is ever drawn is the 520 px hull on the turret-mount screen and the
    // largest rock is 500 px across, so a 512 px map costs no visible detail - while the 1024 px
    // originals are minified 2-19x (enemy-1: a 762 px texture drawn 40 px across).
    const int MaxLitMapEdge = 512;

    // Maps are sampled with the base texture's normalized UVs, so a pure resolution difference is fine;
    // a size mismatch still warns because differently-framed maps would misregister silently.
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

        ClampToMaxEdge(ref baseImg);
        ClampToMaxEdge(ref normalsImg);
        ClampToMaxEdge(ref depthImg);

        // A differently sized depth map is resampled to the normal map: the shader samples both with
        // the same UVs, so the two channels must refer to the same texel.
        if (depthImg.Width != normalsImg.Width || depthImg.Height != normalsImg.Height)
            Raylib.ImageResize(ref depthImg, normalsImg.Width, normalsImg.Height);

        // Only the base is premultiplied: its transparent margin is black, so halving and mipmapping it
        // un-premultiplied would average the background into the hull silhouette (the shader divides the
        // alpha back out). Every raylib call that can reallocate a buffer runs on this function's own
        // variable - Image is a handle over native memory, and mutating a copy leaves this one pointing
        // at freed bytes (TROUBLE_SHOOTING.md).
        Raylib.ImageAlphaPremultiply(ref baseImg);

        Image surfaceImg = PackSurfaceMap(normalsImg, depthImg);

        var baseTex = UploadLitTexture(baseImg);
        var normalDepthTex = UploadLitTexture(surfaceImg);

        Raylib.UnloadImage(baseImg);
        Raylib.UnloadImage(normalsImg);
        Raylib.UnloadImage(depthImg);
        Raylib.UnloadImage(surfaceImg);

        return new LitSprite(baseTex, normalDepthTex);
    }

    // Downscale to MaxLitMapEdge on the long edge, keeping the aspect. Halving a 1024 px map is an
    // exact 2:1, which is the ratio raylib's resample filters best.
    static void ClampToMaxEdge(ref Image image)
    {
        int longest = Math.Max(image.Width, image.Height);
        if (longest <= MaxLitMapEdge) return;

        float scale = (float)MaxLitMapEdge / longest;
        Raylib.ImageResize(ref image, (int)MathF.Round(image.Width * scale), (int)MathF.Round(image.Height * scale));
    }

    // Builds the one surface map a lit sprite needs: RGB from the normal map, alpha from the depth
    // map's red channel. A lit fragment reads the normal at one UV and the depth at two, so packing
    // them saves a sampler and a texture fetch per fragment and a third of the lit maps' VRAM.
    // It writes into a fresh RGBA8 image instead of into the normal map, because the maps decode as
    // 8-bit RGB - three bytes a texel - and writing four bytes there runs off the end of that buffer.
    static unsafe Image PackSurfaceMap(Image normals, Image depth)
    {
        Image packed = Raylib.GenImageColor(normals.Width, normals.Height, Color.White);
        int texels = packed.Width * packed.Height;
        int normalStride = BytesPerTexel(normals.Format);
        int depthStride = BytesPerTexel(depth.Format);
        bool normalHasRgb = normalStride >= 3;

        // A normal map with fewer than three channels cannot carry a normal; a flat one is drawn rather
        // than a wrong one. The condition is the same for every texel, so the warning that names it is
        // issued once per map — inside the texel loop it printed one identical line per texel.
        if (!normalHasRgb)
            DiagnosticLogger.LogWarning($"normal map format {normals.Format} has no colour channels; drawing a flat normal");

        byte* dst = (byte*)packed.Data;
        byte* n = (byte*)normals.Data;
        byte* d = (byte*)depth.Data;

        for (int t = 0, i = 0; t < texels; t++, i += 4)
        {
            if (normalHasRgb)
            {
                dst[i] = n[t * normalStride];
                dst[i + 1] = n[t * normalStride + 1];
                dst[i + 2] = n[t * normalStride + 2];
            }
            else
            {
                dst[i] = 128;
                dst[i + 1] = 128;
                dst[i + 2] = 255;
            }
            dst[i + 3] = d[t * depthStride];
        }

        return packed;
    }

    // Bytes a texel occupies in the formats a PNG can decode to. Guessing a stride on anything else is
    // how a heap gets corrupted, so an unexpected format stops here instead of reading past a buffer.
    static int BytesPerTexel(PixelFormat format) => format switch
    {
        PixelFormat.UncompressedGrayscale => 1,
        PixelFormat.UncompressedGrayAlpha => 2,
        PixelFormat.UncompressedR8G8B8 => 3,
        PixelFormat.UncompressedR8G8B8A8 => 4,
        _ => throw new InvalidDataException($"lit map format {format} is not one a PNG decodes to"),
    };

    // Uploads with a mip chain, which is what stops a minified sprite shimmering: without one, a filter
    // that reads 4 texels out of a 19x19 footprint makes a spinning rock crawl. The base needs its
    // albedo recovered in the shader (it is uploaded premultiplied); the surface map does not, because
    // averaging a normal and a depth is meaningful and the shader normalizes what it reads.
    // The image is taken by value because this only reads it: anything that can reallocate a buffer has
    // to run on the caller's own variable.
    static Texture2D UploadLitTexture(Image image)
    {
        var texture = Raylib.LoadTextureFromImage(image);
        Raylib.GenTextureMipmaps(ref texture);
        Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
        return texture;
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
