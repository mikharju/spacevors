using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EnemyShipSpawnSystem : GameSystem
{
    private float _timer = InitialDelay;
    private const float InitialDelay = 5f;
    private const float MinInterval = 2f;
    private const float MaxInterval = 4f;
    private const int MaxEnemyShips = 100;
    private const float MinSpawnDistance = 300f;
    public const float FollowFactor = 0.5f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var rng = view.Rng;

        bool hasPlayer = view.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;

        if (!hasPlayer) return;

        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeShips = view.GetEntitiesWithComponents<EnemyShip>().Count();
        if (activeShips >= MaxEnemyShips) return;

        var playerPos = view.GetComponent<Position>(playerEntity);
        view.TryGetComponent<Velocity>(playerEntity, out var playerVelComp);
        Vector2 playerVel = playerVelComp.Value;

        float velMagnitude = playerVel.Magnitude;
        if (velMagnitude < SpawnPlacement.MinDirectionalSpeed) return;

        Vector2 spawnDir = SpawnPlacement.ForwardDirection(playerVel / velMagnitude, rng);
        Vector2 testSpawnPos = SpawnPlacement.OutsideScreen(playerPos.Value, view.ViewportSize, spawnDir);

        if (!IsSpawnClear(view, testSpawnPos)) return;

        var enemyShipType = EnemyShipFactory.PickRandomType(rng);
        Vector2 initialVel = playerVel * FollowFactor + (playerPos.Value - testSpawnPos).Normalized * SpawnPlacement.DriftSpeed;
        float facingAngle = SpawnPlacement.AngleFromTo(testSpawnPos, playerPos.Value);

        IInitialComponent[] components = EnemyShipFactory.CreateComponents(testSpawnPos, initialVel, facingAngle, 0f, enemyShipType);

        commands.AddEntity(components);

        float elapsed = view.ElapsedTime;
        float rampDuration = 180f;
        float progress = MathF.Min(elapsed / rampDuration, 1f);
        float currentMinInterval = MinInterval + (5f - MinInterval) * (1f - progress);
        float currentMaxInterval = MaxInterval + (10f - MaxInterval) * (1f - progress);

        _timer = currentMinInterval + (float)rng.NextDouble() * (currentMaxInterval - currentMinInterval);
    }

    private bool IsSpawnClear(WorldView view, Vector2 spawnPos)
    {
        foreach (var (shipEntity, ship, pos) in view.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            float dx = pos.Value.X - spawnPos.X;
            float dy = pos.Value.Y - spawnPos.Y;
            float distSq = dx * dx + dy * dy;
            float minDist = ship.Radius + MinSpawnDistance;
            if (distSq < minDist * minDist) return false;
        }
        return true;
    }
}
