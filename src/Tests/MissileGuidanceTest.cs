using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// Guidance is the whole of what makes a missile a missile: it rotates the round's velocity toward
// its lock, at a rate the target can survive, and never touches anything else.
public class MissileGuidanceTest
{
    const float Dt = 1f / 60f;

    static (EntityManager Em, Entity Missile) MissileWorld(
        Vector2 start, Vector2 velocity, Entity lockedTarget, float turnRateDeg, out Entity target)
    {
        var em = new EntityManager();
        float speed = velocity.Magnitude;

        target = em.CreateEntity();
        em.AddComponent(target, new Position(new Vector2(0f, -400f)));
        em.AddComponent(target, new Velocity(Vector2.Zero));
        em.AddComponent(target, new EnemyShip(Radius: 20f, Speed: 0f, TurnRate: 0f, FiringRange: 0f, TurretFireRate: 0f, TurretAmmoSpeed: 0f, Acceleration: 0f, GraphicsId: 0));
        em.AddComponent(target, new Health(3, 3)); // one 3-damage missile kill ends it

        var missile = em.CreateEntity();
        em.AddComponent(missile, new Position(start));
        em.AddComponent(missile, new Velocity(velocity));
        em.AddComponent(missile, new Ammo(velocity, Radius: 4f, Lifetime: 10f, IsEnemy: false, Damage: 3));
        em.AddComponent(missile, new Missile(LockedTarget: target, Speed: speed, TurnRate: Angle.FromDeg(turnRateDeg), Steering: 0f));

        return (em, missile);
    }

    // A missile launched broadside to its lock turns onto it and still kills it: the turn limit costs
    // it travel time, not the hit.
    [Fact]
    public void LockedMissileTurnsOntoItsTargetAndKillsIt()
    {
        var (em, missile) = MissileWorld(new Vector2(0f, 0f), new Vector2(-200f, 0f), lockedTarget: default, turnRateDeg: 90f, out var target);
        var view = new WorldView(em);
        var guidance = new MissileGuidanceSystem();
        var collision = new CollisionSystem();
        var physics = new PhysicsSystem();
        var integrate = new PositionIntegrationSystem();
        var lifetime = new AmmoLifetimeSystem();

        bool shipAlive = true, missileAlive = true;
        for (int tick = 0; tick < 600 && shipAlive && missileAlive; tick++)
        {
            var commands = new CommandBuffer();
            guidance.Update(view, Dt, commands);
            physics.Update(view, Dt, commands);
            integrate.Update(view, Dt, commands);
            lifetime.Update(view, Dt, commands);
            commands.Apply(em);

            commands = new CommandBuffer();
            collision.Update(view, Dt, commands);
            commands.Apply(em);

            // A hit consumes the round, and CollisionSystem marks a killed hull Dead rather than
            // destroying it (ShipDeathExplosionSystem does), so both are results here, not failures.
            shipAlive = !em.HasComponent<Dead>(target);
            missileAlive = em.HasComponent<Ammo>(missile);
        }

        Assert.True(em.HasComponent<Dead>(target), "the locked ship survived a missile that was hunting it");
    }

    // The turn limit is the whole reason a missile is dodgeable: one tick may never swing the heading
    // by more than TurnRate × dt.
    [Fact]
    public void HeadingTurnsAtMostByItsTurnRatePerTick()
    {
        var (em, missile) = MissileWorld(new Vector2(0f, 0f), new Vector2(-200f, 0f), lockedTarget: default, turnRateDeg: 60f, out _);
        var view = new WorldView(em);

        float before = Angle.Of(em.GetComponent<Velocity>(missile).Value);

        var commands = new CommandBuffer();
        new MissileGuidanceSystem().Update(view, Dt, commands);
        commands.Apply(em);

        float after = Angle.Of(em.GetComponent<Velocity>(missile).Value);
        Assert.True(MathF.Abs(Angle.Delta(before, after)) <= Angle.FromDeg(60f) * Dt + 1e-4f,
            $"one tick turned the heading {Angle.ToDeg(MathF.Abs(Angle.Delta(before, after))):0.00}°");
    }

    // Guidance rotates; it never thrusts. Speed is set at launch (weapon speed × the spawn roll × any
    // ProjectileSpeed upgrade) and stays there — that is what keeps the reach rule on the card honest.
    [Fact]
    public void GuidanceKeepsTheLaunchSpeedAndNeverAddsAcceleration()
    {
        var (em, missile) = MissileWorld(new Vector2(0f, 0f), new Vector2(-200f, 0f), lockedTarget: default, turnRateDeg: 90f, out _);
        var view = new WorldView(em);

        for (int tick = 0; tick < 60; tick++)
        {
            var commands = new CommandBuffer();
            new MissileGuidanceSystem().Update(view, Dt, commands);
            commands.Apply(em);
        }

        Assert.Equal(200f, em.GetComponent<Velocity>(missile).Value.Magnitude, precision: 3);
        Assert.False(em.HasComponent<Acceleration>(missile), "a missile must not be integrated by forces");
    }

