using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

public static class GameInitializer
{
    private const int ClutterCount = 40;
    private const float BackgroundExtent = 6000f; // stars and clutter spread across ±extent/2 around the origin

    // (count, parallax, minSize, maxSize) per star layer, far to near.
    private static readonly (int Count, float Parallax, float MinSize, float MaxSize)[] StarLayers =
    [
        (150, 0.2f, 0.5f, 1f),
        (100, 0.5f, 1f, 1.5f),
        (50, 1.0f, 1.5f, 2f)
    ];

    public static (EntityManager em, Entity playerEntity, Entity cameraEntity, List<(Vector2 Position, float Size, Color Color, float Parallax)> stars, List<(Vector2 Position, float Width, float Height, Color Color)> clutter) Initialize(
        ShipType shipType, Vector2 viewportSize, int seed = EntityManager.DefaultRngSeed)
    {
        var (em, playerEntity, cameraEntity) = WorldFactory.Create(shipType, viewportSize, seed);

        // Starfield and clutter are presentation-only, but they draw from the world RNG after the
        // entities, so their position here is part of the deterministic world.
        var rand = em.Rng;

        // Background starfield with parallax layers
        var stars = new List<(Vector2 Position, float Size, Color Color, float Parallax)>();

        foreach (var (count, parallax, sizeMin, sizeMax) in StarLayers)
        {
            for (int i = 0; i < count; i++)
            {
                float x = (float)(rand.NextDouble() - 0.5) * BackgroundExtent;
                float y = (float)(rand.NextDouble() - 0.5) * BackgroundExtent;
                float size = sizeMin + (float)rand.NextDouble() * (sizeMax - sizeMin);

                Color color;
                float roll = (float)rand.NextDouble();
                if (roll < 0.5f) color = new Color(140, 130, 100, 160);
                else if (roll < 0.75f) color = new Color(160, 90, 70, 160);
                else color = new Color(80, 100, 140, 160);

                stars.Add((new Vector2(x, y), size, color, parallax));
            }
        }

        // Stationary clutter fixed to world coordinates
        var clutter = new List<(Vector2 Position, float Width, float Height, Color Color)>();

        for (int i = 0; i < ClutterCount; i++)
        {
            float x = (float)(rand.NextDouble() - 0.5) * BackgroundExtent;
            float y = (float)(rand.NextDouble() - 0.5) * BackgroundExtent;
            float w = 5f + (float)rand.NextDouble() * 20f;
            float h = 3f + (float)rand.NextDouble() * 12f;

            Color color;
            float roll = (float)rand.NextDouble();
            if (roll < 0.6f) color = new Color(35, 35, 38, 140);
            else color = new Color(55, 45, 32, 140);

            clutter.Add((new Vector2(x, y), w, h, color));
        }

        return (em, playerEntity, cameraEntity, stars, clutter);
    }
}
