using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public class MineRespawnSystem : GameSystem
{
    private float _timer = SpawningStats.Mines.InitialDelay;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeMines = view.GetEntitiesWithComponents<EnemyMine>().Count();
        if (activeMines >= SpawningStats.Mines.MaxMines) return;

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
        var mineType = MineType.FromSize(mSize);

        commands.AddEntity(new Position(minePos), new Velocity(Vector2.Zero), new EnemyMine(mSize, MineStats.SpawnSpeedMin + (float)rng.NextDouble() * (MineStats.SpawnSpeedMax - MineStats.SpawnSpeedMin), mineAngle), new Health(mineType.Health, mineType.Health));

        float elapsed = view.ElapsedTime;
        float progress = MathF.Min(elapsed / SpawningStats.Mines.RampDuration, 1f);
        float currentMinInterval = SpawningStats.Mines.StartMinInterval + (SpawningStats.Mines.LateMinInterval - SpawningStats.Mines.StartMinInterval) * (1f - progress);
        float currentMaxInterval = SpawningStats.Mines.StartMaxInterval + (SpawningStats.Mines.LateMaxInterval - SpawningStats.Mines.StartMaxInterval) * (1f - progress);

        _timer = currentMinInterval + (float)rng.NextDouble() * (currentMaxInterval - currentMinInterval);
    }
}
