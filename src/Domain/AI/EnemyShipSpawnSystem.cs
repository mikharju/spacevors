using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public class EnemyShipSpawnSystem : GameSystem
{
    private float _timer = SpawningStats.EnemyShips.InitialDelay;

    public static float SpeedFactor(float playerSpeed) => Math.Clamp(playerSpeed / SpawningStats.EnemyShips.ReferenceSpeed, 1f, SpawningStats.EnemyShips.MaxSpeedFactor);

    // Random spawn interval for the given elapsed time and player speed. Pure: rng is the only input.
    public static float NextInterval(float elapsedTime, float playerSpeed, Random rng)
    {
        float progress = MathF.Min(elapsedTime / SpawningStats.EnemyShips.RampDuration, 1f);
        float minInterval = SpawningStats.EnemyShips.MinInterval + (SpawningStats.EnemyShips.StartMinInterval - SpawningStats.EnemyShips.MinInterval) * (1f - progress);
        float maxInterval = SpawningStats.EnemyShips.MaxInterval + (SpawningStats.EnemyShips.StartMaxInterval - SpawningStats.EnemyShips.MaxInterval) * (1f - progress);
        return (minInterval + (float)rng.NextDouble() * (maxInterval - minInterval)) / SpeedFactor(playerSpeed);
    }

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var rng = view.Rng;

        bool hasPlayer = view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;

        if (!hasPlayer) return;

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

        var enemyShipType = EnemyShipFactory.PickRandomType(rng);
        Vector2 initialVel = playerVel * SpawningStats.EnemyShips.FollowFactor + (playerPos.Value - testSpawnPos).Normalized * SpawnPlacement.DriftSpeed;
        float facingAngle = SpawnPlacement.AngleFromTo(testSpawnPos, playerPos.Value);

        // Tier stats are derived from elapsed time at spawn (plans/DIFFICULTY_SCALING.md P5).
        IInitialComponent[] components = EnemyShipFactory.CreateComponents(testSpawnPos, initialVel, facingAngle, 0f, enemyShipType, view.ElapsedTime);

        commands.AddEntity(components);

        _timer = NextInterval(view.ElapsedTime, velMagnitude, rng);
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
