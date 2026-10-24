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

    // One threat read a second: the `[THREAT]` line goes to the log always, and rides into the
    // diagnostics overlay (plans/DIFFICULTY_SCALING.md R2.8).
    private const float ThreatReadoutInterval = 1f;
    private float _threatNextAt;
    private string _threatLine = string.Empty;
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

    // Which menu is on screen while the game is paused: the upgrade cards, then the mount/cargo screen
    // where a weapon gets placed on the hull or left in cargo.
    enum UpgradeStep { Cards, Mounts }

    UpgradeStep _upgradeStep = UpgradeStep.Cards;

    // The weapon in hand on the mount screen: a new weapon from a card, or one taken down from the
    // hull (then it is already in cargo). "" means nothing is in hand.
    string _heldWeapon = "";
    // The held weapon is the card's, so it is not the player's yet: backing out hands the card back.
    bool _heldIsCardWeapon;
    // The card's weapon has been mounted or stored, so the level-up is spent even while a displaced
    // gun is still in hand.
    bool _cardResolved;
    // The mount screen was opened from the stats screen instead of from a card.
    bool _mountsFromStats;

    // raylib closes the window on its exit key (Escape by default); key 0 is no key at all, which is
    // how the mount screen gets Escape back for "back".
    const KeyboardKey DefaultExitKey = KeyboardKey.Escape;
    const KeyboardKey NoExitKey = (KeyboardKey)0;

    // Diagnostics: position the player is pinned to while T freeze is active.
    private Vector2? _diagnosticFreezePosition;

    // Non-null in perf-scenario mode: a seeded world, one tick per frame, no pause, auto-exit.
    private readonly ScenarioSpec? _scenario;
    private int _scenarioFramesMeasured;
    private HashSet<int> _shotFrames = [];

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
            _shotFrames = ParseShotFrames(scenario);
            MarkGcCounters();
            Console.WriteLine($"[PERF-BEGIN] scenario={scenario.Name} ticks={scenario.MeasureTicks} bullets={scenario.BulletCount} budgetFrameMs={scenario.FrameMsBudget:F2} expectedOverBudget={scenario.ExpectedOverBudget}");
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

        // Tab toggles the stats screen (also while picking an upgrade). It does nothing on the mount
        // screen, which has its own way out (Escape).
        bool onMountsScreen = _upgradeStep == UpgradeStep.Mounts;
        if (!_gameOver && !onMountsScreen && Raylib.IsKeyPressed(KeyboardKey.Tab)) _showStats = !_showStats;

        // Scenario mode never pauses: a level-up applies its first option immediately, so a
        // PendingChoice cannot insert unmeasured frames into the run.
        if (_scenario is not null && hasPendingChoice)
        {
            ChooseUpgrade(0, autoMountInSlot: true);
            hasPendingChoice = false;
        }

        bool paused = hasPendingChoice || _showStats || onMountsScreen;
        Raylib.SetExitKey(onMountsScreen ? NoExitKey : DefaultExitKey);

        // Entering pause: stop the ship's thrusters so flames/lights don't burn behind the menu.
        if (paused && !_wasPaused) StopThrusters();
        _wasPaused = paused;

        if (!paused) UpdateRunning(frameTime);
        else if (onMountsScreen) HandleMountsScreen();
        else if (_showStats) HandleStatsScreen();
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

        UpdateThreatReadout();

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

    // The threat readout: one row a second saying what the fleet can currently throw at the hull and
    // what the hull can answer with. It prints in every run — it is the tuning record behind the
    // difficulty knobs — and the same row shows on screen when diagnostics mode is on.
    private void UpdateThreatReadout()
    {
        if (_gameOver || _em.ElapsedTime < _threatNextAt) return;
        _threatNextAt = _em.ElapsedTime + ThreatReadoutInterval;

        var s = ThreatMeter.Measure(_worldView);
        // `tier=` is the stat tier and the armament rung it has reached; `missiles=` is the enemy's guided
        // rounds in the air over what the ladder allows, which is the number the missile knobs are tuned
        // against.
        _threatLine = $"[THREAT] t={s.Elapsed:F0}s tier={s.Tier}/{s.CompositionTier} ships={s.Ships} ring={s.ShipsInRing} " +
                      $"rounds={s.EnemyRounds} missiles={s.EnemyMissiles}/{s.MissileCap} in={s.IncomingDps:F1}/s out={s.PlayerDps:F1}/s " +
                      $"hp={s.Health}/{s.MaxHealth} lvl={s.Level} mines={s.Mines} rocks={s.SmallRocks}";
        DiagnosticLogger.LogThreat(_threatLine);
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

    private void SyncTurrets(Vector2 playerPos, float angle) => TurretMount.SyncWorldTransforms(_em, playerPos, angle);

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
            EnemyShipFactory.AddComponents(_em, testEnemy, spawnPos, Vector2.Zero, 0f, 0f, EnemyShipType.Default, EnemyArmament.Guns);
            _em.AddComponent(testEnemy, new Health(DiagnosticTestEnemyHealth, DiagnosticTestEnemyHealth));
            DiagnosticLogger.LogEvent("target", $"test enemy {testEnemy} at ({spawnPos.X}, {spawnPos.Y})");
        }

        // Diagnostic only: spawn a rack-armed hull, so an enemy missile can be watched without waiting for
        // the armament ladder to roll one (composition tier 3, ~3 minutes of play).
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.U))
        {
            var pos = _em.GetComponent<Position>(_playerEntity);
            var spawnPos = pos.Value + DiagnosticEnemySpawnOffset;
            var rackHull = _em.CreateEntity();
            EnemyShipFactory.AddComponents(_em, rackHull, spawnPos, Vector2.Zero, 0f, 0f,
                EnemyShipType.Interceptor, EnemyArmament.Missiles, _em.ElapsedTime);
            _em.AddComponent(rackHull, new Health(DiagnosticTestEnemyHealth, DiagnosticTestEnemyHealth));
            DiagnosticLogger.LogEvent("target", $"rack test hull {rackHull} at ({spawnPos.X}, {spawnPos.Y})");
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

        // Diagnostic only: B toggles the guided-round bench — the missile draw repeated over every heading
        // and both plume sizes at fixed screen positions — so one screenshot measures the plume in all
        // sixteen orientations with nothing else in the frame orange or grey.
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.B))
        {
            WorldRenderer.ShowMissileBench = !WorldRenderer.ShowMissileBench;
            DiagnosticLogger.LogEvent("diag", $"B: missile bench {(WorldRenderer.ShowMissileBench ? "on" : "off")}");
        }

        // Diagnostic only: V toggles the plume anchor overlay — for every live missile and every bench dart
        // it marks the plume centre the code computes and the heading line it sits on, from the same helper
        // the draw calls. A flame not centred on that cross is a placement bug rather than an eyesight one.
        if (_diagnostics && Raylib.IsKeyPressed(KeyboardKey.V))
        {
            WorldRenderer.ShowPlumeAnchors = !WorldRenderer.ShowPlumeAnchors;
            DiagnosticLogger.LogEvent("diag", $"V: plume anchors {(WorldRenderer.ShowPlumeAnchors ? "on" : "off")}");
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

        int frame = ++_scenarioFramesMeasured;
        bool shot = _shotFrames.Contains(frame);

        Console.WriteLine(
            $"[PERF] frame={frame} dt={frameMs:F2} sim={simMs:F2} apply={TicksToMs(_lastApplyTicks):F2} " +
            $"render={renderMs:F2} other={frameMs - simMs - renderMs:F2} " +
            $"ents={Count<Position>()} ammo={Count<Ammo>()} sparks={Count<DamageSpark>()} explosions={Count<Explosion>()} " +
            $"beams={Count<LaserBeam>()} mines={Count<EnemyMine>()} ships={Count<EnemyShip>()} asteroids={Count<Asteroid>()} pickups={Count<XpPickup>()} " +
            GcColumns() + (shot ? " shot=1" : string.Empty));

        if (frame == _scenario!.MeasureTicks)
            Console.WriteLine($"[PERF-END] scenario={_scenario.Name} budgetFrameMs={_scenario.FrameMsBudget:F2}");

        // Captured after the row is written, so the PNG encode cannot inflate this frame; its cost
        // lands on the next row, which is why the aggregator drops shot frames and their successors.
        if (shot) Raylib.TakeScreenshot(ShotFileName(_scenario.Name, frame));
    }

    // SPACEVORS_SCENARIO_SHOTS: frames to write as PNGs - 1-based numbers, or the words start, mid
    // and end. The game captures its own frames because raylib's F12 path needs the window to be
    // focused, which a headless display does not reliably provide.
    private static HashSet<int> ParseShotFrames(ScenarioSpec spec)
    {
        var frames = new HashSet<int>();

        foreach (string part in (Environment.GetEnvironmentVariable(ScenarioShotsEnvVar) ?? string.Empty)
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int frame = part.ToLowerInvariant() switch
            {
                "start" => FirstScenarioFrame,
                "mid" => spec.MeasureTicks / 2,
                "end" => spec.MeasureTicks,
                _ => int.TryParse(part, out int parsed) ? parsed : 0,
            };

            if (frame >= FirstScenarioFrame && frame <= spec.MeasureTicks) frames.Add(frame);
        }

        return frames;
    }

    private static string ShotFileName(string scenario, int frame) => $"scenario-{scenario}-frame{frame:D3}.png";

    const string ScenarioShotsEnvVar = "SPACEVORS_SCENARIO_SHOTS";
    const int FirstScenarioFrame = 1;

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
        Renderer.Render(_em, renderCam.Target.X, renderCam.Target.Y, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), _gameOver, _stars, _clutter, _playerEntity, _ship, _diagnostics,
            _diagnostics ? _threatLine : string.Empty);
        _renderSw.Stop();
        _lastRenderTicks = _renderSw.ElapsedTicks;
        DiagnosticLogger.LogRender(_renderSw.ElapsedTicks);
    }

    // The stats screen: read-only stats plus the way into mount and cargo management.
    private void HandleStatsScreen()
    {
        int windowWidth = Raylib.GetScreenWidth();
        int windowHeight = Raylib.GetScreenHeight();
        bool canManageMounts = !_em.GetEntitiesWithComponents<PendingChoice>().Any();

        _renderSw.Restart();
        var screen = StatsScreenRenderer.Prepare(_em, _playerEntity, windowWidth, windowHeight, hasCards: false);

        var (buttonX, buttonY, buttonW, buttonH) = StatsScreenRenderer.ManageMountsButtonRect(windowWidth, windowHeight, screen.Scale);
        int mouseX = Raylib.GetMouseX();
        int mouseY = Raylib.GetMouseY();
        bool buttonHovered = canManageMounts
            && mouseX >= buttonX && mouseX <= buttonX + buttonW && mouseY >= buttonY && mouseY <= buttonY + buttonH;

        if (buttonHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) OpenMountsFromStats();

        var statsCam = _em.GetComponent<Camera>(_cameraEntity);
        Renderer.RenderPaused(_em, statsCam.Target.X, statsCam.Target.Y, windowWidth, windowHeight, _stars, _clutter, _playerEntity, _ship, _diagnostics,
            new Renderer.StatsOverlay(screen, null, GetPlayerLevel(), -1, canManageMounts, buttonHovered));
        _renderSw.Stop();
        DiagnosticLogger.LogRender(_renderSw.ElapsedTicks);
    }

    private void HandleUpgradeMenu()
    {
        // Game is paused — no simulation runs. Only handle choice input.
        int windowWidth = Raylib.GetScreenWidth();
        int windowHeight = Raylib.GetScreenHeight();

        _renderSw.Restart();
        HandleCardStep(windowWidth, windowHeight);
        _renderSw.Stop();
        DiagnosticLogger.LogRender(_renderSw.ElapsedTicks);
    }

    private void HandleCardStep(int windowWidth, int windowHeight)
    {
        bool hasPending = _em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pendingTuple);
        PendingUpgradeOptions? upgradeOptions = hasPending ? pendingTuple.Value2 : null;

        bool hasCards = upgradeOptions is { Options.Length: > 0 };
        var screen = StatsScreenRenderer.Prepare(_em, _playerEntity, windowWidth, windowHeight, hasCards);

        int hoveredIndex = hasCards
            ? StatsScreenRenderer.GetHoveredCardIndex(screen, upgradeOptions!.Value, Raylib.GetMouseX(), Raylib.GetMouseY(), windowWidth, windowHeight)
            : -1;

        int selectedIndex = ReadChoiceSelection(hoveredIndex);
        if (selectedIndex >= 0) ChooseUpgrade(selectedIndex, autoMountInSlot: _scenario is not null);

        RenderCardScreen(screen, upgradeOptions, hoveredIndex, windowWidth, windowHeight);
    }

    void RenderCardScreen(StatsScreenRenderer.Prepared screen, PendingUpgradeOptions? options, int hoveredIndex, int windowWidth, int windowHeight)
    {
        var upgradeCam = _em.GetComponent<Camera>(_cameraEntity);
        Renderer.RenderPaused(_em, upgradeCam.Target.X, upgradeCam.Target.Y, windowWidth, windowHeight, _stars, _clutter, _playerEntity, _ship, _diagnostics,
            new Renderer.StatsOverlay(screen, options, GetPlayerLevel(), hoveredIndex, false, false));
    }

    // The mount/cargo screen: put what is in hand into a mount, take a gun down into cargo, or leave
    // a weapon inactive in cargo. Reached from a new-weapon card or from the stats screen.
    private void HandleMountsScreen()
    {
        int windowWidth = Raylib.GetScreenWidth();
        int windowHeight = Raylib.GetScreenHeight();
        var cargo = ReadCargo();

        int mouseX = Raylib.GetMouseX();
        int mouseY = Raylib.GetMouseY();
        var cargoNames = new List<string>();
        foreach (var carried in cargo.Weapons) cargoNames.Add(carried.WeaponName);

        var mounts = MountMenuRenderer.Prepare(_em, _ship, windowWidth, windowHeight, _heldWeapon, cargoNames, MountsHint(cargo));
        int hoveredMount = MountMenuRenderer.GetHoveredMountIndex(mounts, mouseX, mouseY);
        int hoveredCargo = MountMenuRenderer.GetHoveredCargoIndex(mounts, mouseX, mouseY);

        int picked = ReadMountSelection(hoveredMount, mounts.Markers);
        if (picked >= 0) ApplyMountClick(mounts.Markers[picked]);
        else if (hoveredCargo >= 0 && Raylib.IsMouseButtonPressed(MouseButton.Left)) _heldWeapon = mounts.Cargo[hoveredCargo].WeaponName;
        else if (Raylib.IsMouseButtonPressed(MouseButton.Right)) DropHold();
        else if (Raylib.IsKeyPressed(KeyboardKey.Escape)) LeaveMountsScreen();
        else if (Raylib.IsKeyPressed(KeyboardKey.C)) StoreHeldInCargo();

        // The card has been paid for and nothing is left in hand: the choice is done.
        if (!_mountsFromStats && _cardResolved && _heldWeapon.Length == 0)
        {
            ClearPendingChoices();
            return;
        }

        _renderSw.Restart();
        var mountCam = _em.GetComponent<Camera>(_cameraEntity);
        Renderer.RenderMount(_em, mountCam.Target.X, mountCam.Target.Y, windowWidth, windowHeight, _stars, _clutter, _playerEntity, _diagnostics, mounts, hoveredMount, hoveredCargo);
        _renderSw.Stop();
        DiagnosticLogger.LogRender(_renderSw.ElapsedTicks);
    }

    private string MountsHint(WeaponCargo cargo)
    {
        if (_heldWeapon.Length == 0)
            return "Click a mounted turret, or press its number, to take it down into cargo — Esc to go back";

        string exit = _cardResolved ? "Esc to finish" : _mountsFromStats ? "Esc to go back" : "Esc back to the cards";

        if (_heldIsCardWeapon || !cargo.Holds(_heldWeapon))
            return $"Green ring = the mount fits {_heldWeapon}: click one, or press its number — C stores it in cargo — {exit}";

        return $"{_heldWeapon} is in hand: click a mount to fit it, a filled mount swaps it into cargo — {exit}";
    }

    // A mount click. With a weapon in hand it goes into that mount, and any gun already there comes
    // off into cargo and becomes what the player holds next. With nothing in hand, the mounted gun
    // comes down into cargo and is picked up — this is how a turret is taken out of the fight.
    private void ApplyMountClick(MountMenuRenderer.SlotMarker marker)
    {
        string placingName = _heldWeapon;
        var playerPos = _em.GetComponent<Position>(_playerEntity).Value;
        float hullRotation = _em.GetComponent<Rotation>(_playerEntity).Angle;

        // Nothing in hand: the mounted gun comes down, upgraded stats and all, and is what is held now.
        if (_heldWeapon.Length == 0)
        {
            if (!_em.TryGetComponent<Turret>(marker.SlotEntity, out var taken)) return;

            WriteCargo(ReadCargo().With(taken));
            TurretMount.Unmount(_em, marker.SlotEntity);
            _heldWeapon = taken.WeaponName;
            _heldIsCardWeapon = false;
            DiagnosticLogger.LogEvent("MOUNT", $"{taken.WeaponName} taken off {marker.Slot.Name} into cargo");
            return;
        }

        // Something in hand: the gun in the way is displaced into cargo and becomes the new hold.
        if (_em.TryGetComponent<Turret>(marker.SlotEntity, out var displaced))
        {
            WriteCargo(ReadCargo().With(displaced));
            _heldWeapon = displaced.WeaponName;
            _heldIsCardWeapon = false;
            DiagnosticLogger.LogEvent("MOUNT", $"{displaced.WeaponName} displaced from {marker.Slot.Name} into cargo");
        }
        else
        {
            _heldWeapon = "";
            _heldIsCardWeapon = false;
        }

        if (ReadCargo().TryFind(placingName, out var carried))
        {
            WriteCargo(ReadCargo().Without(carried.WeaponName));
            TurretMount.Mount(_em, marker.SlotEntity, marker.Slot, carried, playerPos, hullRotation);
            DiagnosticLogger.LogEvent("MOUNT", $"{carried.WeaponName} mounted out of cargo in {marker.Slot.Name}");
        }
        else if (WeaponType.FromName(placingName) is { } fresh)
        {
            TurretMount.Mount(_em, marker.SlotEntity, marker.Slot, fresh, playerPos, hullRotation);
            DiagnosticLogger.LogEvent("MOUNT", $"new weapon {fresh.Name} mounted in {marker.Slot.Name}");
        }

        if (!_mountsFromStats) _cardResolved = true;
    }

    // Right-click puts the held weapon down. A card weapon that was never placed goes back to the card.
    private void DropHold()
    {
        if (_heldIsCardWeapon && !_cardResolved)
        {
            LeaveMountsScreen();
            return;
        }

        _heldWeapon = "";
    }

    private void StoreHeldInCargo()
    {
        if (_heldWeapon.Length == 0) return;

        string stored = _heldWeapon;
        var cargo = ReadCargo();

        // A weapon from a card has never been on the hull, so it enters cargo as a fresh turret; one
        // taken off the hull is already there with its upgrades.
        if (!cargo.Holds(stored) && WeaponType.FromName(stored) is { } fresh)
            WriteCargo(cargo.With(TurretMount.CarriedTurret(fresh)));

        _heldIsCardWeapon = false;
        _heldWeapon = "";
        if (!_mountsFromStats) _cardResolved = true;
        DiagnosticLogger.LogEvent("MOUNT", $"{stored} stored in cargo");
    }

    private void LeaveMountsScreen()
    {
        if (!_mountsFromStats && _cardResolved)
        {
            ClearPendingChoices();
            return;
        }

        _upgradeStep = UpgradeStep.Cards;
        _heldWeapon = "";
        _heldIsCardWeapon = false;
        _cardResolved = false;
        _mountsFromStats = false;
    }

    private void OpenMountsFromStats()
    {
        _upgradeStep = UpgradeStep.Mounts;
        _mountsFromStats = true;
        _heldWeapon = "";
        _heldIsCardWeapon = false;
        _cardResolved = false;
    }

    private WeaponCargo ReadCargo() =>
        _em.TryGetComponent<WeaponCargo>(_playerEntity, out var cargo) ? cargo : WeaponCargo.Empty;

    private void WriteCargo(WeaponCargo cargo) => _em.AddComponent(_playerEntity, cargo);

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

    // Keys 1..n pick the n-numbered mount on the slot screen; ships have at most this many.
    static readonly KeyboardKey[] SlotKeys = [KeyboardKey.One, KeyboardKey.Two, KeyboardKey.Three, KeyboardKey.Four, KeyboardKey.Five, KeyboardKey.Six, KeyboardKey.Seven, KeyboardKey.Eight];

    private static int ReadMountSelection(int hoveredMount, MountMenuRenderer.SlotMarker[] markers)
    {
        for (int key = 0; key < SlotKeys.Length; key++)
        {
            int marker = IndexOfNumber(markers, key + 1);
            if (marker >= 0 && markers[marker].Selectable && Raylib.IsKeyPressed(SlotKeys[key])) return marker;
        }

        return Raylib.IsMouseButtonPressed(MouseButton.Left) ? hoveredMount : -1;
    }

    static int IndexOfNumber(MountMenuRenderer.SlotMarker[] markers, int number)
    {
        for (int i = 0; i < markers.Length; i++)
            if (markers[i].Number == number) return i;
        return -1;
    }

    private void ChooseUpgrade(int selectedIndex, bool autoMountInSlot)
    {
        if (!_em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var choiceTuple)) return;

        var options = choiceTuple.Value2.Options;
        if (selectedIndex >= options.Length)
        {
            ClearPendingChoices();
            return;
        }

        var option = options[selectedIndex];

        // A new weapon is only half a choice: it goes into hand and the player puts it on the hull or
        // leaves it in cargo. The pending choice stays up until that is settled, so backing out of the
        // mount screen hands the cards straight back.
        if (option.IsNewWeapon && option.Stat == UpgradeOption.Damage && WeaponType.FromName(option.WeaponName) is { } weapon)
        {
            _heldWeapon = weapon.Name;
            _heldIsCardWeapon = true;
            _cardResolved = false;
            _mountsFromStats = false;
            DiagnosticLogger.LogEvent("UPGRADE", $"weapon {weapon.Name} in hand: mount it or store it in cargo");

            if (autoMountInSlot)
            {
                MountIntoFirstFit(weapon);
                ClearPendingChoices();
            }
            else
            {
                _upgradeStep = UpgradeStep.Mounts;
            }

            return;
        }

        ApplyUpgrade(_em, _playerEntity, option);
        ClearPendingChoices();
    }

    // Perf scenarios never pause: fit the weapon into the first mount that is free, and if the hull is
    // full let it ride in cargo until the player makes room.
    private void MountIntoFirstFit(WeaponType weapon)
    {
        var playerPos = _em.GetComponent<Position>(_playerEntity).Value;
        float hullRotation = _em.GetComponent<Rotation>(_playerEntity).Angle;

        var free = TurretMount.FreeSlots(_worldView, weapon.Size);
        if (free.Count > 0 && TurretMount.TryFindSlot(_worldView, free[0].Name, out var freeSlot))
        {
            TurretMount.Mount(_em, freeSlot, free[0], weapon, playerPos, hullRotation);
            return;
        }

        WriteCargo(ReadCargo().With(TurretMount.CarriedTurret(weapon)));
    }

    private void ClearPendingChoices()
    {
        foreach (var (entity, _) in _em.GetEntitiesWithComponents<PendingChoice>().ToList())
            _em.DestroyEntity(entity);

        _upgradeStep = UpgradeStep.Cards;
        _heldWeapon = "";
        _heldIsCardWeapon = false;
        _cardResolved = false;
        _mountsFromStats = false;
    }

    private static void ApplyUpgrade(EntityManager em, Entity playerEntity, UpgradableOption upgrade)
    {
        var playerStats = em.GetComponent<Player>(playerEntity);
        var allPlayerTurrets = new List<(Entity Entity, Turret Value)>();
        foreach (var t in em.GetEntitiesWithComponents<Turret>())
        {
            if (!t.Value1.IsEnemy) allPlayerTurrets.Add(t);
        }

        var def = UpgradeDefinition.For(upgrade.Stat);

        var counts = em.TryGetComponent<UpgradeCounts>(playerEntity, out var existingCounts) ? existingCounts : UpgradeCounts.Empty;
        em.AddComponent(playerEntity, counts.Increment(upgrade.Stat, upgrade.WeaponName));

        switch (upgrade.Stat)
        {
            case UpgradeOption.FireRate:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                {
                    // A volley gun grows a shot per card; a guided volley stops at the round budget
                    // (R17/R18), and a gun whose volley never grows takes the card as a faster rate.
                    bool grows = turret.Weapon.CanGrowSalvo;
                    int newPelletCount = grows ? turret.Weapon.PelletCount + 1 : turret.Weapon.PelletCount;
                    float newFireRate = grows ? turret.Weapon.FireRate : turret.Weapon.FireRate * def.Multiplier;
                    em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { FireRate = newFireRate, PelletCount = newPelletCount } });
                }
                break;

            case UpgradeOption.ProjectileSpeed:
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                    em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { AmmoSpeed = turret.Weapon.AmmoSpeed * def.Multiplier } });
                break;

            case UpgradeOption.MissileHealth:
                // The card is bought for the mount, so it lands on the rounds the mount fires next and
                // not on the ones already in the air.
                foreach (var (turretEntity, turret) in allPlayerTurrets.Where(t => t.Value.WeaponName == upgrade.WeaponName))
                    em.AddComponent(turretEntity, turret with { Weapon = turret.Weapon with { MissileHealth = turret.Weapon.MissileHealth + def.Additive } });
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
}
