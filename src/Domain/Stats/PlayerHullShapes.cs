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

    // Falcon (R 74): the slim hull — 1.01 R² of paint, and the widest thing about it is 1.22 R across.
    // Its two flank pods are detached from the body by see-through gaps, so the one bar that joins them
    // is where the hurtbox sits on air; the body is a spine tapered fore to aft, each pod a thin capsule.
    // Covers 90% of the paint, 18% of it over air.
    public static HullShape Falcon { get; } = new(
    [
        new Capsule(new Vector2(0f, -0.75f), new Vector2(0f, -0.10f), 0.20f),
        new Capsule(new Vector2(0f, 0f), new Vector2(0f, 0.40f), 0.17f),
        new Capsule(new Vector2(0f, 0.42f), new Vector2(0f, 0.76f), 0.11f),
        new Capsule(new Vector2(-0.52f, 0.03f), new Vector2(0.55f, 0.03f), 0.16f),
        new Capsule(new Vector2(-0.44f, -0.26f), new Vector2(-0.53f, 0.54f), 0.08f),
        new Capsule(new Vector2(0.47f, -0.26f), new Vector2(0.56f, 0.54f), 0.08f),
    ]);

    // Eagle (R 66): the wide-delta hull — 1.11 R² of paint, wings painted out to ±0.93 R at the waist.
    // A thin spine, one capsule per swept leading wing edge, a bar across the full span, and the aft
    // wing/pod slab as its own bar. The 1.85 R wing span is what the bounding circle can never
    // over-reach: it is nearly two radii, exactly what the disc always indexed.
    // Covers 87% of the paint, 23% of it over air — thin swept wings, the same tax the dart pays.
    public static HullShape Eagle { get; } = new(
    [
        new Capsule(new Vector2(0f, -0.83f), new Vector2(0f, 0.52f), 0.15f),
        new Capsule(new Vector2(-0.86f, 0f), new Vector2(0.86f, 0f), 0.10f),
        new Capsule(new Vector2(-0.28f, -0.16f), new Vector2(-0.86f, 0.02f), 0.11f),
        new Capsule(new Vector2(0.28f, -0.16f), new Vector2(0.86f, 0.02f), 0.11f),
        new Capsule(new Vector2(-0.45f, 0.28f), new Vector2(0.45f, 0.28f), 0.15f),
        new Capsule(new Vector2(-0.19f, 0.72f), new Vector2(0.19f, 0.72f), 0.08f),
    ]);

    // Manta (R 74): the wide, stubby hull — 1.57 R across against only 1.13 R long, and 0.77 R² of
    // paint, the least in the game. A bar across the wing slab, a capsule down each swept leading
    // edge, a bar across the trailing wing and engine pods, and the nose-to-tail spine. This is the
    // hull where the bounding circle over-reaches most on purpose: the disc is 3.14 R² of broad phase
    // against 0.77 of paint, and what gets hit stays inside the wings.
    // Covers 94% of the paint, 27% of the shape over air — the see-through panel slots in the wings.
    public static HullShape Manta { get; } = new(
    [
        new Capsule(new Vector2(0f, -0.56f), new Vector2(0f, 0.40f), 0.14f),
        new Capsule(new Vector2(-0.64f, 0.02f), new Vector2(0.64f, 0.02f), 0.15f),
        new Capsule(new Vector2(-0.34f, -0.24f), new Vector2(-0.66f, -0.10f), 0.12f),
        new Capsule(new Vector2(0.34f, -0.24f), new Vector2(0.66f, -0.10f), 0.12f),
        new Capsule(new Vector2(-0.58f, 0.18f), new Vector2(0.58f, 0.18f), 0.10f),
    ]);

    // Raptor (R 78): the twin-pod hull — a body flanked by two big detached pods joined by a wing root
    // across v = 0. The body is a spine with a split nose; each pod is one swept capsule running from
    // the wing root out past the aft tip. 0.97 R² of paint against the disc's 3.14.
    // Covers 85% of the paint, 21% of it over air.
    public static HullShape Raptor { get; } = new(
    [
        new Capsule(new Vector2(0f, -0.58f), new Vector2(0f, -0.42f), 0.09f),
        new Capsule(new Vector2(0f, -0.42f), new Vector2(0f, 0.42f), 0.16f),
        new Capsule(new Vector2(-0.46f, -0.05f), new Vector2(0.46f, -0.05f), 0.20f),
        new Capsule(new Vector2(-0.55f, -0.02f), new Vector2(-0.37f, 0.58f), 0.13f),
        new Capsule(new Vector2(0.55f, -0.02f), new Vector2(0.37f, 0.58f), 0.13f),
    ]);
}