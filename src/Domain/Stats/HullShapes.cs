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

        // Streak: a dart — 0.84 R² painted, the least of any hull, and 0.98 R wide across at its widest.
        // A tapered spine from the long nose to the tail, side fins across the waist, and the aft
        // engine block as a bar with a pod capsule hanging off each end.
        // Covers 89% of the paint, 20% of it over air — the price of thin swept wings.
        new(
        [
            new Capsule(new Vector2(0f, -0.78f), new Vector2(0f, -0.45f), 0.11f),
            new Capsule(new Vector2(0f, -0.45f), new Vector2(0f, 0.28f), 0.19f),
            new Capsule(new Vector2(-0.31f, -0.02f), new Vector2(0.31f, -0.02f), 0.15f),
            new Capsule(new Vector2(-0.44f, 0.36f), new Vector2(0.44f, 0.36f), 0.16f),
            new Capsule(new Vector2(-0.37f, 0.45f), new Vector2(-0.37f, 0.58f), 0.08f),
            new Capsule(new Vector2(0.37f, 0.45f), new Vector2(0.37f, 0.58f), 0.08f),
            new Capsule(new Vector2(0f, 0.52f), new Vector2(0f, 0.74f), 0.06f),
        ]),

        // Carmine: an arrowhead — 1.33 R² painted, the body a diamond widest at the waist (|u| = 0.79
        // at v = 0) tapering fore to the needle and aft to the tail. Spine through it, a bar across the
        // waist and one across the fore wings, one across the aft taper. The needle tip past v = -0.6
        // is too thin to reach without swimming in air.
        // Covers 92% of the paint, 17% of it over air.
        new(
        [
            new Capsule(new Vector2(0f, -0.78f), new Vector2(0f, 0.52f), 0.19f),
            new Capsule(new Vector2(-0.44f, -0.34f), new Vector2(0.44f, -0.34f), 0.15f),
            new Capsule(new Vector2(-0.58f, -0.02f), new Vector2(0.58f, -0.02f), 0.21f),
            new Capsule(new Vector2(-0.38f, 0.25f), new Vector2(0.38f, 0.25f), 0.17f),
            new Capsule(new Vector2(-0.22f, 0.50f), new Vector2(0.22f, 0.50f), 0.13f),
        ]),
    ];

    public static HullShape ForEnemy(byte graphicsId) =>
        graphicsId < ByGraphicsId.Length ? ByGraphicsId[graphicsId] : HullShape.Circle;
}
