using System.Diagnostics;
using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Scenarios;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

// One playthrough: owns the world from ship selection to game over.
public sealed class GameSession
{
    // Public: WorldRenderer draws ammo as the segment it swept during the tick,
    // which needs the same dt the collision system used.
    public const float FixedDeltaTime = 1f / SpaceVorsApp.MaxFps;

    // Cap catch-up work after a hitch (window drag, GC pause) instead of running dozens of steps in one frame.
    private const float MaxAccumulator = 0.25f;

    // Diagnostics: world offset where the test enemy ship spawns. To the side of the fixed test asteroid at (0, -300) so collisions do not push it away.
    private static readonly Vector2 DiagnosticEnemySpawnOffset = new(250f, -250f);

    // Diagnostics: health of the spawned test enemy (high enough to survive auto-targeting fire until a manual click is tested).
    private const int DiagnosticTestEnemyHealth = 100000;

    // Diagnostics: player health set by key H so test enemies cannot end the run during visual tests.
    private const int DiagnosticTestPlayerHealth = 10000;

    private readonly EntityManager _em;
    private readonly WorldView _worldView;
    private readonly CommandBuffer _commands = new();
    private readonly Entity _playerEntity;
    private readonly Entity _cameraEntity;
    private readonly List<(Vector2 Position, float Size, Color Color, float Parallax)> _stars;
    private readonly List<(Vector2 Position, float Width, float Height, Color Color)> _clutter;
    private readonly ShipType _ship;
    private readonly bool _diagnostics;
    private readonly SimulationRunner _runner = new();

    // Diagnostics timing. Reused instances: a fresh Stopwatch per tick/frame would allocate on the
    // hot path, and DiagnosticLogger no-ops when SPACEVORS_DIAGNOSTIC is off.
    private readonly Stopwatch _tickSw = new();
    private readonly Stopwatch _renderSw = new();
    private readonly Stopwatch _frameSw = new();

    private float _accumulator;
    private bool _gameOver;

    // After the player dies the simulation keeps running this long so the death moment
    // plays out — full hull damage graphics (holes, embers), smoke and sparks — before
    // the game over overlay appears.
    private const float DeathAnimationSeconds = 1.2f;
    private float _deathCountdown = -1f; // < 0: player alive; >= 0: counting down to game over
    private bool _wasPaused;
    private bool _showStats;

    // Diagnostics: position the player is pinned to while T freeze is active.
    private Vector2? _diagnosticFreezePosition;

    // Non-null in perf-scenario mode: a seeded world, one tick per frame, no pause, auto-exit.
    private readonly ScenarioSpec? _scenario;
    private int _scenarioFramesMeasured;

    // Last frame's stage totals, in stopwatch ticks, for the [PERF] line.
    private long _lastSimTicks;
    private long _lastApplyTicks;
    private long _lastRenderTicks;
    private int _lastGc0, _lastGc1, _lastGc2;
    private long _lastAllocBytes;

    public bool ScenarioFinished => _scenario is not null && _scenarioFramesMeasured >= _scenario.MeasureTicks;

    public GameSession(ShipType ship, ScenarioSpec? scenario = null)
    {
        _scenario = scenario;

        // A scenario pins the viewport: world generation places mines and ships relative to the
        // screen and the renderer culls by screen box, so a different window would measure a
        // different world.
        var viewport = scenario is null
            ? new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight())
            : WorldView.DefaultViewportSize;

        var (em, playerEntity, cameraEntity, stars, clutter) = GameInitializer.Initialize(
            ship, viewport, scenario?.Seed ?? EntityManager.DefaultRngSeed, scenario is not null);
        _em = em;
        _worldView = new WorldView(em);
        _playerEntity = playerEntity;
        _cameraEntity = cameraEntity;
        _stars = stars;
        _clutter = clutter;
        _ship = ship;
        _diagnostics = Environment.GetEnvironmentVariable(DiagnosticLogger.EnvVar) == "1";

