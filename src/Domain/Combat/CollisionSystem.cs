using System.Diagnostics;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public readonly record struct Dead();

public class CollisionSystem : GameSystem
{
    private const float PlayerRestitution = 0.2f;
    private const float AsteroidAsteroidRestitution = 0.4f;
    private const float AmmoRestitution = 0.15f;
    private const float CorrectionPercent = 0.4f;
    private const float Slop = 1.0f;

    private const float EnemyShipMass = 3000f;
    private readonly List<(Entity, Position)> _asteroidPositions = new();
    private readonly List<(Entity, Position)> _shipPositions = new();
    private readonly HashSet<Entity> _entitiesToDestroy = new();
    private readonly HashSet<(Vector2 Position, MineType Type)> _effectsToSpawn = new();
    private readonly List<(Entity ammo, Entity target, Contact hit, Vector2 minePos, MineSize mineSize, int ammoDamage, Vector2 ammoVel)> _ammoToMineHits = new();
    private readonly List<(Entity ammo, Entity target, Vector2 hitPoint, Vector2 shipCenter, int ammoDamage, float shipRadius, byte graphicsId)> _ammoToShipHits = new();
    private readonly List<(int ammoDamage, int playerHealth)> _ammoToPlayerHits = new();

    // Per-frame collision state accumulators (fixes deferred command overwrite bug)
    private readonly Dictionary<Entity, Vector2> _collisionVelocities = new();
    private readonly Dictionary<Entity, Vector2> _positionCorrections = new();
    private readonly Dictionary<Entity, float> _angularVelocityAccumulator = new();
    private readonly Dictionary<Entity, int> _frameRemainingHealth = new();
    private readonly HashSet<Entity> _hitMines = new();
    private readonly HashSet<Entity> _hitAsteroids = new();
    private readonly HashSet<Entity> _hitMissiles = new();
    private readonly Dictionary<Entity, (Vector2 hitPoint, Vector2 shipPos, float shipRadius, byte graphicsId)> _shipDeathData = new();

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        _grid.Clear();
        _asteroidPositions.Clear();
        _shipPositions.Clear();
        _entitiesToDestroy.Clear();
        _effectsToSpawn.Clear();
        _ammoToMineHits.Clear();
        _ammoToShipHits.Clear();
        _ammoToPlayerHits.Clear();
        _collisionVelocities.Clear();
        _positionCorrections.Clear();
        _angularVelocityAccumulator.Clear();
        _frameRemainingHealth.Clear();
        _hitMines.Clear();
        _hitAsteroids.Clear();
        _hitMissiles.Clear();
        _shipDeathData.Clear();
        bool hasPlayer = view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;

        // The player's body, built once per tick: its shape turns with the hull, so every test the player is
        // in reads the same transformed primitives instead of re-deriving them.
        NarrowPhase.CollisionBody playerBody = hasPlayer ? PlayerBody(view, playerEntity, playerTuple.Value1) : default;

        // The player's collision circle is the conservative reject in front of its shape, exactly as a hull's
        // circle is in front of a bullet's sweep: the shape never reaches past the radius, so a body that
        // misses the circle cannot touch the shape either.
        void CheckPlayerCollision(Vector2 entityPos, float entityRadius, Action onCollision)
        {
            if (!hasPlayer) return;
            if (NarrowPhase.TryCollide(playerTuple.Value2.Value, playerTuple.Value1.Radius, entityPos, entityRadius, out _))
                onCollision();
        }

        var gridBuildSw = Stopwatch.StartNew();

        foreach (var (entity, asteroid) in view.GetEntitiesWithComponents<Asteroid>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, pos.Value, asteroid.Radius);
            _asteroidPositions.Add((entity, pos));

