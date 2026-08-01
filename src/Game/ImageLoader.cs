using System.IO;
using System.Linq;
using Raylib_cs;

namespace Spacevors.Game;

public static class ImageLoader
{
    public static Texture2D? ShipTexture { get; private set; }

    public static readonly int AsteroidTextureCount = 6;
    public static Texture2D[]? AsteroidTextures { get; private set; }

    public static void LoadAssets()
    {
        ShipTexture = Raylib.LoadTexture("assets/ships/ship-test-1.png");

        var asteroidFiles = Directory.GetFiles("assets/asteroids", "*.png").OrderBy(f => f).ToArray();
        var textures = new Texture2D[Math.Min(asteroidFiles.Length, AsteroidTextureCount)];
        for (int i = 0; i < textures.Length; i++)
        {
            textures[i] = Raylib.LoadTexture(asteroidFiles[i]);
        }
        AsteroidTextures = textures;
    }

    public static void UnloadAssets()
    {
        if (ShipTexture.HasValue)
        {
            Raylib.UnloadTexture(ShipTexture.Value);
            ShipTexture = null;
        }

        if (AsteroidTextures != null)
        {
            foreach (var tex in AsteroidTextures)
            {
                if (tex.Id != 0)
                    Raylib.UnloadTexture(tex);
            }
            AsteroidTextures = null;
        }
    }
}
