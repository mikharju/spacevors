namespace Spacevors.Domain.Scenarios;

// The pinned entity mix for the ceiling probe (plans/CEILING_TEST.md), which asks how many objects
// still fit inside the frame budget. The answer is only comparable across runs if the composition is
// fixed, so it lives here as reviewed constants rather than being improvised per run.
//
// v1 is rounded from the composition of the three heaviest recorded scenarios - ammo-spray (11456
// live entities), impact-storm (23882) and missile-1024 (12931) - where effects are 58-80% of
// everything alive. The version string goes into every ceiling record row: rebalancing the mix is the
// right thing to do when gameplay changes, but it invalidates the earlier rows, so a new mix is a new
// version rather than an edit of these numbers.
public static class CeilingMix
{
    public const string Version = "v1";

    const float SparksPer1000 = 650f;
    const float BulletsPer1000 = 200f;
    const float ExplosionsPer1000 = 120f;
    const float MissilesPer1000 = 25f;
    const float AsteroidsPer1000 = 10f;
    const float MinesPer1000 = 3f;
    const float HullsPer1000 = 2f;

    // A thousand entities fill roughly one 1920x1024 screen at this density, and the disc grows with
    // sqrt(count), so a bigger world gets bigger rather than more crowded. That is how play scales,
    // and it keeps collision cost linear; crowding is what hull-crowding already prices.
    const float RadiusFor1000 = 700f;

    public static CeilingCounts CountsFor(int total) => new(
        Scale(total, SparksPer1000),
        Scale(total, BulletsPer1000),
        Scale(total, ExplosionsPer1000),
        Scale(total, MissilesPer1000),
        Scale(total, AsteroidsPer1000),
        Scale(total, MinesPer1000),
        Scale(total, HullsPer1000));

    public static float SpawnRadius(int total) => RadiusFor1000 * MathF.Sqrt(total / 1000f);

    static int Scale(int total, float per1000) => (int)MathF.Round(total * per1000 / 1000f, MidpointRounding.AwayFromZero);
}

// How many of each kind a ceiling world of a given size holds.
public readonly record struct CeilingCounts(
    int Sparks, int Bullets, int Explosions, int Missiles, int Asteroids, int Mines, int Hulls);
