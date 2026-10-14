using System.Numerics;
using Raylib_cs;

namespace Spacevors.Game;

// raylib's shape draws (DrawCircle, DrawLineEx) flush the 2D batch and issue their own GL draw call
// per shape. Measured on a real GPU that is ~1.1us per bullet, spark and explosion regardless of
// size, so at ten thousand entities the shape passes are the whole frame.
//
// This accumulates the same triangles into one rlBegin/rlEnd block instead: colour travels with the
// vertices (the shape shader has no per-shape uniforms to leak), and a pass costs one GL draw per
// VertexBudget shapes. Geometry mirrors raylib's own circles so the picture does not change.
static class ShapeBatch
{
    // rlgl's dynamic vertex buffer is finite, so a frame of many particles becomes a handful of draws
    // rather than one enormous one. 16k vertices is ~2.7k small circles.
    const int VertexBudget = 16384;

    // raylib chooses circle segments by radius; matching its thresholds keeps the silhouettes identical.
    const int TinyRadius = 2;
    const int SmallRadius = 20;
    const int MediumRadius = 50;
    const int TinySegments = 4;
    const int SmallSegments = 6;
    const int MediumSegments = 12;
    const int LargeSegments = 16;

    static int _vertices;
    static bool _open;

    public static void Begin()
    {
        if (_open) End();
        Open();
    }

    public static void End()
    {
        if (!_open) return;

        Rlgl.End();
        _open = false;
    }

    public static void AddCircle(float cx, float cy, float radius, Color color)
    {
        int segments = radius < TinyRadius ? TinySegments : radius < SmallRadius ? SmallSegments
            : radius < MediumRadius ? MediumSegments : LargeSegments;

        EnsureCapacity(segments * 3);
        SetColor(color);

        float step = MathF.PI * 2f / segments;
        float drawn = MathF.Max(radius, 1f);

        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step;
            float a1 = a0 + step;

            AddVertex(cx, cy);
            AddVertex(cx + MathF.Cos(a0) * drawn, cy + MathF.Sin(a0) * drawn);
            AddVertex(cx + MathF.Cos(a1) * drawn, cy + MathF.Sin(a1) * drawn);
        }
    }

    // A bullet's swept segment, thickened into a quad: the streak that replaces the dot when a bullet
    // travels further in one tick than it is wide.
    public static void AddStreak(Vector2 from, Vector2 to, float thickness, Color color)
    {
        Vector2 axis = to - from;
        float length = axis.Length();
        if (length <= float.Epsilon)
        {
            AddCircle(to.X, to.Y, thickness * 0.5f, color);
            return;
        }

        Vector2 side = new Vector2(axis.Y, -axis.X) * (thickness * 0.5f / length);

        EnsureCapacity(6);
        SetColor(color);

        AddVertex(from.X + side.X, from.Y + side.Y);
        AddVertex(to.X + side.X, to.Y + side.Y);
        AddVertex(to.X - side.X, to.Y - side.Y);
        AddVertex(from.X + side.X, from.Y + side.Y);
        AddVertex(to.X - side.X, to.Y - side.Y);
        AddVertex(from.X - side.X, from.Y - side.Y);
    }

    static void Open()
    {
        Rlgl.Begin(DrawMode.Triangles);
        _vertices = 0;
        _open = true;
    }

    static void EnsureCapacity(int needed)
    {
        if (!_open) Open();
        if (_vertices + needed <= VertexBudget) return;

        Rlgl.End();
        Open();
    }

    static void SetColor(Color color) => Rlgl.Color4ub(color.R, color.G, color.B, color.A);

    static void AddVertex(float x, float y)
    {
        Rlgl.Vertex2f(x, y);
        _vertices++;
    }
}
