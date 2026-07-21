using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class SpaceVorsApp
{
    const float FixedDeltaTime = 1f / 60f;
    public const int WindowWidth = 1280;
    const int WindowHeight = 720;

    public static void Main()
    {
        var (em, playerEntity, cameraEntity, turretEntity, stars, clutter) = GameInitializer.Initialize();

        bool gameOver = false;

        var systems = new GameSystem[] { new FiringSystem(), new PhysicsSystem(), new CollisionSystem(), new UpgradePickupSystem(), new AmmoLifetimeSystem(), new MineDriftSystem(), new EnemyShipSpawnSystem(), new EnemyShipSystem(), new CameraSystem(), new TurretFiringSystem(), new EffectSystem() };

        Raylib.InitWindow(WindowWidth, WindowHeight, "SpaceVors");

        float accumulator = 0f;

        while (!Raylib.WindowShouldClose())
        {
            float frameTime = (float)Raylib.GetFrameTime();
            accumulator += frameTime;

            // Handle player input
            var playerPos = em.GetComponent<Position>(playerEntity);
            var playerRot = em.GetComponent<Rotation>(playerEntity);
            var playerStats = em.GetComponent<Player>(playerEntity);
            float thrustForce = playerStats.Thrust;
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
                thrustForce *= playerStats.Boost;

            // Thrust: apply acceleration in direction of ship rotation
            if (Raylib.IsKeyDown(KeyboardKey.W))
            {
                float cos = (float)Math.Cos(playerRot.Angle);
                float sin = (float)Math.Sin(playerRot.Angle);
                var thrustAccel = new Vector2(sin * thrustForce, -cos * thrustForce);
                em.AddComponent(playerEntity, new Acceleration(thrustAccel));
            }
            else
            {
                em.AddComponent(playerEntity, new Acceleration(Vector2.Zero));
            }

            // Rotation: A/D changes angular velocity
            if (Raylib.IsKeyDown(KeyboardKey.A))
            {
                var angVel = em.GetComponent<AngularVelocity>(playerEntity);
                em.AddComponent(playerEntity, new AngularVelocity(angVel.Value - 5f * frameTime));
            }
            else if (Raylib.IsKeyDown(KeyboardKey.D))
            {
                var angVel = em.GetComponent<AngularVelocity>(playerEntity);
                em.AddComponent(playerEntity, new AngularVelocity(angVel.Value + 5f * frameTime));
            }

            // Firing: Space key sets negative cooldown to signal "ready to fire"
            if (Raylib.IsKeyDown(KeyboardKey.Space))
            {
                var hasCooldown = em.HasComponent<FireCooldown>(playerEntity);
                var currentCooldown = hasCooldown ? em.GetComponent<FireCooldown>(playerEntity).Timer : -1f;

                if (!hasCooldown || currentCooldown <= 0f)
                {
                    em.AddComponent(playerEntity, new FireCooldown(-1f));
                }
            }

            em.AddComponent(turretEntity, new Position(playerPos.Value));
            em.AddComponent(turretEntity, new Rotation(playerRot.Angle));

            // Fixed timestep simulation
            while (accumulator >= FixedDeltaTime)
            {
                foreach (var system in systems)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    system.Update(em, FixedDeltaTime);
                    sw.Stop();
                    DiagnosticLogger.LogSystem(system.GetType().Name, sw.ElapsedTicks);
                }
                accumulator -= FixedDeltaTime;
            }

            if (!gameOver && em.HasComponent<Dead>(playerEntity))
            {
                gameOver = true;
            }

            var cam = em.GetComponent<Camera>(cameraEntity);
            float camX = (float)cam.Target.X;
            float camY = (float)cam.Target.Y;

            Renderer.Render(em, camX, camY, WindowWidth, WindowHeight, gameOver, stars, clutter, playerEntity, GameInitializer.PlayerMaxHealth);
        }

        Raylib.CloseWindow();
    }
}
