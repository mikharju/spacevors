using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class MineRespawnSystem : GameSystem
{
    private float _timer = InitialDelay;
    private const float InitialDelay = 10f;
    private const int MinInterval = 4;
    private const int MaxInterval = 8;
    private const int MaxMines = 23; // hard ceiling on live mines before respawning pauses

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeMines = view.GetEntitiesWithComponents<EnemyMine>().Count();
        if (activeMines >= MaxMines) return;

        if (!view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple)) return;
        Entity playerEntity = playerTuple.Entity;

        var rng = view.Rng;
        var playerPos = view.GetComponent<Position>(playerEntity);
        view.TryGetComponent<Velocity>(playerEntity, out var playerVelComp);
        Vector2 playerVel = playerVelComp.Value;

        // No meaningful "front" while the player is stationary: spawn in any direction.
        Vector2 spawnDir = playerVel.Magnitude >= SpawnPlacement.MinDirectionalSpeed
            ? SpawnPlacement.ForwardDirection(playerVel / playerVel.Magnitude, rng)
            : SpawnPlacement.AnyDirection(rng);

        Vector2 minePos = SpawnPlacement.OutsideScreen(playerPos.Value, view.ViewportSize, spawnDir);
        float mineAngle = (float)(rng.NextDouble() * Math.PI * 2);

        MineSize mSize = rng.NextDouble() < 0.5f ? MineSize.Large : MineSize.Small;

        commands.AddEntity(new Position(minePos), new Velocity(Vector2.Zero), new EnemyMine(mSize, 30f + (float)rng.NextDouble() * 20f, mineAngle), new Health(2));

        float elapsed = view.ElapsedTime;
        float rampDuration = 180f;
        float progress = MathF.Min(elapsed / rampDuration, 1f);
        float currentMinInterval = MinInterval + (10 - MinInterval) * (1f - progress);
        float currentMaxInterval = MaxInterval + (20 - MaxInterval) * (1f - progress);

        _timer = currentMinInterval + (float)rng.NextDouble() * (currentMaxInterval - currentMinInterval);
    }
}
