using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class ShipDeathExplosionSystem : GameSystem
{
    private const float TotalDuration = 1.0f;
    private const float SecondaryThreshold = 0.5f;
    private const float FinalThreshold = 0.0f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        foreach (var (entity, explosion, pos, rot, enemyShip) in view.GetEntitiesWithComponents<ShipDeathExplosion, Position, Rotation, EnemyShip>())
        {
            var newTimeRemaining = explosion.TimeRemaining - deltaTime;

            if (explosion.TimeRemaining > SecondaryThreshold && newTimeRemaining <= SecondaryThreshold)
            {
                SpawnSecondaryExplosions(commands, pos.Value, rot.Angle, enemyShip.Radius, explosion.InheritedVelocity);
                ApplyImpactVelocity(view, commands, entity, pos.Value, enemyShip.Radius, lateralOnly: true);
            }

            if (explosion.TimeRemaining > FinalThreshold && newTimeRemaining <= FinalThreshold)
            {
                SpawnFinalExplosion(commands, pos.Value, enemyShip.Radius, explosion.InheritedVelocity);
                ApplyImpactVelocity(view, commands, entity, pos.Value, enemyShip.Radius, lateralOnly: false);
                commands.Add(new DestroyEntityCommand(entity));
            }

            if (newTimeRemaining <= FinalThreshold)
                continue;

            commands.Add(new AddComponentCommand<ShipDeathExplosion>(entity, new ShipDeathExplosion(newTimeRemaining, explosion.ImpactPoint, explosion.ShipRadius, explosion.GraphicsId, explosion.InheritedVelocity)));
        }
    }

    private void SpawnSecondaryExplosions(CommandBuffer commands, Vector2 shipPos, float shipRotation, float shipRadius, Vector2 inheritedVel)
    {
        for (int i = 0; i < 2; i++)
        {
            float offsetAngle = shipRotation + (float)(Random.Shared.NextDouble() - 0.5f) * MathF.PI;
            Vector2 impactPos = shipPos + new Vector2((float)Math.Sin(offsetAngle), -(float)Math.Cos(offsetAngle)) * shipRadius;

            commands.AddEntity(
                new Position(impactPos),
                inheritedVel,
                new Explosion(shipRadius * 0.4f, 0.5f, 0.5f)
            );

            int sparkCount = 6;
            for (int j = 0; j < sparkCount; j++)
            {
                SpawnSpark(commands, impactPos, shipRadius);
            }
        }
    }

    private void SpawnFinalExplosion(CommandBuffer commands, Vector2 shipPos, float shipRadius, Vector2 inheritedVel)
    {
        commands.AddEntity(
            new Position(shipPos),
            inheritedVel,
            new Explosion(shipRadius * 1.3f, 0.8f, 0.8f)
        );

        int sparkCount = (int)(shipRadius / 4f);
        for (int i = 0; i < sparkCount; i++)
        {
            SpawnSpark(commands, shipPos, shipRadius);
        }
    }

    private void ApplyImpactVelocity(WorldView view, CommandBuffer commands, Entity entity, Vector2 shipPos, float shipRadius, bool lateralOnly)
    {
        if (!lateralOnly && view.TryGetComponent<AngularVelocity>(entity, out var currentAngVel))
        {
            commands.Add(new AddComponentCommand<AngularVelocity>(entity, new AngularVelocity(currentAngVel.Value * 0.8f)));
        }

        float angle = (float)(Random.Shared.NextDouble() * MathF.PI * 2f);
        float speed = lateralOnly ? 4f : 3f;
        Vector2 impulse = new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed);

        if (view.TryGetComponent<Velocity>(entity, out var currentVel))
        {
            commands.Add(new AddComponentCommand<Velocity>(entity, new Velocity(currentVel.Value + impulse)));
        }
    }

    private void SpawnSpark(CommandBuffer commands, Vector2 position, float explosionRadius)
    {
        float angle = (float)(Random.Shared.NextDouble() * MathF.PI * 2f);
        float speed = (explosionRadius + 15f) / 0.7f;
        float speedVariation = 0.8f + (float)Random.Shared.NextDouble() * 0.4f;
        Vector2 velocity = new Vector2((float)Math.Cos(angle) * speed * speedVariation, (float)Math.Sin(angle) * speed * speedVariation);
        float sparkLifetime = 2.5f + (float)Random.Shared.NextDouble() * 0.5f;
        commands.AddEntity(new Position(position), new Velocity(velocity), new Spark(sparkLifetime, sparkLifetime));
    }
}
