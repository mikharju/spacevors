using System.Diagnostics;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public readonly record struct Dead();

public class CollisionSystem : GameSystem
{
    private const float PlayerRestitution = 0.2f;
    // The player's death blast is bigger than an equal-hull enemy's - the run ends with the big one.
    private const float PlayerDeathBlastScale = 1.5f;
    private const float AsteroidAsteroidRestitution = 0.4f;
    private const float AmmoRestitution = 0.15f;
    private const float CorrectionPercent = 0.4f;
    private const float Slop = 1.0f;

    // Rock/ship queries reach this far past their own radius to find mines. It must stay the radius
    // of the widest mine in the game: too small and the broad phase silently drops mine contacts.
    private static readonly float MaxMineRadius = MineType.Large.Radius;

    // How much of the hull's own velocity a shatter shard inherits, so the pieces trail behind a moving
    // hull the way a damage spark does (DamageEffectSystem.FollowFactor is the same idea at 0.5).
    private const float ShardVelocityInherit = 0.4f;
    // How fast the pieces of a broken field fly off. Slow enough to read as glass, not as a blast.
    private const float ShardSpeed = 150f;

    private readonly List<(Entity, Position)> _asteroidPositions = new();
    private readonly List<(Entity, Position)> _shipPositions = new();
    private readonly HashSet<Entity> _entitiesToDestroy = new();
    private readonly HashSet<(Vector2 Position, MineType Type)> _effectsToSpawn = new();
    private readonly List<(Entity ammo, Entity target, Contact hit, Vector2 minePos, MineSize mineSize, int ammoDamage, Vector2 ammoVel)> _ammoToMineHits = new();
    private readonly List<(Entity ammo, Entity target, Vector2 hitPoint, Vector2 shipCenter, int ammoDamage, float shipRadius, byte graphicsId)> _ammoToShipHits = new();

    // Per-frame collision state accumulators (fixes deferred command overwrite bug)
    private readonly Dictionary<Entity, Vector2> _collisionVelocities = new();
    private readonly Dictionary<Entity, Vector2> _positionCorrections = new();
    private readonly Dictionary<Entity, float> _angularVelocityAccumulator = new();
    private readonly Dictionary<Entity, int> _frameRemainingHealth = new();
    // Per-tick shield bookkeeping (plans/SHIELDS.md). A shield is written back the same way Health is —
    // accumulate in the tick, issue one command at the end — because several rounds can soak off one
    // hull in a single tick and last-write-wins would drop all but the last.
    //   pool       what the pool has left after this tick's soaks
    //   intercepts the bearing of the last hit that spent points (its presence also arms the shock)
    //   breaks     hulls whose pool hit zero this tick, with the hull radius the shards fly from
    private readonly Dictionary<Entity, float> _frameRemainingShield = new();
    private readonly Dictionary<Entity, float> _shieldIntercepts = new();
    private readonly Dictionary<Entity, float> _shieldBreaks = new();
    private readonly HashSet<Entity> _hitMines = new();
    private readonly HashSet<Entity> _hitAsteroids = new();
    private readonly HashSet<Entity> _hitMissiles = new();
    private readonly Dictionary<Entity, (Vector2 hitPoint, Vector2 shipPos, float shipRadius, byte graphicsId)> _shipDeathData = new();

    // One tick, in order: find everything touching everything else, spend each round and beam on
    // what it hit, then write every accumulated result back once. Order within each pass is part of
    // the contract: RNG draws for sparks and loot follow collection insertion order (determinism).
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        ResetPerFrameState(view);

        var gridBuildSw = Stopwatch.StartNew();
        BuildBroadPhaseAndResolveVsPlayer(view, commands);
        gridBuildSw.Stop();

        var asteroidSw = Stopwatch.StartNew();
        ResolveRockPairs(view, commands);
        asteroidSw.Stop();

        var shipSw = Stopwatch.StartNew();
        ResolveShipPairs(view, commands);
        shipSw.Stop();

        var ammoDetectionSw = Stopwatch.StartNew();
        DetectRoundHits(view, deltaTime, commands);
        DetectBeamHits(view, commands);
        ammoDetectionSw.Stop();

        sw.Stop();
        DiagnosticLogger.LogSystem("Collision: grid build", gridBuildSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: asteroid collisions", asteroidSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: ship collisions", shipSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: detection", ammoDetectionSw.ElapsedTicks);

        sw.Restart();
        ApplyMineKnockback(view);
        WriteBackDeaths(view, commands);
        WriteBackShields(view, commands);

        foreach (var entity in _entitiesToDestroy)
        {
            commands.Add(new DestroyEntityCommand(entity));
        }

        foreach (var (position, mineType) in _effectsToSpawn)
        {
            SpawnExplosion(commands, position, mineType);
            for (int i = 0; i < mineType.HitSparkCount; i++)
            {
                SpawnSpark(commands, position, view.Rng);
            }
        }

        FlushCollisions(commands, view);

        sw.Stop();
        DiagnosticLogger.LogSystem("Collision: resolution", sw.ElapsedTicks);
    }

    // The player as the tick sees it, resolved once: every pass asks the same body.
    private Entity _playerEntity;
    private NarrowPhase.CollisionBody _playerBody;
    private bool _hasPlayer;

    private void ResetPerFrameState(WorldView view)
    {
        _grid.Clear();
        _asteroidPositions.Clear();
        _shipPositions.Clear();
        _entitiesToDestroy.Clear();
        _effectsToSpawn.Clear();
        _ammoToMineHits.Clear();
        _ammoToShipHits.Clear();
        _collisionVelocities.Clear();
        _positionCorrections.Clear();
        _angularVelocityAccumulator.Clear();
        _frameRemainingHealth.Clear();
        _frameRemainingShield.Clear();
        _shieldIntercepts.Clear();
        _shieldBreaks.Clear();
        _hitMines.Clear();
        _hitAsteroids.Clear();
        _hitMissiles.Clear();
        _shipDeathData.Clear();

        _hasPlayer = view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        _playerEntity = playerTuple.Entity;
        // The player's body, built once per tick: its shape turns with the hull, so every test the player is
        // in reads the same transformed primitives instead of re-deriving them.
        _playerBody = _hasPlayer ? PlayerBody(view, _playerEntity, playerTuple.Value1) : default;
    }

    // The player's collision circle is the conservative reject in front of its shape, exactly as a hull's
    // circle is in front of a bullet's sweep: the shape never reaches past the radius, so a body that
    // misses the circle cannot touch the shape either. A plain test, not a callback: a closure per
    // asteroid per tick in the build pass is exactly the allocation the hot path must not make.
    private bool TouchesPlayerBody(Vector2 entityPos, float entityRadius) =>
        _hasPlayer && NarrowPhase.TryCollide(_playerBody.Position, _playerBody.Scale, entityPos, entityRadius, out _);

    private void BuildBroadPhaseAndResolveVsPlayer(WorldView view, CommandBuffer commands)
    {
        foreach (var (entity, asteroid) in view.GetEntitiesWithComponents<Asteroid>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, pos.Value, asteroid.Radius);
            _asteroidPositions.Add((entity, pos));
            StoreBody(ref _asteroidBodies, entity, AsteroidBody(view, entity, pos.Value, asteroid));

            if (TouchesPlayerBody(pos.Value, asteroid.Radius))
                ResolveCollision(view, _asteroidBodies[entity.Value], entity,
                    _playerBody, _playerEntity, false, commands);
        }

        foreach (var (entity, mine) in view.GetEntitiesWithComponents<EnemyMine>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyMine, pos.Value, mine.Radius, mine.Size);

