using Raylib_cs;
using Spacevors.Domain;

namespace Spacevors.Game;

// The start screen's backdrop: the game's parallax starfield, drifting slowly behind the ship list so
// the menu looks like the game it leads into. It carries its own fixed seed — GameInitializer builds an
// in-game field from the world RNG, and the menu must not perturb that (plans/START_SCREEN_ONBOARDING.md).
// Two near layers only: on a menu nobody is looking for depth, and a menu should not pay for 300 stars.
public static class MenuBackdrop
{
    const float Extent = 6000f;
    const float DriftPerSecond = 8f;
    const int Seed = 20261104;

    // (count, parallax, minSize, maxSize), far to near — the same layer shape the world builds with.
    static readonly (int Count, float Parallax, float MinSize, float MaxSize)[] Layers =
    [
        (120, 0.2f, 0.5f, 1f),
        (80, 0.5f, 1f, 1.5f),
    ];

    static readonly Color[] Tints =
    [
        new Color(140, 130, 100, 110),
        new Color(160, 90, 70, 110),
        new Color(120, 140, 190, 130),
    ];

    static readonly List<(Vector2 Position, float Width, float Height, Color Color)> NoClutter = [];

    public static List<(Vector2 Position, float Size, Color Color, float Parallax)> CreateStars()
    {
        var rand = new Random(Seed);
        var stars = new List<(Vector2 Position, float Size, Color Color, float Parallax)>();

        foreach (var (count, parallax, minSize, maxSize) in Layers)
            for (int i = 0; i < count; i++)
            {
                float x = (float)(rand.NextDouble() - 0.5) * Extent;
                float y = (float)(rand.NextDouble() - 0.5) * Extent;
                float size = minSize + (float)rand.NextDouble() * (maxSize - minSize);
                stars.Add((new Vector2(x, y), size, Tints[rand.Next(Tints.Length)], parallax));
            }

        return stars;
    }

    public static void DrawStars(List<(Vector2 Position, float Size, Color Color, float Parallax)> stars, int windowWidth, int windowHeight)
    {
        float drift = (float)Raylib.GetTime() * DriftPerSecond;
        BackgroundRenderer.Draw(stars, NoClutter, drift, drift * 0.5f, windowWidth, windowHeight);
    }
}
