using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// One read of how much threat is on screen right now (plans/DIFFICULTY_SCALING.md R2.8). Read-only and
// taken about once a second, never from the hot path. The difficulty knobs are tuned against this row
// instead of against a feeling, so what the fleet throws and what the player can answer sit together.
public readonly record struct ThreatSnapshot(
    float Elapsed,
    int Tier,
    // Which rung of the armament ladder the clock has reached (EnemyShipStats.CompositionTierFor):
    // the stat tier above says how hard individuals are, this says what appears.
    int CompositionTier,
    int Ships,
    // Ships standing within their own gun range of the hull: the ones that can hit it right now.
    int ShipsInRing,
    // Their summed fire rate × pellets × damage per second, before any of it misses.
    float IncomingDps,
    int EnemyRounds,
    // Guided rounds the enemy has in the air, against what the ladder allows (plans/MISSILES.md R22):
    // the pair says whether late pressure is arriving as mixing or as volume.
    int EnemyMissiles,
    int MissileCap,
    // The same sum for the guns mounted on the player's hull.
    float PlayerDps,
    int Health,
    int MaxHealth,
    int Level,
    int Mines,
    int SmallRocks);

public static class ThreatMeter
{
    public static ThreatSnapshot Measure(WorldView view)
    {
        if (!view.GetEntitiesWithComponents<Player, Position, Health>().TryFirst(out var playerTuple))
            return default;

        var player = playerTuple.Value1;
        var playerPos = playerTuple.Value2.Value;
        var health = playerTuple.Value3;

        int ships = 0, shipsInRing = 0, enemyRounds = 0, enemyMissiles = 0, mines = 0, smallRocks = 0;
        float incomingDps = 0f, playerDps = 0f;

        foreach (var (entity, ship, pos) in view.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            // A wreck still carries its gun while its death animation plays.
            if (view.HasComponent<Dead>(entity)) continue;

            ships++;

            // The ring is the hull's own firing range, which its gun is built from; a rack reaches
            // farther than the guns and is counted as the hull's reach too. Read off the hull rather
            // than off a `Turret`, because a missile-only hull has no gun to look at.
            float reach = ship.FiringRange + player.Radius;
            if (view.TryGetComponent<MissileWeapon>(entity, out var rack)) reach = Math.Max(reach, rack.Range + player.Radius);

            var diff = pos.Value - playerPos;
            if (diff.X * diff.X + diff.Y * diff.Y > reach * reach) continue;

            shipsInRing++;
            if (view.TryGetComponent<Turret>(entity, out var turret))
                incomingDps += turret.Weapon.FireRate * turret.Weapon.PelletCount * turret.Weapon.Damage;
        }

        foreach (var (_, ammo) in view.GetEntitiesWithComponents<Ammo>())
            if (ammo.IsEnemy) enemyRounds++;

        foreach (var (_, _, ammo) in view.GetEntitiesWithComponents<Missile, Ammo>())
            if (ammo.IsEnemy) enemyMissiles++;

        // A player turret lives on a hull slot, so requiring the slot filters out the enemy guns.
        foreach (var (_, turret, _) in view.GetEntitiesWithComponents<Turret, TurretSlot>())
            if (!turret.IsEnemy)
                playerDps += turret.Weapon.FireRate * turret.Weapon.PelletCount * turret.Weapon.Damage;

        foreach (var (entity, _) in view.GetEntitiesWithComponents<EnemyMine>())
        {
            if (view.HasComponent<Dead>(entity)) continue;
            mines++;
        }

        foreach (var (_, asteroid) in view.GetEntitiesWithComponents<Asteroid>())
            if (asteroid.IsSmall) smallRocks++;

        int compositionTier = EnemyShipStats.CompositionTierFor(view.ElapsedTime);

        return new ThreatSnapshot(
            view.ElapsedTime,
            EnemyShipStats.TierFor(view.ElapsedTime),
            compositionTier,
            ships,
            shipsInRing,
            incomingDps,
            enemyRounds,
            enemyMissiles,
            EnemyShipStats.MissileCapFor(compositionTier),
            playerDps,
            health.Current,
            health.Max,
            player.Level,
            mines,
            smallRocks);
    }
}
