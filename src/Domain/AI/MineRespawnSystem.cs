using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class MineRespawnSystem : GameSystem
{
    private float _timer = InitialDelay;
    private const float InitialDelay = 10f;
    private const int MinInterval = 4;
    private const int MaxInterval = 8;
    private const int TargetMineCount = 8;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeMines = view.GetEntitiesWithComponents<EnemyMine>().Count();
        if (activeMines >= TargetMineCount + 15) return;

        view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;
        if (playerEntity.Value < 0) return;

        var rng = view.Rng;
        var playerPos = view.GetComponent<Position>(playerEntity);

        float angle = (float)(rng.NextDouble() * Math.PI * 2f);
        float dist = 300f + (float)rng.NextDouble() * 3000f;
        float mx = playerPos.Value.X + (float)Math.Cos(angle) * dist;
        float my = playerPos.Value.Y + (float)Math.Sin(angle) * dist;
        float mineAngle = (float)(rng.NextDouble() * Math.PI * 2);

        MineSize mSize = rng.NextDouble() < 0.5f ? MineSize.Large : MineSize.Small;

        commands.AddEntity(new Position(new Vector2(mx, my)), new Velocity(Vector2.Zero), new EnemyMine(mSize, 30f + (float)rng.NextDouble() * 20f, mineAngle), new Health(2));

        float elapsed = view.ElapsedTime;
        float rampDuration = 180f;
        float progress = MathF.Min(elapsed / rampDuration, 1f);
        float currentMinInterval = MinInterval + (10 - MinInterval) * (1f - progress);
        float currentMaxInterval = MaxInterval + (20 - MaxInterval) * (1f - progress);

        _timer = currentMinInterval + (float)rng.NextDouble() * (currentMaxInterval - currentMinInterval);
    }
}
