using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class SpaceVorsApp
{
    public const int MaxFps = 120;
    const float FixedDeltaTime = 1f / MaxFps;

    // Cap catch-up work after a hitch (window drag, GC pause) instead of running dozens of steps in one frame.
    const float MaxAccumulator = 0.25f;
    const int DefaultWindowWidth = 1920;
    const int DefaultWindowHeight = 1024;

    public static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(DefaultWindowWidth, DefaultWindowHeight, "SpaceVors");
        Raylib.SetTargetFPS(MaxFps);

        ImageLoader.LoadAssets();
        Lighting.Init();

        bool diagnostics = Environment.GetEnvironmentVariable("SPACEVORS_DIAGNOSTIC") == "1";

        int GetW() => Raylib.GetScreenWidth();
        int GetH() => Raylib.GetScreenHeight();

        void HandleGlobalKeys()
        {
            if (Raylib.IsKeyPressed(KeyboardKey.F11))
                Raylib.ToggleFullscreen();

            if (Raylib.IsKeyPressed(KeyboardKey.F12))
                Raylib.TakeScreenshot("screenshot.png");
        }

        ShipType chosenShip = ShipType.Scout;
        bool showingShipScreen = true;
        var shipSelect = new ShipSelectScreen();

        while (!Raylib.WindowShouldClose())
        {
            HandleGlobalKeys();

            if (showingShipScreen)
            {
                var chosen = shipSelect.Update(GetW(), GetH());
                shipSelect.Draw(GetW(), GetH());

                if (chosen is { } selected)
                {
                    chosenShip = selected;
                    showingShipScreen = false;
                }

                continue;
            }

            var (em, playerEntity, cameraEntity, stars, clutter) = GameInitializer.Initialize(chosenShip, new Vector2(GetW(), GetH()));

            bool gameOver = false;
            bool wasPaused = false;
            bool showStats = false;

            int GetPlayerLevel(EntityManager em)
            {
                return em.TryGetComponent<Player>(playerEntity, out var player) ? player.Level : 1;
            }

            float accumulator = 0f;
            var runner = new SimulationRunner();

            while (!Raylib.WindowShouldClose())
            {
                HandleGlobalKeys();

                if (gameOver && Raylib.IsKeyPressed(KeyboardKey.R))
                {
                    showingShipScreen = true;
                    break;
                }

                float frameTime = (float)Raylib.GetFrameTime();
                DiagnosticLogger.UpdateFps(frameTime);

                bool hasPendingChoice = em.GetEntitiesWithComponents<PendingChoice>().Any();

                // Player died on the level-up frame: drop the stale choice and show game over.
                if (hasPendingChoice && em.HasComponent<Dead>(playerEntity))
                {
                    foreach (var (entity, _) in em.GetEntitiesWithComponents<PendingChoice>().ToList())
                        em.DestroyEntity(entity);
                    hasPendingChoice = false;
                    gameOver = true;
                }

                if (gameOver) showStats = false;

                // Tab toggles the stats screen (also while picking an upgrade).
                // Escape is not used: raylib's default exit key would close the window.
                if (!gameOver && Raylib.IsKeyPressed(KeyboardKey.Tab)) showStats = !showStats;

                bool paused = hasPendingChoice || showStats;

                // Entering pause: stop the ship's thrusters so flames/lights don't burn behind the menu.
                if (paused && !wasPaused)
                {
                    em.RemoveComponent<Acceleration>(playerEntity);
                    em.RemoveComponent<AngularVelocity>(playerEntity);
                }
                wasPaused = paused;

                if (!paused)
                {
                    accumulator = Math.Min(accumulator + frameTime, MaxAccumulator);

                    if (!gameOver)
                    {
                        // Handle player input
                        var playerPos = em.GetComponent<Position>(playerEntity);
                        var playerRot = em.GetComponent<Rotation>(playerEntity);
                        var playerStats = em.GetComponent<Player>(playerEntity);
                        var aimCam = em.GetComponent<Camera>(cameraEntity);

                        float mouseWorldX = aimCam.Target.X + ((float)Raylib.GetMouseX() - GetW() / 2f);
                        float mouseWorldY = aimCam.Target.Y + ((float)Raylib.GetMouseY() - GetH() / 2f);
                        Vector2 toMouse = new Vector2(mouseWorldX - playerPos.Value.X, mouseWorldY - playerPos.Value.Y);
                        float distToMouse = (float)Math.Sqrt(toMouse.X * toMouse.X + toMouse.Y * toMouse.Y);
                        float targetAngle = distToMouse > 1f ? (float)Math.Atan2(toMouse.X, -toMouse.Y) : playerRot.Angle;

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
                        var playerTuples = em.GetEntitiesWithComponents<Turret, TurretOffset, ArcOffset>();

                        foreach (var (turretEntity, turret, offset, arcOffset) in playerTuples)
                        {
                            if (turret.IsEnemy) continue;
                            var rotatedOffset = new Vector2(
                                offset.Value.X * cos - offset.Value.Y * sin,
                                offset.Value.X * sin + offset.Value.Y * cos
                            );

                            Vector2 worldPos = playerPos.Value + rotatedOffset;
                            float turretAngle = playerRot.Angle + arcOffset.Angle;
                            em.AddComponent(turretEntity, new Position(worldPos));
                            em.AddComponent(turretEntity, new Rotation(turretAngle));
                        }

                        // Diagnostic only: force level-up to test the upgrade screen
                        if (diagnostics && Raylib.IsKeyPressed(KeyboardKey.L))
                        {
                            em.AddComponent(playerEntity, playerStats with { Xp = playerStats.Level * 10 });
                        }

                        // Diagnostic only: spawn a test explosion on the fixed test asteroid to verify lighting
                        if (diagnostics && Raylib.IsKeyPressed(KeyboardKey.M))
                        {
                            var testExplosion = em.CreateEntity();
                            em.AddComponent(testExplosion, new Position(new Vector2(0f, -300f)));
                            em.AddComponent(testExplosion, new Explosion(80f, 1.5f, 1.5f));
                        }
                    }

                    // Fixed timestep simulation
                    while (accumulator >= FixedDeltaTime)
                    {
                        DiagnosticLogger.LogFrameStart();

                        var view = new WorldView(em) { ViewportSize = new Vector2(GetW(), GetH()) };
                        var commands = new CommandBuffer();

                        runner.RunPhase(view, commands, runner.MovementSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
                        commands.Apply(em);

                        runner.RunPhase(view, commands, runner.ActionSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
                        commands.Apply(em);

                        runner.RunPhase(view, commands, runner.ResolutionSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
                        commands.Apply(em);

                        runner.RunPhase(view, commands, runner.CleanupSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
                        commands.Apply(em);

                        accumulator -= FixedDeltaTime;
                        em.AddElapsedTime(FixedDeltaTime);
                    }

                    if (!gameOver && em.HasComponent<Dead>(playerEntity))
                    {
                        gameOver = true;
                    }

                    var renderCam = em.GetComponent<Camera>(cameraEntity);
                    Renderer.Render(em, renderCam.Target.X, renderCam.Target.Y, GetW(), GetH(), gameOver, stars, clutter, playerEntity, chosenShip, diagnostics);
                }
                else if (showStats)
                {
                    var statsCam = em.GetComponent<Camera>(cameraEntity);
                    Renderer.RenderPaused(em, (float)statsCam.Target.X, (float)statsCam.Target.Y, GetW(), GetH(), stars, clutter, playerEntity, chosenShip, diagnostics, null, GetPlayerLevel(em), -1);
                }
                else
                {
                    // Game is paused — no simulation runs. Only handle choice input.
                    em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pendingTuple);
                    PendingUpgradeOptions? upgradeOptions = null;
                    if (pendingTuple.Entity.Value >= 0)
                        upgradeOptions = pendingTuple.Value2;

                    int hoveredIndex = -1;
                    if (upgradeOptions is { Options.Length: > 0 } opts)
                        hoveredIndex = StatsScreenRenderer.GetHoveredCardIndex(em, playerEntity, opts, Raylib.GetMouseX(), Raylib.GetMouseY(), GetW(), GetH());

                    int selectedIndex = -1;

                    for (int i = 1; i <= 5; i++)
                    {
                        var key = i switch { 1 => KeyboardKey.One, 2 => KeyboardKey.Two, 3 => KeyboardKey.Three, 4 => KeyboardKey.Four, 5 => KeyboardKey.Five, _ => (KeyboardKey)0 };
                        if (Raylib.IsKeyPressed(key))
                        {
                            selectedIndex = i - 1;
                            break;
                        }
                    }

                    if (selectedIndex < 0 && Raylib.IsMouseButtonPressed(MouseButton.Left))
                        selectedIndex = hoveredIndex;

                    if (selectedIndex >= 0)
                    {
                        em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var choiceTuple);
                        Entity choiceEntity = choiceTuple.Entity;

                        if (choiceEntity.Value >= 0)
                        {
                            var options = choiceTuple.Value2;
                            if (selectedIndex < options.Options.Length)
                            {
                                ApplyUpgrade(em, playerEntity, options.Options[selectedIndex]);
                            }
                        }

                        foreach (var (entity, _) in em.GetEntitiesWithComponents<PendingChoice>().ToList())
                            em.DestroyEntity(entity);
                    }

                    int playerLevel = GetPlayerLevel(em);

                    var upgradeCam = em.GetComponent<Camera>(cameraEntity);
                    float upgradeCamX = (float)upgradeCam.Target.X;
                    float upgradeCamY = (float)upgradeCam.Target.Y;

                    Renderer.RenderPaused(em, upgradeCamX, upgradeCamY, GetW(), GetH(), stars, clutter, playerEntity, chosenShip, diagnostics, upgradeOptions, playerLevel, hoveredIndex);
                }
            }
        }

        Lighting.Shutdown();
        ImageLoader.UnloadAssets();
        Raylib.CloseWindow();
    }

    private static void ApplyUpgrade(EntityManager em, Entity playerEntity, UpgradableOption upgrade)
    {
        var playerStats = em.GetComponent<Player>(playerEntity);
        var allPlayerTurrets = new List<(Entity Entity, Turret Value)>();
        foreach (var t in em.GetEntitiesWithComponents<Turret>())
        {
            if (!t.Value1.IsEnemy) allPlayerTurrets.Add(t);
        }
        

        var existingWeaponNames = allPlayerTurrets.Select(t => t.Value.WeaponName).ToHashSet();
        bool isNewWeapon = !existingWeaponNames.Contains(upgrade.WeaponName);

        if (isNewWeapon && upgrade.Stat == UpgradeOption.Damage)
        {
            AddNewWeaponTurret(em, playerEntity, upgrade.WeaponName);
            return;
        }

        var def = UpgradeDefinition.For(upgrade.Stat);

        var counts = em.TryGetComponent<UpgradeCounts>(playerEntity, out var existingCounts) ? existingCounts : UpgradeCounts.Empty;
        em.AddComponent(playerEntity, counts.Increment(upgrade.Stat, upgrade.WeaponName));

        switch (upgrade.Stat)
        {
            case UpgradeOption.FireRate:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                {
                    int newPelletCount = turret.Weapon.PelletCount > 1 ? turret.Weapon.PelletCount + 1 : turret.Weapon.PelletCount;
                    float newFireRate = turret.Weapon.PelletCount == 1 ? turret.Weapon.FireRate * def.Multiplier : turret.Weapon.FireRate;

                    em.AddComponent(turretEntity, new Turret(
                        Weapon: new WeaponStats(newFireRate, turret.Weapon.AmmoSpeed, turret.Weapon.KickbackForce, newPelletCount, turret.Weapon.Scatter, turret.Weapon.ShotLifetime, turret.Weapon.Damage),
                        WeaponName: turret.WeaponName,
                        ArcAngle: turret.ArcAngle,
                        Range: turret.Range,
                        AutoTarget: turret.AutoTarget,
                        IsEnemy: turret.IsEnemy));
                }
                break;

            case UpgradeOption.ProjectileSpeed:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                {
                    em.AddComponent(turretEntity, new Turret(
                        Weapon: new WeaponStats(turret.Weapon.FireRate, turret.Weapon.AmmoSpeed * def.Multiplier, turret.Weapon.KickbackForce, turret.Weapon.PelletCount, turret.Weapon.Scatter, turret.Weapon.ShotLifetime, turret.Weapon.Damage),
                        WeaponName: turret.WeaponName,
                        ArcAngle: turret.ArcAngle,
                        Range: turret.Range,
                        AutoTarget: turret.AutoTarget,
                        IsEnemy: turret.IsEnemy));
                }
                break;

            case UpgradeOption.PickupRadius:
                em.AddComponent(playerEntity, new Player(
                    playerStats.Thrust,
                    playerStats.SideThrust,
                    playerStats.BackThrust,
                    playerStats.Boost,
                    playerStats.MaxHealth,
                    playerStats.Radius,
                    playerStats.Xp,
                    playerStats.Level,
                    playerStats.PickupRadius * def.Multiplier,
                    playerStats.RotationSpeed));
                break;

            case UpgradeOption.Range:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                {
                    em.AddComponent(turretEntity, new Turret(
                        Weapon: new WeaponStats(turret.Weapon.FireRate, turret.Weapon.AmmoSpeed, turret.Weapon.KickbackForce, turret.Weapon.PelletCount, turret.Weapon.Scatter, turret.Weapon.ShotLifetime * def.Multiplier, turret.Weapon.Damage),
                        WeaponName: turret.WeaponName,
                        ArcAngle: turret.ArcAngle,
                        Range: turret.Range * def.Multiplier,
                        AutoTarget: turret.AutoTarget,
                        IsEnemy: turret.IsEnemy));
                }
                break;

            case UpgradeOption.Damage:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                {
                    int newDamage = turret.Weapon.Damage + def.Additive;

                    if (newDamage > turret.Weapon.Damage)
                    {
                        em.AddComponent(turretEntity, new Turret(
                            Weapon: new WeaponStats(turret.Weapon.FireRate, turret.Weapon.AmmoSpeed, turret.Weapon.KickbackForce, turret.Weapon.PelletCount, turret.Weapon.Scatter, turret.Weapon.ShotLifetime, newDamage),
                            WeaponName: turret.WeaponName,
                            ArcAngle: turret.ArcAngle,
                            Range: turret.Range,
                            AutoTarget: turret.AutoTarget,
                            IsEnemy: turret.IsEnemy));
                    }
                }
                break;

            case UpgradeOption.Hp:
                if (!em.HasComponent<Health>(playerEntity)) break;
                var currentHealth = em.GetComponent<Health>(playerEntity);
                em.AddComponent(playerEntity, new Health(currentHealth.Current + def.Additive));
                em.AddComponent(playerEntity, new Player(
                    playerStats.Thrust,
                    playerStats.SideThrust,
                    playerStats.BackThrust,
                    playerStats.Boost,
                    playerStats.MaxHealth + def.Additive,
                    playerStats.Radius,
                    playerStats.Xp,
                    playerStats.Level,
                    playerStats.PickupRadius,
                    playerStats.RotationSpeed));
                break;

            case UpgradeOption.ForwardAcceleration:
                {
                    var stats = em.GetComponent<Player>(playerEntity);
                    float newThrust = stats.Thrust * def.Multiplier;
                    em.AddComponent(playerEntity, new Player(
                        newThrust,
                        stats.SideThrust,
                        stats.BackThrust,
                        stats.Boost,
                        stats.MaxHealth,
                        stats.Radius,
                        stats.Xp,
                        stats.Level,
                        stats.PickupRadius,
                        stats.RotationSpeed));
                }
                break;

            case UpgradeOption.TurnSpeed:
                {
                    var stats = em.GetComponent<Player>(playerEntity);
                    float newRotationSpeed = stats.RotationSpeed * def.Multiplier;
                    em.AddComponent(playerEntity, new Player(
                        stats.Thrust,
                        stats.SideThrust,
                        stats.BackThrust,
                        stats.Boost,
                        stats.MaxHealth,
                        stats.Radius,
                        stats.Xp,
                        stats.Level,
                        stats.PickupRadius,
                        newRotationSpeed));
                }
                break;

            case UpgradeOption.SideThrust:
                {
                    var stats = em.GetComponent<Player>(playerEntity);
                    float newSideThrust = stats.SideThrust * def.Multiplier;
                    em.AddComponent(playerEntity, new Player(
                        stats.Thrust,
                        newSideThrust,
                        stats.BackThrust,
                        stats.Boost,
                        stats.MaxHealth,
                        stats.Radius,
                        stats.Xp,
                        stats.Level,
                        stats.PickupRadius,
                        stats.RotationSpeed));
                }
                break;

            case UpgradeOption.BackThrust:
                {
                    var stats = em.GetComponent<Player>(playerEntity);
                    float newBackThrust = stats.BackThrust * def.Multiplier;
                    em.AddComponent(playerEntity, new Player(
                        stats.Thrust,
                        stats.SideThrust,
                        stats.BackThrust,
                        stats.Boost,
                        stats.MaxHealth,
                        stats.Radius,
                        stats.Xp,
                        stats.Level,
                        stats.PickupRadius,
                        stats.RotationSpeed));
                }
                break;
        }
    }

    private static void AddNewWeaponTurret(EntityManager em, Entity playerEntity, string weaponName)
    {
        var slots = em.GetComponent<WeaponSlots>(playerEntity);
        if (slots.Used >= slots.Max)
        {
            DiagnosticLogger.LogEvent("UPGRADE", $"new weapon {weaponName} skipped: no free weapon slots ({slots.Used}/{slots.Max})");
            return;
        }

        var type = WeaponType.FromName(weaponName);
        if (type is not { } weapon || weapon.AddOn is not { } mount)
            throw new InvalidOperationException($"Unknown weapon: {weaponName}");

        var playerPos = em.GetComponent<Position>(playerEntity);

        var turretEntity = em.CreateEntity();
        Vector2 worldPos = playerPos.Value;
        em.AddComponent(turretEntity, new Position(worldPos));
        em.AddComponent(turretEntity, new Rotation(mount.ArcOffset));
        em.AddComponent(turretEntity, new Turret(Weapon: weapon.Stats, WeaponName: weapon.Name, ArcAngle: mount.ArcAngle, Range: mount.Range, AutoTarget: mount.AutoTarget));
        em.AddComponent(turretEntity, new TurretOffset(mount.Offset));
        em.AddComponent(turretEntity, new ArcOffset(mount.ArcOffset));

        em.AddComponent(playerEntity, new WeaponSlots(slots.Used + 1, slots.Max));
        DiagnosticLogger.LogEvent("UPGRADE", $"added new weapon {weaponName} (slots {slots.Used + 1}/{slots.Max})");
    }
}
