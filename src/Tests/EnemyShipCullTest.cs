using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class EnemyShipCullTest
{
    [Fact]
    public void ShipBeyondCullDistance_IsDestroyed()
    {
        var em = new EntityManager();
        AddPlayer(em);

        var shipEntity = em.CreateEntity();
        AddEnemyShip(em, shipEntity, new Vector2(0f, EnemyShipSystem.CullDistance + 500f));

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new EnemyShipSystem().Update(view, 1f / 60f, commands);
        commands.Apply(em);

        Assert.False(em.TryGetComponent<EnemyShip>(shipEntity, out _), "ship beyond cull distance should be destroyed");
    }

    [Fact]
    public void ShipWithinCullDistance_IsKeptAndChases()
    {
        var em = new EntityManager();
        AddPlayer(em);

        var shipEntity = em.CreateEntity();
        AddEnemyShip(em, shipEntity, new Vector2(0f, 3000f));

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new EnemyShipSystem().Update(view, 1f / 60f, commands);
        commands.Apply(em);

        Assert.True(em.TryGetComponent<EnemyShip>(shipEntity, out _), "ship within cull distance should survive");
        Assert.True(em.TryGetComponent<Acceleration>(shipEntity, out var accel));
        Assert.Equal(new Vector2(0f, -1f), accel.Value.Normalized);
    }

    private static void AddPlayer(EntityManager em)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
    }

    private static void AddEnemyShip(EntityManager em, Entity entity, Vector2 position)
    {
        em.AddComponent(entity, new Position(position));
        em.AddComponent(entity, new Velocity(Vector2.Zero));
        em.AddComponent(entity, new Rotation(0f));
        em.AddComponent(entity, new AngularVelocity(0f));
        em.AddComponent(entity, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, Damage: 3, GraphicsId: 0));
    }
}
