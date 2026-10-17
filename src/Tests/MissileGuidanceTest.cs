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
        Vector2 start, Vector2 velocity, Entity lockedTarget, float turnRateDeg, out Entity target, float topSpeed = 0f)
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
        em.AddComponent(missile, new Missile(
            LockedTarget: target,
            Speed: speed,
            MaxSpeed: topSpeed > 0f ? topSpeed : speed,
            Acceleration: WeaponStats.MissileLaunchAcceleration,
            TurnRate: Angle.FromDeg(turnRateDeg),
            Steering: 0f));

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

    // Guidance turns, and it builds the round's speed to the speed the gun named — then stops. The ramp is
    // written at the tick boundary rather than as an `Acceleration` component, so the collision pass still
    // reads one constant velocity between ticks and the turn radius stays a single number.
    [Fact]
    public void ARoundBuildsToItsTopSpeedAndStopsThere()
    {
        const float topSpeed = 420f;
        var (em, missile) = MissileWorld(new Vector2(0f, 0f), new Vector2(-200f, 0f), lockedTarget: default, turnRateDeg: 90f, out _, topSpeed: topSpeed);
        var view = new WorldView(em);
        var guidance = new MissileGuidanceSystem();

        float previous = em.GetComponent<Missile>(missile).Speed;
        for (int tick = 0; tick < 120; tick++)
        {
            var commands = new CommandBuffer();
            guidance.Update(view, Dt, commands);
            commands.Apply(em);

            float speed = em.GetComponent<Velocity>(missile).Value.Magnitude;
            Assert.True(speed >= previous - 1e-4f, "a guided round slowed down mid-flight");
            previous = speed;
        }

        Assert.Equal(topSpeed, previous, precision: 2);
        Assert.Equal(topSpeed, em.GetComponent<Missile>(missile).Speed, precision: 2);
        Assert.False(em.HasComponent<Acceleration>(missile), "a missile must not be integrated by forces");
    }

    // A round with no ceiling above its launch speed keeps it: the ramp belongs to the round, and a test
    // world that says "top speed = launch speed" must stay a straight-line world.
    [Fact]
    public void ARoundAtItsCeilingStaysAtIt()
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

        // A guided round's speed is not rolled: it leaves at the launch fraction of the card speed, and the
        // card speed is the ceiling it builds to.
        Assert.Equal(300f, missile.MaxSpeed, precision: 3);
        Assert.Equal(300f * WeaponStats.MissileLaunchFraction, missile.Speed, precision: 3);

        // Not lead: this target sits dead ahead, so the nose the round left along and the bearing to the
        // ship are the same line.
        Vector2 launched = em.GetComponent<Velocity>(fired.Entity).Value;
        Assert.True(MathF.Abs(Angle.Delta(Angle.Of(launched), Angle.Of(new Vector2(0f, -600f)))) <= Angle.FromDeg(1f),
            $"the round left {Angle.ToDeg(Angle.Delta(Angle.Of(launched), Angle.Of(new Vector2(0f, -600f)))):0.0}° off the target's own bearing");
    }

    // A rack does not aim, and its round is not gated by the slot's arc: it leaves along the hull's nose
    // and steers from there. That is what lets a mount that points forward cover what is behind the hull —
    // and it makes the round fly the whole way around, which is the price of the exemption.
    [Fact]
    public void ARackFiresAlongItsNoseAtATargetOutsideItsArc()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 46f, Xp: 0, Level: 1, PickupRadius: 120f, RotationSpeed: 1.33f));

        foreach (var slot in TurretLayouts.Fighter)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));
        var mounts = new WorldView(em);
        TurretMount.TryFindSlot(mounts, TurretSlotName.SmallLeft, out var slotEntity);
        TurretMount.Mount(em, slotEntity, em.GetComponent<TurretSlot>(slotEntity).Definition, WeaponType.SeekerPod, Vector2.Zero, 0f);

        // Directly behind the hull, well inside the pod's 900 px ring. The Fighter's small side mounts
        // cover 45° off the nose, so this bearing is nowhere near the slot's arc.
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(new Vector2(0f, 500f)));
        em.AddComponent(ship, new Velocity(Vector2.Zero));
        em.AddComponent(ship, new EnemyShip(Radius: 20f, Speed: 0f, TurnRate: 0f, FiringRange: 700f, TurretFireRate: 1f, TurretAmmoSpeed: 200f, Acceleration: 0f, GraphicsId: 0));
        em.AddComponent(ship, new Health(10, 10));

        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(view, Dt, commands);
        commands.Apply(em);

        var fired = Assert.Single(em.GetEntitiesWithComponents<Missile>().ToList());
        Assert.Equal(ship, em.GetComponent<Missile>(fired.Entity).LockedTarget);

        // It left along the nose (an angle of 0 faces up the screen), not at the target: guidance picks the
        // lock up from there, one tick at a time.
        Vector2 launched = em.GetComponent<Velocity>(fired.Entity).Value;
        Assert.True(MathF.Abs(Angle.Delta(Angle.Of(launched), 0f)) <= Angle.FromDeg(1f),
            $"the round left {Angle.ToDeg(Angle.Delta(Angle.Of(launched), 0f)):0.0}° off the hull's nose");
    }
}
