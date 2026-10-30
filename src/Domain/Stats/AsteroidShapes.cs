namespace Spacevors.Domain.Stats;

// The collision shape of each asteroid art variant, one entry per `Asteroid.Variant`.
//
// Authored the same way the hull shapes are: `.tools/measure-hull.py` prints the painted span of each
// slice of the art and scores a candidate layout, and the numbers below are the fit it came back with.
// Coordinates are in units of the rock's collision radius, in the frame its sprite is drawn in, so a
// rock's shape turns with its `Rotation` exactly as the art does.
//
// The first variants of each size are discs: their paint fills 2.4-3.2 R² of the 3.14 R² collision
// circle, so a circle is the honest fit and every rock-vs-rock pair keeps the circle fast path. The
// later ones are lumps rather than balls - 1.7-2.3 R² of paint, so a circle sits on 0.9-1.5 R² of
// empty space around them - and each is two capsules stretched across its lobes instead. Those cover
// 96-98% of the paint while sitting on 0.02-0.06 R² of air.
public static class AsteroidShapes
{
    private static readonly HullShape[] BySmallVariant =
    [
        HullShape.Circle,   // small-1: 2.41 R² of paint, a rounded blob
        HullShape.Circle,   // small-2: 3.02 R² - as full a disc as the art paints
        HullShape.Circle,   // small-3: 2.99 R²

        // small-4 (1.68 R²): tall and leaning, fat through the middle and cut away fore and aft.
        new(
        [
            new Capsule(new Vector2(-0.48f, -0.05f), new Vector2(0.39f, 0.16f), 0.33f),
            new Capsule(new Vector2(0.01f, -0.01f), new Vector2(-0.13f, -0.01f), 0.68f),
        ]),

        // small-5 (1.85 R²): a slab tilted across the frame, thick at its lower-left end.
        new(
        [
            new Capsule(new Vector2(-0.37f, 0.39f), new Vector2(0.34f, 0.02f), 0.40f),
            new Capsule(new Vector2(0.20f, -0.19f), new Vector2(-0.17f, 0.17f), 0.60f),
        ]),
    ];

    private static readonly HullShape[] ByLargeVariant =
    [
        HullShape.Circle,   // large-asteroid-1: 3.12 R² of paint
        HullShape.Circle,   // large-asteroid-2: 3.18 R²
        HullShape.Circle,   // large-asteroid-3: 3.11 R²

        // large-asteroid-4 (2.25 R²): a blocky hexagon, corners stubbed off.
        new(
        [
            new Capsule(new Vector2(-0.26f, 0.33f), new Vector2(0.30f, -0.19f), 0.54f),
            new Capsule(new Vector2(-0.16f, -0.20f), new Vector2(0.19f, 0.08f), 0.62f),
        ]),

        // large-asteroid-5 (2.06 R²): round-ish with a shoulder on the left and a bite top-right.
        new(
        [
            new Capsule(new Vector2(-0.19f, 0.03f), new Vector2(0.07f, -0.07f), 0.70f),
            new Capsule(new Vector2(-0.14f, -0.46f), new Vector2(0.22f, 0.45f), 0.36f),
        ]),

        // large-asteroid-6 (1.85 R²): two lobes stacked, pinched in at the right.
        new(
        [
            new Capsule(new Vector2(-0.15f, 0.10f), new Vector2(0.11f, -0.17f), 0.61f),
            new Capsule(new Vector2(-0.05f, -0.38f), new Vector2(0.18f, 0.41f), 0.43f),
        ]),

        // large-asteroid-7 (1.87 R²): a ball with the rim nibbled all round.
        new(
        [
            new Capsule(new Vector2(-0.20f, 0.07f), new Vector2(0.21f, -0.07f), 0.60f),
            new Capsule(new Vector2(-0.19f, -0.30f), new Vector2(0.00f, 0.31f), 0.46f),
        ]),
    ];

    public static HullShape For(bool isSmall, byte variant)
    {
        HullShape[] byVariant = isSmall ? BySmallVariant : ByLargeVariant;
        // The domain's variant count and the art directory can be out of step; an unknown variant is
        // the old behaviour rather than a guess.
        return variant < byVariant.Length ? byVariant[variant] : HullShape.Circle;
    }
}
