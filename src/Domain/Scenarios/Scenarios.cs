using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Scenarios;

// A named, seeded performance scenario. The start state *is* the scenario: it is reviewable in code
// and cannot drift when gameplay tuning changes, unlike a run-up that reaches a state by playing.
//
// ViewportSize is pinned by the host (WorldView.DefaultViewportSize) because world generation places
// mines and ships relative to the screen and the renderer culls by screen box.
public sealed record ScenarioSpec(
    string Name,
    string Description,
    ShipType Ship,
    // Non-null seeds that many bullets as a volley already in flight.
    WeaponType? SeededWeapon,
    int BulletCount,
    // Direction the seeded stream travels. The target asteroid sits along it.
    Vector2 AimDirection,
    // Lateral shift of the stream, in target radii: 0 is dead centre, 1 grazes the rim, above 1 the
    // stream misses entirely. Real auto-target fire is a wide cone around a small target, so sustained
    // play destroys far fewer bullets per tick than a centred stream does. Expressed in radii because
    // the test asteroid's size comes from the seed.
    float AimOffsetR,
    // How far along AimDirection the seeded stream is spread; 0 = the weapon's full reach.
    // Shorter than the distance to a target, otherwise bullets are placed inside and behind it and
    // are destroyed in the first ticks — a transient that is not part of steady-state cost.
    float StreamLength,
    // Whether the ship's own turrets keep firing while measured (holds bullet density steady).
    bool WeaponFires,
    float PlayerHealthRatio,
    int MeasureTicks,
    double FrameMsBudget,
    // Set where a scenario is knowingly slower than FrameMsBudget, so the harness reports the number
    // without failing. Clearing this flag is how an accepted shortfall gets closed.
    bool ExpectedOverBudget = false,
    // Effect entities to place in the impact area, already aged. Without them the first second of a
    // scenario measures particles piling up toward a steady state rather than the steady state.
    SeededEffects? Effects = null,
    // Unarmed hulls placed by ScenarioWorld. The rock-based scenarios price bullet-vs-circle and
    // nothing else, so a change to how hulls collide would pass them unmeasured.
    ScenarioHulls Hulls = ScenarioHulls.None,
    int Seed = EntityManager.DefaultRngSeed);

// Steady-state particle counts for the impact area, in entities.
public sealed record SeededEffects(int Sparks, int Explosions);

// Which unarmed hulls ScenarioWorld puts in the world (plans/NON_CIRCLE_COLLISION.md).
//   StreamTarget: one hull beside the seeded volley — what a bullet costs to test against a hull.
//   Crowding:     a ring of hulls charging the player — hull against hull, grinding.
public enum ScenarioHulls { None, StreamTarget, Crowding }

public static class Scenarios
{
    // 1 s at the fixed 1/120 timestep.
    public const int DefaultMeasureTicks = 120;

    // A LoadTestWeapon volley lives 1.9 s, so a window longer than ~1.5 s drains the whole stream and
    // measures its death instead of its density. Density is held by the seeded particle counts instead.

    // One budget for every scenario: the project goal of 120 fps. They used to be per-scenario and
    // loose (10-32ms), which caught collapses but could not fail on real drift - and a gate that
    // cannot fail is not a gate. Raise one deliberately, with a reason, and expect it to come down.
    private const double TargetFrameMsBudget = 1000.0 / 120.0;

    // The diagnostics Shadow copy carrying LoadTestWeapon (8000 pellets per volley).
    private static ShipType LoadTestShip =>
        ShipType.DiagnosticShips.First(s => s.Loadout.Name == WeaponType.LoadTestWeapon.Name);

    // Up: the pinned test asteroid sits directly ahead of the ship. Down: empty space.
    private static readonly Vector2 TowardTestAsteroid = new(0f, -1f);
    private static readonly Vector2 IntoEmptySpace = new(0f, 1f);

