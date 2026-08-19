using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public readonly record struct Dead();

public class CollisionSystem : GameSystem
{
    private const float PlayerRestitution = 0.2f;
    private const float AsteroidAsteroidRestitution = 0.4f;
    private const float AmmoRestitution = 0.15f;
    private const float CorrectionPercent = 0.4f;
    private const float Slop = 1.0f;
    private readonly List<(Entity, Position)> _asteroidPositions = new();
    private readonly List<(Entity, Position)> _shipPositions = new();
    private readonly List<Entity> _entitiesToDestroy = new();
    private readonly List<(Vector2 Position, MineType Type)> _effectsToSpawn = new();
    private readonly List<(Entity ammo, Entity target, Vector2 minePos, MineSize mineSize, int ammoDamage, Vector2 ammoVel)> _ammoToMineHits = new();
    private readonly List<(Entity ammo, Entity target, Vector2 hitPoint, Vector2 shipCenter, int ammoDamage, float shipRadius, byte graphicsId)> _ammoToShipHits = new();
    private readonly List<(int ammoDamage, int playerHealth)> _ammoToPlayerHits = new();

    // Per-frame collision state accumulators (fixes deferred command overwrite bug)
    private Dictionary<Entity, Vector2> _collisionVelocities = new();
    private Dictionary<Entity, Vector2> _positionCorrections = new();
    private Dictionary<Entity, float> _angularVelocityAccumulator = new();
    private Dictionary<Entity, int> _frameRemainingHealth = new();

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
        bool anyTruncated = false;
        Span<SpatialGrid.SpatialItem> queryBuffer = stackalloc SpatialGrid.SpatialItem[256];

        view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        void CheckPlayerCollision(Vector2 entityPos, float entityRadius, Action onCollision)
        {
            if (!hasPlayer) return;

            var diff = playerTuple.Value2.Value - entityPos;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = entityRadius + playerTuple.Value1.Radius;

            if (distSq < radiusSum * radiusSum && distSq >= 0.001f)
            {
                onCollision();
            }
        }

        var gridBuildSw = Stopwatch.StartNew();

        foreach (var (entity, asteroid) in view.GetEntitiesWithComponents<Asteroid>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, pos.Value, asteroid.Radius);
            _asteroidPositions.Add((entity, pos));

