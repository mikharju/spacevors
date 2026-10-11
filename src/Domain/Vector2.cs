namespace Spacevors.Domain;

public readonly record struct Vector2(float X, float Y)
{
    public static Vector2 Zero => new(0f, 0f);
    public static Vector2 One => new(1f, 1f);

    public override string ToString() => $"({X}, {Y})";

    public float Magnitude => (float)Math.Sqrt(X * X + Y * Y);

    public Vector2 Normalized
    {
        get
        {
            var mag = Magnitude;
            return mag > 0 ? this / mag : Zero;
        }
    }

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 v, float s) => new(v.X * s, v.Y * s);
    public static Vector2 operator /(Vector2 v, float s) => new(v.X / s, v.Y / s);

    public static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;

    // Closest point on segment [from, to] to `point`, plus its projection parameter
    // t in [0,1] (0 = at `from`). Used for swept-segment hits: t orders hits along travel.
    public static Vector2 ClosestPointOnSegment(Vector2 from, Vector2 to, Vector2 point, out float t)
    {
        var delta = to - from;
        float lengthSq = delta.X * delta.X + delta.Y * delta.Y;
        if (lengthSq <= float.Epsilon)
        {
            t = 0f;
            return from;
        }

        t = Math.Clamp(Dot(point - from, delta) / lengthSq, 0f, 1f);
        return from + delta * t;
    }
}
