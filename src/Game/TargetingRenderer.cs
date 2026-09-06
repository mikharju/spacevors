using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

// Draws corner brackets around the player's manually selected target (enemy ship or mine).
public static class TargetingRenderer
{
    private static readonly Color BracketColor = new(255, 80, 80, 230);

    // Gap between the target radius and the bracket corners.
    private const float Margin = 10f;
    private const float ArmLength = 10f;
    private const float Thickness = 2.5f;

    public static void Draw(EntityManager em, Entity playerEntity, float camX, float camY, int windowWidth, int windowHeight)
    {
        if (!em.TryGetComponent<PrimaryTarget>(playerEntity, out var primary)) return;
        if (em.HasComponent<Dead>(primary.Target)) return;

        float radius = GetRadius(em, primary.Target);
        if (radius <= 0f) return;

        var pos = em.GetComponent<Position>(primary.Target);
        float cx = (float)pos.Value.X - camX + windowWidth / 2f;
        float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

        if (RenderHelpers.IsOffScreen(cx, cy, radius + Margin, windowWidth, windowHeight)) return;

        DrawBrackets(cx, cy, radius + Margin);
    }

    private static float GetRadius(EntityManager em, Entity target)
    {
        if (em.TryGetComponent<EnemyShip>(target, out var ship)) return ship.Radius;
        if (em.TryGetComponent<EnemyMine>(target, out var mine)) return mine.Radius;
        return 0f;
    }

    private static void DrawBrackets(float cx, float cy, float halfSize)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                var corner = new System.Numerics.Vector2(cx + sx * halfSize, cy + sy * halfSize);
                Raylib.DrawLineEx(corner, new System.Numerics.Vector2(corner.X - sx * ArmLength, corner.Y), Thickness, BracketColor);
                Raylib.DrawLineEx(corner, new System.Numerics.Vector2(corner.X, corner.Y - sy * ArmLength), Thickness, BracketColor);
            }
    }
}
