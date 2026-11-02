using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Scenarios;
using Spacevors.Domain.Stats;
using Xunit;

// The ceiling probe varies only the size of the world, so the mix and the geometry it is spread over
// are pinned here: quietly changing either would invalidate every ceiling number recorded before it.
public class CeilingWorldTest
{
    [Fact]
    public void MixedWorld_SeedsThePinnedMix()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7);
        ScenarioWorld.SeedMixedWorld(em, player, 2000);

        CeilingCounts counts = CeilingMix.CountsFor(2000);

        Assert.Equal(counts.Sparks, Count<DamageSpark>(em));
        Assert.Equal(counts.Explosions, Count<Explosion>(em));
        // A guided round carries Ammo as well as Missile, so the ammo count covers both kinds.
        Assert.Equal(counts.Bullets + counts.Missiles, Count<Ammo>(em));
        Assert.Equal(counts.Missiles, Count<Missile>(em));

        // Asteroids, mines and hulls also come from WorldFactory's opening world, so the seeded share
        // of those is a floor rather than an exact total.
        Assert.True(Count<Asteroid>(em) >= counts.Asteroids);
        Assert.True(Count<EnemyMine>(em) >= counts.Mines);
        Assert.True(Count<EnemyShip>(em) >= counts.Hulls);
    }

    [Fact]
    public void MixedWorld_SpreadsOverTheWholeDisc_NotJustTheCentre()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Shadow, WorldView.DefaultViewportSize, seed: 7);
        ScenarioWorld.SeedMixedWorld(em, player, 2000);

        Vector2 playerPos = em.GetComponent<Position>(player).Value;
        float radius = CeilingMix.SpawnRadius(2000);
        float innerRadiusSquared = radius * radius * 0.25f;

        int innerHalf = 0, outerHalf = 0;
        foreach (var (_, position, _) in em.GetEntitiesWithComponents<Position, DamageSpark>())
        {
            Vector2 offset = position.Value - playerPos;
            if (offset.X * offset.X + offset.Y * offset.Y < innerRadiusSquared) innerHalf++;
            else outerHalf++;
        }

        // The inner half-radius covers a quarter of the disc's area, so an even sample puts about a
        // quarter of the particles in it. Sampling the radius instead of its square root piles them
        // into the middle, which would measure crowding instead of a bigger world.
        Assert.True(innerHalf < outerHalf, $"{innerHalf} sparks inside half the radius against {outerHalf} outside: the disc is not evenly filled");
    }

    [Fact]
    public void SpawnRadius_GrowsWithTheSquareRootOfTheCount()
    {
        // sqrt(8000/2000) = 2, so quadrupling the world doubles its radius instead of quadrupling it.
        Assert.Equal(2f, CeilingMix.SpawnRadius(8000) / CeilingMix.SpawnRadius(2000), 3);
    }

    [Fact]
    public void CeilingSpecField_SeedsTheMixedWorldThroughApply()
    {
        ScenarioSpec spec = Scenarios.Find("baseline-idle")! with { CeilingCount = 1000 };
        var (em, player, _) = WorldFactory.Create(spec.Ship, WorldView.DefaultViewportSize, seed: 7);
        ScenarioWorld.Apply(em, player, spec);

        Assert.Equal(CeilingMix.CountsFor(1000).Sparks, Count<DamageSpark>(em));
    }

    static int Count<T>(EntityManager em) where T : notnull =>
        em.TryGetStorage(out ComponentStorage<T> storage) ? storage.Count : 0;
}
