using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

// One playthrough: owns the world from ship selection to game over.
public sealed class GameSession
{
    private const float FixedDeltaTime = 1f / SpaceVorsApp.MaxFps;

    // Cap catch-up work after a hitch (window drag, GC pause) instead of running dozens of steps in one frame.
    private const float MaxAccumulator = 0.25f;

    private readonly EntityManager _em;
    private readonly Entity _playerEntity;
    private readonly Entity _cameraEntity;
    private readonly List<(Vector2 Position, float Size, Color Color, float Parallax)> _stars;
    private readonly List<(Vector2 Position, float Width, float Height, Color Color)> _clutter;
    private readonly ShipType _ship;
    private readonly bool _diagnostics;
    private readonly SimulationRunner _runner = new();

    private float _accumulator;
    private bool _gameOver;
    private bool _wasPaused;
    private bool _showStats;

    public GameSession(ShipType ship)
    {
        var (em, playerEntity, cameraEntity, stars, clutter) = GameInitializer.Initialize(ship, new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()));
        _em = em;
        _playerEntity = playerEntity;
        _cameraEntity = cameraEntity;
        _stars = stars;
        _clutter = clutter;
        _ship = ship;
        _diagnostics = Environment.GetEnvironmentVariable("SPACEVORS_DIAGNOSTIC") == "1";
    }

    // Returns true when the player restarts from game over (caller returns to ship select).
    public bool Update()
    {
        if (_gameOver && Raylib.IsKeyPressed(KeyboardKey.R)) return true;

        float frameTime = (float)Raylib.GetFrameTime();
        DiagnosticLogger.UpdateFps(frameTime);

        bool hasPendingChoice = _em.GetEntitiesWithComponents<PendingChoice>().Any();

        // Player died on the level-up frame: drop the stale choice and show game over.
        if (hasPendingChoice && _em.HasComponent<Dead>(_playerEntity))
        {
            ClearPendingChoices();
            hasPendingChoice = false;
            _gameOver = true;
        }

        if (_gameOver) _showStats = false;

        // Tab toggles the stats screen (also while picking an upgrade).
        // Escape is not used: raylib's default exit key would close the window.
        if (!_gameOver && Raylib.IsKeyPressed(KeyboardKey.Tab)) _showStats = !_showStats;

        bool paused = hasPendingChoice || _showStats;

        // Entering pause: stop the ship's thrusters so flames/lights don't burn behind the menu.
        if (paused && !_wasPaused) StopThrusters();
        _wasPaused = paused;

        if (!paused) UpdateRunning(frameTime);
        else if (_showStats) RenderStatsScreen();
        else HandleUpgradeMenu();

        return false;
    }

    private void UpdateRunning(float frameTime)
    {
        _accumulator = Math.Min(_accumulator + frameTime, MaxAccumulator);

        if (!_gameOver) ReadPlayerInput();

        StepSimulation();

        if (!_gameOver && _em.HasComponent<Dead>(_playerEntity))
            _gameOver = true;

        RenderGame();
    }

    private void ReadPlayerInput()
    {
        var playerPos = _em.GetComponent<Position>(_playerEntity);
        var playerRot = _em.GetComponent<Rotation>(_playerEntity);
        var playerStats = _em.GetComponent<Player>(_playerEntity);
        var aimCam = _em.GetComponent<Camera>(_cameraEntity);

        float mouseWorldX = aimCam.Target.X + ((float)Raylib.GetMouseX() - Raylib.GetScreenWidth() / 2f);
        float mouseWorldY = aimCam.Target.Y + ((float)Raylib.GetMouseY() - Raylib.GetScreenHeight() / 2f);
        Vector2 toMouse = new(mouseWorldX - playerPos.Value.X, mouseWorldY - playerPos.Value.Y);
        float distToMouse = (float)Math.Sqrt(toMouse.X * toMouse.X + toMouse.Y * toMouse.Y);

        _em.AddComponent(_playerEntity, new Acceleration(ComputeThrust(playerStats, playerRot.Angle)));

        // Mouse aiming: set angular velocity toward cursor (rad/s)
        if (distToMouse > 1f)
        {
            float targetAngle = (float)Math.Atan2(toMouse.X, -toMouse.Y);
            float angleDiff = targetAngle - playerRot.Angle;
            while (angleDiff > MathF.PI) angleDiff -= MathF.PI * 2f;
            while (angleDiff < -MathF.PI) angleDiff += MathF.PI * 2f;

            float newAngVel = Math.Clamp(angleDiff / FixedDeltaTime, -playerStats.RotationSpeed, playerStats.RotationSpeed);
            _em.AddComponent(_playerEntity, new AngularVelocity(newAngVel));
        }

        SyncTurrets(playerPos.Value, playerRot.Angle);
        HandleDiagnosticKeys(playerStats);
    }

    private static Vector2 ComputeThrust(Player stats, float angle)
    {
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);
        Vector2 thrustAccel = Vector2.Zero;

        // Forward thrust (W) — boost applies only to forward
        if (Raylib.IsKeyDown(KeyboardKey.W))
        {
            float forwardForce = stats.Thrust;
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
                forwardForce *= stats.Boost;
            thrustAccel += new Vector2(sin * forwardForce, -cos * forwardForce);
        }

        // Backward thrust (S)
        if (Raylib.IsKeyDown(KeyboardKey.S))
            thrustAccel += new Vector2(-sin * stats.BackThrust, cos * stats.BackThrust);

        // Left sideways thrust (A)
        if (Raylib.IsKeyDown(KeyboardKey.A))
            thrustAccel += new Vector2(-cos * stats.SideThrust, -sin * stats.SideThrust);

        // Right sideways thrust (D)
        if (Raylib.IsKeyDown(KeyboardKey.D))
            thrustAccel += new Vector2(cos * stats.SideThrust, sin * stats.SideThrust);

        return thrustAccel;
    }

    private void SyncTurrets(Vector2 playerPos, float angle)
    {
        // Sync turret positions and rotations to player ship
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        foreach (var (turretEntity, turret, offset, arcOffset) in _em.GetEntitiesWithComponents<Turret, TurretOffset, ArcOffset>())
        {
            if (turret.IsEnemy) continue;
            var rotatedOffset = new Vector2(
                offset.Value.X * cos - offset.Value.Y * sin,
                offset.Value.X * sin + offset.Value.Y * cos);

            _em.AddComponent(turretEntity, new Position(playerPos + rotatedOffset));
            _em.AddComponent(turretEntity, new Rotation(angle + arcOffset.Angle));
        }
    }

    private void HandleDiagnosticKeys(Player playerStats)
    {
        // Diagnostic only: force level-up to test the upgrade screen
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.L))
            _em.AddComponent(_playerEntity, playerStats with { Xp = playerStats.Level * 10 });

        // Diagnostic only: spawn a test explosion on the fixed test asteroid to verify lighting
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.M))
        {
            var testExplosion = _em.CreateEntity();
            _em.AddComponent(testExplosion, new Position(new Vector2(0f, -300f)));
            _em.AddComponent(testExplosion, new Explosion(80f, 1.5f, 1.5f));
        }
    }

    private void StepSimulation()
    {
        // Fixed timestep simulation
        while (_accumulator >= FixedDeltaTime)
        {
            DiagnosticLogger.LogFrameStart();

            var view = new WorldView(_em) { ViewportSize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()) };
            var commands = new CommandBuffer();

            _runner.RunPhase(view, commands, _runner.MovementSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
            commands.Apply(_em);

            _runner.RunPhase(view, commands, _runner.ActionSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
            commands.Apply(_em);

            _runner.RunPhase(view, commands, _runner.ResolutionSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
            commands.Apply(_em);

            _runner.RunPhase(view, commands, _runner.CleanupSystems, FixedDeltaTime, (name, ticks) => DiagnosticLogger.LogSystem(name, ticks));
            commands.Apply(_em);

            _accumulator -= FixedDeltaTime;
            _em.AddElapsedTime(FixedDeltaTime);
        }
    }

    private void StopThrusters()
    {
        _em.RemoveComponent<Acceleration>(_playerEntity);
        _em.RemoveComponent<AngularVelocity>(_playerEntity);
    }

    private int GetPlayerLevel() => _em.TryGetComponent<Player>(_playerEntity, out var player) ? player.Level : 1;

    private void RenderGame()
    {
        var renderCam = _em.GetComponent<Camera>(_cameraEntity);
        Renderer.Render(_em, renderCam.Target.X, renderCam.Target.Y, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), _gameOver, _stars, _clutter, _playerEntity, _ship, _diagnostics);
    }

    private void RenderStatsScreen()
    {
        var statsCam = _em.GetComponent<Camera>(_cameraEntity);
        Renderer.RenderPaused(_em, (float)statsCam.Target.X, (float)statsCam.Target.Y, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), _stars, _clutter, _playerEntity, _ship, _diagnostics, null, GetPlayerLevel(), -1);
    }

    private void HandleUpgradeMenu()
    {
        // Game is paused — no simulation runs. Only handle choice input.
        _em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pendingTuple);
        PendingUpgradeOptions? upgradeOptions = null;
        if (pendingTuple.Entity.Value >= 0)
            upgradeOptions = pendingTuple.Value2;

        int hoveredIndex = -1;
        if (upgradeOptions is { Options.Length: > 0 } opts)
            hoveredIndex = StatsScreenRenderer.GetHoveredCardIndex(_em, _playerEntity, opts, Raylib.GetMouseX(), Raylib.GetMouseY(), Raylib.GetScreenWidth(), Raylib.GetScreenHeight());

        int selectedIndex = ReadChoiceSelection(hoveredIndex);
        if (selectedIndex >= 0) ApplySelectedUpgrade(selectedIndex);

        var upgradeCam = _em.GetComponent<Camera>(_cameraEntity);
        Renderer.RenderPaused(_em, (float)upgradeCam.Target.X, (float)upgradeCam.Target.Y, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), _stars, _clutter, _playerEntity, _ship, _diagnostics, upgradeOptions, GetPlayerLevel(), hoveredIndex);
    }

    private static int ReadChoiceSelection(int hoveredIndex)
    {
        for (int i = 1; i <= 5; i++)
        {
            var key = i switch { 1 => KeyboardKey.One, 2 => KeyboardKey.Two, 3 => KeyboardKey.Three, 4 => KeyboardKey.Four, 5 => KeyboardKey.Five, _ => (KeyboardKey)0 };
            if (Raylib.IsKeyPressed(key)) return i - 1;
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left)) return hoveredIndex;
        return -1;
    }

    private void ApplySelectedUpgrade(int selectedIndex)
    {
        _em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var choiceTuple);
        Entity choiceEntity = choiceTuple.Entity;

        if (choiceEntity.Value >= 0 && selectedIndex < choiceTuple.Value2.Options.Length)
            ApplyUpgrade(_em, _playerEntity, choiceTuple.Value2.Options[selectedIndex]);

        ClearPendingChoices();
    }

    private void ClearPendingChoices()
    {
        foreach (var (entity, _) in _em.GetEntitiesWithComponents<PendingChoice>().ToList())
            _em.DestroyEntity(entity);
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