    public static ScenarioSpec[] All { get; } =
    [
        new("baseline-idle",
            "Normal starting world, player idle, no seeded bullets, turrets silenced.",
            Ship: ShipType.Shadow, SeededWeapon: null, BulletCount: 0, AimDirection: TowardTestAsteroid,
            AimOffsetR: 0f, StreamLength: 0f, WeaponFires: false, PlayerHealthRatio: 1f,
            MeasureTicks: DefaultMeasureTicks, FrameMsBudget: TargetFrameMsBudget),

        new("ammo-spray",
            "Seeded LoadTestWeapon volley grazing the rim of the test asteroid: dense ammo and a few " +
            "impacts per tick, which is the mix a playthrough records (its ~1k live sparks imply only " +
            "1-2 impacts per tick, since one impact spawns ~7 particles). Prices broad-phase candidate " +
            "testing, lifetime expiry churn and a realistic amount of impact resolution at ~5.5k " +
            "bullets. Turrets stay silenced so no 8000-pellet volley lands inside the measured window.",
            Ship: LoadTestShip, SeededWeapon: WeaponType.LoadTestWeapon, BulletCount: 5500,
            AimDirection: TowardTestAsteroid, AimOffsetR: 1.05f, StreamLength: 0f, WeaponFires: false,
            PlayerHealthRatio: 1f, MeasureTicks: DefaultMeasureTicks, FrameMsBudget: TargetFrameMsBudget,
            Effects: new SeededEffects(Sparks: 6000, Explosions: 1200)),

        new("ammo-spray-miss",
            "Same seeded volley aimed into empty space: detection with no candidate hits at all. The " +
            "pair with ammo-spray prices broad-phase candidate testing against a rock.",
            Ship: LoadTestShip, SeededWeapon: WeaponType.LoadTestWeapon, BulletCount: 5500,
            AimDirection: IntoEmptySpace, AimOffsetR: 0f, StreamLength: 0f, WeaponFires: false,
            PlayerHealthRatio: 1f, MeasureTicks: DefaultMeasureTicks, FrameMsBudget: TargetFrameMsBudget),

        new("impact-storm",
            "The same volley driven near the centre of the asteroid on purpose: ~12 impacts per tick, " +
            "and each impact spawns ~7 particles, so this is the worst case for collision resolution, " +
            "effect spawning and spark rendering (~16-20k entities). A stress case, not a representative one.",
            Ship: LoadTestShip, SeededWeapon: WeaponType.LoadTestWeapon, BulletCount: 5500,
            AimDirection: TowardTestAsteroid, AimOffsetR: 0.3f, StreamLength: 0f, WeaponFires: false,
            // Was marked ExpectedOverBudget at 29.48ms while every shape was its own GL draw call.
            // Shape batching took it to 4.76ms, inside the 120fps budget, so the mark is gone.
            PlayerHealthRatio: 1f, MeasureTicks: DefaultMeasureTicks, FrameMsBudget: TargetFrameMsBudget,
            Effects: new SeededEffects(Sparks: 15000, Explosions: 2000)),

        new("hull-damage",
            "Player hull at 10%: damage shading, embers and smoke, no bullets in flight.",
            Ship: ShipType.Heavy, SeededWeapon: null, BulletCount: 0, AimDirection: TowardTestAsteroid,
            AimOffsetR: 0f, StreamLength: 0f, WeaponFires: false, PlayerHealthRatio: 0.1f,
            MeasureTicks: DefaultMeasureTicks, FrameMsBudget: TargetFrameMsBudget),

        new("hull-stream",
            "The ammo-spray volley with an unarmed hull standing beside the stream: every bullet in that " +
            "hull's grid cell tests against a hull instead of only against the rock. The pair with " +
            "ammo-spray prices a bullet's narrow-phase test against a *hull*, which is what changes when " +
            "hulls stop being circles. Most bullets still miss the hull, so density holds.",
            Ship: LoadTestShip, SeededWeapon: WeaponType.LoadTestWeapon, BulletCount: 5500,
            AimDirection: TowardTestAsteroid, AimOffsetR: 1.05f, StreamLength: 0f, WeaponFires: false,
            PlayerHealthRatio: 1f, MeasureTicks: DefaultMeasureTicks, FrameMsBudget: TargetFrameMsBudget,
            // The same aged particle load as ammo-spray, so the two differ by exactly one thing: the hull.
            Effects: new SeededEffects(Sparks: 6000, Explosions: 1200),
            Hulls: ScenarioHulls.StreamTarget),

        new("hull-crowding",
            "No bullets: rings of unarmed hulls charging the player and grinding into each other. Prices " +
            "hull-against-hull narrow phase and resolution, which every ammo-dominated scenario leaves " +
            "invisible, and is where a composite of several primitives costs the most.",
            Ship: ShipType.Heavy, SeededWeapon: null, BulletCount: 0, AimDirection: TowardTestAsteroid,
            AimOffsetR: 0f, StreamLength: 0f, WeaponFires: false, PlayerHealthRatio: 1f,
            MeasureTicks: DefaultMeasureTicks, FrameMsBudget: TargetFrameMsBudget,
            Hulls: ScenarioHulls.Crowding),
    ];

    public static ScenarioSpec? Find(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string Names => string.Join(", ", All.Select(s => s.Name));
}
