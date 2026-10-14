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
    // Direction the seeded stream travels. The diagnostics world puts a fixed asteroid at (0, -300).
    Vector2 AimDirection,
    // How far along AimDirection the seeded stream is spread; 0 = the weapon's full reach.
    // Shorter than the distance to a target, otherwise bullets are placed inside and behind it and
    // are destroyed in the first ticks — a transient that is not part of steady-state cost.
    float StreamLength,
    // Whether the ship's own turrets keep firing while measured (holds bullet density steady).
    bool WeaponFires,
    float PlayerHealthRatio,
    int MeasureTicks,
    double FrameMsBudget,
    int Seed = EntityManager.DefaultRngSeed);

public static class Scenarios
{
    // 1 s at the fixed 1/120 timestep.
    public const int DefaultMeasureTicks = 120;

    // Loose frame budgets: they catch large regressions, not tuning drift. Refresh deliberately.
    private const double IdleFrameMsBudget = 10.0;
    private const double HeavyFrameMsBudget = 20.0;
    private const double MediumFrameMsBudget = 14.0;

    // The diagnostics Shadow copy carrying LoadTestWeapon (8000 pellets per volley).
    private static ShipType LoadTestShip =>
        ShipType.DiagnosticShips.First(s => s.Weapon.Name == WeaponType.LoadTestWeapon.Name);

    // Up: the diagnostics asteroid sits at (0, -300), so this aims at it. Down: empty space.
    private static readonly Vector2 TowardTestAsteroid = new(0f, -1f);
    private static readonly Vector2 IntoEmptySpace = new(0f, 1f);

    public static ScenarioSpec[] All { get; } =
    [
        new("baseline-idle",
            "Normal starting world, player idle, no seeded bullets, turrets silenced.",
            Ship: ShipType.Shadow, SeededWeapon: null, BulletCount: 0, AimDirection: TowardTestAsteroid,
            StreamLength: 0f, WeaponFires: false, PlayerHealthRatio: 1f,
            MeasureTicks: DefaultMeasureTicks, FrameMsBudget: IdleFrameMsBudget),

        new("ammo-spray",
            "Seeded LoadTestWeapon volley converging on the test asteroid; turrets silenced so no " +
            "8000-pellet volley lands inside the measured second.",
            Ship: LoadTestShip, SeededWeapon: WeaponType.LoadTestWeapon, BulletCount: 7000,
            AimDirection: TowardTestAsteroid, StreamLength: 280f, WeaponFires: false, PlayerHealthRatio: 1f,
            MeasureTicks: DefaultMeasureTicks, FrameMsBudget: HeavyFrameMsBudget),

        new("ammo-spray-miss",
            "Same seeded volley aimed into empty space: detection without hits. The pair with " +
            "ammo-spray prices collision resolution and impact effects directly.",
            Ship: LoadTestShip, SeededWeapon: WeaponType.LoadTestWeapon, BulletCount: 7000,
            AimDirection: IntoEmptySpace, StreamLength: 0f, WeaponFires: false, PlayerHealthRatio: 1f,
            MeasureTicks: DefaultMeasureTicks, FrameMsBudget: HeavyFrameMsBudget),

        new("hull-damage",
            "Player hull at 10%: damage shading, embers and smoke, no bullets in flight.",
            Ship: ShipType.Heavy, SeededWeapon: null, BulletCount: 0, AimDirection: TowardTestAsteroid,
            StreamLength: 0f, WeaponFires: false, PlayerHealthRatio: 0.1f,
            MeasureTicks: DefaultMeasureTicks, FrameMsBudget: MediumFrameMsBudget),
    ];

    public static ScenarioSpec? Find(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string Names => string.Join(", ", All.Select(s => s.Name));
}
