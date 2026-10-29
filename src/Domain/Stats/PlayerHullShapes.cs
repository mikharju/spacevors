namespace Spacevors.Domain.Stats;

// The player hulls' collision shapes, one per hull type, mounted on `ShipType.Shape` and carried by the
// `Player` component. Same primitives, same local frame and the same reach rule as the enemy hulls
// (`HullShapes`), and authored the same way: `.tools/measure-hull.py <sprite> --fit "…"` scores a layout
// against the paint — how much of the paint it covers, and how much of the shape sits on visible air.
//
// A player's shape is its hurtbox, so the direction the fit errs in matters. It can only ever be smaller
// than the disc it replaces: the shape must stay inside the hull's bounding radius, which is what the
// broad phase, explosions, spawn clearance and the circle reject in front of every shape test all read.
// Where the paint reaches past that radius — the fighter's fore wings are at 1.10 R — nothing can cover it,
// and that is the collision circle's long-standing under-reach, not something a shape can fix.
//
// Coverage and air are what the tool printed for the numbers below, as a share of the sprite's painted
// area and of the shape's own area. A bare collision circle leaves 0.17-0.95 R² of the box as air.
public static class PlayerHullShapes
{
    // Scout (R 46): a diamond — widest across the middle (±0.98 R at v = 0), tapering to ±0.33 at the nose
    // and ±0.40 at the tail. A fat cross misses the shoulders, so a bar across each shoulder.
    // Covers 90% of the paint, 9% of it over air.
    public static HullShape Scout { get; } = new(
    [
        new Capsule(new Vector2(0f, -0.70f), new Vector2(0f, 0.70f), 0.30f),
        new Capsule(new Vector2(-0.70f, 0f), new Vector2(0.70f, 0f), 0.30f),
        new Capsule(new Vector2(-0.52f, -0.36f), new Vector2(0.52f, -0.36f), 0.22f),
        new Capsule(new Vector2(-0.48f, 0.36f), new Vector2(0.48f, 0.36f), 0.22f),
    ]);

    // Fighter (R 58): the roundest hull in the game — 2.97 R² painted against a disc's 3.14, and 0.34 R² of
    // that paint outside the collision circle. Cross through the wide fore and aft bodies; the wing corners
    // are past the bounding radius and stay out of reach of any shape.
    // Covers 79% of the paint — 89% of the paint a collider is allowed to reach — 12% of it over air.
    public static HullShape Fighter { get; } = new(
    [
        new Capsule(new Vector2(0f, -0.70f), new Vector2(0f, 0.70f), 0.30f),
        new Capsule(new Vector2(-0.70f, 0f), new Vector2(0.70f, 0f), 0.30f),
        new Capsule(new Vector2(-0.50f, -0.52f), new Vector2(0.50f, -0.52f), 0.24f),
        new Capsule(new Vector2(-0.58f, 0.45f), new Vector2(0.58f, 0.45f), 0.26f),
    ]);

    // Heavy (R 84): a wide slab (±1.00 R at v = 0) whose fore half is a U — two pods either side of a gap
    // you can see the stars through — on a narrow spine. So the fore end gets a thin bar at each pod rather
    // than one bar across the slot: covering that gap is the one thing that would hit the player on air.
    // Covers 90% of the paint, 14% of it over air.
    public static HullShape Heavy { get; } = new(
    [
        new Capsule(new Vector2(0f, -0.76f), new Vector2(0f, 0.76f), 0.24f),
        new Capsule(new Vector2(-0.70f, 0f), new Vector2(0.70f, 0f), 0.30f),
        new Capsule(new Vector2(-0.63f, 0.35f), new Vector2(0.63f, 0.35f), 0.28f),
        new Capsule(new Vector2(-0.75f, -0.45f), new Vector2(-0.75f, -0.20f), 0.12f),
        new Capsule(new Vector2(0.75f, -0.45f), new Vector2(0.75f, -0.20f), 0.12f),
    ]);

    // Shadow (R 58): a slab a whole hull-length wide — ±0.93 R from v = -0.6 to v = +0.6. A cross alone
    // leaves its fore and aft edges, so it takes a bar at each wing's end and one across each end.
    // Covers 90% of the paint, 8% of it over air.
    public static HullShape Shadow { get; } = new(
    [
        new Capsule(new Vector2(0f, -0.70f), new Vector2(0f, 0.70f), 0.30f),
        new Capsule(new Vector2(-0.70f, 0f), new Vector2(0.70f, 0f), 0.30f),
        new Capsule(new Vector2(-0.60f, -0.45f), new Vector2(-0.60f, 0.45f), 0.24f),
        new Capsule(new Vector2(0.60f, -0.45f), new Vector2(0.60f, 0.45f), 0.24f),
        new Capsule(new Vector2(-0.42f, -0.55f), new Vector2(0.42f, -0.55f), 0.25f),
        new Capsule(new Vector2(-0.42f, 0.55f), new Vector2(0.42f, 0.55f), 0.25f),
    ]);
}
