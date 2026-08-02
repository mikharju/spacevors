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
    private readonly List<Vector2> _mineCollisionPositions = new();

    private readonly List<(Entity, Position)> _asteroidPositions = new();
    private readonly List<(Entity, Position)> _shipPositions = new();
    private readonly List<Entity> _entitiesToDestroy = new();
    private readonly List<(Vector2 Position, MineSize Size)> _effectsToSpawn = new();
    private readonly List<(Entity ammo, Entity target, int health, Vector2 minePos, MineSize mineSize, int ammoDamage)> _ammoToMineHits = new();
    private readonly List<(Entity ammo, Entity target, int health, Vector2 hitPoint, Vector2 shipCenter, int ammoDamage, float shipRadius, byte graphicsId)> _ammoToShipHits = new();
    private readonly List<(Entity ammo, Entity target, Vector2 asteroidPos)> _ammoToAsteroidHits = new();
    private readonly List<(Entity ammo, int ammoDamage, int playerHealth)> _ammoToPlayerHits = new();

    // Per-frame collision state accumulators (fixes deferred command overwrite bug)
    private Dictionary<Entity, Vector2> _collisionVelocities = new();
    private Dictionary<Entity, Vector2> _positionCorrections = new();
    private Dictionary<Entity, float> _angularVelocityAccumulator = new();

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
        _ammoToAsteroidHits.Clear();
        _ammoToPlayerHits.Clear();
        _collisionVelocities.Clear();
        _positionCorrections.Clear();
        _angularVelocityAccumulator.Clear();

        view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        long playerCollisionTicks = 0;
        var playerSw = new Stopwatch();

        var gridBuildSw = Stopwatch.StartNew();

        foreach (var (entity, asteroid) in view.GetEntitiesWithComponents<Asteroid>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, pos.Value, asteroid.Radius);
            _asteroidPositions.Add((entity, pos));

            if (!hasPlayer) continue;

            playerSw.Restart();
            var diff = playerTuple.Value2.Value - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = asteroid.Radius + playerTuple.Value1.Radius;

            if (distSq < radiusSum * radiusSum && distSq >= 0.001f)
            {
                ResolveCircleVsCircle(view, entity, asteroid, playerEntity, commands);
            }
            playerSw.Stop();
            playerCollisionTicks += playerSw.ElapsedTicks;
        }

        foreach (var (entity, mine) in view.GetEntitiesWithComponents<EnemyMine>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyMine, pos.Value, mine.Radius, mine.Size);

            if (!hasPlayer) continue;

            playerSw.Restart();
            var diff = playerTuple.Value2.Value - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = mine.Radius + playerTuple.Value1.Radius;

            if (distSq < radiusSum * radiusSum && distSq >= 0.001f)
            {
                ResolveMineVsPlayer(view, entity, mine, playerEntity, commands, playerTuple.Value1.Radius);
            }
            playerSw.Stop();
            playerCollisionTicks += playerSw.ElapsedTicks;
        }

        foreach (var (entity, ship) in view.GetEntitiesWithComponents<EnemyShip>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyShip, pos.Value, ship.Radius);
            _shipPositions.Add((entity, pos));

            if (!hasPlayer) continue;

            playerSw.Restart();
            var diff = playerTuple.Value2.Value - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = ship.Radius + playerTuple.Value1.Radius;

            if (distSq < radiusSum * radiusSum && distSq >= 0.001f)
            {
                ResolveEnemyShipVsPlayer(view, entity, ship, playerEntity, commands);
            }
            playerSw.Stop();
            playerCollisionTicks += playerSw.ElapsedTicks;

            foreach (var (aEntity, aPos) in _asteroidPositions)
            {
                var asteroid = view.GetComponent<Asteroid>(aEntity);
                float radiusSum2 = ship.Radius + asteroid.Radius;
                var diff2 = pos.Value - aPos.Value;
                float distSq2 = diff2.X * diff2.X + diff2.Y * diff2.Y;

                if (distSq2 < radiusSum2 * radiusSum2 && distSq2 >= 0.001f)
                {
                    ResolveEnemyShipVsAsteroid(view, entity, ship, aEntity, asteroid, commands);
                }
            }
        }

        gridBuildSw.Stop();
        DiagnosticLogger.LogSystem("Collision: grid build", gridBuildSw.ElapsedTicks);

        var asteroidSw = Stopwatch.StartNew();

        Span<SpatialGrid.SpatialItem> queryBuffer = stackalloc SpatialGrid.SpatialItem[256];

        foreach (var (aEntity, aPos) in _asteroidPositions)
        {
            var asteroid = view.GetComponent<Asteroid>(aEntity);
            float aRadius = asteroid.Radius;
            int count = _grid.GetQueryItems(aPos.Value, aRadius, queryBuffer);

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.Asteroid) continue;
                if (candidate.Id.Value <= aEntity.Value) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                ResolveCollision(view, aPos, bPos, aEntity, candidate.Id, aRadius, candidate.Radius, true, commands);
            }
        }

        asteroidSw.Stop();
        DiagnosticLogger.LogSystem("Collision: asteroid collisions", asteroidSw.ElapsedTicks);

        var shipSw = Stopwatch.StartNew();

        foreach (var (sEntity, sPos) in _shipPositions)
        {
            var ship = view.GetComponent<EnemyShip>(sEntity);
            float aRadius = ship.Radius;
            int count = _grid.GetQueryItems(sPos.Value, aRadius, queryBuffer);

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyShip) continue;
                if (candidate.Id.Value <= sEntity.Value) continue;

                var bPos = view.GetComponent<Position>(candidate.Id);
                ResolveCollision(view, sPos, bPos, sEntity, candidate.Id, aRadius, candidate.Radius, true, commands);
            }
        }

        shipSw.Stop();
        DiagnosticLogger.LogSystem("Collision: ship collisions", shipSw.ElapsedTicks);

        long gridQueryTicks = 0;
        long candidateFilterTicks = 0;
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

            var qsw = Stopwatch.StartNew();
            int count = _grid.GetQueryItems(ammoPos.Value, ammoRadius, queryBuffer);
            qsw.Stop();
            gridQueryTicks += qsw.ElapsedTicks;

            for (int i = 0; i < count; i++)
            {
                ref readonly var candidate = ref queryBuffer[i];
                var fsw = Stopwatch.StartNew();
                var diff = candidate.Position - ammoPos.Value;
                float dSq = diff.X * diff.X + diff.Y * diff.Y;
                if (dSq < 0.001f)
                {
                    fsw.Stop();
                    candidateFilterTicks += fsw.ElapsedTicks;
                    continue;
                }

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
                fsw.Stop();
                candidateFilterTicks += fsw.ElapsedTicks;
            }

            if (closestAsteroidHit.HasValue)
            {
                var asteroid = view.GetComponent<Asteroid>(closestAsteroidHit.Value);
                ResolveAmmoVsAsteroid(view, ammoEntity, ammo, closestAsteroidHit.Value, asteroid, commands);
                _effectsToSpawn.Add((ammoPos.Value, MineSize.Small));
                _entitiesToDestroy.Add(ammoEntity);
            }

            if (closestMineHit.HasValue)
            {
                var mineComp = view.GetComponent<EnemyMine>(closestMineHit.Value);
                var health = view.GetComponent<Health>(closestMineHit.Value).Current;
                _ammoToMineHits.Add((ammoEntity, closestMineHit.Value, health, mineHitPos, mineHitSize!.Value, ammo.Damage));
                _effectsToSpawn.Add((mineHitPos!, mineHitSize!.Value));
            }

            if (closestShipHit.HasValue)
            {
                var shipHealth = view.GetComponent<Health>(closestShipHit.Value).Current;
                var enemyShip = view.GetComponent<EnemyShip>(closestShipHit.Value);
                var shipPos = view.GetComponent<Position>(closestShipHit.Value);
                _ammoToShipHits.Add((ammoEntity, closestShipHit.Value, shipHealth, shipHitPos, shipPos.Value, ammo.Damage, enemyShip.Radius, enemyShip.GraphicsId));
            }

            if (!ammo.IsEnemy) continue;

            if (!hasPlayer) continue;

            var playerPos = playerTuple.Value2;
            var diff2 = playerPos.Value - ammoPos.Value;
            float distSq2 = diff2.X * diff2.X + diff2.Y * diff2.Y;
            float radiusSum2 = ammoRadius + playerTuple.Value1.Radius;

            if (distSq2 >= radiusSum2 * radiusSum2 || distSq2 < 0.001f) continue;

            var playerHealth = view.GetComponent<Health>(playerEntity);
            _ammoToPlayerHits.Add((ammoEntity, ammo.Damage, playerHealth.Current));
            _effectsToSpawn.Add((playerPos.Value, MineSize.Small));
        }

        ammoDetectionSw.Stop();
        DiagnosticLogger.LogSystem("Collision: grid query", gridQueryTicks);
        DiagnosticLogger.LogSystem("Collision: candidate filtering", candidateFilterTicks);
        DiagnosticLogger.LogSystem("Collision: ammo collisions", ammoDetectionSw.ElapsedTicks - gridQueryTicks - candidateFilterTicks);

        playerSw.Stop();
        DiagnosticLogger.LogSystem("Collision: player collisions", playerCollisionTicks);

        sw.Stop();
        DiagnosticLogger.LogSystem("Collision: detection", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (ammoEntity, mineEntity, health, minePos, mineSize, ammoDamage) in _ammoToMineHits)
        {
            if (health <= ammoDamage)
            {
                SpawnLootOnDeath(commands, minePos, mineSize);
                commands.Add(new DestroyEntityCommand(mineEntity));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(mineEntity, new Health(health - ammoDamage)));
            }
            _entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var (ammoEntity, shipEntity, health, hitPoint, shipPos, ammoDamage, shipRadius, graphicsId) in _ammoToShipHits)
        {
            if (health <= ammoDamage)
            {
                SpawnShipLootOnDeath(commands, shipPos);
                commands.Add(new AddComponentCommand<Dead>(shipEntity, new Dead()));
                commands.Add(new AddComponentCommand<ShipDeathExplosion>(shipEntity, new ShipDeathExplosion(1.0f, hitPoint, shipRadius, graphicsId)));
                commands.AddEntity(new Position(hitPoint), new Explosion(shipRadius * 0.8f, 0.6f, 0.6f));
                for (int i = 0; i < 4; i++)
                {
                    float sparkAngle = (float)(Random.Shared.NextDouble() * MathF.PI * 2f);
                    float sparkSpeed = (shipRadius + 15f) / 0.7f * (0.8f + (float)Random.Shared.NextDouble() * 0.4f);
                    Vector2 sparkVel = new Vector2((float)Math.Cos(sparkAngle) * sparkSpeed, (float)Math.Sin(sparkAngle) * sparkSpeed);
                    float sparkLifetime = 0.7f + (float)Random.Shared.NextDouble() * 0.3f;
                    commands.AddEntity(new Position(hitPoint), new Velocity(sparkVel), new Spark(sparkLifetime, sparkLifetime));
                }
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(shipEntity, new Health(health - ammoDamage)));
            }
            _entitiesToDestroy.Add(ammoEntity);
        }

        var hitMineOrShip = new HashSet<Entity>();
        foreach (var (_, mineEntity, _, _, _, _) in _ammoToMineHits) hitMineOrShip.Add(mineEntity);
        foreach (var (_, shipEntity, _, _, _, _, _, _) in _ammoToShipHits) hitMineOrShip.Add(shipEntity);

        foreach (var entity in _entitiesToDestroy.Distinct())
        {
            if (!hitMineOrShip.Contains(entity))
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
        }

        foreach (var (ammoEntity, ammoDamage, playerHealth) in _ammoToPlayerHits)
        {
            if (playerHealth <= ammoDamage)
            {
                commands.Add(new AddComponentCommand<Dead>(playerEntity, new Dead()));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(playerHealth - ammoDamage)));
            }
            _entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var entity in _entitiesToDestroy.Distinct())
        {
            if (_ammoToPlayerHits.Any(x => x.ammo == entity) || hitMineOrShip.Contains(entity))
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
        }

        foreach (var (position, mineSize) in _effectsToSpawn.Distinct())
        {
            SpawnExplosion(commands, position, mineSize);
            int sparkCount = mineSize == MineSize.Large ? 7 : 3;
            for (int i = 0; i < sparkCount; i++)
            {
                SpawnSpark(commands, position);
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

    private void SpawnExplosion(CommandBuffer commands, Vector2 position, MineSize mineSize)
    {
        float radius = mineSize == MineSize.Large ? 30f : 15f;
        commands.AddEntity(new Position(position), new Explosion(radius, 0.5f, 0.5f));
    }

    private void SpawnSpark(CommandBuffer commands, Vector2 position)
    {
        float angle = (float)(Random.Shared.NextDouble() * MathF.PI * 2f);
        float speed = 50f + (float)Random.Shared.NextDouble() * 100f;
        Vector2 velocity = new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed);
        float sparkLifetime = 0.8f + (float)Random.Shared.NextDouble() * 0.6f;
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

        var diff = asteroidPos.Value - ammoPos.Value;
        float dist = (float)Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
        var normal = diff / dist;

        Vector2 ammoVel = ammo.Velocity;
        var asteroidVel = GetCollisionVelocity(view, asteroidEntity, default);

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

            asteroidVel += frictionImpulse / asteroidMass;
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

        _mineCollisionPositions.Add(minePos.Value);

        commands.Add(new DestroyEntityCommand(mineEntity));

        var playerHealth = view.GetComponent<Health>(playerEntity);
        if (playerHealth.Current <= 3)
        {
            commands.Add(new AddComponentCommand<Dead>(playerEntity, new Dead()));
        }
        else
        {
            commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(playerHealth.Current - 3)));
        }

        var normal = diff / (float)Math.Sqrt(distSq);
        float explosionForce = mine.Size == MineSize.Large ? 240f : 120f;
        Vector2 playerVel = GetCollisionVelocity(view, playerEntity, default) + normal * explosionForce;

        SetCollisionVelocity(playerEntity, playerVel);

        int sparkCount = mine.Size == MineSize.Large ? 10 : 5;
        for (int i = 0; i < sparkCount; i++)
        {
            SpawnSpark(commands, minePos.Value);
        }
    }

    private void ResolveEnemyShipVsPlayer(
        WorldView view,
        Entity aEntity,
        EnemyShip aShip,
        Entity bEntity,
        CommandBuffer commands)
    {
        var aPos = view.GetComponent<Position>(aEntity);
        var bPos = view.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
        float bRadius = view.GetComponent<Player>(bEntity).Radius;

        ResolveCollision(view, aPos, bPos, aEntity, bEntity, aRadius, bRadius, false, commands, 3000f);
    }

    private void ResolveEnemyShipVsAsteroid(
        WorldView view,
        Entity aEntity,
        EnemyShip aShip,
        Entity bEntity,
        Asteroid bAst,
        CommandBuffer commands)
    {
        var aPos = view.GetComponent<Position>(aEntity);
        var bPos = view.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
        float bRadius = bAst.Radius;

        ResolveCollision(view, aPos, bPos, aEntity, bEntity, aRadius, bRadius, true, commands, 3000f);
    }

    private void ResolveEnemyShipVsEnemyShip(
        WorldView view,
        Entity aEntity,
        EnemyShip aShip,
        Entity bEntity,
        CommandBuffer commands)
    {
        var aPos = view.GetComponent<Position>(aEntity);
        var bPos = view.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
        var bShip = view.GetComponent<EnemyShip>(bEntity);
        float bRadius = bShip.Radius;

        ResolveCollision(view, aPos, bPos, aEntity, bEntity, aRadius, bRadius, true, commands, 3000f);
    }

    private void ResolveEnemyShipVsMine(
        WorldView view,
        Entity aEntity,
        EnemyShip aShip,
        Entity bEntity,
        EnemyMine bMine,
        CommandBuffer commands)
    {
        var aPos = view.GetComponent<Position>(aEntity);
        var bPos = view.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
        float bRadius = bMine.Radius;

        ResolveCollision(view, aPos, bPos, aEntity, bEntity, aRadius, bRadius, true, commands, 3000f);
    }

    private void SpawnLootOnDeath(CommandBuffer commands, Vector2 position, MineSize mineSize)
    {
        int xpAmount = mineSize == MineSize.Small ? 1 : 2;
        float xpRadius = mineSize == MineSize.Small ? 6f : 9f;

        commands.AddEntity(new Position(position), new XpPickup(xpAmount, Radius: xpRadius));

        if (Random.Shared.NextDouble() < 0.05)
        {
            commands.AddEntity(new Position(position), new HealthOrb(Radius: xpRadius + 2f));
        }
    }

    private void SpawnShipLootOnDeath(CommandBuffer commands, Vector2 position)
    {
        commands.AddEntity(new Position(position), new XpPickup(3, Radius: 18f));

        if (Random.Shared.NextDouble() < 0.05)
        {
            commands.AddEntity(new Position(position), new HealthOrb(Radius: 20f));
        }
    }
}
