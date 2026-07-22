using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EnemyShipSpawnSystem : GameSystem
{
    private float _timer = 5f + (float)new Random().NextDouble() * 5f;
    private const float MinInterval = 2f;
    private const float MaxInterval = 4f;
    private const int MaxEnemyShips = 100;

    public override void Update(EntityManager em, float deltaTime)
    {
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (!hasPlayer) return;

        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeShips = em.GetEntitiesWithComponents<EnemyShip>().Count();
        if (activeShips >= MaxEnemyShips) return;

        var playerPos = em.GetComponent<Position>(playerEntity);
        var playerVel = em.HasComponent<Velocity>(playerEntity)
            ? em.GetComponent<Velocity>(playerEntity).Value
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

        var shipEntity = em.CreateEntity();
        EnemyShipFactory.AddEnemyShipComponents(em, shipEntity, new Vector2(sx, sy), Vector2.Zero, (float)(rand.NextDouble() * Math.PI * 2f), 0f);

        float elapsed = ElapsedTime;
        float rampDuration = 180f;
        float progress = MathF.Min(elapsed / rampDuration, 1f);
        float currentMinInterval = MinInterval + (5f - MinInterval) * (1f - progress);
        float currentMaxInterval = MaxInterval + (10f - MaxInterval) * (1f - progress);

        _timer = currentMinInterval + (float)rand.NextDouble() * (currentMaxInterval - currentMinInterval);
    }
}
