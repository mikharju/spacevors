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

        EngineLayout chosenEngine = EngineLayout.Balanced;
        WeaponLoadout chosenWeapon = WeaponLoadout.MachineGun;
        bool showingEngineScreen = true;

        while (!Raylib.WindowShouldClose())
        {
            if (showingEngineScreen)
            {
                bool pressed1 = Raylib.IsKeyPressed(KeyboardKey.One);
                bool pressed2 = Raylib.IsKeyPressed(KeyboardKey.Two);
                bool pressed3 = Raylib.IsKeyPressed(KeyboardKey.Three);

                if (pressed1) chosenEngine = EngineLayout.Balanced;
                else if (pressed2) chosenEngine = EngineLayout.Maneuverable;
                else if (pressed3) chosenEngine = EngineLayout.Pursuit;

                bool engineSelected = pressed1 || pressed2 || pressed3;
                if (!engineSelected && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    int mouseX = Raylib.GetMouseX();
                    int mouseY = Raylib.GetMouseY();
                    for (int i = 0; i < 3; i++)
                    {
                        var (topLeft, w, h) = Renderer.GetEngineCardRect(i, WindowWidth, WindowHeight);
                        if (mouseX >= topLeft.X && mouseX <= topLeft.X + w && mouseY >= topLeft.Y && mouseY <= topLeft.Y + h)
                        {
                            chosenEngine = i switch { 0 => EngineLayout.Balanced, 1 => EngineLayout.Maneuverable, _ => EngineLayout.Pursuit };
                            engineSelected = true;
                            break;
                        }
                    }
                }

                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(15, 15, 25, 255));
                Renderer.DrawEngineCards(WindowWidth, WindowHeight);
                Raylib.EndDrawing();

                if (engineSelected)
                    showingEngineScreen = false;

                continue;
            }

            bool showingWeaponScreen = true;

            while (!Raylib.WindowShouldClose())
            {
                if (showingWeaponScreen)
                {
                    bool pressed4 = Raylib.IsKeyPressed(KeyboardKey.Four);
                    bool pressed5 = Raylib.IsKeyPressed(KeyboardKey.Five);

                    if (pressed4) chosenWeapon = WeaponLoadout.MachineGun;
                    else if (pressed5) chosenWeapon = WeaponLoadout.Shotgun;

                    bool weaponSelected = pressed4 || pressed5;
                    if (!weaponSelected && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        int mouseX = Raylib.GetMouseX();
                        int mouseY = Raylib.GetMouseY();
                        for (int i = 0; i < 2; i++)
                        {
                            var (topLeft, w, h) = Renderer.GetLoadoutCardRect(i, WindowWidth, WindowHeight);
                            if (mouseX >= topLeft.X && mouseX <= topLeft.X + w && mouseY >= topLeft.Y && mouseY <= topLeft.Y + h)
                            {
                                chosenWeapon = i == 0 ? WeaponLoadout.MachineGun : WeaponLoadout.Shotgun;
                                weaponSelected = true;
                                break;
                            }
                        }
                    }

                    Raylib.BeginDrawing();
                    Raylib.ClearBackground(new Color(15, 15, 25, 255));
                    Renderer.DrawLoadoutCards(WindowWidth, WindowHeight);
                    Raylib.EndDrawing();

                    if (weaponSelected)
                        showingWeaponScreen = false;

                    continue;
                }

                break;
            }

            var gameChoice = new GameChoice(chosenEngine, chosenWeapon);
            var (em, playerEntity, cameraEntity, turretEntities, stars, clutter) = GameInitializer.Initialize(gameChoice);

            bool gameOver = false;

            var systems = new GameSystem[] { new FiringSystem(), new PhysicsSystem(), new BlueSparkHomeSystem(), new CollisionSystem(), new PickupMagnetSystem(), new LevelUpSystem(), new AmmoLifetimeSystem(), new MineDriftSystem(), new MineRespawnSystem(), new EnemyShipSpawnSystem(), new EnemyShipSystem(), new CameraSystem(), new TurretFiringSystem(), new EffectSystem() };

            float accumulator = 0f;
            GameSystem.ResetElapsedTime();

            while (!Raylib.WindowShouldClose())
            {
                float frameTime = (float)Raylib.GetFrameTime();
                DiagnosticLogger.UpdateFps(frameTime);

                bool hasPendingChoice = em.GetEntitiesWithComponents<PendingChoice>().Any();

                if (!hasPendingChoice)
                {
                    accumulator += frameTime;

                    // Handle player input
                    var playerPos = em.GetComponent<Position>(playerEntity);
                    var playerRot = em.GetComponent<Rotation>(playerEntity);
                    var playerStats = em.GetComponent<Player>(playerEntity);
                    var angVel = em.GetComponent<AngularVelocity>(playerEntity);

                    float mouseWorldX = playerPos.Value.X + ((float)Raylib.GetMouseX() - WindowWidth / 2f);
                    float mouseWorldY = playerPos.Value.Y + ((float)Raylib.GetMouseY() - WindowHeight / 2f);
                    Vector2 toMouse = new Vector2(mouseWorldX - playerPos.Value.X, mouseWorldY - playerPos.Value.Y);
                    float distToMouse = (float)Math.Sqrt(toMouse.X * toMouse.X + toMouse.Y * toMouse.Y);
                    float targetAngle = distToMouse > 1f ? (float)Math.Atan2(toMouse.X, -toMouse.Y) : playerRot.Angle;

                    DiagnosticLogger.LogMouse(
                        Raylib.GetMouseX(),
                        Raylib.GetMouseY(),
                        Raylib.IsMouseButtonDown(MouseButton.Left),
                        Raylib.IsMouseButtonDown(MouseButton.Right),
                        Raylib.IsMouseButtonDown(MouseButton.Middle));
                    Console.WriteLine($"[ROTATION] playerRot:{playerRot.Angle:F3} targetAngle:{targetAngle:F3} angleDiff:{(targetAngle - playerRot.Angle):F3} angVel:{angVel.Value:F3}");

                    float cos = (float)Math.Cos(playerRot.Angle);
                    float sin = (float)Math.Sin(playerRot.Angle);
                    Vector2 thrustAccel = Vector2.Zero;

                    // Forward thrust (W) — boost applies only to forward
                    if (Raylib.IsKeyDown(KeyboardKey.W))
                    {
                        float forwardForce = playerStats.Thrust;
                        if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
                            forwardForce *= playerStats.Boost;
                        thrustAccel += new Vector2(sin * forwardForce, -cos * forwardForce);
                    }

                    // Backward thrust (S)
                    if (Raylib.IsKeyDown(KeyboardKey.S))
                    {
                        thrustAccel += new Vector2(-sin * playerStats.BackThrust, cos * playerStats.BackThrust);
                    }

                    // Left sideways thrust (A)
                    if (Raylib.IsKeyDown(KeyboardKey.A))
                    {
                        thrustAccel += new Vector2(-cos * playerStats.SideThrust, -sin * playerStats.SideThrust);
                    }

                    // Right sideways thrust (D)
                    if (Raylib.IsKeyDown(KeyboardKey.D))
                    {
                        thrustAccel += new Vector2(cos * playerStats.SideThrust, sin * playerStats.SideThrust);
                    }

                    em.AddComponent(playerEntity, new Acceleration(thrustAccel));

                    // Mouse aiming: set angular velocity toward cursor (rad/s)
                    if (distToMouse > 1f)
                    {
                        float currentAngle = playerRot.Angle;
                        float angleDiff = targetAngle - currentAngle;

                        while (angleDiff > MathF.PI) angleDiff -= MathF.PI * 2f;
                        while (angleDiff < -MathF.PI) angleDiff += MathF.PI * 2f;

                        float newAngVel = Math.Clamp(angleDiff / FixedDeltaTime, -playerStats.RotationSpeed, playerStats.RotationSpeed);
                        em.AddComponent(playerEntity, new AngularVelocity(newAngVel));
                    }

                    // Sync turret positions and rotations to player ship
                    foreach (var turretEntity in turretEntities)
                    {
                        var offset = em.GetComponent<TurretOffset>(turretEntity);
                        var arcOffset = em.GetComponent<ArcOffset>(turretEntity);

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
                        GameSystem.AddElapsedTime(FixedDeltaTime);
                    }

                    if (!gameOver && em.HasComponent<Dead>(playerEntity))
                    {
                        gameOver = true;
                    }

                    var renderCam = em.GetComponent<Camera>(cameraEntity);
                    Renderer.Render(em, renderCam.Target.X, renderCam.Target.Y, WindowWidth, WindowHeight, gameOver, stars, clutter, playerEntity, GameInitializer.PlayerMaxHealth);
                }
                else
                {
                    // Game is paused — no simulation runs. Only handle choice input.
                    bool pressed1 = Raylib.IsKeyPressed(KeyboardKey.One);
                    bool pressed2 = Raylib.IsKeyPressed(KeyboardKey.Two);

                    int selectedIndex = -1;
                    if (pressed1) selectedIndex = 0;
                    else if (pressed2) selectedIndex = 1;

                    bool clicked = Raylib.IsMouseButtonPressed(MouseButton.Left);
                    if (clicked && selectedIndex < 0)
                    {
                        int mouseX = Raylib.GetMouseX();
                        int mouseY = Raylib.GetMouseY();
                        for (int i = 0; i < 2; i++)
                        {
                            var (topLeft, w, h) = Renderer.GetUpgradeCardRect(i, WindowWidth, WindowHeight);
                            if (mouseX >= topLeft.X && mouseX <= topLeft.X + w && mouseY >= topLeft.Y && mouseY <= topLeft.Y + h)
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                    }

                    if (selectedIndex >= 0)
                    {
                        var choiceTuple = em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().FirstOrDefault();
                        Entity choiceEntity = choiceTuple.Entity;

                        if (choiceEntity.Value >= 0 && em.HasComponent<PendingUpgradeOptions>(choiceEntity))
                        {
                            var options = em.GetComponent<PendingUpgradeOptions>(choiceEntity);
                            UpgradeOption selected = selectedIndex == 0 ? options.OptionA : options.OptionB;

                            ApplyUpgrade(em, playerEntity, turretEntities, selected);
                        }

                        foreach (var (entity, _) in em.GetEntitiesWithComponents<PendingChoice>().ToList())
                            em.DestroyEntity(entity);
                    }

                    var pendingTuple = em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().FirstOrDefault();
                    PendingUpgradeOptions? upgradeOptions = null;
                    if (pendingTuple.Entity.Value >= 0 && em.HasComponent<PendingUpgradeOptions>(pendingTuple.Entity))
                    {
                        upgradeOptions = em.GetComponent<PendingUpgradeOptions>(pendingTuple.Entity);
                    }

                    var upgradeCam = em.GetComponent<Camera>(cameraEntity);
                    float upgradeCamX = (float)upgradeCam.Target.X;
                    float upgradeCamY = (float)upgradeCam.Target.Y;

                    Renderer.Render(em, upgradeCamX, upgradeCamY, WindowWidth, WindowHeight, false, stars, clutter, playerEntity, GameInitializer.PlayerMaxHealth);
                    Renderer.DrawUpgradeCards(WindowWidth, WindowHeight, upgradeOptions);
                }
            }
        }

        Raylib.CloseWindow();
    }

    private static void ApplyUpgrade(EntityManager em, Entity playerEntity, List<Entity> turretEntities, UpgradeOption upgrade)
    {
        var playerStats = em.GetComponent<Player>(playerEntity);

        switch (upgrade)
        {
            case UpgradeOption.FireRate:
                foreach (var turretEntity in turretEntities)
                {
                    var turret = em.GetComponent<Turret>(turretEntity);
                    int newPelletCount = turret.Weapon.PelletCount > 1 ? turret.Weapon.PelletCount + 1 : turret.Weapon.PelletCount;
                    float newFireRate = turret.Weapon.PelletCount == 1 ? turret.Weapon.FireRate * 1.15f : turret.Weapon.FireRate;

                    em.AddComponent(turretEntity, new Turret(
                        Weapon: new WeaponStats(newFireRate, turret.Weapon.AmmoSpeed, turret.Weapon.KickbackForce, newPelletCount, turret.Weapon.Scatter),
                        ArcAngle: turret.ArcAngle,
                        Range: turret.Range,
                        IsEnemy: turret.IsEnemy));
                }
                break;

            case UpgradeOption.ProjectileSpeed:
                foreach (var turretEntity in turretEntities)
                {
                    var turret = em.GetComponent<Turret>(turretEntity);
                    em.AddComponent(turretEntity, new Turret(
                        Weapon: new WeaponStats(turret.Weapon.FireRate, turret.Weapon.AmmoSpeed * 1.3f, turret.Weapon.KickbackForce, turret.Weapon.PelletCount, turret.Weapon.Scatter),
                        ArcAngle: turret.ArcAngle,
                        Range: turret.Range,
                        IsEnemy: turret.IsEnemy));
                }
                break;

            case UpgradeOption.PickupRadius:
                em.AddComponent(playerEntity, new Player(
                    playerStats.Thrust,
                    playerStats.SideThrust,
                    playerStats.BackThrust,
                    playerStats.Boost,
                    playerStats.Radius,
                    playerStats.Xp,
                    playerStats.Level,
                    playerStats.PickupRadius * 1.2f,
                    playerStats.RotationSpeed));
                break;
        }
    }
}