        if (scenario is not null)
        {
            _worldView.ViewportSize = viewport;
            ScenarioWorld.Apply(em, playerEntity, scenario);
            MarkGcCounters();
            Console.WriteLine($"[PERF-BEGIN] scenario={scenario.Name} ticks={scenario.MeasureTicks} bullets={scenario.BulletCount} budgetFrameMs={scenario.FrameMsBudget:F2}");
        }
    }

    // Returns true when the player restarts from game over (caller returns to ship select).
    public bool Update()
    {
        if (_gameOver && Raylib.IsKeyPressed(KeyboardKey.R)) return true;

        float frameTime = (float)Raylib.GetFrameTime();
        DiagnosticLogger.UpdateFps(frameTime);

        bool hasPendingChoice = _em.GetEntitiesWithComponents<PendingChoice>().Any();

        // Player died on the level-up frame: drop the stale choice; the death animation
        // in UpdateRunning then leads to game over like on any other frame.
        if (hasPendingChoice && _em.HasComponent<Dead>(_playerEntity))
        {
            ClearPendingChoices();
            hasPendingChoice = false;
        }

        if (_gameOver) _showStats = false;

        // Tab toggles the stats screen (also while picking an upgrade).
        // Escape is not used: raylib's default exit key would close the window.
        if (!_gameOver && Raylib.IsKeyPressed(KeyboardKey.Tab)) _showStats = !_showStats;

        // Scenario mode never pauses: a level-up applies its first option immediately, so a
        // PendingChoice cannot insert unmeasured frames into the run.
        if (_scenario is not null && hasPendingChoice)
        {
            ApplySelectedUpgrade(0);
            hasPendingChoice = false;
        }

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
        _frameSw.Restart();
        _accumulator = Math.Min(_accumulator + frameTime, MaxAccumulator);

        bool playerDead = _em.HasComponent<Dead>(_playerEntity);

        if (!_gameOver && !playerDead) ReadPlayerInput();

        StepSimulation();

        // Death animation: keep simulating (smoke, sparks, embers, hull shading) for a
        // moment after the fatal hit, then freeze into game over.
        if (!_gameOver && playerDead)
        {
            if (_deathCountdown < 0f) _deathCountdown = DeathAnimationSeconds;
            _deathCountdown -= frameTime;
            if (_deathCountdown <= 0f) _gameOver = true;
        }

        RenderGame();
        _frameSw.Stop();

        // The frame interval is measured here rather than taken from Raylib.GetFrameTime(): that
        // value describes the *previous* frame, which makes `other` land on the wrong row and can
        // even go negative.
        if (_scenario is not null) ReportScenarioFrame(TicksToMs(_frameSw.ElapsedTicks));
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

        if (_diagnostics && _diagnosticFreezePosition is { } freezePos)
        {
            // Diagnostics: T pins the player in place so kickback and collisions cannot move it during visual tests.
            _em.AddComponent(_playerEntity, new Position(freezePos));
            _em.AddComponent(_playerEntity, new Acceleration(Vector2.Zero));
        }
        else
        {
            _em.AddComponent(_playerEntity, new Acceleration(ComputeThrust(playerStats, playerRot.Angle)));
        }

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

        // Left click: lock the clicked enemy ship or mine as primary target; empty space clears it.
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            HandleTargetClick(new Vector2(mouseWorldX, mouseWorldY));

        SyncTurrets(playerPos.Value, playerRot.Angle);
        HandleDiagnosticKeys(playerStats);
    }

    private void HandleTargetClick(Vector2 worldPoint)
    {
        var target = PrimaryTargetPicker.Pick(_em, worldPoint);

        if (target.HasValue)
            _em.AddComponent(_playerEntity, new PrimaryTarget(target.Value));
        else if (_em.HasComponent<PrimaryTarget>(_playerEntity))
            _em.RemoveComponent<PrimaryTarget>(_playerEntity);

        var playerPos = _em.GetComponent<Position>(_playerEntity).Value;
        DiagnosticLogger.LogEvent("target", $"{(target.HasValue ? "locked" : "cleared")} click=({worldPoint.X}, {worldPoint.Y}) player=({playerPos.X}, {playerPos.Y})");
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

        // Diagnostic only: set player health very high so test enemies cannot end the run during visual tests.
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.H))
        {
            _em.AddComponent(_playerEntity, new Health(DiagnosticTestPlayerHealth, DiagnosticTestPlayerHealth));
            _em.AddComponent(_playerEntity, playerStats with { MaxHealth = DiagnosticTestPlayerHealth });
            DiagnosticLogger.LogEvent("diag", "H pressed");
        }

        // Diagnostic only: T toggles a position pin for stable visual tests.
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.T))
        {
            if (_diagnosticFreezePosition is null)
            {
                _diagnosticFreezePosition = _em.GetComponent<Position>(_playerEntity).Value;
                _em.AddComponent(_playerEntity, new Velocity(Vector2.Zero));
                DiagnosticLogger.LogEvent("diag", $"T freeze on at ({_diagnosticFreezePosition.Value.X}, {_diagnosticFreezePosition.Value.Y})");
            }
            else
            {
                _diagnosticFreezePosition = null;
                DiagnosticLogger.LogEvent("diag", "T freeze off");
            }
        }

        // Diagnostic only: spawn an enemy ship to test click targeting.
        // Excessive health so auto-targeting turrets cannot kill it before a manual click is tested.
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.N))
        {
            var pos = _em.GetComponent<Position>(_playerEntity);
            var spawnPos = pos.Value + DiagnosticEnemySpawnOffset;
            var testEnemy = _em.CreateEntity();
            EnemyShipFactory.AddComponents(_em, testEnemy, spawnPos, Vector2.Zero, 0f, 0f, EnemyShipType.Default);
            _em.AddComponent(testEnemy, new Health(DiagnosticTestEnemyHealth, DiagnosticTestEnemyHealth));
            DiagnosticLogger.LogEvent("target", $"test enemy {testEnemy} at ({spawnPos.X}, {spawnPos.Y})");
        }

        // Diagnostic only: halve the hp of the locked target (or nearest enemy ship) to reach
        // the smoke and hull damage shading tiers quickly.
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.K))
        {
            var victim = PickDamageTestVictim();
            if (victim.HasValue && _em.TryGetComponent<Health>(victim.Value, out var health))
            {
                int halved = Math.Max(health.Current / 2, 1);
                _em.AddComponent(victim.Value, new Health(halved, health.Max));
                DiagnosticLogger.LogEvent("diag", $"K pressed: hp of {victim.Value} -> {halved}/{health.Max}");
            }
            else
            {
                DiagnosticLogger.LogEvent("diag", "K pressed: no target to damage");
            }
        }

        // Diagnostic only: halve the player hp to walk the player hull through the damage
        // shading tiers (K only damages targets, never the player).
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.J))
        {
            if (_em.TryGetComponent<Health>(_playerEntity, out var health))
            {
                int halved = Math.Max(health.Current / 2, 1);
                _em.AddComponent(_playerEntity, new Health(halved, health.Max));
                DiagnosticLogger.LogEvent("diag", $"J pressed: player hp -> {halved}/{health.Max}");
            }
        }
    }

    // The locked primary target if alive, otherwise the nearest enemy ship with health.
    private Entity? PickDamageTestVictim()
    {
        if (_em.TryGetComponent<PrimaryTarget>(_playerEntity, out var primary)
            && _em.HasComponent<Health>(primary.Target)
            && !_em.HasComponent<Dead>(primary.Target))
            return primary.Target;

        var playerPos = _em.GetComponent<Position>(_playerEntity).Value;
        Entity? nearest = null;
        float bestDistSq = float.MaxValue;

        foreach (var (entity, _, _, pos) in _em.GetEntitiesWithComponents<EnemyShip, Health, Position>())
        {
            if (_em.HasComponent<Dead>(entity)) continue;
            var diff = pos.Value - playerPos;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                nearest = entity;
            }
        }

        return nearest;
    }

    private void StepSimulation()
    {
        // WorldView and CommandBuffer are reused across steps: the command list's backing
        // array (thousands of entries on ammo-heavy ticks) would otherwise be reallocated
        // every tick. Apply() clears the buffer after each phase.
        Action<string, long>? onSystemComplete = DiagnosticLogger.IsEnabled ? LogSystemTiming : null;

        // Scenario mode runs exactly one tick per rendered frame — the accumulator's 1-2 ticks per
        // frame is wall-clock noise — and pins the camera so screen culling cannot vary between runs.
        if (_scenario is not null)
        {
            _accumulator = FixedDeltaTime;
            _worldView.MouseScreenPosition = _worldView.ViewportSize * 0.5f;
        }

        while (_accumulator >= FixedDeltaTime)
        {
            DiagnosticLogger.LogFrameStart();

            if (_scenario is null)
            {
                _worldView.ViewportSize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                _worldView.MouseScreenPosition = new Vector2((float)Raylib.GetMouseX(), (float)Raylib.GetMouseY());
            }

            _tickSw.Restart();
            _lastApplyTicks = 0;
            _runner.RunTick(_worldView, _em, _commands, FixedDeltaTime, onSystemComplete, LogApplyTiming);
            _tickSw.Stop();
            _lastSimTicks = _tickSw.ElapsedTicks;
            DiagnosticLogger.LogSimTotal(_tickSw.ElapsedTicks);

            _accumulator -= FixedDeltaTime;
            _em.AddElapsedTime(FixedDeltaTime);
        }
    }

    private void LogApplyTiming(string phase, long elapsedTicks)
    {
        _lastApplyTicks += elapsedTicks;
        DiagnosticLogger.LogApply(phase, elapsedTicks);
    }

    private static void LogSystemTiming(string name, long ticks) => DiagnosticLogger.LogSystem(name, ticks);

    // One machine-readable row per measured scenario frame; .tools/scenario.sh turns these into
    // frames.jsonl. `apply` is already contained in `sim`; `other` is what neither stage accounts
    // for (input, camera, UI, GC, present wait) and should keep the fractions summing to ~1.
    private void ReportScenarioFrame(double frameMs)
    {
        if (ScenarioFinished) return;

        double simMs = TicksToMs(_lastSimTicks);
        double renderMs = TicksToMs(_lastRenderTicks);

        Console.WriteLine(
            $"[PERF] frame={++_scenarioFramesMeasured} dt={frameMs:F2} sim={simMs:F2} apply={TicksToMs(_lastApplyTicks):F2} " +
            $"render={renderMs:F2} other={frameMs - simMs - renderMs:F2} " +
            $"ents={Count<Position>()} ammo={Count<Ammo>()} sparks={Count<DamageSpark>()} explosions={Count<Explosion>()} " +
            $"beams={Count<LaserBeam>()} mines={Count<EnemyMine>()} ships={Count<EnemyShip>()} asteroids={Count<Asteroid>()} pickups={Count<XpPickup>()} " +
            GcColumns());

        if (_scenarioFramesMeasured == _scenario!.MeasureTicks)
            Console.WriteLine($"[PERF-END] scenario={_scenario.Name} budgetFrameMs={_scenario.FrameMsBudget:F2}");
    }

    // Garbage collected since the last row, plus bytes allocated in it: the sim's p99 spikes are
    // otherwise invisible in a timing-only trace, and the scenario runner correlates them with the
    // frames that were slow.
    private string GcColumns()
    {
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long allocated = GC.GetTotalAllocatedBytes(precise: false);

        string columns = $"gc0={gen0 - _lastGc0} gc1={gen1 - _lastGc1} gc2={gen2 - _lastGc2} allocKb={(allocated - _lastAllocBytes) / 1024}";

        _lastGc0 = gen0;
        _lastGc1 = gen1;
        _lastGc2 = gen2;
        _lastAllocBytes = allocated;
        return columns;
    }

    private void MarkGcCounters()
    {
        _lastGc0 = GC.CollectionCount(0);
        _lastGc1 = GC.CollectionCount(1);
        _lastGc2 = GC.CollectionCount(2);
        _lastAllocBytes = GC.GetTotalAllocatedBytes(precise: false);
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private int Count<T>() where T : notnull => _em.TryGetStorage(out ComponentStorage<T> storage) ? storage.Count : 0;

    private void StopThrusters()
    {
        _em.RemoveComponent<Acceleration>(_playerEntity);
        _em.RemoveComponent<AngularVelocity>(_playerEntity);
    }

    private int GetPlayerLevel() => _em.TryGetComponent<Player>(_playerEntity, out var player) ? player.Level : 1;

    private void RenderGame()
    {
        var renderCam = _em.GetComponent<Camera>(_cameraEntity);
        _renderSw.Restart();
        Renderer.Render(_em, renderCam.Target.X, renderCam.Target.Y, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), _gameOver, _stars, _clutter, _playerEntity, _ship, _diagnostics);
        _renderSw.Stop();
        _lastRenderTicks = _renderSw.ElapsedTicks;
        DiagnosticLogger.LogRender(_renderSw.ElapsedTicks);
    }

    private void RenderStatsScreen()
    {
        var statsCam = _em.GetComponent<Camera>(_cameraEntity);
        int windowWidth = Raylib.GetScreenWidth();
        int windowHeight = Raylib.GetScreenHeight();

        _renderSw.Restart();
        var screen = StatsScreenRenderer.Prepare(_em, _playerEntity, windowWidth, windowHeight, hasCards: false);

        Renderer.RenderPaused(_em, (float)statsCam.Target.X, (float)statsCam.Target.Y, windowWidth, windowHeight, _stars, _clutter, _playerEntity, _ship, _diagnostics,
            new Renderer.StatsOverlay(screen, null, GetPlayerLevel(), -1));
        _renderSw.Stop();
        DiagnosticLogger.LogRender(_renderSw.ElapsedTicks);
    }

    private void HandleUpgradeMenu()
    {
        // Game is paused — no simulation runs. Only handle choice input.
        bool hasPending = _em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pendingTuple);
        PendingUpgradeOptions? upgradeOptions = hasPending ? pendingTuple.Value2 : null;

        int windowWidth = Raylib.GetScreenWidth();
        int windowHeight = Raylib.GetScreenHeight();
        bool hasCards = upgradeOptions is { Options.Length: > 0 };
        var screen = StatsScreenRenderer.Prepare(_em, _playerEntity, windowWidth, windowHeight, hasCards);

        int hoveredIndex = hasCards
            ? StatsScreenRenderer.GetHoveredCardIndex(screen, upgradeOptions!.Value, Raylib.GetMouseX(), Raylib.GetMouseY(), windowWidth, windowHeight)
            : -1;

        int selectedIndex = ReadChoiceSelection(hoveredIndex);
        if (selectedIndex >= 0) ApplySelectedUpgrade(selectedIndex);

        var upgradeCam = _em.GetComponent<Camera>(_cameraEntity);
        _renderSw.Restart();
        Renderer.RenderPaused(_em, (float)upgradeCam.Target.X, (float)upgradeCam.Target.Y, windowWidth, windowHeight, _stars, _clutter, _playerEntity, _ship, _diagnostics,
            new Renderer.StatsOverlay(screen, upgradeOptions, GetPlayerLevel(), hoveredIndex));
        _renderSw.Stop();
        DiagnosticLogger.LogRender(_renderSw.ElapsedTicks);
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
        bool hasChoice = _em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var choiceTuple);

        if (hasChoice && selectedIndex < choiceTuple.Value2.Options.Length)
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
                    em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { FireRate = newFireRate, PelletCount = newPelletCount } });
                }
                break;

            case UpgradeOption.ProjectileSpeed:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                    em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { AmmoSpeed = turret.Weapon.AmmoSpeed * def.Multiplier } });
                break;

            case UpgradeOption.PickupRadius:
                em.AddComponent(playerEntity, playerStats with { PickupRadius = playerStats.PickupRadius * def.Multiplier });
                break;

            case UpgradeOption.Range:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                    em.AddComponent(turretEntity, turret with { Range = turret.Range * def.Multiplier, Weapon = turret.Weapon with { ShotLifetime = turret.Weapon.ShotLifetime * def.Multiplier } });
                break;

            case UpgradeOption.Damage:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                    em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { Damage = turret.Weapon.Damage + def.Additive } });
                break;

            case UpgradeOption.Hp:
                if (!em.HasComponent<Health>(playerEntity)) break;
                var currentHealth = em.GetComponent<Health>(playerEntity);
                int newMaxHealth = playerStats.MaxHealth + def.Additive;
                em.AddComponent(playerEntity, new Health(currentHealth.Current + def.Additive, newMaxHealth));
                em.AddComponent(playerEntity, playerStats with { MaxHealth = newMaxHealth });
                break;

            case UpgradeOption.ForwardAcceleration:
                em.AddComponent(playerEntity, playerStats with { Thrust = playerStats.Thrust * def.Multiplier });
                break;

            case UpgradeOption.TurnSpeed:
                em.AddComponent(playerEntity, playerStats with { RotationSpeed = playerStats.RotationSpeed * def.Multiplier });
                break;

            case UpgradeOption.SideThrust:
                em.AddComponent(playerEntity, playerStats with { SideThrust = playerStats.SideThrust * def.Multiplier });
                break;

            case UpgradeOption.BackThrust:
                em.AddComponent(playerEntity, playerStats with { BackThrust = playerStats.BackThrust * def.Multiplier });
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