            CheckPlayerCollision(pos.Value, asteroid.Radius, () =>
                ResolveCircleVsCircle(view, entity, asteroid, playerEntity, commands));
        }

        foreach (var (entity, mine) in view.GetEntitiesWithComponents<EnemyMine>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyMine, pos.Value, mine.Radius, mine.Size);

            CheckPlayerCollision(pos.Value, mine.Radius, () =>
                ResolveMineVsPlayer(view, entity, mine, playerEntity, commands, playerTuple.Value1.Radius));
        }

        foreach (var (entity, ship) in view.GetEntitiesWithComponents<EnemyShip>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyShip, pos.Value, ship.Radius);
            _shipPositions.Add((entity, pos));

            CheckPlayerCollision(pos.Value, ship.Radius, () =>
            {
                var aPos = view.GetComponent<Position>(entity);
                var bPos = view.GetComponent<Position>(playerEntity);
                ResolveCollision(view, aPos, bPos, entity, playerEntity, ship.Radius, playerTuple.Value1.Radius, false, commands, 3000f);
            });

            int asteroidCount = _grid.GetQueryItems(pos.Value, ship.Radius, queryBuffer, out bool truncated);
            if (truncated) anyTruncated = true;

            for (int i = 0; i < asteroidCount; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.Asteroid) continue;

                ResolveCollision(view, pos, new Position(candidate.Position), entity, candidate.Id, ship.Radius, candidate.Radius, true, commands, 3000f);
            }
        }

        gridBuildSw.Stop();

        var asteroidSw = Stopwatch.StartNew();

        foreach (var (aEntity, aPos) in _asteroidPositions)
        {
            var asteroid = view.GetComponent<Asteroid>(aEntity);
            float aRadius = asteroid.Radius;
            int count = _grid.GetQueryItems(aPos.Value, aRadius, queryBuffer, out bool truncated);
            if (truncated) anyTruncated = true;

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.Asteroid) continue;
                if (candidate.Id.Value <= aEntity.Value) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                ResolveCollision(view, aPos, bPos, aEntity, candidate.Id, aRadius, candidate.Radius, true, commands);
            }

            int count2 = _grid.GetQueryItems(aPos.Value, aRadius + 15f, queryBuffer, out truncated);
            if (truncated) anyTruncated = true;

            for (int i = 0; i < count2; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyMine) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                var mine = view.GetComponent<EnemyMine>(candidate.Id);
                ResolveCollision(view, aPos, bPos, aEntity, candidate.Id, aRadius, mine.Radius, true, commands);
            }
        }

        asteroidSw.Stop();

        var shipSw = Stopwatch.StartNew();

        foreach (var (sEntity, sPos) in _shipPositions)
        {
            var ship = view.GetComponent<EnemyShip>(sEntity);
            float aRadius = ship.Radius;
            int count = _grid.GetQueryItems(sPos.Value, aRadius, queryBuffer, out bool truncated);
            if (truncated) anyTruncated = true;

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyShip) continue;
                if (candidate.Id.Value <= sEntity.Value) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                ResolveCollision(view, sPos, bPos, sEntity, candidate.Id, aRadius, candidate.Radius, true, commands);
            }

            int count2 = _grid.GetQueryItems(sPos.Value, aRadius + 15f, queryBuffer, out truncated);
            if (truncated) anyTruncated = true;

            for (int i = 0; i < count2; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyMine) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                var mine = view.GetComponent<EnemyMine>(candidate.Id);
                ResolveCollision(view, sPos, bPos, sEntity, candidate.Id, aRadius, mine.Radius, true, commands, 3000f);
            }
        }

        shipSw.Stop();

        var ammoDetectionSw = Stopwatch.StartNew();

        foreach (var (ammoEntity, ammo, ammoPos) in view.GetEntitiesWithComponents<Ammo, Position>())
        {
            float ammoRadius = ammo.Radius;

            Entity? closestAsteroidHit = null;
            Vector2 asteroidDiff = default;
            float asteroidDistSq = float.MaxValue;

            Entity? closestMineHit = null;
            MineSize? mineHitSize = null;
            Vector2 mineHitPos = default;
            float mineDistSq = float.MaxValue;

            Entity? closestShipHit = null;
            Vector2 shipHitPos = default;
            float shipDistSq = float.MaxValue;

            int count = _grid.GetQueryItems(ammoPos.Value, ammoRadius, queryBuffer, out bool truncated);
            if (truncated) anyTruncated = true;

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                var diff = candidate.Position - ammoPos.Value;
                float dSq = diff.X * diff.X + diff.Y * diff.Y;
                if (dSq < 0.001f) continue;

                switch (candidate.Kind)
                {
                    case SpatialGrid.CollisionKind.Asteroid:
                        {
                            float rSum = ammoRadius + candidate.Radius;
                            if (dSq < rSum * rSum && dSq < asteroidDistSq)
                            {
                                closestAsteroidHit = candidate.Id;
                                asteroidDiff = diff;
                                asteroidDistSq = dSq;
                            }
                        }
                        break;

                    case SpatialGrid.CollisionKind.EnemyMine when !ammo.IsEnemy:
                        {
                            float rSum = ammoRadius + candidate.Radius;
                            if (dSq < rSum * rSum && dSq < mineDistSq)
                            {
                                closestMineHit = candidate.Id;
                                mineHitPos = candidate.Position;
                                mineHitSize = candidate.Size!;
                                mineDistSq = dSq;
                            }
                        }
                        break;

                    case SpatialGrid.CollisionKind.EnemyShip when !ammo.IsEnemy:
                        {
                            float rSum = ammoRadius + candidate.Radius;
                            if (dSq < rSum * rSum && dSq < shipDistSq)
                            {
                                closestShipHit = candidate.Id;
                                var toAmmo = ammoPos.Value - candidate.Position;
                                float distToAmmo = (float)Math.Sqrt(toAmmo.X * toAmmo.X + toAmmo.Y * toAmmo.Y);
                                if (distToAmmo > 0.001f)
                                {
                                    shipHitPos = candidate.Position + (toAmmo / distToAmmo) * candidate.Radius;
                                }
                                else
                                {
                                    shipHitPos = candidate.Position;
                                }
                                shipDistSq = dSq;
                            }
                        }
                        break;
                }
            }

            if (closestAsteroidHit.HasValue)
            {
                var asteroid = view.GetComponent<Asteroid>(closestAsteroidHit.Value);
                ResolveAmmoVsAsteroid(view, ammoEntity, ammo, closestAsteroidHit.Value, asteroid, commands);
                _effectsToSpawn.Add((ammoPos.Value, MineType.Small));
                _entitiesToDestroy.Add(ammoEntity);
            }

            if (closestMineHit.HasValue)
            {
                if (!view.TryGetComponent<Health>(closestMineHit.Value, out var healthComp)) continue;
                if (!_frameRemainingHealth.TryGetValue(closestMineHit.Value, out var health))
                {
                    health = healthComp.Current;
                    _frameRemainingHealth[closestMineHit.Value] = health;
                }
                _frameRemainingHealth[closestMineHit.Value] -= ammo.Damage;
                _ammoToMineHits.Add((ammoEntity, closestMineHit.Value, mineHitPos, mineHitSize!.Value, ammo.Damage, ammo.Velocity));
                _effectsToSpawn.Add((mineHitPos!, MineType.FromSize(mineHitSize!.Value)));
            }

            if (closestShipHit.HasValue)
            {
                if (!view.TryGetComponent<Health>(closestShipHit.Value, out var shipHealthComp)) continue;
                var shipHealth = shipHealthComp.Current;
                if (!_frameRemainingHealth.TryGetValue(closestShipHit.Value, out var h))
                {
                    h = shipHealth;
                    _frameRemainingHealth[closestShipHit.Value] = h;
                }
                _frameRemainingHealth[closestShipHit.Value] -= ammo.Damage;
                var enemyShip = view.GetComponent<EnemyShip>(closestShipHit.Value);
                var shipPos = view.GetComponent<Position>(closestShipHit.Value);
                _ammoToShipHits.Add((ammoEntity, closestShipHit.Value, shipHitPos, shipPos.Value, ammo.Damage, enemyShip.Radius, enemyShip.GraphicsId));
            }

            if (!ammo.IsEnemy) continue;

            if (!hasPlayer) continue;

            var playerPos = playerTuple.Value2;
            var diff2 = playerPos.Value - ammoPos.Value;
            float distSq2 = diff2.X * diff2.X + diff2.Y * diff2.Y;
            float radiusSum2 = ammoRadius + playerTuple.Value1.Radius;

            if (distSq2 >= radiusSum2 * radiusSum2 || distSq2 < 0.001f) continue;

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

        ammoDetectionSw.Stop();

        sw.Stop();
        if (anyTruncated)
            DiagnosticLogger.LogWarning("CollisionSystem: spatial query buffer truncated; some collisions may be missed");

        DiagnosticLogger.LogSystem("Collision: grid build", gridBuildSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: asteroid collisions", asteroidSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: ship collisions", shipSw.ElapsedTicks);
        DiagnosticLogger.LogSystem("Collision: detection", ammoDetectionSw.ElapsedTicks);

        sw.Restart();

        foreach (var (ammoEntity, mineEntity, minePos, mineSize, ammoDamage, ammoVel) in _ammoToMineHits)
        {
            var minePosComp = view.GetComponent<Position>(mineEntity);
            var ammoPosComp = view.GetComponent<Position>(ammoEntity);

            var mineType = MineType.FromSize(mineSize);
            float mineRadius = mineType.Radius;
            const float AmmoMass = 1f;
            float mineMass = MathF.PI * mineRadius * mineRadius;
            float totalInvMass = 1f / AmmoMass + 1f / mineMass;

            var diff = ammoPosComp.Value - minePosComp.Value;
            float dist = (float)Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
            if (dist < 0.001f) dist = 0.001f;
            var normal = diff / dist;

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

        var mineDamageMap = new Dictionary<Entity, int>();
        foreach (var (_, mineEntity, _, _, ammoDamage, _) in _ammoToMineHits)
        {
            if (!mineDamageMap.TryGetValue(mineEntity, out var d)) mineDamageMap[mineEntity] = 0;
            mineDamageMap[mineEntity] += ammoDamage;
        }

        foreach (var (mineEntity, totalDamage) in mineDamageMap)
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
                commands.Add(new AddComponentCommand<Health>(mineEntity, new Health(remaining)));
            }
        }

        var shipDeathData = new Dictionary<Entity, (Vector2 hitPoint, Vector2 shipPos, float shipRadius, byte graphicsId)>();
        foreach (var (_, shipEntity, hitPoint, shipPos, _, shipRadius, graphicsId) in _ammoToShipHits)
        {
            if (!shipDeathData.ContainsKey(shipEntity))
                shipDeathData[shipEntity] = (hitPoint, shipPos, shipRadius, graphicsId);
        }

        foreach (var (shipEntity, data) in shipDeathData)
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
                    commands.AddEntity(new Position(data.hitPoint), new Velocity(sparkVel), new Spark(sparkLifetime, sparkLifetime));
                }
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(shipEntity, new Health(remaining)));
            }
        }

        var protectedEntities = new HashSet<Entity>();
        foreach (var (_, mineEntity, _, _, _, _) in _ammoToMineHits) protectedEntities.Add(mineEntity);
        foreach (var (_, shipEntity, _, _, _, _, _) in _ammoToShipHits) protectedEntities.Add(shipEntity);

        if (hasPlayer && _frameRemainingHealth.TryGetValue(playerEntity, out var playerRemaining))
        {
            if (playerRemaining <= 0)
            {
                commands.Add(new AddComponentCommand<Dead>(playerEntity, new Dead()));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(playerRemaining)));
            }
        }

        foreach (var entity in _entitiesToDestroy.Distinct())
        {
            if (!protectedEntities.Contains(entity))
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
        }

        foreach (var (position, mineType) in _effectsToSpawn.Distinct())
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
        commands.AddEntity(new Position(position), new Velocity(velocity), new Spark(sparkLifetime, sparkLifetime));
    }

    private void ResolveCircleVsCircle(
        WorldView view,
        Entity aEntity,
        Asteroid aAst,
        Entity bEntity,
        CommandBuffer commands)
    {
        var aPos = view.GetComponent<Position>(aEntity);
        var bPos = view.GetComponent<Position>(bEntity);

        float aRadius = aAst.Radius;
        float bRadius;
        bool isAsteroidVsAsteroid;

        if (view.TryGetComponent<Asteroid>(bEntity, out var bAst))
        {
            bRadius = bAst.Radius;
            isAsteroidVsAsteroid = true;
        }
        else if (view.TryGetComponent<EnemyShip>(bEntity, out var bShip))
        {
            bRadius = bShip.Radius;
            isAsteroidVsAsteroid = false;
        }
        else
        {
            var playerComp = view.GetComponent<Player>(bEntity);
            bRadius = playerComp.Radius;
            isAsteroidVsAsteroid = false;
        }

        ResolveCollision(view, aPos, bPos, aEntity, bEntity, aRadius, bRadius, isAsteroidVsAsteroid, commands);
    }


    private void ResolveCollision(
        WorldView view,
        Position aPos,
        Position bPos,
        Entity aEntity,
        Entity bEntity,
        float aRadius,
        float bRadius,
        bool isAsteroidVsAsteroid,
        CommandBuffer commands,
        float? aOverrideMass = null)
    {
        var diff = bPos.Value - aPos.Value;
        float distSq = diff.X * diff.X + diff.Y * diff.Y;
        float radiusSum = aRadius + bRadius;

        if (distSq >= radiusSum * radiusSum || distSq < 0.001f) return;

        float dist = (float)Math.Sqrt(distSq);
        var normal = diff / dist;

        float penetration = radiusSum - dist;

        float aMass = aOverrideMass ?? (MathF.PI * aRadius * aRadius);
        float bMass = MathF.PI * bRadius * bRadius;
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
            var rA = normal * aRadius;
            var rB = normal * bRadius;

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

                float aMOI = 0.5f * aMass * aRadius * aRadius;
                if (hasAAngVel)
                {
                    float torqueA = rA.X * frictionImpulse.Y - rA.Y * frictionImpulse.X;
                    SetCollisionAngularVelocity(aEntity, aAngVel + torqueA / aMOI);
                }

                float bMOI = 0.5f * bMass * bRadius * bRadius;
                if (hasBAngVel)
                {
                    float torqueB = rB.X * (-frictionImpulse.Y) - rB.Y * (-frictionImpulse.X);
                    SetCollisionAngularVelocity(bEntity, bAngVel + torqueB / bMOI);
                }
            }
        }
    }

    private void ResolveAmmoVsAsteroid(
        WorldView view,
        Entity ammoEntity,
        Ammo ammo,
        Entity asteroidEntity,
        Asteroid asteroid,
        CommandBuffer commands)
    {
        var ammoPos = view.GetComponent<Position>(ammoEntity);
        var asteroidPos = view.GetComponent<Position>(asteroidEntity);

        var diff = ammoPos.Value - asteroidPos.Value;
        float dist = (float)Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
        var normal = diff / dist;

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

    private void ResolveMineVsPlayer(WorldView view, Entity mineEntity, EnemyMine mine, Entity playerEntity, CommandBuffer commands, float PlayerRadius)
    {
        var minePos = view.GetComponent<Position>(mineEntity);
        var playerPos = view.GetComponent<Position>(playerEntity);

        var diff = playerPos.Value - minePos.Value;
        float distSq = diff.X * diff.X + diff.Y * diff.Y;
        float radiusSum = mine.Radius + PlayerRadius;

        if (distSq >= radiusSum * radiusSum || distSq < 0.001f) return;

        commands.Add(new DestroyEntityCommand(mineEntity));

        var playerHealth = view.GetComponent<Health>(playerEntity);
        if (!_frameRemainingHealth.TryGetValue(playerEntity, out var remaining))
        {
            remaining = playerHealth.Current;
            _frameRemainingHealth[playerEntity] = remaining;
        }
        _frameRemainingHealth[playerEntity] -= 3;

        var normal = diff / (float)Math.Sqrt(distSq);
        var mineType = MineType.FromSize(mine.Size);
        Vector2 playerVel = GetCollisionVelocity(view, playerEntity, default) + normal * mineType.PlayerContactForce;

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

        if (rng.NextDouble() < 0.05)
        {
            commands.AddEntity(new Position(position), new HealthOrb(Radius: mineType.XpPickupRadius + 2f));
        }
    }

    private void SpawnShipLootOnDeath(CommandBuffer commands, Vector2 position, Random rng)
    {
        commands.AddEntity(new Position(position), new XpPickup(3, Radius: 18f));

        if (rng.NextDouble() < 0.05)
        {
            commands.AddEntity(new Position(position), new HealthOrb(Radius: 20f));
        }
    }
}
