namespace Spacevors.Domain.Stats;

// One round primitive of a hull's collision shape, in the hull's own frame
// (plans/NON_CIRCLE_COLLISION.md). It is the segment [From,To] inflated by `HalfThickness`: a disc
// is the case where the two ends coincide, so circles and limbs run through the same math.
//
// Coordinates are in units of the hull's collision radius, in the sprite's frame as it is drawn:
// u across (the drawn diameter is 2R), v along it with the nose at -v. World position is
// `Position + rotate(local, Rotation.Angle) · Radius`, which is the same frame the sprite is drawn
// in, so an authored shape lines up with the art.
public readonly record struct Capsule(Vector2 From, Vector2 To, float HalfThickness)
{
    public static Capsule Disc(Vector2 centre, float halfThickness) => new(centre, centre, halfThickness);
}

// How one hull art collides: the primitives, in local units. A hull's `Radius` stays its bounding
// radius, so the spatial grid, off-screen culling, explosions, spawn placement and the targeting
// ring keep reading exactly what they read today — only the narrow phase looks at the shape.
public sealed record HullShape(Capsule[] Capsules)
{
    // One disc is the old behaviour, and it is what an art id without a shape falls back to.
    public static HullShape Circle { get; } = new([Capsule.Disc(Vector2.Zero, 1f)]);

    public bool IsCircle => Capsules.Length == 1 && Capsules[0].From == Capsules[0].To;
}

// The hull shapes, one entry per enemy art id (`EnemyShipType.GraphicsId`).
//
// Hand-authored from the sprite silhouette — `.tools/measure-hull.py` prints the extents and the
// painted span of each slice, and the numbers below are fitted to those. Not derived from the
// pixels at run time: a collider is tuned forgiveness rather than a tracing of the paint, and game
// logic must not depend on art assets. Reviewing the fit is the diagnostics overlay's job.
public static class HullShapes
{
    private static readonly HullShape[] ByGraphicsId =
    [
        // enemy-1 (the swarmer): the paint is a rounded blob covering 3.97 R², more than a disc's
        // 3.14 R², and it is the most numerous hull in the game. A disc is the honest fit here.
        HullShape.Circle,

        // Interceptor: a cross — 1.96 R² painted against a disc's 3.14, so its collision circle is
        // 60% air. Spine through the nose and tail, bar across the wings (|u| = 1.0 at v = 0,
        // tapering to 0.8 at v = ±0.2, body half-width 0.35 at |v| = 0.6).
        new(
        [
            new Capsule(new Vector2(0f, -0.67f), new Vector2(0f, 0.63f), 0.33f),
            new Capsule(new Vector2(-0.80f, 0f), new Vector2(0.80f, 0f), 0.20f),
        ]),

        // HeavyCannon: a wide slab with fore and aft pods — 2.37 R² against a disc's 3.14. The slab
        // holds |u| = 0.89 out to |v| = 0.6, so a bar across it, spine along it, and a bar at each
        // wing's end to reach the slab's corners.
        new(
        [
            new Capsule(new Vector2(0f, -0.64f), new Vector2(0f, 0.58f), 0.30f),
            new Capsule(new Vector2(-0.58f, 0f), new Vector2(0.58f, 0f), 0.30f),
            new Capsule(new Vector2(-0.60f, -0.30f), new Vector2(-0.60f, 0.30f), 0.30f),
            new Capsule(new Vector2(0.60f, -0.30f), new Vector2(0.60f, 0.30f), 0.30f),
        ]),
    ];

    public static HullShape ForEnemy(byte graphicsId) =>
        graphicsId < ByGraphicsId.Length ? ByGraphicsId[graphicsId] : HullShape.Circle;
}
