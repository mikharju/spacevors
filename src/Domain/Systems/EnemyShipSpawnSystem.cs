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
        float angleToPlayer = (float)Math.Atan2(velocityDir.X, -velocityDir.Y);

        Random rand = new Random();
        float randomAngle = angleToPlayer + (float)(rand.NextDouble() * MathF.PI / 2f - MathF.PI / 4f);

        float spawnDist = 500f;
        float sx = playerPos.Value.X + (float)Math.Cos(randomAngle) * spawnDist;
        float sy = playerPos.Value.Y + (float)Math.Sin(randomAngle) * spawnDist;

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
