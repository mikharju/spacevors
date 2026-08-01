using System.Collections.Generic;
using System.IO;
using System.Linq;
using Raylib_cs;

namespace Spacevors.Game;

public static class ImageLoader
{
    public static readonly int AsteroidTextureCount = 6;
    public static Texture2D[]? AsteroidTextures { get; private set; }

    // Enemy ship textures keyed by filename stem (e.g. "enemy-1", "interceptor", "heavy-cannon")
    public static Dictionary<string, Texture2D>? EnemyShipTextures { get; private set; }

    // Player ship textures keyed by filename stem (e.g. "scout", "fighter", "heavy")
    public static Dictionary<string, Texture2D>? PlayerShipTextures { get; private set; }

    public static void LoadAssets()
    {
        var asteroidFiles = Directory.GetFiles("assets/asteroids", "*.png").OrderBy(f => f).ToArray();
        var asteroidTexs = new Texture2D[Math.Min(asteroidFiles.Length, AsteroidTextureCount)];
        for (int i = 0; i < asteroidTexs.Length; i++)
        {
            asteroidTexs[i] = Raylib.LoadTexture(asteroidFiles[i]);
        }
        AsteroidTextures = asteroidTexs;

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
        if (AsteroidTextures != null)
        {
            foreach (var tex in AsteroidTextures)
            {
                if (tex.Id != 0)
                    Raylib.UnloadTexture(tex);
            }
            AsteroidTextures = null;
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
