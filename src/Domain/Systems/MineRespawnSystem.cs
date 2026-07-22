using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class MineRespawnSystem : GameSystem
{
    private float _timer = 10f + (float)new Random().NextDouble() * 10f;
    private const int MinInterval = 4;
    private const int MaxInterval = 8;
    private const int TargetMineCount = 8;

    public override void Update(EntityManager em, float deltaTime)
    {
        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeMines = em.GetEntitiesWithComponents<EnemyMine>().Count();
        if (activeMines >= TargetMineCount + 15) return;

        var playerTuple = em.GetEntitiesWithComponents<Player, Position>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        if (playerEntity.Value < 0) return;

        var playerPos = em.GetComponent<Position>(playerEntity);

        Random rand = new Random();
        float angle = (float)(rand.NextDouble() * Math.PI * 2f);
        float dist = 300f + (float)rand.NextDouble() * 3000f;
        float mx = playerPos.Value.X + (float)Math.Cos(angle) * dist;
        float my = playerPos.Value.Y + (float)Math.Sin(angle) * dist;
        float mineAngle = (float)(rand.NextDouble() * Math.PI * 2);

        var mineEntity = em.CreateEntity();
        em.AddComponent(mineEntity, new Position(new Vector2(mx, my)));
        em.AddComponent(mineEntity, new Velocity(Vector2.Zero));
        MineSize mSize = rand.NextDouble() < 0.5f ? MineSize.Large : MineSize.Small;
        em.AddComponent(mineEntity, new EnemyMine(mSize, 30f + (float)rand.NextDouble() * 20f, mineAngle));
        em.AddComponent(mineEntity, new Health(2));

        float elapsed = ElapsedTime;
        float rampDuration = 180f;
        float progress = MathF.Min(elapsed / rampDuration, 1f);
        float currentMinInterval = MinInterval + (10 - MinInterval) * (1f - progress);
        float currentMaxInterval = MaxInterval + (20 - MaxInterval) * (1f - progress);

        _timer = currentMinInterval + (float)rand.NextDouble() * (currentMaxInterval - currentMinInterval);
    }
}
