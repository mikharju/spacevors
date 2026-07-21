using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EnemyShipSpawnSystem : GameSystem
{
    private float _timer = 5f + (float)new Random().NextDouble() * 5f;
    private const float MinInterval = 5f;
    private const float MaxInterval = 10f;

    public override void Update(EntityManager em, float deltaTime)
    {
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (!hasPlayer) return;

        _timer -= deltaTime;

        if (_timer > 0f) return;

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
        em.AddComponent(shipEntity, new Position(new Vector2(sx, sy)));
        em.AddComponent(shipEntity, new Velocity(Vector2.Zero));
        em.AddComponent(shipEntity, new Rotation((float)(rand.NextDouble() * Math.PI * 2f)));
        em.AddComponent(shipEntity, new AngularVelocity(0f));

        float detectionRange = 240f;
        em.AddComponent(shipEntity, new EnemyShip(
            Radius: 20f,
            Speed: 35f,
            TurnRate: 3.5f,
            Health: 3,
            DetectionRange: detectionRange,
            TurretRange: detectionRange,
            TurretFireRate: 1.5f,
            TurretAmmoSpeed: 200f));
        em.AddComponent(shipEntity, new Turret(
            FireRate: 1.5f,
            AmmoSpeed: 200f,
            KickbackForce: 0f,
            ArcAngle: MathF.PI / 8f,
            Range: detectionRange,
            IsEnemy: true));
        em.AddComponent(shipEntity, new Health(3));

        _timer = MinInterval + (float)rand.NextDouble() * (MaxInterval - MinInterval);
    }
}
