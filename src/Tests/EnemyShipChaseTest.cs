using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class EnemyShipChaseTest
{
    [Fact]
    public void FarEnemyTurnsAndAcceleratesTowardPlayer()
    {
        var em = new EntityManager();

        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(Vector2.Zero));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));

        // Far beyond the old detection range (1200), facing directly away from the player
        var shipEntity = em.CreateEntity();
        em.AddComponent(shipEntity, new Position(new Vector2(0f, 3000f)));
        em.AddComponent(shipEntity, new Velocity(Vector2.Zero));
        em.AddComponent(shipEntity, new Rotation(MathF.PI));
        em.AddComponent(shipEntity, new AngularVelocity(0f));
        em.AddComponent(shipEntity, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, Damage: 3, GraphicsId: 0));

        var view = new WorldView(em);
        float dt = 1f / 60f;
        var commands = new CommandBuffer();
        new EnemyShipSystem().Update(view, dt, commands);
        commands.Apply(em);

        Assert.True(em.TryGetComponent<Acceleration>(shipEntity, out var accel));
        Assert.Equal(45f, accel.Value.Magnitude, precision: 3);
        Assert.Equal(new Vector2(0f, -1f), accel.Value.Normalized);

        // Turns toward the player at turn rate (PI -> PI - dt)
        float angle = em.GetComponent<Rotation>(shipEntity).Angle;
        Assert.Equal(MathF.PI - dt, angle, precision: 5);
    }

    [Fact]
    public void NoPlayer_EnemyShipStopsInsteadOfChasingOrigin()
    {
        var em = new EntityManager();

        // Enemy ship far from origin, but no player exists in the world.
        var shipEntity = em.CreateEntity();
        em.AddComponent(shipEntity, new Position(new Vector2(0f, 3000f)));
        em.AddComponent(shipEntity, new Velocity(Vector2.Zero));
        em.AddComponent(shipEntity, new Rotation(MathF.PI));
        em.AddComponent(shipEntity, new AngularVelocity(0f));
        em.AddComponent(shipEntity, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, Damage: 3, GraphicsId: 0));

        var view = new WorldView(em);
        float dt = 1f / 60f;
        var commands = new CommandBuffer();
        new EnemyShipSystem().Update(view, dt, commands);
        commands.Apply(em);

        // With no player the ship must stop (zero acceleration), not steer toward origin.
        Assert.True(em.TryGetComponent<Acceleration>(shipEntity, out var accel));
        Assert.Equal(Vector2.Zero, accel.Value);
    }
}
