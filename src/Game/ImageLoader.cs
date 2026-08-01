using Raylib_cs;

namespace Spacevors.Game;

public static class ImageLoader
{
    public static Texture2D? ShipTexture { get; private set; }

    public static void LoadAssets()
    {
        ShipTexture = Raylib.LoadTexture("assets/ships/ship-test-1.png");
    }

    public static void UnloadAssets()
    {
        if (ShipTexture.HasValue)
        {
            Raylib.UnloadTexture(ShipTexture.Value);
            ShipTexture = null;
        }
    }
}