    // The lock is an entity id, and ids are never reused, so a dead target is simply a lookup that
    // fails: the round stops steering and lets its fuse end it.
    [Fact]
    public void ADeadLockStopsGuidanceAndTheFuseEndsTheRound()
    {
        var (em, missile) = MissileWorld(new Vector2(0f, 0f), new Vector2(0f, -200f), lockedTarget: default, turnRateDeg: 90f, out var target);
        var view = new WorldView(em);
        em.DestroyEntity(target);
        var original = em.GetComponent<Velocity>(missile).Value;

        var guidance = new MissileGuidanceSystem();
        var lifetime = new AmmoLifetimeSystem();
        for (int tick = 0; tick < 40; tick++)
        {
            var commands = new CommandBuffer();
            guidance.Update(view, Dt, commands);
            commands.Apply(em);
            commands = new CommandBuffer();
            lifetime.Update(view, Dt, commands);
            commands.Apply(em);
        }

        Assert.Equal(original, em.GetComponent<Velocity>(missile).Value);
        Assert.True(em.GetComponent<Ammo>(missile).Lifetime < 10f - 40 * Dt + 1e-4f, "the fuse never ran down");
    }

    // The exhaust is drawn from Steering, so it has to read hard-while-turning and 0 once the round
    // is dead on its lock.
    [Fact]
    public void SteeringReadsTheTurnTheRoundActuallyApplied()
    {
        var (em, missile) = MissileWorld(new Vector2(0f, 0f), new Vector2(-200f, 0f), lockedTarget: default, turnRateDeg: 90f, out _);
        var view = new WorldView(em);

        var commands = new CommandBuffer();
        new MissileGuidanceSystem().Update(view, Dt, commands);
        commands.Apply(em);
        Assert.Equal(1f, em.GetComponent<Missile>(missile).Steering, precision: 3);

        // Straight at the lock: nothing to correct, so nothing to show.
        em.AddComponent(missile, new Velocity(new Vector2(0f, -200f)));
        commands = new CommandBuffer();
        new MissileGuidanceSystem().Update(view, Dt, commands);
        commands.Apply(em);
        Assert.Equal(0f, em.GetComponent<Missile>(missile).Steering, precision: 3);
    }

    // A homing gun locks what it acquired instead of solving an intercept for it: the round steers,
    // so leading it would only aim at where the target was going to be.
    [Fact]
    public void HomingGunLocksItsTargetAndAimsAtWhereItIs()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 46f, Xp: 0, Level: 1, PickupRadius: 120f, RotationSpeed: 1.33f));

        // Crossing at 300 px/s: a lead solution would aim well ahead of it.
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(new Vector2(0f, -600f)));
        em.AddComponent(ship, new Velocity(new Vector2(300f, 0f)));
        em.AddComponent(ship, new EnemyShip(Radius: 20f, Speed: 0f, TurnRate: 0f, FiringRange: 700f, TurretFireRate: 1f, TurretAmmoSpeed: 200f, Acceleration: 0f, GraphicsId: 0));
        em.AddComponent(ship, new Health(10, 10));

        var turret = em.CreateEntity();
        em.AddComponent(turret, new Position(Vector2.Zero));
        em.AddComponent(turret, new Rotation(0f));
        em.AddComponent(turret, new Turret(
            Weapon: new WeaponStats(FireRate: 1f, AmmoSpeed: 300f, KickbackForce: 0f, PelletCount: 1, Scatter: 0f, ShotLifetime: 4f, Damage: 3, Homing: true, HomingTurnRate: Angle.FromDeg(150f)),
            WeaponName: "SeekerPod", ArcAngle: MathF.PI / 3f, Range: 900f, AutoTarget: true));

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(view, Dt, commands);
        commands.Apply(em);

        var fired = Assert.Single(em.GetEntitiesWithComponents<Missile>().ToList());
        var missile = em.GetComponent<Missile>(fired.Entity);
        Assert.Equal(ship, missile.LockedTarget);
        Assert.InRange(missile.Speed, 300f * (1f - TurretFiringSystem.AmmoSpeedVariation / 2f), 300f * (1f + TurretFiringSystem.AmmoSpeedVariation / 2f));

        // Not lead: the round leaves aimed at the ship, within the missile's own scatter-free bearing.
        Vector2 launched = em.GetComponent<Velocity>(fired.Entity).Value;
        Assert.True(MathF.Abs(Angle.Delta(Angle.Of(launched), Angle.Of(new Vector2(0f, -600f)))) <= Angle.FromDeg(8f),
            $"the round left {Angle.ToDeg(Angle.Delta(Angle.Of(launched), Angle.Of(new Vector2(0f, -600f)))):0.0}° off the target's own bearing");
    }
}
