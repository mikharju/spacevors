using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class PickupMagnetTest
{
    [Fact]
    public void HealthOrb_LifetimeDecaysAndExpires()
    {
        var em = new EntityManager();

        // Player far away: no magnet pull, no collection.
        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(new Vector2(0f, 1000f)));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));

        var orbEntity = em.CreateEntity();
        em.AddComponent(orbEntity, new Position(Vector2.Zero));
        em.AddComponent(orbEntity, new HealthOrb(Lifetime: 0.1f, Radius: 8f));

        var view = new WorldView(em);
        float dt = 1f / 60f;
        var system = new PickupMagnetSystem();

        // Frame 1: orb survives with its lifetime aged by one step.
        var commands = new CommandBuffer();
        system.Update(view, dt, commands);
        commands.Apply(em);
        Assert.Equal(0.1f - dt, em.GetComponent<HealthOrb>(orbEntity).Lifetime, precision: 5);

        // Subsequent frames: lifetime keeps decaying until the orb is destroyed.
        bool destroyed = false;
        for (int frame = 0; frame < 20 && !destroyed; frame++)
        {
            commands = new CommandBuffer();
            system.Update(view, dt, commands);
            commands.Apply(em);
            destroyed = !em.HasComponent<HealthOrb>(orbEntity);
        }

        Assert.True(destroyed, "health orb should expire after its lifetime elapses");
    }
}
