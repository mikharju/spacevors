using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Rocks: the small ones are shootable scenery that comes apart in smoke, the large ones are
// obstacles that nothing damages.
public class AsteroidHealthTest
{
    private static Entity AddRock(EntityManager em, bool isSmall)
    {
        var rock = em.CreateEntity();
        em.AddComponent(rock, new Position(Vector2.Zero));
        em.AddComponent(rock, new Velocity(Vector2.Zero));
        em.AddComponent(rock, new Asteroid(isSmall, Radius: isSmall ? 40f : 200f));
        if (isSmall) em.AddComponent(rock, new Health(Asteroid.SmallHealth, Asteroid.SmallHealth));
        return rock;
    }

    // One collision tick with a bullet that reaches the rock at the origin this tick.
    private static void ShootRock(EntityManager em, int damage)
    {
        var ammo = em.CreateEntity();
        em.AddComponent(ammo, new Position(new Vector2(-40f, 0f)));
        em.AddComponent(ammo, new Ammo(Velocity: new Vector2(2000f, 0f), Radius: 3f, Lifetime: 10f, Damage: damage));

        var commands = new CommandBuffer();
        new CollisionSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);
    }

    [Fact]
    public void SmallRock_KeepsTrackOfGunfireDamage()
    {
        var em = new EntityManager();
        var rock = AddRock(em, isSmall: true);

        ShootRock(em, damage: 10);

        Assert.True(em.TryGetComponent<Health>(rock, out var health), "A small rock carries Health");
        Assert.Equal(Asteroid.SmallHealth - 10, health.Current);
    }

    [Fact]
    public void SmashedRock_ComesApartInSmokeNotAnExplosion()
    {
        var em = new EntityManager();
        AddRock(em, isSmall: true);

        ShootRock(em, damage: Asteroid.SmallHealth);

        Assert.True(em.GetEntitiesWithComponents<Asteroid>().Count() == 0, "The rock is gone");
        Assert.True(em.GetEntitiesWithComponents<SmokePuff>().Count() >= 12, "A broken rock leaves a cloud of puffs");
        Assert.False(em.GetEntitiesWithComponents<ShipDeathExplosion>().Any(), "A rock does not detonate");
        Assert.False(em.GetEntitiesWithComponents<Explosion>().Any(), "Not even the killing hit flashes fire");
    }

    [Fact]
    public void SmokeFromABrokenRock_DissipatesInsteadOfLoungingAround()
    {
        var em = new EntityManager();
        AddRock(em, isSmall: true);
        ShootRock(em, damage: Asteroid.SmallHealth);

        int puffs = em.GetEntitiesWithComponents<SmokePuff>().Count();
        Assert.True(puffs > 0);

        // The puffs live a couple of seconds: long enough to read as a break-up, gone after that.
        for (int tick = 0; tick < 400; tick++)
        {
            var commands = new CommandBuffer();
            new EffectSystem().Update(new WorldView(em), 1f / 120f, commands);
            commands.Apply(em);
        }

        Assert.Equal(0, em.GetEntitiesWithComponents<SmokePuff>().Count());
    }

    [Fact]
    public void LargeRock_IsAnObstacleThatNothingWearsDown()
    {
        var em = new EntityManager();
        var rock = AddRock(em, isSmall: false);

        ShootRock(em, damage: 1000);

        Assert.True(em.HasComponent<Asteroid>(rock), "The big scenery survives");
        Assert.False(em.HasComponent<Health>(rock), "and never gains Health to lose");
        Assert.Equal(0, em.GetEntitiesWithComponents<SmokePuff>().Count());
    }

    [Fact]
    public void RammingARockDoesNotChipIt()
    {
        // Rocks are broken by gunfire only: otherwise the field would grind itself away as ships and
        // asteroids collide with each other and with the player.
        var em = new EntityManager();
        var rock = AddRock(em, isSmall: true);

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(new Vector2(-30f, 0f)));
        em.AddComponent(player, new Velocity(new Vector2(600f, 0f)));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
        em.AddComponent(player, new Health(10, 10));

        var commands = new CommandBuffer();
        new CollisionSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);

        Assert.True(em.TryGetComponent<Health>(rock, out var health));
        Assert.Equal(Asteroid.SmallHealth, health.Current);
    }

    [Fact]
    public void Factory_ArmsOnlyTheSmallRocks()
    {
        var em = new EntityManager();
        var rand = new Random(7);
        int small = 0, large = 0;

        for (int i = 0; i < 40; i++)
        {
            var entity = em.CreateEntity();
            AsteroidFactory.AddAsteroidComponents(em, entity, Vector2.Zero, speed: 10f, angle: 0f, rand);

            bool isSmall = em.GetComponent<Asteroid>(entity).IsSmall;
            Assert.Equal(isSmall, em.HasComponent<Health>(entity));
            if (isSmall) small++;
            else large++;
        }

        Assert.True(small > 0 && large > 0, "The sample must cover both sizes");
    }
}
