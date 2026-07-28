using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EnemyShipSpawnSystem : GameSystem
{
    private float _timer = 5f + (float)new Random().NextDouble() * 5f;
    private const float MinInterval = 2f;
    private const float MaxInterval = 4f;
    private const int MaxEnemyShips = 100;
    private const float MinSpawnDistance = 300f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var playerTuple = view.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (!hasPlayer) return;

        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeShips = view.GetEntitiesWithComponents<EnemyShip>().Count();
        if (activeShips >= MaxEnemyShips) return;

        var playerPos = view.GetComponent<Position>(playerEntity);
        Vector2 playerVel = view.HasComponent<Velocity>(playerEntity)
            ? view.GetComponent<Velocity>(playerEntity).Value
            : Vector2.Zero;

        float velMagnitude = playerVel.Magnitude;
        if (velMagnitude < 0.1f) return;

        Vector2 velocityDir = playerVel / velMagnitude;

        Random rand = new Random();
        float randomAngle = (float)(rand.NextDouble() * MathF.PI / 2f - MathF.PI / 4f);

        float cosA = (float)Math.Cos(randomAngle);
        float sinA = (float)Math.Sin(randomAngle);
        Vector2 spawnDir = new Vector2(
            velocityDir.X * cosA - velocityDir.Y * sinA,
            velocityDir.X * sinA + velocityDir.Y * cosA
        );

        float spawnDist = 500f;
        float sx = playerPos.Value.X + spawnDir.X * spawnDist;
        float sy = playerPos.Value.Y + spawnDir.Y * spawnDist;

        if (!IsSpawnClear(view, new Vector2(sx, sy))) return;

        var spawnPos = new Vector2(sx, sy);
        float variantRoll = (float)rand.NextDouble();

        IInitialComponent[] components;
        if (variantRoll < 0.333f)
        {
            components = EnemyShipFactory.CreateInterceptorComponents(spawnPos, Vector2.Zero, (float)(rand.NextDouble() * Math.PI * 2f), 0f);
        }
        else if (variantRoll < 0.666f)
        {
            components = EnemyShipFactory.CreateHeavyCannonComponents(spawnPos, Vector2.Zero, (float)(rand.NextDouble() * Math.PI * 2f), 0f);
        }
        else
        {
            components = EnemyShipFactory.CreateEnemyShipComponents(spawnPos, Vector2.Zero, (float)(rand.NextDouble() * Math.PI * 2f), 0f);
        }

        commands.AddEntity(components);

        float elapsed = ElapsedTime;
        float rampDuration = 180f;
        float progress = MathF.Min(elapsed / rampDuration, 1f);
        float currentMinInterval = MinInterval + (5f - MinInterval) * (1f - progress);
        float currentMaxInterval = MaxInterval + (10f - MaxInterval) * (1f - progress);

        _timer = currentMinInterval + (float)rand.NextDouble() * (currentMaxInterval - currentMinInterval);
    }

    private bool IsSpawnClear(WorldView view, Vector2 spawnPos)
    {
        foreach (var (shipEntity, ship) in view.GetEntitiesWithComponents<EnemyShip>())
        {
            var pos = view.GetComponent<Position>(shipEntity);
            float dx = pos.Value.X - spawnPos.X;
            float dy = pos.Value.Y - spawnPos.Y;
            float distSq = dx * dx + dy * dy;
            float minDist = ship.Radius + MinSpawnDistance;
            if (distSq < minDist * minDist) return false;
        }
        return true;
    }
}
