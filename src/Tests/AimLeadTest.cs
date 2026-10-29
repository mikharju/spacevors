using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// The arc gates the *firing solution*, not the target's bearing: a mount aims where its round and
// the target meet, and the round leaves the barrel carrying the hull's own velocity. A shot fired
// with no lead is therefore off by exactly (hull velocity x flight time) — 350 px at 200 px/s of
// drift across a 700 px gap.
public class AimLeadTest
{
    const float TargetRadius = 20f;

    // A slow, unscattered 400 px/s gun in the Fighter's left mount (arc 0 deg +/-22.5 deg), firing at
    // a ship 700 px dead ahead. Hitting it needs a lead of asin(hull drift / 400 px/s).
    [Theory]
    [InlineData(0f)]     // no drift: no lead needed
    [InlineData(100f)]   // 14.5 deg lead, inside the arc — the shot has to land on the hull
    [InlineData(200f)]   // 30 deg lead, outside the arc — hold fire rather than throw 350 px misses
    [InlineData(300f)]   // 48.6 deg lead, far outside the arc
    public void AutoTargetNeverFiresAShotThatIgnoresTheHullsDrift(float hullSpeed)
    {
        float worstMiss = WorstMissAcrossVolley(hullSpeed);

        // 150 px leaves room for a best-effort shot at the arc edge but not for an unled one.
        Assert.True(worstMiss <= 150f, $"Hull at {hullSpeed} px/s fired a shot {worstMiss} px off the target");
    }

    // The same geometry where the lead really is reachable: the shot has to connect.
    [Fact]
    public void ReachableLeadPutsTheShotOnTarget()
    {
        Assert.True(WorstMissAcrossVolley(hullSpeed: 100f) <= 5f, "Shot must run onto a hull the size of a house at 20 px");
    }

    // The arc is about where the shot goes, not where the ship is: one crossing at 45 deg off the
    // nose — well outside the +/-22.5 deg arc — is engaged because its motion carries it into the
    // shot line, and the lead puts the round on it. It only reaches the arc's edge 0.6 s later, so
    // everything this test measures happens while the ship is outside the arc.
    [Fact]
    public void EngagesTargetCrossingIntoTheShotLine()
    {
        var targetPos = new Vector2(500f, -500f);
        var targetVel = new Vector2(-500f, 0f);
        var em = CreateWorld(Vector2.Zero, targetPos, targetVel);
        int hits = 0, shots = 0;

        for (int tick = 0; tick < 30; tick++)
        {
            foreach (var shot in FireAndCollect(em))
            {
                shots++;
                if (ClosestGapToMovingTarget(shot.Spawn, shot.Velocity, targetPos, targetVel, shot.Lifetime) <= TargetRadius) hits++;
            }
        }

        Assert.True(shots > 0, "Mount refused to fire at a ship whose path crosses its shot line");
        Assert.True(hits > 0, $"{shots} shots fired at the crossing ship and none met it");
    }

    private readonly record struct Shot(Vector2 Spawn, Vector2 Velocity, float Lifetime);

    // Strafes right at hullSpeed at a stationary ship dead ahead; the largest distance any shot's
    // world path comes to it across a second of firing (0 when the mount never fired).
    private static float WorstMissAcrossVolley(float hullSpeed)
    {
        var targetPos = new Vector2(0f, -700f);
        var em = CreateWorld(new Vector2(hullSpeed, 0f), targetPos, Vector2.Zero);
        float worst = 0f;

        for (int tick = 0; tick < 120; tick++)
            foreach (var shot in FireAndCollect(em))
                worst = MathF.Max(worst, ClosestGapToMovingTarget(shot.Spawn, shot.Velocity, targetPos, Vector2.Zero, shot.Lifetime));

        return worst;
    }

    // Runs one tick and hands back the shots it spawned, removed from the world so each tick is clean.
    private static List<Shot> FireAndCollect(EntityManager em)
    {
        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 120f, commands);
        commands.Apply(em);

        var shots = new List<Shot>();
        foreach (var (ammoEntity, ammo, ammoPos, ammoVel) in em.GetEntitiesWithComponents<Ammo, Position, Velocity>().ToList())
        {
            shots.Add(new Shot(ammoPos.Value, ammoVel.Value, ammo.Lifetime));
            em.RemoveComponent<Ammo>(ammoEntity);
            em.RemoveComponent<Position>(ammoEntity);
            em.RemoveComponent<Velocity>(ammoEntity);
        }

        return shots;
    }

    // Smallest gap between a shot's straight path and the target's, while the shot is still alive.
    private static float ClosestGapToMovingTarget(Vector2 from, Vector2 velocity, Vector2 targetPos, Vector2 targetVel, float lifetime)
    {
        var relVel = targetVel - velocity;
        var toTarget = targetPos - from;
        float speedSq = relVel.X * relVel.X + relVel.Y * relVel.Y;
        if (speedSq < 0.0001f) return toTarget.Magnitude;

        float at = Math.Clamp(-(toTarget.X * relVel.X + toTarget.Y * relVel.Y) / speedSq, 0f, lifetime);
        return ((targetPos + targetVel * at) - (from + velocity * at)).Magnitude;
    }

    // Hull pinned at the origin, nose up (-Y), moving at hullVel; one enemy ship, gun in SmallLeft.
    private static EntityManager CreateWorld(Vector2 hullVel, Vector2 targetPos, Vector2 targetVel)
    {
        // Scatter-free, kickback-free gun: only the lead solution is under test here.
        var gun = new WeaponType("AimProbeGun", new WeaponStats(FireRate: 2f, AmmoSpeed: 400f, KickbackForce: 0f, PelletCount: 1, Scatter: 0f, ShotLifetime: 3f, Damage: 1), TurretSize.Small, Range: 900f);

        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(hullVel));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 58f, Xp: 0, Level: 1, PickupRadius: 120f, RotationSpeed: 1.2f));

        foreach (var slot in TurretLayouts.Fighter)
            em.AddComponent(em.CreateEntity(), new TurretSlot(slot));
        var view = new WorldView(em);
        TurretMount.TryFindSlot(view, TurretSlotName.SmallLeft, out var slotEntity);
        TurretMount.Mount(em, slotEntity, view.GetComponent<TurretSlot>(slotEntity).Definition, gun, Vector2.Zero, 0f);

        var enemy = em.CreateEntity();
        em.AddComponent(enemy, new Position(targetPos));
        em.AddComponent(enemy, new Velocity(targetVel));
        em.AddComponent(enemy, new Rotation(0f));
        em.AddComponent(enemy, new EnemyShip(Radius: TargetRadius, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, GraphicsId: 0));
        em.AddComponent(enemy, new Rotation(0f));
        return em;
    }
}
