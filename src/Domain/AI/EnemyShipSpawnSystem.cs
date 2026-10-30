using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public class EnemyShipSpawnSystem : GameSystem
{
    // Which wave's quota is being spent and how much of it is left. Derived state: the wave index
    // always follows view.ElapsedTime through WaveSchedule, never an independent clock.
    private int _waveIndex;
    private int _quotaLeft;
    private float _timer;

    public static float SpeedFactor(float playerSpeed) => Math.Clamp(playerSpeed / SpawningStats.EnemyShips.ReferenceSpeed, 1f, SpawningStats.EnemyShips.MaxSpeedFactor);

    // Random interval between spawns within a wave. Pure: rng is the only input.
    public static float NextInterval(float playerSpeed, Random rng)
    {
        float interval = WaveStats.InWaveMinInterval
            + (float)rng.NextDouble() * (WaveStats.InWaveMaxInterval - WaveStats.InWaveMinInterval);
        return interval / SpeedFactor(playerSpeed);
    }

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var rng = view.Rng;

        // Joined on Position so the player's post-death husk (physical components stripped) is skipped:
        // spawning a fresh wave around a corpse teaches nothing anyway.
        bool hasPlayer = view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;

        if (!hasPlayer) return;

        // Lulls stop spawning. The schedule is pure arithmetic over elapsed time, so no system
        // needs to be told about phase changes (plans/WAVES_AND_BREATHERS.md).
        var phase = WaveSchedule.PhaseFor(view.ElapsedTime);
        if (!phase.InWave) return;

        if (phase.WaveIndex != _waveIndex)
        {
            _waveIndex = phase.WaveIndex;
            _quotaLeft = WaveSchedule.WaveSize(phase.WaveIndex);
            _timer = 0f;
        }

        if (_quotaLeft <= 0) return;

        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeShips = view.GetEntitiesWithComponents<EnemyShip>().Count();
        if (activeShips >= SpawningStats.EnemyShips.MaxEnemyShips) return;

        var playerPos = view.GetComponent<Position>(playerEntity);
        view.TryGetComponent<Velocity>(playerEntity, out var playerVelComp);
        Vector2 playerVel = playerVelComp.Value;

        float velMagnitude = playerVel.Magnitude;
        if (velMagnitude < SpawnPlacement.MinDirectionalSpeed) return;

        Vector2 spawnDir = SpawnPlacement.ForwardDirection(playerVel / velMagnitude, rng);
        Vector2 testSpawnPos = SpawnPlacement.OutsideScreen(playerPos.Value, view.ViewportSize, spawnDir);

        if (!IsSpawnClear(view, testSpawnPos)) return;

        var (enemyShipType, armament) = EnemyShipFactory.PickRandomType(rng, EnemyShipStats.CompositionTierFor(view.ElapsedTime));
        Vector2 initialVel = playerVel * SpawningStats.EnemyShips.FollowFactor + (playerPos.Value - testSpawnPos).Normalized * SpawnPlacement.DriftSpeed;
        float facingAngle = SpawnPlacement.AngleFromTo(testSpawnPos, playerPos.Value);

        // Tier stats are derived from elapsed time at spawn (plans/DIFFICULTY_SCALING.md P5).
        IInitialComponent[] components = EnemyShipFactory.CreateComponents(testSpawnPos, initialVel, facingAngle, 0f, enemyShipType, armament, view.ElapsedTime);

        commands.AddEntity(components);

        _quotaLeft--;
        _timer = NextInterval(velMagnitude, rng);
    }

    private bool IsSpawnClear(WorldView view, Vector2 spawnPos)
    {
        foreach (var (shipEntity, ship, pos) in view.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            float dx = pos.Value.X - spawnPos.X;
            float dy = pos.Value.Y - spawnPos.Y;
            float distSq = dx * dx + dy * dy;
            float minDist = ship.Radius + SpawningStats.EnemyShips.MinSpawnDistance;
            if (distSq < minDist * minDist) return false;
        }
        return true;
    }
}