            CheckPlayerCollision(pos.Value, asteroid.Radius, () =>
                ResolveCollision(view, CircleBody(pos.Value, asteroid.Radius), entity,
                    playerBody, playerEntity, false, commands));
        }

        foreach (var (entity, mine) in view.GetEntitiesWithComponents<EnemyMine>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyMine, pos.Value, mine.Radius, mine.Size);

            CheckPlayerCollision(pos.Value, mine.Radius, () =>
                ResolveMineVsPlayer(view, entity, mine, playerEntity, commands, playerBody));
        }

        foreach (var (entity, ship) in view.GetEntitiesWithComponents<EnemyShip>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyShip, pos.Value, ship.Radius);
            _shipPositions.Add((entity, pos));
            var hull = HullBody(view, entity, ship);

            CheckPlayerCollision(pos.Value, ship.Radius, () =>
            {
                ResolveCollision(view, hull, entity,
                    playerBody, playerEntity, false, commands, EnemyShipMass);
            });

            int asteroidCount = QueryCircle(pos.Value, ship.Radius);
            var queryBuffer = _queryBuffer;

            for (int i = 0; i < asteroidCount; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.Asteroid) continue;

                ResolveCollision(view, hull, entity,
                    CircleBody(candidate.Position, candidate.Radius), candidate.Id, true, commands, EnemyShipMass);
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

        gridBuildSw.Stop();

        var asteroidSw = Stopwatch.StartNew();

        foreach (var (aEntity, aPos) in _asteroidPositions)
        {
            var asteroid = view.GetComponent<Asteroid>(aEntity);
            float aRadius = asteroid.Radius;
            int count = QueryCircle(aPos.Value, aRadius);
            var queryBuffer = _queryBuffer;

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.Asteroid) continue;
                if (candidate.Id.Value <= aEntity.Value) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                ResolveCollision(view, CircleBody(aPos.Value, aRadius), aEntity,
                    CircleBody(bPos.Value, candidate.Radius), candidate.Id, true, commands);
            }

            int count2 = QueryCircle(aPos.Value, aRadius + 15f);
            queryBuffer = _queryBuffer;

            for (int i = 0; i < count2; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyMine) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                var mine = view.GetComponent<EnemyMine>(candidate.Id);
                ResolveCollision(view, CircleBody(aPos.Value, aRadius), aEntity,
                    CircleBody(bPos.Value, mine.Radius), candidate.Id, true, commands);
            }
        }

        asteroidSw.Stop();

        var shipSw = Stopwatch.StartNew();

        foreach (var (sEntity, sPos) in _shipPositions)
        {
            var ship = view.GetComponent<EnemyShip>(sEntity);
            float aRadius = ship.Radius;
            var hull = HullBody(view, sEntity, ship);
            int count = QueryCircle(sPos.Value, aRadius);
            var queryBuffer = _queryBuffer;

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyShip) continue;
                if (candidate.Id.Value <= sEntity.Value) continue;

                ResolveCollision(view, hull, sEntity,
                    HullBody(view, candidate.Id, view.GetComponent<EnemyShip>(candidate.Id)), candidate.Id, true, commands);
            }

            int count2 = QueryCircle(sPos.Value, aRadius + 15f);
            queryBuffer = _queryBuffer;

            for (int i = 0; i < count2; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyMine) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                var mine = view.GetComponent<EnemyMine>(candidate.Id);
                ResolveCollision(view, hull, sEntity,
                    CircleBody(bPos.Value, mine.Radius), candidate.Id, true, commands, EnemyShipMass);
            }
        }

        shipSw.Stop();

        var ammoDetectionSw = Stopwatch.StartNew();

        foreach (var (ammoEntity, ammo, ammoPos) in view.GetEntitiesWithComponents<Ammo, Position>())
        {
            float ammoRadius = ammo.Radius;

            // Swept capsule: ammo velocity is constant (ammo never gets Acceleration), so the
            // position at the start of the tick is exactly pos - vel*dt. The bullet is the
            // segment [sweepFrom, sweepTo] inflated by its radius, so a bullet that jumps over
            // a target between ticks still intersects it. t orders hits along travel.
            Vector2 sweepFrom = ammoPos.Value - ammo.Velocity * deltaTime;
            Vector2 sweepTo = ammoPos.Value;
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
                        HullBody(view, candidate.Id, view.GetComponent<EnemyShip>(candidate.Id)), out hit, out t))
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

            if (earliestAsteroidHit.HasValue)
            {
                var asteroid = view.GetComponent<Asteroid>(earliestAsteroidHit.Value);
                ResolveAmmoVsAsteroid(view, ammo, earliestAsteroidHit.Value, asteroid, asteroidHit, commands);
                if (DamageAsteroid(view, earliestAsteroidHit.Value, ammo.Damage))
                    _effectsToSpawn.Add((asteroidHit.Point, MineType.Small));
                _entitiesToDestroy.Add(ammoEntity);
            }

            if (earliestMineHit.HasValue && view.TryGetComponent<Health>(earliestMineHit.Value, out var healthComp))
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

            if (earliestShipHit.HasValue && view.TryGetComponent<Health>(earliestShipHit.Value, out var shipHealthComp))
            {
                var shipHealth = shipHealthComp.Current;
                if (!_frameRemainingHealth.TryGetValue(earliestShipHit.Value, out var h))
                {
                    h = shipHealth;
                    _frameRemainingHealth[earliestShipHit.Value] = h;
                }
                _frameRemainingHealth[earliestShipHit.Value] -= ammo.Damage;
                var enemyShip = view.GetComponent<EnemyShip>(earliestShipHit.Value);
                var shipPos = view.GetComponent<Position>(earliestShipHit.Value);
                _ammoToShipHits.Add((ammoEntity, earliestShipHit.Value, shipHitPos, shipPos.Value, ammo.Damage, enemyShip.Radius, enemyShip.GraphicsId));
                // Consume the bullet on hit so it cannot re-hit the same ship on later frames.
                _entitiesToDestroy.Add(ammoEntity);
            }

            if (earliestMissileHit.HasValue && view.TryGetComponent<Health>(earliestMissileHit.Value, out var missileHealthComp))
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

            if (!ammo.IsEnemy) continue;

            if (!hasPlayer) continue;

            var playerPos = playerTuple.Value2;

            // The same two steps as against a hull: the circle rejects a round that cannot reach the player,
            // and only a round inside it is tested against the hull's shape.
            if (!NarrowPhase.TrySweep(sweepFrom, sweepTo, ammoRadius, playerPos.Value, playerTuple.Value1.Radius, out _, out _))
                continue;
            if (!NarrowPhase.TrySweep(sweepFrom, sweepTo, ammoRadius, playerBody, out _, out _))
                continue;

            var playerHealth = view.GetComponent<Health>(playerEntity);
            if (!_frameRemainingHealth.TryGetValue(playerEntity, out var remaining))
            {
                remaining = playerHealth.Current;
                _frameRemainingHealth[playerEntity] = remaining;
            }
            _frameRemainingHealth[playerEntity] -= ammo.Damage;
            _effectsToSpawn.Add((playerPos.Value, MineType.Small));
            _entitiesToDestroy.Add(ammoEntity);
        }

        // Laser beams: static segments, damage applied exactly once (DamagePending) to the
        // first thing hit. Runs before the mine/ship death loops so damage flows through the
        // same per-frame health accumulators.
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
                if (hasPlayer && NarrowPhase.TrySweep(beamStart, beamEnd, beam.Width, playerBody, out _, out _))
                {
                    var beamPlayerHealth = view.GetComponent<Health>(playerEntity);
                    if (!_frameRemainingHealth.TryGetValue(playerEntity, out var remaining))
                    {
                        remaining = beamPlayerHealth.Current;
                        _frameRemainingHealth[playerEntity] = remaining;
                    }
                    _frameRemainingHealth[playerEntity] -= beam.Damage;

                    stopLength = BeamEntryLength(playerBody, beamStart, beam.Direction, beam.Width, beam.Length);
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
                    if (!NarrowPhase.TrySweep(beamStart, beamEnd, beam.Width, candidate.Position, candidate.Radius, out Contact hit, out float t)
                        || t >= firstT) continue;

                    // The same two steps as the ammo pass: the bounding circle rejects cheaply, and a
                    // hull that survives it is tested against its shape. The shape sits inside the
                    // circle, so its touch along the beam is never earlier and has to be re-compared.
                    if (candidate.Kind == SpatialGrid.CollisionKind.EnemyShip
                        && (!NarrowPhase.TrySweep(beamStart, beamEnd, beam.Width,
                                HullBody(view, candidate.Id, view.GetComponent<EnemyShip>(candidate.Id)), out hit, out t)
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
                                _frameRemainingHealth[firstHit.Value] -= beam.Damage;
                                var beamShip = view.GetComponent<EnemyShip>(firstHit.Value);
                                var beamShipPos = view.GetComponent<Position>(firstHit.Value);
                                Vector2 beamHitPoint = beamShipPos.Value + firstContact.Normal * beamShip.Radius;
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
                    // exact circle or hull primitive, not the bounding circle — so the beam tip and
                    // its glow sit on the first visible contact, grazing hits included.
                    NarrowPhase.CollisionBody hitBody = firstKind == SpatialGrid.CollisionKind.EnemyShip
                        ? HullBody(view, firstHit.Value, view.GetComponent<EnemyShip>(firstHit.Value))
                        : CircleBody(firstCandidatePos, firstCandidateRadius);
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

        ammoDetectionSw.Stop();

        sw.Stop();
        DiagnosticLogger.LogSystem("Collision: grid build", gridBuildSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: asteroid collisions", asteroidSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: ship collisions", shipSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: detection", ammoDetectionSw.ElapsedTicks);

        sw.Restart();

        foreach (var (ammoEntity, mineEntity, hit, _, mineSize, _, ammoVel) in _ammoToMineHits)
        {
            var minePosComp = view.GetComponent<Position>(mineEntity);

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
            var impulse = normal * j;
            mineVel += impulse * (1f / mineMass);
            SetCollisionVelocity(mineEntity, mineVel);

            _entitiesToDestroy.Add(ammoEntity);
        }

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
                Vector2 inheritedVel = deathVel.Value;
                commands.Add(new AddComponentCommand<ShipDeathExplosion>(shipEntity, new ShipDeathExplosion(1.0f, data.hitPoint, data.shipRadius, data.graphicsId, inheritedVel)));
                commands.AddEntity(new Position(data.hitPoint), new Explosion(data.shipRadius * 0.8f, 0.6f, 0.6f));
                var rng = view.Rng;
                for (int i = 0; i < 4; i++)
                {
                    float sparkAngle = (float)(rng.NextDouble() * MathF.PI * 2f);
                    float sparkSpeed = (data.shipRadius + 15f) / 0.7f * (0.8f + (float)rng.NextDouble() * 0.4f);
                    Vector2 sparkVel = new Vector2((float)Math.Cos(sparkAngle) * sparkSpeed, (float)Math.Sin(sparkAngle) * sparkSpeed);
                    float sparkLifetime = 0.7f + (float)rng.NextDouble() * 0.3f;
                    commands.AddEntity(new Position(data.hitPoint), new Velocity(sparkVel), new DamageSpark(sparkLifetime, sparkLifetime));
                }
            }
            else
            {
                var shipHealth = view.GetComponent<Health>(shipEntity);
                commands.Add(new AddComponentCommand<Health>(shipEntity, new Health(remaining, shipHealth.Max)));
            }
        }

        if (hasPlayer && _frameRemainingHealth.TryGetValue(playerEntity, out var playerRemaining))
        {
            if (playerRemaining <= 0)
            {
                // Zero the health on the fatal hit: the hull damage shading reads Health, and
                // without this write the killing blow from high hp renders a pristine hull.
                var playerHealth = view.GetComponent<Health>(playerEntity);
                commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(0, playerHealth.Max)));
                commands.Add(new AddComponentCommand<Dead>(playerEntity, new Dead()));
            }
            else
            {
                var playerHealth = view.GetComponent<Health>(playerEntity);
                commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(playerRemaining, playerHealth.Max)));
            }
        }

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

    private SpatialGrid _grid = new(128f);

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
    static NarrowPhase.CollisionBody PlayerBody(WorldView view, Entity playerEntity, Player player)
    {
        // A world that never gave the player a heading collides with it facing the way it is drawn.
        view.TryGetComponent<Rotation>(playerEntity, out var rotation);
        return new(view.GetComponent<Position>(playerEntity).Value, rotation.Angle,
            player.Shape ?? HullShape.Circle, player.Radius);
    }

    // Rocks, mines and guided rounds are drawn as dots: they collide as one circle, as they always did.
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
        CommandBuffer commands,
        float? aOverrideMass = null)
    {
        if (!NarrowPhase.TryCollide(a, b, out Contact contact)) return;

        var normal = contact.Normal;
        float penetration = contact.Depth;

        float aMass = aOverrideMass ?? (MathF.PI * a.Scale * a.Scale);
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
        float j = -(1 + restitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        aVel -= impulse * invMassA;
        bVel += impulse * invMassB;

        SetCollisionVelocity(aEntity, aVel);
        SetCollisionVelocity(bEntity, bVel);

        var correctedRelVel = bVel - aVel;
        var velNormalComponent = Vector2.Dot(correctedRelVel, normal);
        var relVelTangent = correctedRelVel - normal * velNormalComponent;
        float tangentSpeed = relVelTangent.Magnitude;

        const float Friction = 0.2f;

        if (tangentSpeed > Slop)
        {
            // Lever arms measured from the contact, not from `normal · radius`: with a circle the two
            // are the same arm, and only the first one is still true for a shape that is not a circle.
            var rA = contact.Point - a.Position;
            var rB = contact.Point - b.Position;

            float aAngVel = GetCollisionAngularVelocity(view, aEntity, 0f);
            bool hasAAngVel = view.TryGetComponent<AngularVelocity>(aEntity, out _);

            float bAngVel = GetCollisionAngularVelocity(view, bEntity, 0f);
            bool hasBAngVel = view.TryGetComponent<AngularVelocity>(bEntity, out _);

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

                // Mass and inertia stay what the hull's bounding radius has always given them: the shape
                // decides what a hull hits, not how heavy it is, and changing that is a balance change
                // that has to be measured on its own.
                float aMOI = 0.5f * aMass * a.Scale * a.Scale;
                if (hasAAngVel)
                {
                    float torqueA = rA.X * frictionImpulse.Y - rA.Y * frictionImpulse.X;
                    SetCollisionAngularVelocity(aEntity, aAngVel + torqueA / aMOI);
                }

                float bMOI = 0.5f * bMass * b.Scale * b.Scale;
                if (hasBAngVel)
                {
                    float torqueB = rB.X * (-frictionImpulse.Y) - rB.Y * (-frictionImpulse.X);
                    SetCollisionAngularVelocity(bEntity, bAngVel + torqueB / bMOI);
                }
            }
        }
    }

    // Impact damage (plans/IMPACT_DAMAGE.md). Damage flows through the same per-frame health
    // accumulator as bullet damage, so the existing death pass covers impact kills.
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
        var impulse = normal * j;

        asteroidVel += impulse * (1f / asteroidMass);
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

    private void ResolveMineVsPlayer(WorldView view, Entity mineEntity, EnemyMine mine, Entity playerEntity, CommandBuffer commands, NarrowPhase.CollisionBody playerBody)
    {
        var minePos = view.GetComponent<Position>(mineEntity);

        if (!NarrowPhase.TryCollide(CircleBody(minePos.Value, mine.Radius), playerBody, out Contact contact)) return;

        commands.Add(new DestroyEntityCommand(mineEntity));

        var playerHealth = view.GetComponent<Health>(playerEntity);
        if (!_frameRemainingHealth.TryGetValue(playerEntity, out var remaining))
        {
            remaining = playerHealth.Current;
            _frameRemainingHealth[playerEntity] = remaining;
        }
        _frameRemainingHealth[playerEntity] -= MineStats.ContactDamage;

        var mineType = MineType.FromSize(mine.Size);
        Vector2 playerVel = GetCollisionVelocity(view, playerEntity, default) + contact.Normal * mineType.PlayerContactForce;

        SetCollisionVelocity(playerEntity, playerVel);

        int sparkCount = mineType.PlayerContactSparkCount;
        for (int i = 0; i < sparkCount; i++)
        {
            SpawnSpark(commands, minePos.Value, view.Rng);
        }
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

        if (rng.NextDouble() < LootStats.HealthOrbChance)
        {
            commands.AddEntity(new Position(position), new HealthOrb(Radius: LootStats.HealthOrbRadius));
        }
    }
}
