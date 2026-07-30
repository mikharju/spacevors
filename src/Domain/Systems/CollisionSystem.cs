using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public readonly record struct Dead();

public class CollisionSystem : GameSystem
{
    private const float PlayerRestitution = 0.2f;
    private const float AsteroidAsteroidRestitution = 0.4f;
    private const float AmmoRestitution = 0.15f;
    private const float RotationFactor = 0.03f;
    private const float CorrectionPercent = 0.8f;
    private const float Slop = 0.01f;
    private const float AmmoMass = 1f;
    private readonly List<Vector2> _mineCollisionPositions = new();

    private readonly List<(Entity, Position)> _asteroidPositions = new();
    private readonly List<(Entity, Position)> _shipPositions = new();
    private readonly List<Entity> _entitiesToDestroy = new();
    private readonly List<(Vector2 Position, MineSize Size)> _effectsToSpawn = new();
    private readonly List<(Entity, Entity)> _ammoToMineHits = new();
    private readonly List<(Entity, Entity)> _ammoToShipHits = new();
    private readonly HashSet<Entity> _ammoToPlayerHits = new();

    public override void GenerateUpdateCommands(WorldView view, float deltaTime, CommandBuffer commands)
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

        var playerTuple = view.GetEntitiesWithComponents<Player, Position>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        foreach (var (entity, asteroid) in view.GetEntitiesWithComponents<Asteroid>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.Asteroid, pos.Value, asteroid.Radius);
            _asteroidPositions.Add((entity, pos));

            if (!hasPlayer) continue;

            var diff = playerTuple.Value2.Value - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = asteroid.Radius + 18f;

