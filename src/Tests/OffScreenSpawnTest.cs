using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
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
    public void OutsideScreen_ZeroDirection_FallsBackToRightEdgeWithoutNaN()
    {
        var viewports = new[] { new Vector2(1920f, 1024f), new Vector2(800f, 600f) };

        foreach (var vp in viewports)
        {
            var p = SpawnPlacement.OutsideScreen(Vector2.Zero, vp, Vector2.Zero);
            float halfW = vp.X * 0.5f;
            float halfH = vp.Y * 0.5f;

            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"zero direction gave non-finite point {p}");
            AssertNear(new Vector2(halfW + SpawnPlacement.ScreenMargin, 0f), p, "zero direction fallback");
            Assert.False(IsInside(p, halfW, halfH), $"zero direction spawned inside the screen at {p}");
        }
    }

    [Fact]
    public void OutsideScreen_ZeroDirection_FallbackIsRelativeToCenter()
    {
        var center = new Vector2(500f, -300f);
        var p = SpawnPlacement.OutsideScreen(center, new Vector2(800f, 600f), Vector2.Zero);

        AssertNear(new Vector2(500f + 400f + SpawnPlacement.ScreenMargin, -300f), p, "centered fallback");
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.NaN)]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 1.0)]
    [InlineData(1.0, double.NegativeInfinity)]
    public void OutsideScreen_DirectionWithoutUsableRay_FallsBackToFinitePointOutsideScreen(double dx, double dy)
    {
        var vp = new Vector2(800f, 600f);
        var p = SpawnPlacement.OutsideScreen(Vector2.Zero, vp, new Vector2((float)dx, (float)dy));

        Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"direction ({dx}, {dy}) gave non-finite point {p}");
        Assert.False(IsInside(p, vp.X * 0.5f, vp.Y * 0.5f), $"direction ({dx}, {dy}) spawned inside the screen at {p}");
    }

    [Fact]
    public void OutsideScreen_DirectionSoSmallItUnderflows_IsTreatedAsZero()
    {
        var vp = new Vector2(800f, 600f);
        var p = SpawnPlacement.OutsideScreen(Vector2.Zero, vp, new Vector2(1e-30f, 1e-30f));

        Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), "underflowing direction gave a non-finite point");
        AssertNear(new Vector2(400f + SpawnPlacement.ScreenMargin, 0f), p, "underflowing direction fallback");
    }

    [Fact]
    public void OutsideScreen_SmallAndNonUnitDirections_MatchTheirNormalizedDirection()
    {
        var vp = new Vector2(800f, 600f);

        var directions = new[]
        {
            new Vector2(1f, 1f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1e-4f, 1e-4f),
            new Vector2(-3f, 3f)
        };

        foreach (var dir in directions)
        {
            var p = SpawnPlacement.OutsideScreen(Vector2.Zero, vp, dir);
            AssertNear(SpawnPlacement.OutsideScreen(Vector2.Zero, vp, dir.Normalized), p, $"direction {dir}");
            Assert.False(IsInside(p, vp.X * 0.5f, vp.Y * 0.5f), $"direction {dir} spawned inside the screen at {p}");
        }
    }

    [Fact]
    public void OutsideScreen_AxisDirections_LandOnEdgeMidpoints()
    {
        var vp = new Vector2(800f, 600f);
        float edgeX = 400f + SpawnPlacement.ScreenMargin;
        float edgeY = 300f + SpawnPlacement.ScreenMargin;

        AssertNear(new Vector2(edgeX, 0f), SpawnPlacement.OutsideScreen(Vector2.Zero, vp, new Vector2(1f, 0f)), "+X");
        AssertNear(new Vector2(-edgeX, 0f), SpawnPlacement.OutsideScreen(Vector2.Zero, vp, new Vector2(-1f, 0f)), "-X");
        AssertNear(new Vector2(0f, edgeY), SpawnPlacement.OutsideScreen(Vector2.Zero, vp, new Vector2(0f, 1f)), "+Y");
        AssertNear(new Vector2(0f, -edgeY), SpawnPlacement.OutsideScreen(Vector2.Zero, vp, new Vector2(0f, -1f)), "-Y");
    }

    private static void AssertNear(Vector2 expected, Vector2 actual, string what)
    {
        Assert.True(Math.Abs(actual.X - expected.X) < Eps && Math.Abs(actual.Y - expected.Y) < Eps,
            $"{what}: expected {expected}, got {actual}");
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
        spawner.Update(view, 10f, commands); // expires the initial delay (9 s)
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
        float maxDiff = (1f - SpawningStats.EnemyShips.FollowFactor) * playerVel.Magnitude + SpawnPlacement.DriftSpeed + Eps;
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
        spawner.Update(view, 21f, commands); // expires the initial delay (20 s)
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
        spawner.Update(view, 21f, commands); // expires the initial delay (20 s)
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
            Assert.True(pos.Value.Magnitude >= SpawningStats.InitialWorld.EnemyShipMinDistance - Eps,
                $"initial ship at {pos.Value} is closer than {SpawningStats.InitialWorld.EnemyShipMinDistance} to the player");
            Assert.True(pos.Value.Magnitude <= SpawningStats.InitialWorld.EnemyShipMaxDistance + Eps,
                $"initial ship at {pos.Value} is farther than {SpawningStats.InitialWorld.EnemyShipMaxDistance} from the player");
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
