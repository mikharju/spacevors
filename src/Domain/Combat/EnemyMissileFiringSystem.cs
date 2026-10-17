using System.Diagnostics;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Enemy racks launch here rather than through TurretFiringSystem: a hull may carry guns, a rack, or
// both, each on its own cooldown, and a guided round needs no lead solution — it steers. It leaves from
// a ring 1.4× the hull's gun range, so arming a hull changes what it throws and nothing about how it
// flies (plans/MISSILES.md §9).
public class EnemyMissileFiringSystem : GameSystem
{
    // Squared distance below which positions are treated as coincident.
    private const float Epsilon = 0.001f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        if (!view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple)) return;
        Entity playerEntity = playerTuple.Entity;
        var player = view.GetComponent<Player>(playerEntity);
        var playerPos = view.GetComponent<Position>(playerEntity);

        int cap = EnemyShipStats.MissileCapFor(EnemyShipStats.CompositionTierFor(view.ElapsedTime));
        int inFlight = CountEnemyMissiles(view);
        int launched = 0;

        foreach (var (hullEntity, rack, cooldown, pos, rotation) in view.GetEntitiesWithComponents<MissileWeapon, MissileCooldown, Position, Rotation>())
        {
            // A rack that is ready stays ready: it launches the moment the world-wide cap frees a slot
            // instead of waiting out another reload.
            float timer = cooldown.Timer - deltaTime;
            if (timer > 0f || inFlight >= cap)
            {
                commands.Add(new AddComponentCommand<MissileCooldown>(hullEntity, new MissileCooldown(MathF.Max(timer, 0f))));
                continue;
            }

            Vector2 hullForward = Angle.Direction(rotation.Angle);
            if (!TryAim(rack, pos.Value, hullForward, player.Radius, playerPos.Value, out Vector2 aimDir))
            {
                // Outside the ring or the cone: hold the round loaded and fire as soon as the player is
                // in range, rather than losing a reload to the wait.
                commands.Add(new AddComponentCommand<MissileCooldown>(hullEntity, new MissileCooldown(0f)));
                continue;
            }

            float hullRadius = view.TryGetComponent<EnemyShip>(hullEntity, out var ship) ? ship.Radius : 0f;
            view.TryGetComponent<Velocity>(hullEntity, out var hullVelocity);
            LaunchRound(commands, pos.Value, aimDir, hullForward, hullRadius, hullVelocity.Value, rack, playerEntity);
            commands.Add(new AddComponentCommand<MissileCooldown>(hullEntity, new MissileCooldown(1f / rack.Weapon.FireRate)));
            inFlight++;
            launched++;
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Enemy rack: launch", sw.ElapsedTicks, launched);
    }

    // The gun pass's own reach and cone, widened by the target's hull radius: only the distance is the
    // rack's, because the launch ring is the grown gun range × 1.4.
    private static bool TryAim(MissileWeapon rack, Vector2 hullPos, Vector2 hullForward, float targetRadius, Vector2 targetPos, out Vector2 aimDir)
    {
        aimDir = default;

        Vector2 relPos = targetPos - hullPos;
        float distSq = relPos.X * relPos.X + relPos.Y * relPos.Y;
        float reach = rack.Range + targetRadius;
        if (distSq < Epsilon || distSq > reach * reach) return false;

        float dist = MathF.Sqrt(distSq);
        if (!FiringArc.Engulfs(hullForward, relPos / dist, EnemyShipStats.WeaponArcAngle / 2f, targetRadius, dist)) return false;

        aimDir = relPos / dist;
        return true;
    }

    // The same launch plumbing as the gun pass — spawn offset, clear of the hull, the hull's inherited
    // velocity — and the same Red the enemy's bullets are, because the colour is the threat channel and
    // the shape of the round is what says "guided". The two things deliberately not shared are the gun
    // pass's speed jitter (on a guided round the speed *is* the turn radius and the fuse reach, so a
    // jittered round would be a different missile than the one the player just read) and its full launch
    // speed: a rack throws its round at MissileLaunchFraction of the card speed and the round builds to
    // that speed in flight, which is why the plume is long just after a launch.
    private static void LaunchRound(CommandBuffer commands, Vector2 hullPos, Vector2 aimDir, Vector2 hullForward,
        float hullRadius, Vector2 hullVelocity, MissileWeapon rack, Entity playerEntity)
    {
        var weapon = rack.Weapon;
        float topSpeed = weapon.AmmoSpeed;
        float launchSpeed = topSpeed * WeaponStats.MissileLaunchFraction;
        Vector2 spawnPos = TurretFiringSystem.EnemySpawnPosition(hullPos, aimDir, hullForward, hullRadius);
        Vector2 roundVelocity = aimDir * launchSpeed + hullVelocity;

        commands.AddEntity(
            new Position(spawnPos),
            new Velocity(roundVelocity),
            new Ammo(roundVelocity, EnemyShipStats.MissileAmmoRadius, weapon.ShotLifetime, IsEnemy: true, weapon.Damage, AmmoColor.Red),
            new Missile(playerEntity, launchSpeed, topSpeed, WeaponStats.MissileLaunchAcceleration, weapon.HomingTurnRate, Steering: 0f),
            new Health(weapon.MissileHealth, weapon.MissileHealth));
    }

    // The cap is counted live instead of tracked in a component: a handful of entities to walk per tick,
    // and nothing that can desync when a missile is shot down, finds its target or runs out of fuse.
    private static int CountEnemyMissiles(WorldView view)
    {
        int count = 0;
        foreach (var (_, _, ammo) in view.GetEntitiesWithComponents<Missile, Ammo>())
            if (ammo.IsEnemy) count++;

        return count;
    }
}
