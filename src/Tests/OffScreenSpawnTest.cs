using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Spacevors.Game;
using Xunit;

public class OffScreenSpawnTest
{
    private const float Eps = 1e-3f;

    [Fact]
    public void OutsideScreen_IsJustOutsideRect()
    {
        var viewports = new[]
        {
            new Vector2(1920f, 1024f),
            new Vector2(800f, 600f),
            new Vector2(3440f, 1440f)
        };

        for (int i = 0; i < 16; i++)
        {
            float angle = i * MathF.PI / 8f;
            var dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));

            foreach (var vp in viewports)
            {
                var p = SpawnPlacement.OutsideScreen(Vector2.Zero, vp, dir);
                float halfW = vp.X * 0.5f;
                float halfH = vp.Y * 0.5f;

                // The point must sit exactly ScreenMargin outside one of the screen edges (or a corner).
                bool onVerticalEdge = Math.Abs(Math.Abs(p.X) - (halfW + SpawnPlacement.ScreenMargin)) < Eps
                    && Math.Abs(p.Y) <= halfH + SpawnPlacement.ScreenMargin + Eps;
                bool onHorizontalEdge = Math.Abs(Math.Abs(p.Y) - (halfH + SpawnPlacement.ScreenMargin)) < Eps
                    && Math.Abs(p.X) <= halfW + SpawnPlacement.ScreenMargin + Eps;

                Assert.True(onVerticalEdge || onHorizontalEdge,
                    $"point {p} is not just outside viewport {vp} for direction {dir}");
            }
        }
    }

    [Fact]
    public void ForwardDirection_StaysWithinForwardCone()
    {
        var rng = new Random(7);
        float minDot = MathF.Cos(MathF.PI / 4f) - Eps;

        for (int i = 0; i < 1000; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2f);
            var velDir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));

            var dir = SpawnPlacement.ForwardDirection(velDir, rng);

            Assert.True(Math.Abs(dir.Magnitude - 1f) < Eps, $"not a unit vector: {dir}");
            float dot = Vector2.Dot(dir, velDir);
            Assert.True(dot >= minDot, $"direction {dir} outside forward cone of {velDir} (dot={dot})");
        }
    }

    [Fact]
    public void EnemyShipSpawn_SpawnsJustOutsideScreenDriftingTowardPlayer()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        AddPlayer(em, velocity: new Vector2(100f, 0f));

        var spawner = new EnemyShipSpawnSystem();
        var commands = new CommandBuffer();
        spawner.Update(view, 6f, commands); // expires the initial delay
        commands.Apply(em);

        var ships = em.GetEntitiesWithComponents<EnemyShip>().ToList();
        Assert.Single(ships);

        var pos = em.GetComponent<Position>(ships[0].Entity).Value;
        Assert.False(IsInsideDefaultViewport(pos), $"spawned ship at {pos} is inside the screen");

        var vel = em.GetComponent<Velocity>(ships[0].Entity).Value;
        var playerVel = new Vector2(100f, 0f);

        // The excess over the player's velocity must point at the player (drift-in on top of follow).
        var inward = (Vector2.Zero - pos).Normalized;
        Assert.True(Vector2.Dot(vel - playerVel, inward) > 0f, $"velocity {vel} does not drift toward the player relative to it");

        float diff = (vel - playerVel).Magnitude;
        float maxDiff = (1f - EnemyShipSpawnSystem.FollowFactor) * playerVel.Magnitude + SpawnPlacement.DriftSpeed + Eps;
        Assert.True(diff <= maxDiff, $"velocity {vel} is not near player velocity {playerVel} (diff={diff})");
    }

    [Fact]
    public void MineSpawn_StationaryPlayer_SpawnsJustOutsideScreen()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        AddPlayer(em, velocity: Vector2.Zero);

        var spawner = new MineRespawnSystem();
        var commands = new CommandBuffer();
        spawner.Update(view, 11f, commands); // expires the initial delay
        commands.Apply(em);

        var mines = em.GetEntitiesWithComponents<EnemyMine>().ToList();
        Assert.Single(mines);

        var pos = em.GetComponent<Position>(mines[0].Entity).Value;
        Assert.False(IsInsideDefaultViewport(pos), $"spawned mine at {pos} is inside the screen");
    }

    [Fact]
    public void MineSpawn_MovingPlayer_SpawnsInForwardQuadrant()
    {
        var em = new EntityManager();
        var view = new WorldView(em);

        AddPlayer(em, velocity: new Vector2(100f, 0f));

        var spawner = new MineRespawnSystem();
        var commands = new CommandBuffer();
        spawner.Update(view, 11f, commands); // expires the initial delay
        commands.Apply(em);

        var mines = em.GetEntitiesWithComponents<EnemyMine>().ToList();
        Assert.Single(mines);

        var pos = em.GetComponent<Position>(mines[0].Entity).Value;
        float dot = Vector2.Dot(pos.Normalized, new Vector2(1f, 0f));
        Assert.True(dot >= MathF.Cos(MathF.PI / 4f) - Eps, $"mine at {pos} is not in the forward quadrant (dot={dot})");
    }

    [Fact]
    public void InitialSpawns_AreJustOutsideScreen()
    {
        var viewport = new Vector2(800f, 600f);
        var (em, _, _, _, _) = GameInitializer.Initialize(ShipType.Scout, viewport);

        float halfW = viewport.X * 0.5f;
        float halfH = viewport.Y * 0.5f;

        foreach (var (_, _, pos) in em.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            Assert.False(IsInside(pos.Value, halfW, halfH), $"initial ship at {pos.Value} is inside the screen");
            Assert.True(pos.Value.Magnitude >= GameInitializer.InitialShipMinDistance - Eps,
                $"initial ship at {pos.Value} is closer than {GameInitializer.InitialShipMinDistance} to the player");
        }

        foreach (var (_, _, pos) in em.GetEntitiesWithComponents<EnemyMine, Position>())
            Assert.False(IsInside(pos.Value, halfW, halfH), $"initial mine at {pos.Value} is inside the screen");
    }

    private static void AddPlayer(EntityManager em, Vector2 velocity)
    {
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(velocity));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));
    }

    private static bool IsInsideDefaultViewport(Vector2 p) =>
        IsInside(p, WorldView.DefaultViewportSize.X * 0.5f, WorldView.DefaultViewportSize.Y * 0.5f);

    private static bool IsInside(Vector2 p, float halfW, float halfH) =>
        Math.Abs(p.X) < halfW - Eps && Math.Abs(p.Y) < halfH - Eps;
}