            if (distSq < radiusSum * radiusSum && distSq >= 0.001f)
            {
                ResolveCircleVsCircle(view, entity, asteroid, playerEntity, commands);
            }
        }

        foreach (var (aEntity, aPos) in _asteroidPositions)
        {
            var asteroid = view.GetComponent<Asteroid>(aEntity);
            foreach (var candidate in _grid.Query(aPos.Value, asteroid.Radius))
            {
                if (candidate.Kind != SpatialGrid.CollisionKind.Asteroid) continue;
                if (candidate.Id.Value <= aEntity.Value) continue;

                ResolveCircleVsCircle(view, aEntity, asteroid, candidate.Id, commands);
            }
        }

        foreach (var (entity, mine) in view.GetEntitiesWithComponents<EnemyMine>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyMine, pos.Value, mine.Radius);

            if (!hasPlayer) continue;

            var diff = playerTuple.Value2.Value - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = mine.Radius + 18f;

            if (distSq < radiusSum * radiusSum && distSq >= 0.001f)
            {
                ResolveMineVsPlayer(view, entity, mine, playerEntity, commands);
            }
        }

        foreach (var (entity, ship) in view.GetEntitiesWithComponents<EnemyShip>())
        {
            var pos = view.GetComponent<Position>(entity);
            _grid.Insert(entity, SpatialGrid.CollisionKind.EnemyShip, pos.Value, ship.Radius);
            _shipPositions.Add((entity, pos));

            if (!hasPlayer) continue;

            var diff = playerTuple.Value2.Value - pos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = ship.Radius + 18f;

            if (distSq < radiusSum * radiusSum && distSq >= 0.001f)
            {
                ResolveEnemyShipVsPlayer(view, entity, ship, playerEntity, commands);
            }
        }

        foreach (var (sEntity, sPos) in _shipPositions)
        {
            var ship = view.GetComponent<EnemyShip>(sEntity);
            foreach (var candidate in _grid.Query(sPos.Value, ship.Radius))
            {
                if (candidate.Kind != SpatialGrid.CollisionKind.EnemyShip) continue;
                if (candidate.Id.Value <= sEntity.Value) continue;

                ResolveEnemyShipVsEnemyShip(view, sEntity, ship, candidate.Id, commands);
            }
        }

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

            foreach (var candidate in _grid.Query(ammoPos.Value, ammoRadius))
            {
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
                                var mineComp = view.GetComponent<EnemyMine>(candidate.Id);
                                mineHitSize = mineComp.Size;
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
                                shipHitPos = candidate.Position;
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
                _effectsToSpawn.Add((ammoPos.Value, MineSize.Small));
                _entitiesToDestroy.Add(ammoEntity);
                continue;
            }

            if (closestMineHit.HasValue)
            {
                _ammoToMineHits.Add((ammoEntity, closestMineHit.Value));
                _effectsToSpawn.Add((mineHitPos!, mineHitSize!.Value));
            }

            if (closestShipHit.HasValue)
            {
                _ammoToShipHits.Add((ammoEntity, closestShipHit.Value));
                _effectsToSpawn.Add((shipHitPos!, MineSize.Small));
            }

            if (!ammo.IsEnemy) continue;

            if (!hasPlayer) continue;

            var playerPos = playerTuple.Value2;
            var diff2 = playerPos.Value - ammoPos.Value;
            float distSq2 = diff2.X * diff2.X + diff2.Y * diff2.Y;
            float radiusSum2 = ammoRadius + 18f;

            if (distSq2 >= radiusSum2 * radiusSum2 || distSq2 < 0.001f) continue;

            _ammoToPlayerHits.Add(ammoEntity);
            _effectsToSpawn.Add((playerPos.Value, MineSize.Small));
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Collision: detection", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (ammoEntity, mineEntity) in _ammoToMineHits)
        {
            var ammo = view.GetComponent<Ammo>(ammoEntity);
            var health = view.GetComponent<Health>(mineEntity);
            if (health.Current <= ammo.Damage)
            {
                var minePos = view.GetComponent<Position>(mineEntity);
                var mine = view.GetComponent<EnemyMine>(mineEntity);
                SpawnLootOnDeath(commands, minePos.Value, mine.Size);
                commands.Add(new DestroyEntityCommand(mineEntity));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(mineEntity, new Health(health.Current - ammo.Damage)));
            }
            _entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var (ammoEntity, shipEntity) in _ammoToShipHits)
        {
            var ammo = view.GetComponent<Ammo>(ammoEntity);
            var health = view.GetComponent<Health>(shipEntity);
            if (health.Current <= ammo.Damage)
            {
                var shipPos = view.GetComponent<Position>(shipEntity);
                SpawnShipLootOnDeath(commands, shipPos.Value);
                commands.Add(new DestroyEntityCommand(shipEntity));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(shipEntity, new Health(health.Current - ammo.Damage)));
            }
            _entitiesToDestroy.Add(ammoEntity);
        }

        var hitMineOrShip = new HashSet<Entity>();
        foreach (var (ammoEntity, _) in _ammoToMineHits) hitMineOrShip.Add(ammoEntity);
        foreach (var (ammoEntity, _) in _ammoToShipHits) hitMineOrShip.Add(ammoEntity);

        foreach (var entity in _entitiesToDestroy.Distinct())
        {
            if (!hitMineOrShip.Contains(entity))
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
        }

        foreach (var ammoEntity in _ammoToPlayerHits)
        {
            var ammo = view.GetComponent<Ammo>(ammoEntity);
            int damage = ammo.Damage;

            var playerHealth = view.GetComponent<Health>(playerEntity);
            if (playerHealth.Current <= damage)
            {
                commands.Add(new AddComponentCommand<Dead>(playerEntity, new Dead()));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(playerHealth.Current - damage)));
            }
            _entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var entity in _entitiesToDestroy.Distinct())
        {
            if (_ammoToPlayerHits.Contains(entity) || hitMineOrShip.Contains(entity))
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

        sw.Stop();
        DiagnosticLogger.LogSystem("Collision: resolution", sw.ElapsedTicks);
    }

    private SpatialGrid _grid = new(128f);

    private void SpawnExplosion(CommandBuffer commands, Vector2 position, MineSize mineSize)
    {
        float radius = mineSize == MineSize.Large ? 30f : 15f;
        commands.AddEntity(new Position(position), new Explosion(radius, 0.5f));
    }

    private void SpawnSpark(CommandBuffer commands, Vector2 position)
    {
        float angle = (float)(new Random().NextDouble() * MathF.PI * 2f);
        float speed = 50f + (float)new Random().NextDouble() * 100f;
        Vector2 velocity = new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed);
        commands.AddEntity(new Position(position), new Velocity(velocity), new Spark(0.8f + (float)new Random().NextDouble() * 0.6f));
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
        else
        {
            var playerComp = view.GetComponent<Player>(bEntity);
            bRadius = playerComp.Radius;
            isAsteroidVsAsteroid = false;
        }

        ResolveCollision(view, aPos, bPos, aEntity, bEntity, aRadius, bRadius, isAsteroidVsAsteroid, commands);
    }

    private void ResolveCircleVsCircle(
        WorldView view,
        Entity aEntity,
        EnemyMine aMine,
        Entity bEntity,
        CommandBuffer commands,
        Asteroid bAst)
    {
        var aPos = view.GetComponent<Position>(aEntity);
        var bPos = view.GetComponent<Position>(bEntity);

        float aRadius = aMine.Radius;
        float bRadius = bAst.Radius;
        bool isAsteroidVsAsteroid = true;

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

        view.TryGetComponent<Velocity>(aEntity, out var aVelComp);
        view.TryGetComponent<Velocity>(bEntity, out var bVelComp);
        var aVel = aVelComp.Value;
        var bVel = bVelComp.Value;

        var relVel = bVel - aVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);

        float correctionMagnitude = Math.Max(penetration - Slop, 0f) * CorrectionPercent;
        if (correctionMagnitude > 0f && totalInvMass > 0f)
        {
            var correctionPerInvMass = normal * (correctionMagnitude / totalInvMass);
            commands.Add(new AddComponentCommand<Position>(aEntity, new Position(aPos.Value - correctionPerInvMass * invMassA)));
            commands.Add(new AddComponentCommand<Position>(bEntity, new Position(bPos.Value + correctionPerInvMass * invMassB)));
        }

        if (velAlongNormal > 0f) return;

        float restitution = isAsteroidVsAsteroid ? AsteroidAsteroidRestitution : PlayerRestitution;
        float j = -(1 + restitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        aVel -= impulse * invMassA;
        bVel += impulse * invMassB;

        commands.Add(new AddComponentCommand<Velocity>(aEntity, new Velocity(aVel)));
        commands.Add(new AddComponentCommand<Velocity>(bEntity, new Velocity(bVel)));

        var correctedRelVel = bVel - aVel;
        var velNormalComponent = Vector2.Dot(correctedRelVel, normal);
        var tangentVel = correctedRelVel - normal * velNormalComponent;
        float tangentSpeed = tangentVel.Magnitude;

        if (view.TryGetComponent<AngularVelocity>(aEntity, out var aAngVel))
        {
            commands.Add(new AddComponentCommand<AngularVelocity>(aEntity, new AngularVelocity(aAngVel.Value + tangentSpeed * RotationFactor)));
        }

        if (view.TryGetComponent<AngularVelocity>(bEntity, out var bAngVel))
        {
            commands.Add(new AddComponentCommand<AngularVelocity>(bEntity, new AngularVelocity(bAngVel.Value - tangentSpeed * RotationFactor)));
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
        view.TryGetComponent<Velocity>(asteroidEntity, out var asteroidVelComp);
        var asteroidVel = asteroidVelComp.Value;

        var relVel = ammoVel - asteroidVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);
        if (velAlongNormal > 0f) return;

        float asteroidMass = MathF.PI * asteroid.Radius * asteroid.Radius;
        float totalInvMass = 1f / AmmoMass + 1f / asteroidMass;

        float j = -(1 + AmmoRestitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        asteroidVel += impulse * (1f / asteroidMass);
        commands.Add(new AddComponentCommand<Velocity>(asteroidEntity, new Velocity(asteroidVel)));

        if (view.TryGetComponent<AngularVelocity>(asteroidEntity, out var angVel))
        {
            var tangentSpeed = relVel.Magnitude - Math.Abs(velAlongNormal);
            commands.Add(new AddComponentCommand<AngularVelocity>(asteroidEntity, new AngularVelocity(angVel.Value + tangentSpeed * 0.01f)));
        }
    }

    private void ResolveMineVsPlayer(WorldView view, Entity mineEntity, EnemyMine mine, Entity playerEntity, CommandBuffer commands)
    {
        var minePos = view.GetComponent<Position>(mineEntity);
        var playerPos = view.GetComponent<Position>(playerEntity);

        var diff = playerPos.Value - minePos.Value;
        float distSq = diff.X * diff.X + diff.Y * diff.Y;
        float radiusSum = mine.Radius + 18f;

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
        view.TryGetComponent<Velocity>(playerEntity, out var playerVelComp);
        Vector2 playerVel = playerVelComp.Value + normal * explosionForce;

        commands.Add(new AddComponentCommand<Velocity>(playerEntity, new Velocity(playerVel)));

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