            if (TouchesPlayerBody(pos.Value, mine.Radius))
                ResolveMineVsPlayer(view, entity, mine, _playerEntity, commands, _playerBody);
        }

        foreach (var (entity, ship) in view.GetEntitiesWithComponents<EnemyShip>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyShip, pos.Value, ship.Radius);
            _shipPositions.Add((entity, pos));
            var hull = HullBody(view, entity, ship);
            StoreBody(ref _shipBodies, entity, hull);

            if (TouchesPlayerBody(pos.Value, ship.Radius))
            {
                ResolveCollision(view, hull, entity,
                    _playerBody, _playerEntity, false, commands);
                SpendTheRam(view, entity, pos.Value);
            }

            int asteroidCount = QueryCircle(pos.Value, ship.Radius);
            var queryBuffer = _queryBuffer;

            for (int i = 0; i < asteroidCount; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.Asteroid) continue;

                ResolveCollision(view, hull, entity,
                    _asteroidBodies[candidate.Id.Value], candidate.Id, true, commands);
            }
        }

        // Guided rounds are bodies, not just shots, so they go in the grid: the other side's fire can
        // shoot them down. Their own side's rounds skip them in the sweep below, which also means a
        // round can never collide with itself.
        foreach (var (entity, _, round) in view.GetEntitiesWithComponents<Missile, Ammo>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.Missile, pos.Value, round.Radius);
        }
    }

    private void ResolveRockPairs(WorldView view, CommandBuffer commands)
    {
        foreach (var (aEntity, aPos) in _asteroidPositions)
        {
            var rock = _asteroidBodies[aEntity.Value];
            float aRadius = rock.Scale;
            int count = QueryCircle(aPos.Value, aRadius);
            var queryBuffer = _queryBuffer;

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.Asteroid) continue;
                if (candidate.Id.Value <= aEntity.Value) continue;

                var bEntity = candidate.Id;
                ResolveCollision(view, rock, aEntity,
                    _asteroidBodies[bEntity.Value], bEntity, true, commands);
            }

            int count2 = QueryCircle(aPos.Value, aRadius + MaxMineRadius);
            queryBuffer = _queryBuffer;

            for (int i = 0; i < count2; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyMine) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                var mine = view.GetComponent<EnemyMine>(candidate.Id);
                ResolveCollision(view, rock, aEntity,
                    CircleBody(bPos.Value, mine.Radius), candidate.Id, true, commands);
            }
        }

    }

    private void ResolveShipPairs(WorldView view, CommandBuffer commands)
    {
        foreach (var (sEntity, sPos) in _shipPositions)
        {
            var hull = _shipBodies[sEntity.Value];
            float aRadius = hull.Scale;
            int count = QueryCircle(sPos.Value, aRadius);
            var queryBuffer = _queryBuffer;

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyShip) continue;
                if (candidate.Id.Value <= sEntity.Value) continue;

                ResolveCollision(view, hull, sEntity,
                    _shipBodies[candidate.Id.Value], candidate.Id, true, commands);
            }

            int count2 = QueryCircle(sPos.Value, aRadius + MaxMineRadius);
            queryBuffer = _queryBuffer;

            for (int i = 0; i < count2; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyMine) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                var mine = view.GetComponent<EnemyMine>(candidate.Id);
                ResolveCollision(view, hull, sEntity,
                    CircleBody(bPos.Value, mine.Radius), candidate.Id, true, commands);
            }
        }

    }

    private void DetectRoundHits(WorldView view, float deltaTime, CommandBuffer commands)
    {
        foreach (var (ammoEntity, ammo, ammoPos) in view.GetEntitiesWithComponents<Ammo, Position>())
            ResolveOneRound(view, ammoEntity, ammo, ammoPos.Value, deltaTime, commands);
    }

    // One round's whole tick: sweep it, find what it met first, spend it there. Deliberately one
    // method with plain locals: an earlier split into a found-targets struct cost ~9% on ammo-spray,
    // because the struct was copied per round at ~5.5k rounds a tick.
    //
    // Swept capsule: ammo velocity is constant (ammo never gets Acceleration), so the
    // position at the start of the tick is exactly pos - vel*dt. The bullet is the
    // segment [sweepFrom, sweepTo] inflated by its radius, so a bullet that jumps over
    // a target between ticks still intersects it. t orders hits along travel.
    private void ResolveOneRound(WorldView view, Entity ammoEntity, Ammo ammo, Vector2 ammoPos, float deltaTime, CommandBuffer commands)
    {
        float ammoRadius = ammo.Radius;

        Vector2 sweepFrom = ammoPos - ammo.Velocity * deltaTime;
        Vector2 sweepTo = ammoPos;
        var boxMin = new Vector2(
            MathF.Min(sweepFrom.X, sweepTo.X) - ammoRadius,
            MathF.Min(sweepFrom.Y, sweepTo.Y) - ammoRadius);
        var boxMax = new Vector2(
            MathF.Max(sweepFrom.X, sweepTo.X) + ammoRadius,
            MathF.Max(sweepFrom.Y, sweepTo.Y) + ammoRadius);

        Entity? earliestAsteroidHit = null;
        Contact asteroidHit = default;
        float asteroidT = float.MaxValue;

        Entity? earliestMineHit = null;
        MineSize? mineHitSize = null;
        Vector2 mineHitPos = default;
        Contact mineHit = default;
        float mineT = float.MaxValue;

        Entity? earliestShipHit = null;
        Vector2 shipHitPos = default;
        float shipT = float.MaxValue;

        Entity? earliestMissileHit = null;
        Contact missileHit = default;
        float missileT = float.MaxValue;

        int count = QueryBox(boxMin, boxMax);
        var queryBuffer = _queryBuffer;

        for (int i = 0; i < count; i++)
        {
            ref readonly var candidate = ref queryBuffer[i];

            // The bounding circle is a conservative reject: a hull's shape never reaches past the
            // radius the grid indexed it by, so anything that misses the circle cannot touch the
            // shape either. Only a candidate the circle cannot reject is tested against its shape,
            // which keeps a volley through empty space exactly as cheap as it was.
            if (!NarrowPhase.TrySweep(sweepFrom, sweepTo, ammoRadius, candidate.Position, candidate.Radius, out Contact hit, out float t))
                continue;

            if (candidate.Kind == SpatialGrid.CollisionKind.EnemyShip
                && !NarrowPhase.TrySweep(sweepFrom, sweepTo, ammoRadius,
                    _shipBodies[candidate.Id.Value], out hit, out t))
                continue;

            // A rock gets the same treatment: its art's lumps are inside the circle the grid indexed
            // it by, so a round that survives the circle is asked again against the rock's shape.
            if (candidate.Kind == SpatialGrid.CollisionKind.Asteroid
                && !NarrowPhase.TrySweep(sweepFrom, sweepTo, ammoRadius,
                    _asteroidBodies[candidate.Id.Value], out hit, out t))
                continue;

            switch (candidate.Kind)
            {
                case SpatialGrid.CollisionKind.Asteroid:
                    if (t < asteroidT)
                    {
                        earliestAsteroidHit = candidate.Id;
                        asteroidHit = hit;
                        asteroidT = t;
                    }
                    break;

                case SpatialGrid.CollisionKind.EnemyMine when !ammo.IsEnemy:
                    if (t < mineT)
                    {
                        earliestMineHit = candidate.Id;
                        mineHitPos = candidate.Position;
                        mineHit = hit;
                        mineHitSize = candidate.Size!;
                        mineT = t;
                    }
                    break;

                case SpatialGrid.CollisionKind.EnemyShip when !ammo.IsEnemy:
                    // The hit point is the hull surface facing the touch — where the spark, the
                    // explosion and the wreck placement all go.
                    if (t < shipT)
                    {
                        earliestShipHit = candidate.Id;
                        shipHitPos = candidate.Position + hit.Normal * candidate.Radius;
                        shipT = t;
                    }
                    break;

                // A guided round is a body with hit points, and only the other side's fire counts:
                // that check is what stops the player's bullets from being eaten by the player's own
                // missiles, and it stops a round from colliding with itself.
                case SpatialGrid.CollisionKind.Missile:
                    if (!view.TryGetComponent<Ammo>(candidate.Id, out var candidateRound)
                        || candidateRound.IsEnemy == ammo.IsEnemy) break;
                    if (t < missileT)
                    {
                        earliestMissileHit = candidate.Id;
                        missileHit = hit;
                        missileT = t;
                    }
                    break;
            }
        }

        // The player is not in the grid, so its touch is found here, beside the grid's
        // candidates, so it orders against them the same way. The same two steps as against a
        // hull: the bounding circle rejects a round that cannot reach the player, and only a
        // round inside it is tested against the hull's shape, which says where it stops.
        bool playerHit = false;
        float playerT = float.MaxValue;
        // The hull-shape contact is kept, not discarded: its point is the direction a soaked hit
        // came from, which is what the shield's intercept flash is drawn at (plans/SHIELDS.md §7).
        Contact playerContact = default;
        if (ammo.IsEnemy && _hasPlayer
            && NarrowPhase.TrySweep(sweepFrom, sweepTo, ammoRadius, _playerBody.Position, _playerBody.Scale, out _, out _)
            && NarrowPhase.TrySweep(sweepFrom, sweepTo, ammoRadius, _playerBody, out playerContact, out float playerTouchedAt))
        {
            playerHit = true;
            playerT = playerTouchedAt;
        }

        // One round, one hit: each kind used to keep its own earliest `t`, so a single round
        // chipped a rock, a mine, a hull and a downed round in the same tick, and an enemy round
        // could clip a rock on its way into the player. The round is spent at the earliest touch
        // along its segment — bodies met at exactly that distance all take it, nothing met later
        // does, so a round that shot a missile down never carries on into the hull that missile
        // was covering. A body the round cannot damage (a wreck past its Health) still spends
        // the tick's hit: the round survives and reaches what is behind it one tick later.
        float spentT = MathF.Min(MathF.Min(MathF.Min(asteroidT, mineT), MathF.Min(shipT, missileT)), playerT);

        if (earliestAsteroidHit.HasValue && asteroidT == spentT)
        {
            var asteroid = view.GetComponent<Asteroid>(earliestAsteroidHit.Value);
            ResolveAmmoVsAsteroid(view, ammo, earliestAsteroidHit.Value, asteroid, asteroidHit, commands);
            if (DamageAsteroid(view, earliestAsteroidHit.Value, ammo.Damage))
                _effectsToSpawn.Add((asteroidHit.Point, MineType.Small));
            _entitiesToDestroy.Add(ammoEntity);
        }

        if (earliestMineHit.HasValue && mineT == spentT && view.TryGetComponent<Health>(earliestMineHit.Value, out var healthComp))
        {
            if (!_frameRemainingHealth.TryGetValue(earliestMineHit.Value, out var health))
            {
                health = healthComp.Current;
                _frameRemainingHealth[earliestMineHit.Value] = health;
            }
            _frameRemainingHealth[earliestMineHit.Value] -= ammo.Damage;
            _hitMines.Add(earliestMineHit.Value);
            _ammoToMineHits.Add((ammoEntity, earliestMineHit.Value, mineHit, mineHitPos, mineHitSize!.Value, ammo.Damage, ammo.Velocity));
            _effectsToSpawn.Add((mineHitPos!, MineType.FromSize(mineHitSize!.Value)));
        }

        if (earliestShipHit.HasValue && shipT == spentT && view.TryGetComponent<Health>(earliestShipHit.Value, out var shipHealthComp))
        {
            var shipHealth = shipHealthComp.Current;
            if (!_frameRemainingHealth.TryGetValue(earliestShipHit.Value, out var h))
            {
                h = shipHealth;
                _frameRemainingHealth[earliestShipHit.Value] = h;
            }
            var enemyShip = view.GetComponent<EnemyShip>(earliestShipHit.Value);
            var shipPos = view.GetComponent<Position>(earliestShipHit.Value);
            int shipDealt = MitigatedGunfireHit(view, earliestShipHit.Value, ammo.Damage, ammo.Pierce, shipPos.Value, shipHitPos);
            _frameRemainingHealth[earliestShipHit.Value] -= shipDealt;
            _ammoToShipHits.Add((ammoEntity, earliestShipHit.Value, shipHitPos, shipPos.Value, shipDealt, enemyShip.Radius, enemyShip.GraphicsId));
            // Consume the bullet on hit so it cannot re-hit the same ship on later frames.
            _entitiesToDestroy.Add(ammoEntity);
        }

        if (earliestMissileHit.HasValue && missileT == spentT && view.TryGetComponent<Health>(earliestMissileHit.Value, out var missileHealthComp))
        {
            if (!_frameRemainingHealth.TryGetValue(earliestMissileHit.Value, out var mHealth))
            {
                mHealth = missileHealthComp.Current;
                _frameRemainingHealth[earliestMissileHit.Value] = mHealth;
            }
            _frameRemainingHealth[earliestMissileHit.Value] -= ammo.Damage;
            _hitMissiles.Add(earliestMissileHit.Value);
            _effectsToSpawn.Add((missileHit.Point, MineType.Small));
            // The round that shot it down is spent, exactly as against a hull: a bullet that kills
            // a missile does not carry on into whatever the missile was covering.
            _entitiesToDestroy.Add(ammoEntity);
        }

        if (!playerHit || playerT != spentT) return;

        var playerPos = _playerBody.Position;

        var playerHealth = view.GetComponent<Health>(_playerEntity);
        if (!_frameRemainingHealth.TryGetValue(_playerEntity, out var remaining))
        {
            remaining = playerHealth.Current;
            _frameRemainingHealth[_playerEntity] = remaining;
        }
        // Armor first, then the shield pool. A hit the field fully ate shows only its flash on the
        // ring; the amber spark is what says "the hull took it", so it is spawned only when damage
        // got through (plans/SHIELDS.md §7). A partial soak shows both reads at once.
        int playerDealt = MitigatedGunfireHit(view, _playerEntity, ammo.Damage, ammo.Pierce, playerPos, playerContact.Point);
        _frameRemainingHealth[_playerEntity] -= playerDealt;
        if (playerDealt > 0) _effectsToSpawn.Add((playerPos, MineType.Small));
        _entitiesToDestroy.Add(ammoEntity);
    }

    // Laser beams: static segments, damage applied exactly once (DamagePending) to the
    // first thing hit. Runs before the mine/ship death loops so damage flows through the
    // same per-frame health accumulators.
    private void DetectBeamHits(WorldView view, CommandBuffer commands)
    {
        foreach (var (beamEntity, beam) in view.GetEntitiesWithComponents<LaserBeam>())
        {
            if (!beam.DamagePending) continue;

            Vector2 beamStart = beam.Start;
            Vector2 beamEnd = beam.End;

            // A beam never draws through what it hit: on a hit, Length shortens to where the beam's
            // inflated axis first touches the target, and a BeamImpact flashes on that very point.
            bool beamStopped = false;
            float stopLength = beam.Length;

            if (beam.IsEnemy)
            {
                // Enemy beams engage only the player, mirroring enemy ammo. A beam is a segment inflated by its
                // width, so it crosses the swept seam and meets the player's shape instead of its bounding
                // circle — which is what lets a beam pass through the gap in a hull that has one.
                if (_hasPlayer && NarrowPhase.TrySweep(beamStart, beamEnd, beam.Width, _playerBody, out Contact beamContact, out _))
                {
                    var beamPlayerHealth = view.GetComponent<Health>(_playerEntity);
                    if (!_frameRemainingHealth.TryGetValue(_playerEntity, out var remaining))
                    {
                        remaining = beamPlayerHealth.Current;
                        _frameRemainingHealth[_playerEntity] = remaining;
                    }
                    _frameRemainingHealth[_playerEntity] -= MitigatedGunfireHit(view, _playerEntity, beam.Damage, beam.Pierce,
                        _playerBody.Position, beamContact.Point);

                    stopLength = BeamEntryLength(_playerBody, beamStart, beam.Direction, beam.Width, beam.Length);
                    beamStopped = true;
                }
            }
            else
            {
                var beamBoxMin = new Vector2(
                    MathF.Min(beamStart.X, beamEnd.X) - beam.Width,
                    MathF.Min(beamStart.Y, beamEnd.Y) - beam.Width);
                var beamBoxMax = new Vector2(
                    MathF.Max(beamStart.X, beamEnd.X) + beam.Width,
                    MathF.Max(beamStart.Y, beamEnd.Y) + beam.Width);

                Entity? firstHit = null;
                SpatialGrid.CollisionKind firstKind = default;
                Contact firstContact = default;
                Vector2 firstCandidatePos = default;
                float firstCandidateRadius = default;
                float firstT = float.MaxValue;

                int beamCount = QueryBox(beamBoxMin, beamBoxMax);
                var queryBuffer = _queryBuffer;

                for (int i = 0; i < beamCount; i++)
                {
                    ref readonly var candidate = ref queryBuffer[i];

                    // A round on the beam's own side is not in the way, exactly as in the ammo pass:
                    // without this a friendly missile is picked as the first hit, so the beam stops on it
                    // and spends its one damage application on nothing — the round the player fired would
                    // eat the beam that was aimed past it.
                    if (candidate.Kind == SpatialGrid.CollisionKind.Missile
                        && (!view.TryGetComponent<Ammo>(candidate.Id, out var ownRound)
                            || ownRound.IsEnemy == beam.IsEnemy)) continue;

                    if (!NarrowPhase.TrySweep(beamStart, beamEnd, beam.Width, candidate.Position, candidate.Radius, out Contact hit, out float t)
                        || t >= firstT) continue;

                    // The same two steps as the ammo pass: the bounding circle rejects cheaply, and a
                    // hull that survives it is tested against its shape. The shape sits inside the
                    // circle, so its touch along the beam is never earlier and has to be re-compared.
                    if (candidate.Kind == SpatialGrid.CollisionKind.EnemyShip
                        && (!NarrowPhase.TrySweep(beamStart, beamEnd, beam.Width,
                                _shipBodies[candidate.Id.Value], out hit, out t)
                            || t >= firstT)) continue;

                    if (candidate.Kind == SpatialGrid.CollisionKind.Asteroid
                        && (!NarrowPhase.TrySweep(beamStart, beamEnd, beam.Width,
                                _asteroidBodies[candidate.Id.Value], out hit, out t)
                            || t >= firstT)) continue;

                    firstHit = candidate.Id;
                    firstKind = candidate.Kind;
                    firstContact = hit;
                    firstCandidatePos = candidate.Position;
                    firstCandidateRadius = candidate.Radius;
                    firstT = t;
                }

                if (firstHit.HasValue)
                {
                    switch (firstKind)
                    {
                        case SpatialGrid.CollisionKind.Asteroid:
                            // The beam stops at the rock with its own impact glow. A small rock also
                            // takes the hit; a big one is scenery with no Health to lose.
                            DamageAsteroid(view, firstHit.Value, beam.Damage);
                            break;

                        case SpatialGrid.CollisionKind.EnemyMine:
                            if (view.TryGetComponent<Health>(firstHit.Value, out var beamMineHealth))
                            {
                                if (!_frameRemainingHealth.TryGetValue(firstHit.Value, out var mh))
                                {
                                    mh = beamMineHealth.Current;
                                    _frameRemainingHealth[firstHit.Value] = mh;
                                }
                                _frameRemainingHealth[firstHit.Value] -= beam.Damage;
                                _hitMines.Add(firstHit.Value);
                            }
                            break;

                        case SpatialGrid.CollisionKind.EnemyShip:
                            if (view.TryGetComponent<Health>(firstHit.Value, out var beamShipHealth))
                            {
                                if (!_frameRemainingHealth.TryGetValue(firstHit.Value, out var sh))
                                {
                                    sh = beamShipHealth.Current;
                                    _frameRemainingHealth[firstHit.Value] = sh;
                                }
                                var beamShip = view.GetComponent<EnemyShip>(firstHit.Value);
                                var beamShipPos = view.GetComponent<Position>(firstHit.Value);
                                Vector2 beamHitPoint = beamShipPos.Value + firstContact.Normal * beamShip.Radius;
                                _frameRemainingHealth[firstHit.Value] -= MitigatedGunfireHit(view, firstHit.Value, beam.Damage, beam.Pierce,
                                    beamShipPos.Value, beamHitPoint);
                                if (!_shipDeathData.ContainsKey(firstHit.Value))
                                    _shipDeathData[firstHit.Value] = (beamHitPoint, beamShipPos.Value, beamShip.Radius, beamShip.GraphicsId);
                            }
                            break;

                        case SpatialGrid.CollisionKind.Missile:
                            // A beam stops at a guided round as it stops at a hull. Only the enemy's
                            // rounds are in the way here: this branch runs for the player's beams, so a
                            // round the player fired is on the same side and passes through.
                            if (view.TryGetComponent<Ammo>(firstHit.Value, out var beamRound)
                                && beamRound.IsEnemy
                                && view.TryGetComponent<Health>(firstHit.Value, out var beamMissileHealth))
                            {
                                if (!_frameRemainingHealth.TryGetValue(firstHit.Value, out var beamMissileRemaining))
                                {
                                    beamMissileRemaining = beamMissileHealth.Current;
                                    _frameRemainingHealth[firstHit.Value] = beamMissileRemaining;
                                }
                                _frameRemainingHealth[firstHit.Value] -= beam.Damage;
                                _hitMissiles.Add(firstHit.Value);
                            }
                            break;
                    }

                    // The stop point is where the beam's inflated axis first touches the body — the
                    // exact circle, hull primitive or rock primitive, not the bounding circle — so the
                    // beam tip and its glow sit on the first visible contact, grazing hits included.
                    NarrowPhase.CollisionBody hitBody = firstKind switch
                    {
                        SpatialGrid.CollisionKind.EnemyShip => _shipBodies[firstHit.Value.Value],
                        SpatialGrid.CollisionKind.Asteroid => _asteroidBodies[firstHit.Value.Value],
                        _ => CircleBody(firstCandidatePos, firstCandidateRadius),
                    };
                    stopLength = BeamEntryLength(hitBody, beamStart, beam.Direction, beam.Width, beam.Length);
                    beamStopped = true;
                }
            }

            if (beamStopped)
            {
                commands.AddEntity(
                    new Position(beamStart + beam.Direction * stopLength),
                    new BeamImpact(BeamImpact.GlowRadiusFor(beam.Width), beam.Color));
                commands.Add(new AddComponentCommand<LaserBeam>(beamEntity, beam with { DamagePending = false, Length = stopLength }));
            }
            else
            {
                commands.Add(new AddComponentCommand<LaserBeam>(beamEntity, beam with { DamagePending = false }));
            }
        }
    }

    // Knockback from rounds that struck mines, run after the detection passes so a mine hit by
    // several rounds in one tick takes their summed impulse exactly once.
    private void ApplyMineKnockback(WorldView view)
    {
        foreach (var (ammoEntity, mineEntity, hit, _, mineSize, _, ammoVel) in _ammoToMineHits)
        {
            var mineType = MineType.FromSize(mineSize);
            float mineRadius = mineType.Radius;
            const float AmmoMass = 1f;
            float mineMass = MathF.PI * mineRadius * mineRadius;
            float totalInvMass = 1f / AmmoMass + 1f / mineMass;

            // The normal comes from the swept touch, so a bullet that already jumped past the mine
            // still pushes it along its travel.
            var normal = hit.Normal;

            Vector2 mineVel = GetCollisionVelocity(view, mineEntity, default);
            float velAlongNormal = Vector2.Dot(ammoVel - mineVel, normal);
            if (velAlongNormal > 0f)
            {
                _entitiesToDestroy.Add(ammoEntity);
                continue;
            }

            float j = -(1 + AmmoRestitution) * velAlongNormal / totalInvMass;
            // TrySweep's normal runs target → mover's path, the reverse of TryCollide's A → B, so the
            // separating impulse on the target is along −normal — the same sign ResolveCollision uses
            // for body A. Pushing it along +normal would knock mines toward the shooter.
            var impulse = normal * j;
            mineVel -= impulse * (1f / mineMass);
            SetCollisionVelocity(mineEntity, mineVel);

            _entitiesToDestroy.Add(ammoEntity);
        }
    }

    // Health write-back, death flows and loot for everything the detection passes damaged this tick.
    private void WriteBackDeaths(WorldView view, CommandBuffer commands)
    {
        foreach (var mineEntity in _hitMines)
        {
            var remaining = _frameRemainingHealth[mineEntity];
            if (remaining <= 0)
            {
                var pos = view.GetComponent<Position>(mineEntity);
                var mine = view.GetComponent<EnemyMine>(mineEntity);
                SpawnLootOnDeath(commands, pos.Value, MineType.FromSize(mine.Size), view.Rng);
                commands.Add(new DestroyEntityCommand(mineEntity));
            }
            else
            {
                var mineHealth = view.GetComponent<Health>(mineEntity);
                commands.Add(new AddComponentCommand<Health>(mineEntity, new Health(remaining, mineHealth.Max)));
            }
        }

        foreach (var asteroidEntity in _hitAsteroids)
        {
            var remaining = _frameRemainingHealth[asteroidEntity];
            if (remaining > 0)
            {
                var asteroidHealth = view.GetComponent<Health>(asteroidEntity);
                commands.Add(new AddComponentCommand<Health>(asteroidEntity, new Health(remaining, asteroidHealth.Max)));
                continue;
            }

            // A rock that is gone leaves a cloud of grey puffs and nothing else: no fireball, no
            // staged explosion, no loot. Only the bullets that killed it paid for it.
            var pos = view.GetComponent<Position>(asteroidEntity);
            var rock = view.GetComponent<Asteroid>(asteroidEntity);
            view.TryGetComponent<Velocity>(asteroidEntity, out var drift);
            AsteroidFactory.SpawnBreakupSmoke(commands, pos.Value, drift.Value, rock.Radius, view.Rng);
            commands.Add(new DestroyEntityCommand(asteroidEntity));
        }

        foreach (var missileEntity in _hitMissiles)
        {
            var remaining = _frameRemainingHealth[missileEntity];
            if (remaining > 0)
            {
                var missileHealth = view.GetComponent<Health>(missileEntity);
                commands.Add(new AddComponentCommand<Health>(missileEntity, new Health(remaining, missileHealth.Max)));
                continue;
            }

            // Shot down, not detonated: a missile that dies to gunfire breaks up in smoke where it hung
            // and its warhead never goes off. A damage popup beside the hull from a shot the player
            // took on purpose would teach the wrong lesson.
            var pos = view.GetComponent<Position>(missileEntity);
            view.TryGetComponent<Velocity>(missileEntity, out var drift);
            var round = view.GetComponent<Ammo>(missileEntity);
            AsteroidFactory.SpawnBreakupSmoke(commands, pos.Value, drift.Value, round.Radius, view.Rng);
            commands.Add(new DestroyEntityCommand(missileEntity));
        }

        foreach (var (_, shipEntity, hitPoint, shipPos, _, shipRadius, graphicsId) in _ammoToShipHits)
        {
            if (!_shipDeathData.ContainsKey(shipEntity))
                _shipDeathData[shipEntity] = (hitPoint, shipPos, shipRadius, graphicsId);
        }

        foreach (var (shipEntity, data) in _shipDeathData)
        {
            var remaining = _frameRemainingHealth.TryGetValue(shipEntity, out var r) ? r : -1;
            if (remaining <= 0)
            {
                SpawnShipLootOnDeath(commands, data.shipPos, view.Rng);
                commands.Add(new RemoveComponentCommand<Health>(shipEntity));
                commands.Add(new AddComponentCommand<Dead>(shipEntity, new Dead()));
                view.TryGetComponent<Velocity>(shipEntity, out var deathVel);
                SpawnDeathBlast(commands, shipEntity, data.hitPoint, data.shipRadius, data.graphicsId, deathVel.Value, view.Rng);
            }
            else
            {
                var shipHealth = view.GetComponent<Health>(shipEntity);
                commands.Add(new AddComponentCommand<Health>(shipEntity, new Health(remaining, shipHealth.Max)));
            }
        }

        if (_hasPlayer && _frameRemainingHealth.TryGetValue(_playerEntity, out var playerRemaining))
        {
            if (playerRemaining <= 0)
            {
                // Zero the health on the fatal hit: the hull damage shading reads Health, and
                // without this write the killing blow from high hp renders a pristine hull.
                var playerHealth = view.GetComponent<Health>(_playerEntity);
                commands.Add(new AddComponentCommand<Health>(_playerEntity, new Health(0, playerHealth.Max)));
                commands.Add(new AddComponentCommand<Dead>(_playerEntity, new Dead()));

                // The run ends with the player blowing up like an enemy hull, only bigger: the
                // death animation window (1.2 s) covers the full 1.0 s sequence. The guard keeps a
                // hit landing on the corpse from restarting the blast.
                if (!view.HasComponent<ShipDeathExplosion>(_playerEntity))
                {
                    view.TryGetComponent<Velocity>(_playerEntity, out var playerDeathVel);
                    float blastRadius = view.GetComponent<Player>(_playerEntity).Radius * PlayerDeathBlastScale;
                    SpawnDeathBlast(commands, _playerEntity, view.GetComponent<Position>(_playerEntity).Value,
                        blastRadius, 0, playerDeathVel.Value, view.Rng);
                }
            }
            else
            {
                var playerHealth = view.GetComponent<Health>(_playerEntity);
                commands.Add(new AddComponentCommand<Health>(_playerEntity, new Health(playerRemaining, playerHealth.Max)));
            }
        }
    }

    // Shield write-back (plans/SHIELDS.md): one command per hull whose pool was spent this tick —
    // the points left, the shock armed by any hit that spent points of it, and the flash's bearing.
    // A hull whose pool hit zero also gets the shatter ring and its shards. Queued before the
    // destroy loop, so a hull that died this tick is destroyed after its pool was written.
    private void WriteBackShields(WorldView view, CommandBuffer commands)
    {
        foreach (var (entity, left) in _frameRemainingShield)
        {
            var shield = view.GetComponent<Shield>(entity);
            float points = MathF.Max(0f, left);
            bool spent = _shieldIntercepts.TryGetValue(entity, out float bearingDeg);
            commands.Add(new AddComponentCommand<Shield>(entity,
                new Shield(points, shield.Max, spent ? ShieldRules.ShockSeconds : shield.Offline)));
            if (spent)
                commands.Add(new AddComponentCommand<ShieldFlash>(entity, new ShieldFlash(ShieldRules.FlashSeconds, bearingDeg)));
        }

        foreach (var (entity, hullRadius) in _shieldBreaks)
        {
            commands.Add(new AddComponentCommand<ShieldBurst>(entity, new ShieldBurst(ShieldRules.BurstSeconds)));
            view.TryGetComponent<Velocity>(entity, out var hullVelocity);
            SpawnShieldShards(commands, view.GetComponent<Position>(entity).Value, hullVelocity.Value, hullRadius, view.Rng);
        }
    }

    private SpatialGrid _grid = new(128f);

    // Each hull and rock becomes a CollisionBody once per tick, at the build pass, and every pass
    // (body pairs, bullet sweeps, beams) reads the same cached body. Positions and headings are fixed
    // through the tick — commands only land at the flush — so a body cannot go stale inside a tick.
    // Indexed directly by entity id, like ComponentStorage; slots belonging to entities not in this
    // tick's grid are stale and never read, because every lookup is keyed on an entity the grid or
    // this tick's build pass just produced.
    private NarrowPhase.CollisionBody[] _asteroidBodies = Array.Empty<NarrowPhase.CollisionBody>();
    private NarrowPhase.CollisionBody[] _shipBodies = Array.Empty<NarrowPhase.CollisionBody>();

    static void StoreBody(ref NarrowPhase.CollisionBody[] bodies, Entity entity, NarrowPhase.CollisionBody body)
    {
        if (entity.Value >= bodies.Length)
            Array.Resize(ref bodies, Math.Max(entity.Value + 1, Math.Max(bodies.Length * 2, 64)));
        bodies[entity.Value] = body;
    }

    // Grid queries fill this buffer. A full buffer makes the grid drop candidates, and dropped
    // candidates are missed collisions, so a truncated query grows the buffer and runs again.
    // The array is reused across frames: it grows once per dense scene, not once per tick.
    private SpatialGrid.SpatialItem[] _queryBuffer = new SpatialGrid.SpatialItem[256];

    private int QueryCircle(Vector2 position, float radius)
    {
        while (true)
        {
            int count = _grid.GetQueryItems(position, radius, _queryBuffer, out bool truncated);
            if (!truncated) return count;
            GrowQueryBuffer();
        }
    }

    private int QueryBox(Vector2 min, Vector2 max)
    {
        while (true)
        {
            int count = _grid.GetQueryItemsInBox(min, max, _queryBuffer, out bool truncated);
            if (!truncated) return count;
            GrowQueryBuffer();
        }
    }

    private void GrowQueryBuffer()
    {
        int grown = _queryBuffer.Length * 2;
        _queryBuffer = new SpatialGrid.SpatialItem[grown];
        DiagnosticLogger.LogWarning($"CollisionSystem: spatial query buffer grew to {grown} entries so no collision is dropped");
    }

    private Vector2 GetCollisionVelocity(WorldView view, Entity entity, Vector2 ecsDefault)
    {
        if (_collisionVelocities.TryGetValue(entity, out var v)) return v;
        if (view.TryGetComponent<Velocity>(entity, out var vel)) return vel.Value;
        return ecsDefault;
    }

    private void SetCollisionVelocity(Entity entity, Vector2 velocity)
    {
        _collisionVelocities[entity] = velocity;
    }

    private void AccumulatePosition(Entity entity, Vector2 correction)
    {
        if (_positionCorrections.TryGetValue(entity, out var existing))
            _positionCorrections[entity] = existing + correction;
        else
            _positionCorrections[entity] = correction;
    }

    private float GetCollisionAngularVelocity(WorldView view, Entity entity, float ecsDefault)
    {
        if (_angularVelocityAccumulator.TryGetValue(entity, out var a)) return a;
        if (view.TryGetComponent<AngularVelocity>(entity, out var av)) return av.Value;
        return ecsDefault;
    }

    private void SetCollisionAngularVelocity(Entity entity, float value)
    {
        _angularVelocityAccumulator[entity] = value;
    }

    private void FlushCollisions(CommandBuffer commands, WorldView view)
    {
        foreach (var (entity, velocity) in _collisionVelocities)
            commands.Add(new AddComponentCommand<Velocity>(entity, new Velocity(velocity)));

        foreach (var (entity, delta) in _positionCorrections)
        {
            if (!view.TryGetComponent<Position>(entity, out var pos)) continue;
            commands.Add(new AddComponentCommand<Position>(entity, new Position(pos.Value + delta)));
        }

        foreach (var (entity, angVel) in _angularVelocityAccumulator)
            commands.Add(new AddComponentCommand<AngularVelocity>(entity, new AngularVelocity(angVel)));
    }



    // The staged death blast every hull gets when shot down: the sequence component, the first
    // fireball, and four sparks off the hit point. Everything scales with blastRadius - the player's
    // death passes a deliberately larger one.
    private static void SpawnDeathBlast(CommandBuffer commands, Entity shipEntity, Vector2 hitPoint,
        float blastRadius, byte graphicsId, Vector2 inheritedVel, Random rng)
    {
        commands.Add(new AddComponentCommand<ShipDeathExplosion>(shipEntity, new ShipDeathExplosion(1.0f, hitPoint, blastRadius, graphicsId, inheritedVel)));
        commands.AddEntity(new Position(hitPoint), new Explosion(blastRadius * 0.8f, 0.6f, 0.6f));
        for (int i = 0; i < 4; i++)
        {
            float sparkAngle = (float)(rng.NextDouble() * MathF.PI * 2f);
            float sparkSpeed = (blastRadius + 15f) / 0.7f * (0.8f + (float)rng.NextDouble() * 0.4f);
            Vector2 sparkVel = new Vector2((float)Math.Cos(sparkAngle) * sparkSpeed, (float)Math.Sin(sparkAngle) * sparkSpeed);
            float sparkLifetime = 0.7f + (float)rng.NextDouble() * 0.3f;
            commands.AddEntity(new Position(hitPoint), new Velocity(sparkVel), new DamageSpark(sparkLifetime, sparkLifetime));
        }
    }

    private void SpawnExplosion(CommandBuffer commands, Vector2 position, MineType mineType)
    {
        commands.AddEntity(new Position(position), new Explosion(mineType.ExplosionRadius, 0.5f, 0.5f));
    }

    private void SpawnSpark(CommandBuffer commands, Vector2 position, Random rng)
    {
        float angle = (float)(rng.NextDouble() * MathF.PI * 2f);
        float speed = 50f + (float)rng.NextDouble() * 100f;
        Vector2 velocity = new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed);
        float sparkLifetime = 0.8f + (float)rng.NextDouble() * 0.6f;
        commands.AddEntity(new Position(position), new Velocity(velocity), new DamageSpark(sparkLifetime, sparkLifetime));
    }

    // A hull's collision body: the shape its art was authored with, at the hull's size and turned the
    // way the hull is turned. The radius stays the bounding radius the grid indexed it by, so the broad
    // phase is untouched and a shape can never reach past the query that found it.
    static NarrowPhase.CollisionBody HullBody(WorldView view, Entity entity, EnemyShip ship) =>
        new(view.GetComponent<Position>(entity).Value, view.GetComponent<Rotation>(entity).Angle,
            HullShapes.ForEnemy(ship.GraphicsId), ship.Radius);

    // The player as a body: the shape its hull type was authored with, turned the way the hull is turned.
    // `Radius` stays the bounding radius everything else reads — explosion damage, spawn clearance, the pickup
    // ring — and the circle every shape test is queued behind, so a shape can only ever be smaller than the
    // disc the player used to have.
    static NarrowPhase.CollisionBody PlayerBody(WorldView view, Entity _playerEntity, Player player)
    {
        // A world that never gave the player a heading collides with it facing the way it is drawn.
        view.TryGetComponent<Rotation>(_playerEntity, out var rotation);
        return new(view.GetComponent<Position>(_playerEntity).Value, rotation.Angle,
            player.Shape ?? HullShape.Circle, player.Radius);
    }

    // A rock as a body: the shape its art was authored with, at the rock's size and turned the way it is
    // spinning. The radius stays the bounding radius the grid indexed it by, so the broad phase is
    // untouched and a shape can never reach past the query that found it.
    static NarrowPhase.CollisionBody AsteroidBody(WorldView view, Entity entity, Vector2 position, Asteroid asteroid)
    {
        // A rock that was never given a heading collides facing the way its art is drawn.
        view.TryGetComponent<Rotation>(entity, out var rotation);
        return new(position, rotation.Angle,
            AsteroidShapes.For(asteroid.IsSmall, asteroid.Variant), asteroid.Radius);
    }

    // Mines and guided rounds are drawn as dots: they collide as one circle, as they always did.
    static NarrowPhase.CollisionBody CircleBody(Vector2 position, float radius) =>
        NarrowPhase.CollisionBody.Circle(position, radius);

    // How far a beam travels before it first touches a body: the earliest entry of the ray into the
    // body's primitives, each inflated by the beam's own half-thickness. A beam's shortened Length and
    // its impact glow both hang off this distance, so the glow sits on the beam tip at the point of
    // first contact. Clamped to the beam's own length; 0 when the muzzle is already inside.
    static float BeamEntryLength(NarrowPhase.CollisionBody body, Vector2 start, Vector2 direction, float beamHalfThickness, float maxLength)
    {
        float best = maxLength;

        if (body.Shape.IsCircle)
        {
            var disc = new Capsule(body.Position, body.Position, body.Scale + beamHalfThickness);
            if (CapsuleGeometry.TryEntryAlongRay(start, direction, disc, out float entry)) best = MathF.Min(best, entry);
            return best;
        }

        float cos = MathF.Cos(body.Angle);
        float sin = MathF.Sin(body.Angle);
        foreach (var primitive in body.Shape.Capsules)
        {
            var inflated = new Capsule(
                NarrowPhase.InWorld(primitive.From, cos, sin, body),
                NarrowPhase.InWorld(primitive.To, cos, sin, body),
                primitive.HalfThickness * body.Scale + beamHalfThickness);
            if (CapsuleGeometry.TryEntryAlongRay(start, direction, inflated, out float entry)) best = MathF.Min(best, entry);
        }

        return best;
    }

    private void ResolveCollision(
        WorldView view,
        NarrowPhase.CollisionBody a,
        Entity aEntity,
        NarrowPhase.CollisionBody b,
        Entity bEntity,
        bool isAsteroidVsAsteroid,
        CommandBuffer commands)
    {
        if (!NarrowPhase.TryCollide(a, b, out Contact contact)) return;

        var normal = contact.Normal;
        float penetration = contact.Depth;

        // Mass and MOI are radius-derived for every body, hulls included: a small enemy is light and a
        // big one heavy. The old fixed EnemyShipMass = 3000 made a 20 px swarmer weigh 2.4x a Scout.
        float aMass = MathF.PI * a.Scale * a.Scale;
        float bMass = MathF.PI * b.Scale * b.Scale;
        float invMassA = 1f / aMass;
        float invMassB = 1f / bMass;
        float totalInvMass = invMassA + invMassB;

        var aVel = GetCollisionVelocity(view, aEntity, default);
        var bVel = GetCollisionVelocity(view, bEntity, default);

        var relVel = bVel - aVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);

        float correctionMagnitude = Math.Max(penetration - Slop, 0f) * CorrectionPercent;
        if (correctionMagnitude > 0f && totalInvMass > 0f)
        {
            var correctionPerInvMass = normal * (correctionMagnitude / totalInvMass);
            AccumulatePosition(aEntity, correctionPerInvMass * (-invMassA));
            AccumulatePosition(bEntity, correctionPerInvMass * invMassB);
        }

        if (velAlongNormal > 0f) return;

        // Impact damage: only reached while approaching, so sustained/resting contact never
        // chips hp; -velAlongNormal is the closing speed (relative, normal component only).
        ApplyImpactDamage(view, aEntity, bEntity, contact.Point, aMass, bMass, -velAlongNormal, commands);

        float restitution = isAsteroidVsAsteroid ? AsteroidAsteroidRestitution : PlayerRestitution;
        if (MathF.Abs(velAlongNormal) < 5f)
        {
            restitution = 0f;
        }

        // The normal impulse acts where the bodies touch, not at their centres, so the lever arms
        // measured from the contact (not from `normal · radius`) decide how much of a shove becomes
        // spin. A circle's every contact sits on the centre line, so its arms are zero and a circle
        // pair resolves exactly as the centre-based impulse always resolved it; push a lumpy rock
        // off a limb and the same impulse now turns it as well as shoving it.
        var rA = contact.Point - a.Position;
        var rB = contact.Point - b.Position;
        float armA = rA.X * normal.Y - rA.Y * normal.X;
        float armB = rB.X * normal.Y - rB.Y * normal.X;

        float aAngVel = GetCollisionAngularVelocity(view, aEntity, 0f);
        float bAngVel = GetCollisionAngularVelocity(view, bEntity, 0f);

        // A body with no AngularVelocity component steers its own heading (input, AI) and a contact
        // cannot spin it: it keeps infinite rotational inertia. The MOI stays the bounding disc's,
        // as it has always been: the shape decides what a body hits, not how heavy it is, and
        // changing that is a balance change that has to be measured on its own.
        float aMOI = view.TryGetComponent<AngularVelocity>(aEntity, out _) ? 0.5f * aMass * a.Scale * a.Scale : 0f;
        float bMOI = view.TryGetComponent<AngularVelocity>(bEntity, out _) ? 0.5f * bMass * b.Scale * b.Scale : 0f;
        float armYieldA = armA * armA * (aMOI > 0f ? 1f / aMOI : 0f);
        float armYieldB = armB * armB * (bMOI > 0f ? 1f / bMOI : 0f);

        // The closing speed is read at the touch: a body already spinning away from the contact is
        // not being pushed into it. And the impulse is divided by what the touch actually feels —
        // an off-centre hit on a long body meets less resistance than a head-on one, so its far end
        // yields into spin instead of the whole body jumping.
        float closingAtContact = velAlongNormal + bAngVel * armB - aAngVel * armA;
        if (closingAtContact >= 0f) return;

        float j = -(1 + restitution) * closingAtContact / (totalInvMass + armYieldA + armYieldB);
        var impulse = normal * j;

        aVel -= impulse * invMassA;
        bVel += impulse * invMassB;

        SetCollisionVelocity(aEntity, aVel);
        SetCollisionVelocity(bEntity, bVel);

        if (aMOI > 0f)
        {
            aAngVel -= armA * j / aMOI;
            SetCollisionAngularVelocity(aEntity, aAngVel);
        }
        if (bMOI > 0f)
        {
            bAngVel += armB * j / bMOI;
            SetCollisionAngularVelocity(bEntity, bAngVel);
        }

        var correctedRelVel = bVel - aVel;
        var velNormalComponent = Vector2.Dot(correctedRelVel, normal);
        var relVelTangent = correctedRelVel - normal * velNormalComponent;
        float tangentSpeed = relVelTangent.Magnitude;

        const float Friction = 0.2f;

        if (tangentSpeed > Slop)
        {
            float contactTangentVelX = relVelTangent.X + aAngVel * rA.Y - bAngVel * rB.Y;
            float contactTangentVelY = relVelTangent.Y - aAngVel * rA.X + bAngVel * rB.X;
            float contactTangentSpeed = MathF.Sqrt(contactTangentVelX * contactTangentVelX + contactTangentVelY * contactTangentVelY);

            if (contactTangentSpeed > Slop)
            {
                float normalImpulseMag = MathF.Abs(j);
                float maxFrictionImpulse = Friction * normalImpulseMag;
                float frictionImpulseMag = MathF.Min(contactTangentSpeed * MathF.Min(invMassA, invMassB), maxFrictionImpulse);

                var frictionImpulse = new Vector2(
                    -contactTangentVelX / contactTangentSpeed * frictionImpulseMag,
                    -contactTangentVelY / contactTangentSpeed * frictionImpulseMag
                );

                aVel -= frictionImpulse * invMassA;
                bVel += frictionImpulse * invMassB;

                SetCollisionVelocity(aEntity, aVel);
                SetCollisionVelocity(bEntity, bVel);

                if (aMOI > 0f)
                {
                    float torqueA = rA.X * frictionImpulse.Y - rA.Y * frictionImpulse.X;
                    SetCollisionAngularVelocity(aEntity, aAngVel + torqueA / aMOI);
                }

                if (bMOI > 0f)
                {
                    float torqueB = rB.X * (-frictionImpulse.Y) - rB.Y * (-frictionImpulse.X);
                    SetCollisionAngularVelocity(bEntity, bAngVel + torqueB / bMOI);
                }
            }
        }
    }

    // Armor mitigation for gunfire hits (plans/ARMOR_AND_PIERCE.md): Ammo and LaserBeam only, in
    // either direction. Only hulls carry an Armor component, so the mine, asteroid and round hit
    // sites skip this by rule, and impact and mine-contact damage bypass it entirely. Unarmored
    // targets pay one failed lookup and the formula's identity.
    private int MitigatedGunfireDamage(WorldView view, Entity target, int damage, int pierce) =>
        ArmorRules.EffectiveDamage(damage, view.TryGetComponent<Armor>(target, out var armor) ? armor.Value : 0, pierce);

    // Gunfire mitigation for one hit (plans/SHIELDS.md): armor first — ArmorRules changes the size of the
    // number — then the target's shield pool, which banks what is left of it. Returns the damage that
    // reaches Health. A hit that spends points of the pool arms the shock and records the bearing the
    // ring flashes at; emptying the pool records the break, which the write-back turns into the shatter
    // and its shards. A target with no shield pays one failed lookup and the plain armor result.
    private int MitigatedGunfireHit(WorldView view, Entity target, int damage, int pierce, Vector2 hullCentre, Vector2 contactPoint)
    {
        int mitigated = MitigatedGunfireDamage(view, target, damage, pierce);
        if (!view.TryGetComponent<Shield>(target, out var shield) || shield.Points <= 0f) return mitigated;

        float points = _frameRemainingShield.TryGetValue(target, out var left) ? left : shield.Points;
        int soaked = ShieldRules.Soak(points, mitigated);
        if (soaked == 0) return mitigated;

        _frameRemainingShield[target] = points - soaked;
        _shieldIntercepts[target] = BearingDeg(hullCentre, contactPoint);
        if (points - soaked <= 0f) _shieldBreaks[target] = HullRadius(view, target);
        return mitigated - soaked;
    }

    // The intercept's direction seen from the hull's centre, in degrees, in the screen-space convention
    // the ring is drawn in (world +Y is down, so this is the raylib angle directly). The flash is drawn
    // centred on it, which is what makes the winding of the angle irrelevant.
    private static float BearingDeg(Vector2 hullCentre, Vector2 contactPoint)
    {
        Vector2 offset = contactPoint - hullCentre;
        return MathF.Atan2(offset.Y, offset.X) * 180f / MathF.PI;
    }

    // A hull's drawn radius. Only hulls carry a shield, so one of the two hull components is there.
    private static float HullRadius(WorldView view, Entity hull) =>
        view.TryGetComponent<Player>(hull, out var player) ? player.Radius : view.GetComponent<EnemyShip>(hull).Radius;

    // The shatter: pieces of field flying off roughly outward. The angles are spread evenly under one
    // random offset, so a break always reads as a ring coming apart rather than as a puff, and each
    // piece inherits part of the hull's velocity so the shards trail a moving hull.
    private void SpawnShieldShards(CommandBuffer commands, Vector2 hullPos, Vector2 hullVelocity, float hullRadius, Random rng)
    {
        float ring = ShieldRules.RingRadius(hullRadius);
        float offset = (float)rng.NextDouble() * MathF.PI * 2f;
        for (int i = 0; i < ShieldRules.ShardCount; i++)
        {
            float angle = offset + i * MathF.PI * 2f / ShieldRules.ShardCount;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            commands.AddEntity(
                new Position(hullPos + direction * ring),
                new Velocity(direction * ShardSpeed + hullVelocity * ShardVelocityInherit),
                new ShieldShard(ShieldRules.ShardSeconds, ShieldRules.ShardSeconds));
        }
    }

    // Impact damage (plans/IMPACT_DAMAGE.md). Damage flows through the same per-frame health    // accumulator as bullet damage, so the existing death pass covers impact kills.
    private void ApplyImpactDamage(
        WorldView view,
        Entity aEntity,
        Entity bEntity,
        Vector2 contactPoint,
        float aMass,
        float bMass,
        float closingSpeed,
        CommandBuffer commands)
    {
        var (damageA, damageB) = ImpactDamage.ForMasses(aMass, bMass, closingSpeed);
        if (damageA == 0 && damageB == 0) return;

        AccumulateImpactHealth(view, aEntity, damageA, contactPoint);
        AccumulateImpactHealth(view, bEntity, damageB, contactPoint);

        int biggest = Math.Max(damageA, damageB);
        if (biggest >= ImpactDamage.SparkDamageThreshold)
            for (int i = 0; i < ImpactDamage.SparkCount(biggest); i++)
                SpawnSpark(commands, contactPoint, view.Rng);
    }

    // A ram spends the hull (plans/ENEMY_BEHAVIORS.md B6): the charge is the whole payload, so the contact
    // that lands on the player ends the hull. Left out, a rammer would bounce off and charge again without
    // paying anything for the hit — the one part of it the player could not answer. The impact damage it
    // took on the way in still runs first, so the explosion size follows how much of it survived.
    private void SpendTheRam(WorldView view, Entity hullEntity, Vector2 at)
    {
        if (!view.TryGetComponent<EnemyShip>(hullEntity, out var ship) || ship.Behavior != EnemyBehavior.Rammer) return;
        if (!view.TryGetComponent<Health>(hullEntity, out var health) || health.Current <= 0) return;

        AccumulateImpactHealth(view, hullEntity, health.Current, at);
    }

    // Asteroids and mines (mines have their own contact rule in ResolveMineVsPlayer) never take
    // impact damage: a rock is broken by gunfire only, so the field cannot grind itself away as
    // ships and rocks collide. Ships register death data like bullet hits do, so the death pass
    // writes back damaged health for survivors and runs the full death flow for kills.
    private void AccumulateImpactHealth(WorldView view, Entity entity, int damage, Vector2 hitPoint)
    {
        if (damage == 0) return;
        if (view.HasComponent<EnemyMine>(entity)) return;
        if (view.HasComponent<Asteroid>(entity)) return;
        if (!view.TryGetComponent<Health>(entity, out var health)) return;

        var remaining = (_frameRemainingHealth.TryGetValue(entity, out var r) ? r : health.Current) - damage;
        _frameRemainingHealth[entity] = remaining;

        if (!_shipDeathData.ContainsKey(entity)
            && view.TryGetComponent<EnemyShip>(entity, out var enemyShip)
            && view.TryGetComponent<Position>(entity, out var pos))
        {
            _shipDeathData[entity] = (hitPoint, pos.Value, enemyShip.Radius, enemyShip.GraphicsId);
        }
    }

    // A hit on a small rock chips its Health and returns whether the rock is still there afterwards;
    // the big scenery has no Health, so the hit stays an obstacle hit and no bookkeeping follows.
    // Whether a rock dies is decided once per tick in the resolution pass, so several bullets landing
    // in the same tick cannot each blow it up.
    private bool DamageAsteroid(WorldView view, Entity asteroidEntity, int damage)
    {
        if (damage <= 0) return true;
        if (!view.TryGetComponent<Health>(asteroidEntity, out var health)) return true;

        var remaining = (_frameRemainingHealth.TryGetValue(asteroidEntity, out var r) ? r : health.Current) - damage;
        _frameRemainingHealth[asteroidEntity] = remaining;
        _hitAsteroids.Add(asteroidEntity);
        return remaining > 0;
    }

    private void ResolveAmmoVsAsteroid(
        WorldView view,
        Ammo ammo,
        Entity asteroidEntity,
        Asteroid asteroid,
        Contact hit,
        CommandBuffer commands)
    {
        if (!hit.HasNormal) return; // the segment ran through the centre: no meaningful normal
        var normal = hit.Normal;

        Vector2 asteroidVel = GetCollisionVelocity(view, asteroidEntity, default);
        Vector2 ammoVel = ammo.Velocity;

        var relVel = ammoVel - asteroidVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);
        if (velAlongNormal > 0f) return;

        const float AmmoMass = 1f;

        float asteroidMass = MathF.PI * asteroid.Radius * asteroid.Radius;
        float totalInvMass = 1f / AmmoMass + 1f / asteroidMass;

        float j = -(1 + AmmoRestitution) * velAlongNormal / totalInvMass;
        // Sweep normal runs target → mover, so the target separates along −normal (see the mine pass).
        var impulse = normal * j;

        asteroidVel -= impulse * (1f / asteroidMass);
        SetCollisionVelocity(asteroidEntity, asteroidVel);

        const float Friction = 0.2f;

        var relVelTangent = relVel - normal * velAlongNormal;
        float tangentSpeed = relVelTangent.Magnitude;

        if (tangentSpeed > Slop && view.TryGetComponent<AngularVelocity>(asteroidEntity, out _))
        {
            float asteroidMOI = 0.5f * asteroidMass * asteroid.Radius * asteroid.Radius;

            var rAsteroid = normal * asteroid.Radius;
            float contactTangentSpeed = MathF.Sqrt(relVelTangent.X * relVelTangent.X + relVelTangent.Y * relVelTangent.Y);

            float maxFriction = Friction * MathF.Abs(j);
            float frictionMag = MathF.Min(contactTangentSpeed / totalInvMass, maxFriction);

            var frictionImpulse = new Vector2(
                -relVelTangent.X / contactTangentSpeed * frictionMag,
                -relVelTangent.Y / contactTangentSpeed * frictionMag
            );

            asteroidVel -= frictionImpulse / asteroidMass;
            SetCollisionVelocity(asteroidEntity, asteroidVel);

            float torque = rAsteroid.X * frictionImpulse.Y - rAsteroid.Y * frictionImpulse.X;
            float currentAngVel = GetCollisionAngularVelocity(view, asteroidEntity, 0f);
            SetCollisionAngularVelocity(asteroidEntity, currentAngVel + torque / asteroidMOI);
        }
    }

    private void ResolveMineVsPlayer(WorldView view, Entity mineEntity, EnemyMine mine, Entity _playerEntity, CommandBuffer commands, NarrowPhase.CollisionBody playerBody)
    {
        var minePos = view.GetComponent<Position>(mineEntity);

        if (!NarrowPhase.TryCollide(CircleBody(minePos.Value, mine.Radius), playerBody, out Contact contact)) return;

        // A jammed mine does not detonate: it rebounds off the hull and costs the player nothing. It keeps
        // its hp and stays a body in the way, so a pulse turns a mine field into something the player can
        // shoot clear while the field cannot answer.
        if (view.TryGetComponent<EcmDisabled>(mineEntity, out _))
        {
            BounceJammedMine(commands, mineEntity, mine, minePos.Value, contact);
            return;
        }

        commands.Add(new DestroyEntityCommand(mineEntity));

        var playerHealth = view.GetComponent<Health>(_playerEntity);
        if (!_frameRemainingHealth.TryGetValue(_playerEntity, out var remaining))
        {
            remaining = playerHealth.Current;
            _frameRemainingHealth[_playerEntity] = remaining;
        }
        _frameRemainingHealth[_playerEntity] -= MineStats.ContactDamage;

        var mineType = MineType.FromSize(mine.Size);
        Vector2 playerVel = GetCollisionVelocity(view, _playerEntity, default) + contact.Normal * mineType.PlayerContactForce;

        SetCollisionVelocity(_playerEntity, playerVel);

        int sparkCount = mineType.PlayerContactSparkCount;
        for (int i = 0; i < sparkCount; i++)
        {
            SpawnSpark(commands, minePos.Value, view.Rng);
        }
    }

    // The bounce of a jammed mine off the hull. `TryCollide`'s normal runs a → b, mine → player here, so
    // the mine leaves along its opposite; `Depth` is how deep the two were, which is the push that gets it
    // clear of the hull in the same tick. `MineDriftSystem` skips a jammed mine, so nothing steers it back.
    private static void BounceJammedMine(CommandBuffer commands, Entity mineEntity, EnemyMine mine, Vector2 minePos, Contact contact)
    {
        Vector2 away = Vector2.Zero - contact.Normal;
        commands.Add(new AddComponentCommand<Velocity>(mineEntity, new Velocity(away * mine.Speed)));
        commands.Add(new AddComponentCommand<Position>(mineEntity, new Position(minePos + away * contact.Depth)));
    }

    private void SpawnLootOnDeath(CommandBuffer commands, Vector2 position, MineType mineType, Random rng)
    {
        commands.AddEntity(new Position(position), new XpPickup(mineType.XpAmount, Radius: mineType.XpPickupRadius));

        if (rng.NextDouble() < LootStats.HealthOrbChance)
        {
            commands.AddEntity(new Position(position), new HealthOrb(Radius: mineType.XpPickupRadius + LootStats.HealthOrbRadiusOffset));
        }
    }

    private void SpawnShipLootOnDeath(CommandBuffer commands, Vector2 position, Random rng)
    {
        commands.AddEntity(new Position(position), new XpPickup(LootStats.ShipXpAmount, Radius: LootStats.ShipXpPickupRadius));

        // One band roll, one RNG number — the same number this call consumed before salvage existed.
        switch (LootRoll.RollShipDrop(rng))
        {
            case ShipDrop.HealthOrb:
                commands.AddEntity(new Position(position), new HealthOrb(Radius: LootStats.HealthOrbRadius));
                break;
            case ShipDrop.RerollToken:
                commands.AddEntity(new Position(position), new RerollToken(Radius: LootStats.RerollTokenRadius));
                break;
            case ShipDrop.SalvageField:
                commands.AddEntity(new Position(position), new SalvageField(Radius: LootStats.FieldCapsuleRadius));
                break;
            case ShipDrop.EcmPulse:
                // No lifetime: an ECM capsule waits on the field until the hull comes for it
                // (plans/ECM_PULSE_AND_SALVAGE_FIELD.md D2).
                commands.AddEntity(new Position(position), new EcmPulse(Radius: LootStats.EcmCapsuleRadius));
                break;
        }
    }
}
