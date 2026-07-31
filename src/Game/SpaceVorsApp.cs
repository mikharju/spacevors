using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class SpaceVorsApp
{
    public const int MaxFps = 120;
    const float FixedDeltaTime = 1f / MaxFps;
    const float MaxFrameTime = 1f / MaxFps;
    const int DefaultWindowWidth = 1280;
    const int DefaultWindowHeight = 720;

    public static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(DefaultWindowWidth, DefaultWindowHeight, "SpaceVors");

        int GetW() => Raylib.GetScreenWidth();
        int GetH() => Raylib.GetScreenHeight();

        ShipType chosenShip = ShipType.Scout;
        bool showingShipScreen = true;

        while (!Raylib.WindowShouldClose())
        {
            if (showingShipScreen)
            {
                bool pressed1 = Raylib.IsKeyPressed(KeyboardKey.One);
                bool pressed2 = Raylib.IsKeyPressed(KeyboardKey.Two);
                bool pressed3 = Raylib.IsKeyPressed(KeyboardKey.Three);

                if (pressed1) chosenShip = ShipType.Scout;
                else if (pressed2) chosenShip = ShipType.Fighter;
                else if (pressed3) chosenShip = ShipType.Heavy;

                bool shipSelected = pressed1 || pressed2 || pressed3;
                if (!shipSelected && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    int mouseX = Raylib.GetMouseX();
                    int mouseY = Raylib.GetMouseY();
                    for (int i = 0; i < 3; i++)
                    {
                        var (topLeft, w, h) = Renderer.GetShipCardRect(i, GetW(), GetH());
                        if (mouseX >= topLeft.X && mouseX <= topLeft.X + w && mouseY >= topLeft.Y && mouseY <= topLeft.Y + h)
                        {
                            chosenShip = i switch { 0 => ShipType.Scout, 1 => ShipType.Fighter, _ => ShipType.Heavy };
                            shipSelected = true;
                            break;
                        }
                    }
                }

                if (Raylib.IsKeyPressed(KeyboardKey.F11))
                    Raylib.ToggleFullscreen();

                if (Raylib.IsKeyPressed(KeyboardKey.F12))
                    Raylib.TakeScreenshot("screenshot.png");

                var frameStart = Raylib.GetTime();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(15, 15, 25, 255));
                Renderer.DrawShipCards(GetW(), GetH());
                Raylib.EndDrawing();

                float frameElapsed = (float)(Raylib.GetTime() - frameStart);
                if (frameElapsed < MaxFrameTime)
                {
                    Thread.Sleep((int)((MaxFrameTime - frameElapsed) * 1000));
                }

                if (shipSelected)
                    showingShipScreen = false;

                continue;
            }

            var (em, playerEntity, cameraEntity, turretEntities, stars, clutter) = GameInitializer.Initialize(chosenShip);

            bool gameOver = false;

            float accumulator = 0f;
            GameSystem.ResetElapsedTime();

            while (!Raylib.WindowShouldClose())
            {
                if (Raylib.IsKeyPressed(KeyboardKey.F11))
                    Raylib.ToggleFullscreen();

                if (Raylib.IsKeyPressed(KeyboardKey.F12))
                    Raylib.TakeScreenshot("screenshot.png");

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

                    float mouseWorldX = playerPos.Value.X + ((float)Raylib.GetMouseX() - GetW() / 2f);
                    float mouseWorldY = playerPos.Value.Y + ((float)Raylib.GetMouseY() - GetH() / 2f);
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
                    var playerTuples = em.GetEntitiesWithComponents<Turret, TurretOffset, ArcOffset>()
                        .Where(t => !t.Value1.IsEnemy)
                        .ToList();

                    foreach (var (turretEntity, turret, offset, arcOffset) in playerTuples)
                    {
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
                        DiagnosticLogger.LogFrameStart();

                        var view = new WorldView(em);
                        var commands = new CommandBuffer();

                        SimulationRunner.RunPhase(view, commands, SimulationRunner.MovementSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
                        commands.Apply(em);

                        SimulationRunner.RunPhase(view, commands, SimulationRunner.ActionSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
                        commands.Apply(em);

                        SimulationRunner.RunPhase(view, commands, SimulationRunner.ResolutionSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
                        commands.Apply(em);

                        SimulationRunner.RunPhase(view, commands, SimulationRunner.CleanupSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
                        commands.Apply(em);

                        accumulator -= FixedDeltaTime;
                        GameSystem.AddElapsedTime(FixedDeltaTime);
                    }

                    if (!gameOver && em.HasComponent<Dead>(playerEntity))
                    {
                        gameOver = true;
                    }

                    var renderCam = em.GetComponent<Camera>(cameraEntity);
                    var gameFrameStart = Raylib.GetTime();
                    Renderer.Render(em, renderCam.Target.X, renderCam.Target.Y, GetW(), GetH(), gameOver, stars, clutter, playerEntity, chosenShip);

                    float frameElapsed = (float)(Raylib.GetTime() - gameFrameStart);
                    if (frameElapsed < MaxFrameTime)
                    {
                        Thread.Sleep((int)((MaxFrameTime - frameElapsed) * 1000));
                    }
                }
                else
                {
                    // Game is paused — no simulation runs. Only handle choice input.
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

                    bool clicked = Raylib.IsMouseButtonPressed(MouseButton.Left);
                    if (clicked && selectedIndex < 0)
                    {
                        int mouseX = Raylib.GetMouseX();
                        int mouseY = Raylib.GetMouseY();
                        em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var choiceTuple);
                        if (choiceTuple.Entity.Value >= 0)
                        {
                            var options = choiceTuple.Value2;
                            int optionCount = options.Options.Length;
                            for (int i = 0; i < optionCount; i++)
                            {
                                var (topLeft, w, h) = Renderer.GetUpgradeCardRect(i, GetW(), GetH());
                                if (mouseX >= topLeft.X && mouseX <= topLeft.X + w && mouseY >= topLeft.Y && mouseY <= topLeft.Y + h)
                                {
                                    selectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }

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

                    em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pendingTuple);
                    PendingUpgradeOptions? upgradeOptions = null;
                    if (pendingTuple.Entity.Value >= 0)
                    {
                        upgradeOptions = pendingTuple.Value2;
                    }

                    int playerLevel = 1;
                    var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
                    if (playerTuple.Entity.Value >= 0)
                    {
                        playerLevel = em.GetComponent<Player>(playerTuple.Entity).Level;
                    }

                    var upgradeCam = em.GetComponent<Camera>(cameraEntity);
                    float upgradeCamX = (float)upgradeCam.Target.X;
                    float upgradeCamY = (float)upgradeCam.Target.Y;

                    var pauseFrameStart = Raylib.GetTime();
                    Renderer.Render(em, upgradeCamX, upgradeCamY, GetW(), GetH(), false, stars, clutter, playerEntity, chosenShip);
                    Renderer.DrawUpgradeCards(GetW(), GetH(), upgradeOptions, playerLevel);

                    float frameElapsed2 = (float)(Raylib.GetTime() - pauseFrameStart);
                    if (frameElapsed2 < MaxFrameTime)
                    {
                        Thread.Sleep((int)((MaxFrameTime - frameElapsed2) * 1000));
                    }
                }
            }
        }

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

        switch (upgrade.Stat)
        {
            case UpgradeOption.FireRate:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                {
                    int newPelletCount = turret.Weapon.PelletCount > 1 ? turret.Weapon.PelletCount + 1 : turret.Weapon.PelletCount;
                    float newFireRate = turret.Weapon.PelletCount == 1 ? turret.Weapon.FireRate * 1.15f : turret.Weapon.FireRate;

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
                        Weapon: new WeaponStats(turret.Weapon.FireRate, turret.Weapon.AmmoSpeed * 1.3f, turret.Weapon.KickbackForce, turret.Weapon.PelletCount, turret.Weapon.Scatter, turret.Weapon.ShotLifetime, turret.Weapon.Damage),
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
                    playerStats.Radius,
                    playerStats.Xp,
                    playerStats.Level,
                    playerStats.PickupRadius * 1.2f,
                    playerStats.RotationSpeed));
                break;

            case UpgradeOption.AutoTargetRange:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName && t.Value.AutoTarget))
                {
                    em.AddComponent(turretEntity, new Turret(
                        Weapon: turret.Weapon,
                        WeaponName: turret.WeaponName,
                        ArcAngle: turret.ArcAngle,
                        Range: turret.Range * 1.15f,
                        AutoTarget: true,
                        IsEnemy: turret.IsEnemy));
                }
                break;

            case UpgradeOption.ShotLifetime:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                {
                    em.AddComponent(turretEntity, new Turret(
                        Weapon: new WeaponStats(turret.Weapon.FireRate, turret.Weapon.AmmoSpeed, turret.Weapon.KickbackForce, turret.Weapon.PelletCount, turret.Weapon.Scatter, turret.Weapon.ShotLifetime * 1.15f, turret.Weapon.Damage),
                        WeaponName: turret.WeaponName,
                        ArcAngle: turret.ArcAngle,
                        Range: turret.Range,
                        AutoTarget: turret.AutoTarget,
                        IsEnemy: turret.IsEnemy));
                }
                break;

            case UpgradeOption.Damage:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                {
                    int newDamage = turret.Weapon.Damage + 1;

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
                int newHp = currentHealth.Current + 2;
                em.AddComponent(playerEntity, new Health(newHp));
                break;

            case UpgradeOption.ForwardAcceleration:
                {
                    var stats = em.GetComponent<Player>(playerEntity);
                    float newThrust = stats.Thrust * 1.1f;
                    em.AddComponent(playerEntity, new Player(
                        newThrust,
                        stats.SideThrust,
                        stats.BackThrust,
                        stats.Boost,
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
                    float newRotationSpeed = stats.RotationSpeed * 1.1f;
                    em.AddComponent(playerEntity, new Player(
                        stats.Thrust,
                        stats.SideThrust,
                        stats.BackThrust,
                        stats.Boost,
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
                    float newSideThrust = stats.SideThrust * 1.1f;
                    em.AddComponent(playerEntity, new Player(
                        stats.Thrust,
                        newSideThrust,
                        stats.BackThrust,
                        stats.Boost,
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
                    float newBackThrust = stats.BackThrust * 1.1f;
                    em.AddComponent(playerEntity, new Player(
                        stats.Thrust,
                        stats.SideThrust,
                        newBackThrust,
                        stats.Boost,
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
        if (slots.Used >= slots.Max) return;

        var playerPos = em.GetComponent<Position>(playerEntity);
        var playerRot = em.GetComponent<Rotation>(playerEntity);

        TurretDefinition definition = weaponName switch
        {
            "RailGun" => new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 500f, WeaponType.RailGun, AutoTarget: false),
            "TwinChainGun" => new(new Vector2(-12f, 0f), ArcOffset: MathF.PI / 4f, MathF.PI / 8f, 360f, WeaponType.TwinChainGun, AutoTarget: false),
            "AcidBubbleSpray" => new(Vector2.Zero, ArcOffset: 0f, MathF.PI / 4f, 250f, WeaponType.AcidBubbleSpray, AutoTarget: false),
            "PointDefenceTurret" => new(Vector2.Zero, ArcOffset: -MathF.PI / 4f, MathF.PI * 3 / 4f, 280f, WeaponType.PointDefenceTurret),
            _ => throw new InvalidOperationException($"Unknown weapon: {weaponName}")
        };

        var turretEntity = em.CreateEntity();
        Vector2 worldPos = playerPos.Value;
        em.AddComponent(turretEntity, new Position(worldPos));
        em.AddComponent(turretEntity, new Rotation(definition.ArcOffset));
        em.AddComponent(turretEntity, new Turret(Weapon: definition.Weapon.Stats, WeaponName: definition.Weapon.Name, ArcAngle: definition.ArcAngle, Range: definition.Range, AutoTarget: definition.AutoTarget));
        em.AddComponent(turretEntity, new TurretOffset(definition.Offset));
        em.AddComponent(turretEntity, new ArcOffset(definition.ArcOffset));

        em.AddComponent(playerEntity, new WeaponSlots(slots.Used + 1, slots.Max));
    }
}
