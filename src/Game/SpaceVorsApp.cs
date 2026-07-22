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
        Raylib.InitWindow(WindowWidth, WindowHeight, "SpaceVors");

        Loadout chosenLoadout = Loadout.Forward;
        bool showingLoadoutScreen = true;

        while (!Raylib.WindowShouldClose())
        {
            if (showingLoadoutScreen)
            {
                bool pressed1 = Raylib.IsKeyPressed(KeyboardKey.One);
                bool pressed2 = Raylib.IsKeyPressed(KeyboardKey.Two);

                if (pressed1 && !pressed2)
                    chosenLoadout = Loadout.Forward;
                else if (pressed2 && !pressed1)
                    chosenLoadout = Loadout.Broadside;

                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(15, 15, 25, 255));
                Renderer.DrawLoadoutCards(WindowWidth, WindowHeight);
                Raylib.EndDrawing();

                if (pressed1 || pressed2)
                    showingLoadoutScreen = false;

                continue;
            }

            var (em, playerEntity, cameraEntity, turretEntities, stars, clutter) = GameInitializer.Initialize(chosenLoadout);

            bool gameOver = false;

            var systems = new GameSystem[] { new FiringSystem(), new PhysicsSystem(), new BlueSparkHomeSystem(), new CollisionSystem(), new UpgradePickupSystem(), new AmmoLifetimeSystem(), new MineDriftSystem(), new EnemyShipSpawnSystem(), new EnemyShipSystem(), new CameraSystem(), new TurretFiringSystem(), new EffectSystem() };

            float accumulator = 0f;

            while (!Raylib.WindowShouldClose())
            {
                float frameTime = (float)Raylib.GetFrameTime();

                bool hasPendingChoice = em.GetEntitiesWithComponents<PendingChoice>().Any();

                if (!hasPendingChoice)
                {
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

                    // Sync turret positions and rotations to player ship
                    foreach (var turretEntity in turretEntities)
                    {
                        var offset = em.GetComponent<TurretOffset>(turretEntity);
                        var arcOffset = em.GetComponent<ArcOffset>(turretEntity);

                        float cos = (float)Math.Cos(playerRot.Angle);
                        float sin = (float)Math.Sin(playerRot.Angle);
                        var rotatedOffset = new Vector2(
                            offset.Value.X * cos - offset.Value.Y * sin,
                            offset.Value.X * sin + offset.Value.Y * cos
                        );

                        Vector2 worldPos = playerPos.Value + rotatedOffset;
                        float turretAngle = playerRot.Angle + arcOffset.Angle;
                        em.AddComponent(turretEntity, new Position(worldPos));
                        em.AddComponent(turretEntity, new Rotation(turretAngle));
                    }

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
                else
                {
                    // Game is paused — no simulation runs. Only handle choice input.
                    bool pressed1 = Raylib.IsKeyPressed(KeyboardKey.One);
                    bool pressed2 = Raylib.IsKeyPressed(KeyboardKey.Two);

                    if (pressed1 && !pressed2)
                    {
                        var weapon = em.GetComponent<Weapon>(playerEntity);
                        em.AddComponent(playerEntity, new Weapon(weapon.FireRate, weapon.AmmoSpeed, weapon.KickbackForce,
                            weapon.UpgradeFireRateMultiplier * 1.15f, weapon.UpgradeProjectileSpeedMultiplier));

                        foreach (var turretEntity in turretEntities)
                        {
                            var turret = em.GetComponent<Turret>(turretEntity);
                            em.AddComponent(turretEntity, new Turret(
                                turret.FireRate * 1.15f,
                                turret.AmmoSpeed,
                                turret.KickbackForce,
                                turret.ArcAngle,
                                turret.Range,
                                turret.IsEnemy));
                        }
                    }
                    else if (pressed2 && !pressed1)
                    {
                        var weapon = em.GetComponent<Weapon>(playerEntity);
                        em.AddComponent(playerEntity, new Weapon(weapon.FireRate, weapon.AmmoSpeed, weapon.KickbackForce,
                            weapon.UpgradeFireRateMultiplier, weapon.UpgradeProjectileSpeedMultiplier * 1.3f));

                        foreach (var turretEntity in turretEntities)
                        {
                            var turret = em.GetComponent<Turret>(turretEntity);
                            em.AddComponent(turretEntity, new Turret(
                                turret.FireRate,
                                turret.AmmoSpeed * 1.3f,
                                turret.KickbackForce,
                                turret.ArcAngle,
                                turret.Range,
                                turret.IsEnemy));
                        }
                    }

                    if (pressed1 || pressed2)
                    {
                        foreach (var (entity, _) in em.GetEntitiesWithComponents<PendingChoice>().ToList())
                            em.DestroyEntity(entity);
                    }

                    var cam = em.GetComponent<Camera>(cameraEntity);
                    float camX = (float)cam.Target.X;
                    float camY = (float)cam.Target.Y;

                    Renderer.Render(em, camX, camY, WindowWidth, WindowHeight, false, stars, clutter, playerEntity, GameInitializer.PlayerMaxHealth);
                    Renderer.DrawUpgradeCards(WindowWidth, WindowHeight);
                }
            }
        }

        Raylib.CloseWindow();
    }
}
