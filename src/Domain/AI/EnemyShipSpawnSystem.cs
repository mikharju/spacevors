using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EnemyShipSpawnSystem : GameSystem
{
    private float _timer = InitialDelay;

    // Grace period before the first respawn (plans/DIFFICULTY_SCALING.md P4).
    private const float InitialDelay = 9f;
    // Interval ramps from random StartMin–StartMax s down to random Min–Max s over RampDuration (see NextInterval).
    private const float StartMinInterval = 5f;
    private const float StartMaxInterval = 10f;
    private const float MinInterval = 2f;
    private const float MaxInterval = 4f;
    private const float RampDuration = 180f;
    private const int MaxEnemyShips = 100;
    private const float MinSpawnDistance = 300f;
    public const float FollowFactor = 0.5f;

    // Threat density per unit distance stays roughly constant at any speed (plans/DIFFICULTY_SCALING.md P3):
    // a fast player covers more ground per second, so spawns must arrive faster to keep pressure up.
    public const float ReferenceSpeed = 100f;
    private const float MaxSpeedFactor = 3f;

    public static float SpeedFactor(float playerSpeed) => Math.Clamp(playerSpeed / ReferenceSpeed, 1f, MaxSpeedFactor);

    // Random spawn interval for the given elapsed time and player speed. Pure: rng is the only input.
    public static float NextInterval(float elapsedTime, float playerSpeed, Random rng)
    {
        float progress = MathF.Min(elapsedTime / RampDuration, 1f);
        float minInterval = MinInterval + (StartMinInterval - MinInterval) * (1f - progress);
        float maxInterval = MaxInterval + (StartMaxInterval - MaxInterval) * (1f - progress);
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

        _timer = NextInterval(view.ElapsedTime, velMagnitude, rng);
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
