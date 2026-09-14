using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class EnemySpeedScalingTest
{
    [Theory]
    [InlineData(65f, 0f, 65f)]      // stationary player: base speed unchanged
    [InlineData(65f, 50f, 65f)]     // slow player below the factor threshold: base wins
    [InlineData(65f, 100f, 75f)]    // 0.75 x player speed above base
    [InlineData(90f, 200f, 150f)]   // interceptor chasing a fast player
    [InlineData(65f, 400f, 130f)]   // hard ceiling: 2x base caps the factor
    [InlineData(50f, 400f, 100f)]   // heavy cannon ceiling at 2x its slow base
    public void EffectiveSpeed_ScalesWithPlayerSpeed_CappedAtTwiceBase(float baseSpeed, float playerSpeed, float expected)
    {
        Assert.Equal(expected, EnemyShipSystem.EffectiveSpeed(baseSpeed, playerSpeed), precision: 3);
    }

    [Fact]
    public void ShipAboveBaseButBelowEffectiveCap_ChasesInsteadOfDriftCanceling()
    {
        var em = new EntityManager();

        // Fast-moving player (300 px/s) raises the cap of a base-65 ship to min(225, 130) = 130.
        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(Vector2.Zero));
        em.AddComponent(playerEntity, new Velocity(new Vector2(300f, 0f)));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));

        // Ship directly above the player, facing and moving toward it at 90 px/s —
        // above its base cap (65) but below the effective cap (130).
        var shipEntity = em.CreateEntity();
        em.AddComponent(shipEntity, new Position(new Vector2(0f, 1000f)));
        em.AddComponent(shipEntity, new Velocity(new Vector2(0f, -90f)));
        em.AddComponent(shipEntity, new Rotation(MathF.PI));
        em.AddComponent(shipEntity, new AngularVelocity(0f));
        em.AddComponent(shipEntity, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, Damage: 3, GraphicsId: 0));

        var view = new WorldView(em);
        float dt = 1f / 60f;
        var system = new EnemyShipSystem();

        // Two frames: the first sets the chase acceleration, the second clamps velocity against the effective cap.
        for (int i = 0; i < 2; i++)
        {
            var commands = new CommandBuffer();
            system.Update(view, dt, commands);
            commands.Apply(em);
        }

        // Chasing (full acceleration toward the player), not drift-canceling.
        Assert.True(em.TryGetComponent<Acceleration>(shipEntity, out var accel));
        Assert.Equal(45f, accel.Value.Magnitude, precision: 3);
        Assert.Equal(new Vector2(0f, -1f), accel.Value.Normalized);

        // Velocity keeps growing toward the raised cap instead of being braked to base.
        var vel = em.GetComponent<Velocity>(shipEntity).Value;
        Assert.True(vel.Magnitude > 90f, $"velocity {vel} should accelerate past its current speed");
    }
}
