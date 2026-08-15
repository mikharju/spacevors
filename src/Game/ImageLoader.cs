using System.Collections.Generic;
using System.IO;
using System.Linq;
using Raylib_cs;

namespace Spacevors.Game;

public static class ImageLoader
{
    public static readonly int SmallAsteroidTextureCount = 6;
    public static readonly int LargeAsteroidTextureCount = 6;
    public static Texture2D[]? AsteroidSmallTextures { get; private set; }
    public static Texture2D[]? AsteroidLargeTextures { get; private set; }
    public static Texture2D? MineTexture { get; private set; }

    // Enemy ship textures keyed by filename stem (e.g. "enemy-1", "interceptor", "heavy-cannon")
    public static Dictionary<string, Texture2D>? EnemyShipTextures { get; private set; }

    // Player ship textures keyed by filename stem (e.g. "scout", "fighter", "heavy")
    public static Dictionary<string, Texture2D>? PlayerShipTextures { get; private set; }

    public static void LoadAssets()
    {
        var smallAsteroidFiles = Directory.GetFiles("assets/asteroids/small", "*.png").OrderBy(f => f).ToArray();
        var smallAsteroidTexs = new Texture2D[Math.Min(smallAsteroidFiles.Length, SmallAsteroidTextureCount)];
        for (int i = 0; i < smallAsteroidTexs.Length; i++)
        {
            smallAsteroidTexs[i] = Raylib.LoadTexture(smallAsteroidFiles[i]);
        }
        AsteroidSmallTextures = smallAsteroidTexs;

        var largeAsteroidFiles = Directory.GetFiles("assets/asteroids/large", "*.png").OrderBy(f => f).ToArray();
        var largeAsteroidTexs = new Texture2D[Math.Min(largeAsteroidFiles.Length, LargeAsteroidTextureCount)];
        for (int i = 0; i < largeAsteroidTexs.Length; i++)
        {
            largeAsteroidTexs[i] = Raylib.LoadTexture(largeAsteroidFiles[i]);
        }
        AsteroidLargeTextures = largeAsteroidTexs;

        var mineFiles = Directory.GetFiles("assets/mines", "*.png").OrderBy(f => f).ToArray();
        if (mineFiles.Length > 0)
            MineTexture = Raylib.LoadTexture(mineFiles[0]);

        EnemyShipTextures = LoadDirectoryTextures("assets/enemy-ships");
        PlayerShipTextures = LoadDirectoryTextures("assets/player-ships");
    }

    private static Dictionary<string, Texture2D> LoadDirectoryTextures(string directory)
    {
        var dict = new Dictionary<string, Texture2D>();
        if (!Directory.Exists(directory)) return dict;

        foreach (var file in Directory.GetFiles(directory, "*.png"))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            dict[stem] = Raylib.LoadTexture(file);
        }
        return dict;
    }

    public static void UnloadAssets()
    {
        if (AsteroidSmallTextures != null)
        {
            foreach (var tex in AsteroidSmallTextures)
            {
                if (tex.Id != 0)
                    Raylib.UnloadTexture(tex);
            }
            AsteroidSmallTextures = null;
        }

        if (AsteroidLargeTextures != null)
        {
            foreach (var tex in AsteroidLargeTextures)
            {
                if (tex.Id != 0)
                    Raylib.UnloadTexture(tex);
            }
            AsteroidLargeTextures = null;
        }

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

        if (PlayerShipTextures != null)
        {
            foreach (var tex in PlayerShipTextures.Values)
            {
                if (tex.Id != 0)
                    Raylib.UnloadTexture(tex);
            }
            PlayerShipTextures = null;
        }
    }
}
