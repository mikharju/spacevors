using System.Diagnostics;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Xunit;

public class PerformanceBenchmark
{
    [Fact]
    public void FullGameLoop_CollisionStressTests()
    {
        var scenarios = new[]
        {
            ("5k ammo + 100 ships", 5000, 100, 0, 0),
            //("500 ammo + 1k ships + 100 asteroids", 500, 1000, 100, 0),
            // ("500 ammo + 100 ships + 1k asteroids", 500, 100, 1000, 0),
            // ("3k mines (few others)", 0, 0, 0, 3000),
            // ("1k of everything", 1000, 1000, 1000, 1000),
            // ("15k ammo + 100 each other", 15000, 100, 100, 100),
        };

        foreach (var (name, ammoCount, shipCount, asteroidCount, mineCount) in scenarios)
        {
            RunScenario(name, ammoCount, shipCount, asteroidCount, mineCount);
        }
    }

    private void RunScenario(string name, int ammoCount, int shipCount, int asteroidCount, int mineCount)
    {
        var rng = new Random(42);

        const int iterations = 3;
        const int framesPerIteration = 60;
        const float deltaTime = 1f / 120f;

        var allTimings = new List<Dictionary<string, double>>();
        var allFrameTimes = new List<double>();

        for (int iter = 0; iter < iterations; iter++)
        {
            var em = new EntityManager();
            var runner = new SimulationRunner();

            // Player at origin with some initial velocity
            var playerEntity = em.CreateEntity();
            em.AddComponent(playerEntity, new Position(Vector2.Zero));
            em.AddComponent(playerEntity, new Velocity(new Vector2(10f, -5f)));
            em.AddComponent(playerEntity, new Rotation(0f));
            em.AddComponent(playerEntity, new AngularVelocity(0f));
            em.AddComponent(playerEntity, new Player(0f, 0f, 0f, 1f, MaxHealth: 10));
            em.AddComponent(playerEntity, new Health(10));
            em.AddComponent(playerEntity, new WeaponSlots(0, 4));

            // Turrets on player (world-space positions synced from player rotation)
            var turretPositions = new[] { new Vector2(-15f, -10f), new Vector2(15f, -10f) };
            foreach (var tp in turretPositions)
            {
                var te = em.CreateEntity();
                em.AddComponent(te, new Position(tp));
                em.AddComponent(te, new Rotation(0f));
                em.AddComponent(te, new Turret(
                    Weapon: new WeaponStats(FireRate: 2f, AmmoSpeed: 300f, KickbackForce: 5f, PelletCount: 1, Scatter: 0.05f),
                    WeaponName: "PlayerWeapon", ArcAngle: MathF.PI / 4f, Range: 600f));
                em.AddComponent(te, new FireCooldown(0f));
            }

            var center = Vector2.Zero;
            const float clusterRadius = 150f;
            const float minSpawnDist = 30f;

            // Ships
            for (int i = 0; i < shipCount; i++)
            {
                double angle = rng.NextDouble() * MathF.PI * 2f;
                double dist = minSpawnDist + rng.NextDouble() * (clusterRadius - minSpawnDist);
                var pos = center + new Vector2((float)(Math.Cos(angle) * dist), (float)(Math.Sin(angle) * dist));

                var entity = em.CreateEntity();
                em.AddComponent(entity, new Position(pos));
                em.AddComponent(entity, new Velocity(new Vector2(
                    (float)((rng.NextDouble() - 0.5) * 40f),
                    (float)((rng.NextDouble() - 0.5) * 40f))));
                em.AddComponent(entity, new Rotation((float)(rng.NextDouble() * MathF.PI * 2f)));
                em.AddComponent(entity, new AngularVelocity((float)((rng.NextDouble() - 0.5) * 2f)));
                em.AddComponent(entity, new EnemyShip(
                    Radius: 20f, Speed: 35f, TurnRate: 1f, DetectionRange: 1200f, FiringRange: 300f,
                    TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 9f, Damage: 1, GraphicsId: 0));
                em.AddComponent(entity, new Health(3));

                // Enemy turrets with zero cooldown so they fire immediately
                var te = em.CreateEntity();
                em.AddComponent(te, new Position(pos + new Vector2(-5f, -5f)));
                em.AddComponent(te, new Rotation((float)(rng.NextDouble() * MathF.PI * 2f)));
                em.AddComponent(te, new Turret(
                    Weapon: new WeaponStats(FireRate: 1.5f, AmmoSpeed: 200f, KickbackForce: 0f, PelletCount: 1, Scatter: 0.05f),
                    WeaponName: "EnemyWeapon", ArcAngle: MathF.PI / 8f, Range: 1200f, IsEnemy: true));
                em.AddComponent(te, new FireCooldown(0f));
            }

            // Asteroids
            for (int i = 0; i < asteroidCount; i++)
            {
                double angle = rng.NextDouble() * MathF.PI * 2f;
                double dist = minSpawnDist + rng.NextDouble() * (clusterRadius - minSpawnDist);
                var pos = center + new Vector2((float)(Math.Cos(angle) * dist), (float)(Math.Sin(angle) * dist));
                float radius = 20f + (float)(rng.NextDouble() * 40f);

                var entity = em.CreateEntity();
                em.AddComponent(entity, new Position(pos));
                em.AddComponent(entity, new Velocity(new Vector2(
                    (float)((rng.NextDouble() - 0.5) * 30f),
                    (float)((rng.NextDouble() - 0.5) * 30f))));
                em.AddComponent(entity, new Rotation((float)(rng.NextDouble() * MathF.PI * 2f)));
                em.AddComponent(entity, new AngularVelocity((float)((rng.NextDouble() - 0.5) * 1f)));
                em.AddComponent(entity, new Asteroid(IsSmall: false, radius));
            }

            // Ammo firing toward center (crossfire pattern)
            for (int i = 0; i < ammoCount; i++)
            {
                double angle = rng.NextDouble() * MathF.PI * 2f;
                double dist = clusterRadius + 50f + rng.NextDouble() * 100f;
                var pos = center + new Vector2((float)(Math.Cos(angle) * dist), (float)(Math.Sin(angle) * dist));

                // Aim toward center
                var toCenter = center - pos;
                float speed = 300f;
                var vel = toCenter.Normalized * speed;

                var entity = em.CreateEntity();
                em.AddComponent(entity, new Position(pos));
                em.AddComponent(entity, new Velocity(vel));
                em.AddComponent(entity, new Ammo(vel, 4f, Lifetime: 10f, IsEnemy: false, Damage: 1));
            }

            // Mines
            for (int i = 0; i < mineCount; i++)
            {
                double angle = rng.NextDouble() * MathF.PI * 2f;
                double dist = minSpawnDist + rng.NextDouble() * (clusterRadius - minSpawnDist);
                var pos = center + new Vector2((float)(Math.Cos(angle) * dist), (float)(Math.Sin(angle) * dist));

                bool isLarge = rng.NextDouble() < 0.2;
                float radius = isLarge ? 15f : 7.5f;

                var entity = em.CreateEntity();
                em.AddComponent(entity, new Position(pos));
                em.AddComponent(entity, new Velocity(new Vector2(
                    (float)((rng.NextDouble() - 0.5) * 20f),
                    (float)((rng.NextDouble() - 0.5) * 20f))));
                em.AddComponent(entity, new EnemyMine(isLarge ? MineSize.Large : MineSize.Small, Speed: 0f, Angle: 0f));
                em.AddComponent(entity, new Health(1));
            }

            var timings = new Dictionary<string, double>();

            for (int frame = 0; frame < framesPerIteration; frame++)
            {
                var view = new WorldView(em);
                var commands = new CommandBuffer();

                // Phase 1: movementSystems
                runner.RunPhase(view, commands, runner.MovementSystems, deltaTime, (name, ticks) =>
                {
                    timings[name] = (timings.GetValueOrDefault(name, 0) + ticks * 1000.0 / Stopwatch.Frequency);
                });
                commands.Apply(em);

                // Phase 2: actionSystems
                runner.RunPhase(view, commands, runner.ActionSystems, deltaTime, (name, ticks) =>
                {
                    timings[name] = (timings.GetValueOrDefault(name, 0) + ticks * 1000.0 / Stopwatch.Frequency);
                });
                commands.Apply(em);

                // Phase 3: resolutionSystems
                runner.RunPhase(view, commands, runner.ResolutionSystems, deltaTime, (name, ticks) =>
                {
                    timings[name] = (timings.GetValueOrDefault(name, 0) + ticks * 1000.0 / Stopwatch.Frequency);
                });
                commands.Apply(em);

                // Phase 4: cleanupSystems
                runner.RunPhase(view, commands, runner.CleanupSystems, deltaTime, (name, ticks) =>
                {
                    timings[name] = (timings.GetValueOrDefault(name, 0) + ticks * 1000.0 / Stopwatch.Frequency);
                });
                commands.Apply(em);

                em.AddElapsedTime(deltaTime);

                double frameTime = timings.Values.Sum();
                allFrameTimes.Add(frameTime);
            }

            allTimings.Add(timings);
        }

        // Output results
        Console.WriteLine();
        Console.WriteLine($"=== {name} ===");

        var systemNames = allTimings[0].Keys.OrderBy(k => k).ToList();
        foreach (var key in systemNames)
        {
            double avg = allTimings.Average(t => t[key] / framesPerIteration);
            Console.WriteLine($"  {key,-35} {avg,6:F2}ms");
        }

        double avgFrameTime = allFrameTimes.Average();
        Console.WriteLine($"  {("Total frame time"),-35} {avgFrameTime,6:F2}ms");
    }
}
