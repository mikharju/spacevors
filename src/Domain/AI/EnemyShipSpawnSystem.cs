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

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var rng = view.Rng;

        var playerTuple = view.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (!hasPlayer) return;

        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeShips = view.GetEntitiesWithComponents<EnemyShip>().Count();
        if (activeShips >= MaxEnemyShips) return;

        var playerPos = view.GetComponent<Position>(playerEntity);
        view.TryGetComponent<Velocity>(playerEntity, out var playerVelComp);
        Vector2 playerVel = playerVelComp.Value;

        float velMagnitude = playerVel.Magnitude;
        if (velMagnitude < 0.1f) return;

        Vector2 velocityDir = playerVel / velMagnitude;

        float randomAngle = (float)(rng.NextDouble() * MathF.PI / 2f - MathF.PI / 4f);

        float cosA = (float)Math.Cos(randomAngle);
        float sinA = (float)Math.Sin(randomAngle);
        Vector2 spawnDir = new Vector2(
            velocityDir.X * cosA - velocityDir.Y * sinA,
            velocityDir.X * sinA + velocityDir.Y * sinA
        );

        float spawnDist = 500f + (float)rng.NextDouble() * 500f;
        Vector2 testSpawnPos = new(
            playerPos.Value.X + spawnDir.X * spawnDist,
            playerPos.Value.Y + spawnDir.Y * spawnDist
        );

        if (!IsSpawnClear(view, testSpawnPos)) return;

        float variantRoll = (float)rng.NextDouble();

        IInitialComponent[] components;
        if (variantRoll < 0.333f)
        {
            components = EnemyShipFactory.CreateComponents(testSpawnPos, Vector2.Zero, (float)(rng.NextDouble() * Math.PI * 2f), 0f, EnemyShipType.Interceptor);
        }
        else if (variantRoll < 0.666f)
        {
            components = EnemyShipFactory.CreateComponents(testSpawnPos, Vector2.Zero, (float)(rng.NextDouble() * Math.PI * 2f), 0f, EnemyShipType.HeavyCannon);
        }
        else
        {
            components = EnemyShipFactory.CreateComponents(testSpawnPos, Vector2.Zero, (float)(rng.NextDouble() * Math.PI * 2f), 0f, EnemyShipType.Default);
        }

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
