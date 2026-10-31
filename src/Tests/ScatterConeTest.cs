using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Scatter is the half-angle of the cone a shot pattern covers: several pellets spread evenly across
// it, a lone pellet rolls randomly inside it. Without that roll a spray weapon's Scatter offset is
// `(0 - (1-1)/2) * scatter` = 0 and the stat silently does nothing.
public class ScatterConeTest
{
    const float Cone = 0.3f;

    [Theory]
    [InlineData(11)]
    [InlineData(77)]
    [InlineData(2024)]
    public void LonePelletStaysInsideItsCone(int seed)
    {
        foreach (float angle in FireAngles(seed, Cone, pelletCount: 1))
            Assert.InRange(angle, -Cone, Cone);
    }

    [Fact]
    public void LonePelletsActuallySpread()
    {
        var angles = Enumerable.Range(0, 12).SelectMany(i => FireAngles(9000 + i, Cone, pelletCount: 1)).ToArray();

        Assert.Equal(12, angles.Length);
        Assert.True(angles.Max() - angles.Min() > 0.5f * Cone, $"cone never moved the shot: {string.Join(", ", angles)}");
    }

    [Fact]
    public void PelletsSpreadEvenlyAcrossTheCone()
    {
        float[] angles = FireAngles(seed: 42, scatter: 0.2f, pelletCount: 3);

        Array.Sort(angles);
        Assert.Equal(3, angles.Length);
        Assert.Equal(-0.2f, angles[0], 5);
        Assert.Equal(0f, angles[1], 5);
        Assert.Equal(0.2f, angles[2], 5);
    }

    // An even-count fan straddling the aim line put every round a little off a small target, so the
    // whole volley missed at any range worth shooting at. Whatever the count, one round flies at the
    // target and the rest scatter around it.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void EveryVolleyHasARoundOnTheAimLine(int pelletCount)
    {
        float[] angles = FireAngles(seed: 7, scatter: 0.2f, pelletCount);

        Assert.Equal(pelletCount, angles.Length);
        Assert.Contains(angles, angle => Math.Abs(angle) < 1e-4f);
        Assert.Equal(pelletCount, angles.Distinct().Count());
    }

    // The rounds fan out from that centre round instead of bunching on one side of it.
    [Fact]
    public void RoundsFanOutOnBothSidesOfTheCentre()
    {
        float[] angles = FireAngles(seed: 7, scatter: 0.2f, pelletCount: 4);

        Array.Sort(angles);
        Assert.Equal(0f, angles[2], 5);
        Assert.Equal(-0.4f, angles[0], 5);
        Assert.Equal(-0.2f, angles[1], 5);
        Assert.Equal(0.2f, angles[3], 5);
    }

    // One volley from a turret at the origin aiming straight down (-Y) at an asteroid, returned as
    // each shot's angle from that aim. The entity order is not part of the ECS contract, so callers
    // sort what they get back.
    private static float[] FireAngles(int seed, float scatter, int pelletCount)
    {
        var em = new EntityManager(seed);

        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(new Vector2(100f, 100f)));
        em.AddComponent(playerEntity, new Velocity(Vector2.Zero));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        var turretEntity = em.CreateEntity();
        em.AddComponent(turretEntity, new Position(Vector2.Zero));
        em.AddComponent(turretEntity, new Rotation(0f));
        em.AddComponent(turretEntity, new Turret(
            Weapon: new WeaponStats(FireRate: 1f, AmmoSpeed: 1000f, KickbackForce: 0f, PelletCount: pelletCount, Scatter: scatter),
            WeaponName: "SprayTestWeapon",
            ArcAngle: MathF.PI / 2f,
            Range: 400f));

        var asteroidEntity = em.CreateEntity();
        em.AddComponent(asteroidEntity, new Position(new Vector2(0f, -100f)));
        em.AddComponent(asteroidEntity, new Velocity(Vector2.Zero));
        em.AddComponent(asteroidEntity, new Asteroid(IsSmall: false, Radius: 30f));
        em.AddComponent(playerEntity, new PrimaryTarget(asteroidEntity));

        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);

        return em.GetEntitiesWithComponents<Ammo>().ToList()
            .Select(t => MathF.Atan2(t.Value1.Velocity.X, -t.Value1.Velocity.Y))
            .ToArray();
    }
}
