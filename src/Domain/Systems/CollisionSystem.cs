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

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var asteroids = view.GetEntitiesWithComponents<Asteroid>().ToList();
        var playerTuple = view.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (hasPlayer)
        {
            foreach (var (entity, asteroid) in asteroids)
            {
                ResolveCircleVsCircle(view, entity, asteroid, playerEntity, commands);
            }
        }

        for (int i = 0; i < asteroids.Count; i++)
        {
            for (int j = i + 1; j < asteroids.Count; j++)
            {
                var (aEntity, aAsteroid) = asteroids[i];
                var (bEntity, bAsteroid) = asteroids[j];
                ResolveCircleVsCircle(view, aEntity, aAsteroid, bEntity, commands, bAsteroid);
            }
        }

        var ammoList = view.GetEntitiesWithComponents<Ammo>().ToList();
        var entitiesToDestroy = new List<Entity>();
        var effectsToSpawn = new List<(Vector2 Position, MineSize Size)>();

        foreach (var (ammoEntity, ammo) in ammoList)
        {
            if (!view.HasComponent<Ammo>(ammoEntity)) continue;

            var ammoPos = view.GetComponent<Position>(ammoEntity);
            float ammoRadius = ammo.Radius;

            foreach (var (asteroidEntity, asteroid) in asteroids)
            {
                var asteroidPos = view.GetComponent<Position>(asteroidEntity);
                var diff = asteroidPos.Value - ammoPos.Value;
                float distSq = diff.X * diff.X + diff.Y * diff.Y;
                float radiusSum = ammoRadius + asteroid.Radius;

                if (distSq >= radiusSum * radiusSum || distSq < 0.001f) continue;

                ResolveAmmoVsAsteroid(view, ammoEntity, ammo, asteroidEntity, asteroid, commands);
                effectsToSpawn.Add((ammoPos.Value, MineSize.Small));
                entitiesToDestroy.Add(ammoEntity);
                break;
            }
        }

        foreach (var entity in entitiesToDestroy.Distinct())
        {
            commands.Add(new DestroyEntityCommand(entity));
        }

        var mines = view.GetEntitiesWithComponents<EnemyMine>().ToList();

        if (hasPlayer)
        {
            foreach (var (mineEntity, mine) in mines)
            {
                if (!view.HasComponent<EnemyMine>(mineEntity)) continue;
                ResolveMineVsPlayer(view, mineEntity, mine, playerEntity, commands);
            }

            _mineCollisionPositions.Clear();
        }

        foreach (var (mineEntity, mine) in mines)
        {
            if (!view.HasComponent<EnemyMine>(mineEntity)) continue;
            if (!view.HasComponent<Position>(mineEntity)) continue;
            foreach (var (asteroidEntity, asteroid) in asteroids)
            {
                ResolveCircleVsCircle(view, mineEntity, mine, asteroidEntity, commands, asteroid);
            }
        }

        var ammoToMineHits = new List<(Entity ammoEntity, Entity mineEntity)>();

        foreach (var (ammoEntity, ammo) in ammoList)
        {
            if (!view.HasComponent<Ammo>(ammoEntity)) continue;
            if (ammo.IsEnemy) continue;

            var ammoPos = view.GetComponent<Position>(ammoEntity);
            float ammoRadius = ammo.Radius;

            foreach (var (mineEntity, mine) in mines)
            {
                if (!view.HasComponent<Position>(mineEntity)) continue;
                var minePos = view.GetComponent<Position>(mineEntity);
                var diff = minePos.Value - ammoPos.Value;
                float distSq = diff.X * diff.X + diff.Y * diff.Y;
                float radiusSum = ammoRadius + mine.Radius;

                if (distSq >= radiusSum * radiusSum || distSq < 0.001f) continue;

                ammoToMineHits.Add((ammoEntity, mineEntity));
                effectsToSpawn.Add((minePos.Value, mine.Size));
                break;
            }
        }

        foreach (var (ammoEntity, mineEntity) in ammoToMineHits)
        {
            if (!view.HasComponent<Ammo>(ammoEntity)) continue;
            var ammo = view.GetComponent<Ammo>(ammoEntity);
            if (!view.HasComponent<Health>(mineEntity)) continue;
            var health = view.GetComponent<Health>(mineEntity);
            if (health.Current <= ammo.Damage)
            {
                if (!view.HasComponent<Position>(mineEntity)) continue;
                var minePos = view.GetComponent<Position>(mineEntity);
                if (!view.HasComponent<EnemyMine>(mineEntity)) continue;
                var mine = view.GetComponent<EnemyMine>(mineEntity);
                SpawnLootOnDeath(commands, minePos.Value, mine.Size);
                commands.Add(new DestroyEntityCommand(mineEntity));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(mineEntity, new Health(health.Current - ammo.Damage)));
            }
            entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var entity in entitiesToDestroy.Distinct())
        {
            if (!ammoToMineHits.Any(h => h.ammoEntity == entity))
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
        }

        var enemyShips = view.GetEntitiesWithComponents<EnemyShip>().ToList();

        foreach (var (enemyShipEntity, enemyShip) in enemyShips)
        {
            if (!view.HasComponent<EnemyShip>(enemyShipEntity)) continue;
            if (!view.HasComponent<Position>(enemyShipEntity)) continue;
            if (hasPlayer)
            {
                ResolveEnemyShipVsPlayer(view, enemyShipEntity, enemyShip, playerEntity, commands);
            }
        }

        for (int i = 0; i < enemyShips.Count; i++)
        {
            var (aEntity, aShip) = enemyShips[i];
            if (!view.HasComponent<EnemyShip>(aEntity)) continue;
            if (!view.HasComponent<Position>(aEntity)) continue;
            foreach (var (asteroidEntity, asteroid) in asteroids)
            {
                ResolveEnemyShipVsAsteroid(view, aEntity, aShip, asteroidEntity, asteroid, commands);
            }

            for (int j = i + 1; j < enemyShips.Count; j++)
            {
                var (bEntity, bShip) = enemyShips[j];
                if (!view.HasComponent<EnemyShip>(bEntity)) continue;
                if (!view.HasComponent<Position>(bEntity)) continue;
                ResolveEnemyShipVsEnemyShip(view, aEntity, aShip, bEntity, bShip, commands);
            }

            foreach (var (mineEntity, mine) in mines)
            {
                if (!view.HasComponent<EnemyMine>(mineEntity)) continue;
                if (!view.HasComponent<Position>(mineEntity)) continue;
                ResolveEnemyShipVsMine(view, aEntity, aShip, mineEntity, mine, commands);
            }
        }

        var ammoToEnemyShipHits = new List<(Entity ammoEntity, Entity enemyShipEntity)>();

        foreach (var (ammoEntity, ammo) in ammoList)
        {
            if (!view.HasComponent<Ammo>(ammoEntity)) continue;
            if (ammo.IsEnemy) continue;

            var ammoPos = view.GetComponent<Position>(ammoEntity);
            float ammoRadius = ammo.Radius;

            foreach (var (enemyShipEntity, enemyShip) in enemyShips)
            {
                if (!view.HasComponent<Position>(enemyShipEntity)) continue;
                var shipPos = view.GetComponent<Position>(enemyShipEntity);
                var diff = shipPos.Value - ammoPos.Value;
                float distSq = diff.X * diff.X + diff.Y * diff.Y;
                float radiusSum = ammoRadius + enemyShip.Radius;

                if (distSq >= radiusSum * radiusSum || distSq < 0.001f) continue;

                ammoToEnemyShipHits.Add((ammoEntity, enemyShipEntity));
                effectsToSpawn.Add((shipPos.Value, MineSize.Small));
                break;
            }
        }

        foreach (var (ammoEntity, enemyShipEntity) in ammoToEnemyShipHits)
        {
            if (!view.HasComponent<Ammo>(ammoEntity)) continue;
            var ammo = view.GetComponent<Ammo>(ammoEntity);
            if (!view.HasComponent<Health>(enemyShipEntity)) continue;
            var health = view.GetComponent<Health>(enemyShipEntity);
            if (health.Current <= ammo.Damage)
            {
                if (!view.HasComponent<Position>(enemyShipEntity)) continue;
                var shipPos = view.GetComponent<Position>(enemyShipEntity);
                SpawnShipLootOnDeath(commands, shipPos.Value);
                commands.Add(new DestroyEntityCommand(enemyShipEntity));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(enemyShipEntity, new Health(health.Current - ammo.Damage)));
            }
            entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var entity in entitiesToDestroy.Distinct())
        {
            if (!ammoToEnemyShipHits.Any(h => h.ammoEntity == entity) && !ammoToMineHits.Any(h => h.ammoEntity == entity))
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
        }

        var ammoToPlayerHits = new List<Entity>();

        foreach (var (ammoEntity, ammo) in ammoList)
        {
            if (!view.HasComponent<Ammo>(ammoEntity)) continue;
            if (!ammo.IsEnemy) continue;
            if (ammoToEnemyShipHits.Any(h => h.ammoEntity == ammoEntity)) continue;
            if (ammoToMineHits.Any(h => h.ammoEntity == ammoEntity)) continue;

            var ammoPos = view.GetComponent<Position>(ammoEntity);
            float ammoRadius = ammo.Radius;

            if (!hasPlayer) continue;

            var playerPos = view.GetComponent<Position>(playerEntity);
            var diff = playerPos.Value - ammoPos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = ammoRadius + 18f;

            if (distSq >= radiusSum * radiusSum || distSq < 0.001f) continue;

            ammoToPlayerHits.Add(ammoEntity);
            effectsToSpawn.Add((playerPos.Value, MineSize.Small));
        }

        foreach (var ammoEntity in ammoToPlayerHits.Distinct())
        {
            var ammo = view.GetComponent<Ammo>(ammoEntity);
            int damage = ammo.Damage;

            if (!view.HasComponent<Health>(playerEntity)) continue;
            var playerHealth = view.GetComponent<Health>(playerEntity);
            if (playerHealth.Current <= damage)
            {
                commands.Add(new AddComponentCommand<Dead>(playerEntity, new Dead()));
            }
            else
            {
                commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(playerHealth.Current - damage)));
            }
            entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var entity in entitiesToDestroy.Distinct())
        {
            if (ammoToPlayerHits.Contains(entity) || ammoToEnemyShipHits.Any(h => h.ammoEntity == entity) || ammoToMineHits.Any(h => h.ammoEntity == entity))
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
        }

        foreach (var (position, mineSize) in effectsToSpawn.Distinct())
        {
            SpawnExplosion(commands, position, mineSize);
            int sparkCount = mineSize == MineSize.Large ? 7 : 3;
            for (int i = 0; i < sparkCount; i++)
            {
                SpawnSpark(commands, position);
            }
        }
    }

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
        CommandBuffer commands,
        Asteroid? bAst = null)
    {
        var aPos = view.GetComponent<Position>(aEntity);
        var bPos = view.GetComponent<Position>(bEntity);

        float aRadius;
        float bRadius;
        bool isAsteroidVsAsteroid;

        if (bAst != null)
        {
            aRadius = aAst.Radius;
            bRadius = bAst.Value.Radius;
            isAsteroidVsAsteroid = true;
        }
        else
        {
            aRadius = aAst.Radius;
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

        Vector2 aVel = view.HasComponent<Velocity>(aEntity)
            ? view.GetComponent<Velocity>(aEntity).Value
            : Vector2.Zero;
        Vector2 bVel = view.HasComponent<Velocity>(bEntity)
            ? view.GetComponent<Velocity>(bEntity).Value
            : Vector2.Zero;

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

        if (view.HasComponent<AngularVelocity>(aEntity))
        {
            var aAngVel = view.GetComponent<AngularVelocity>(aEntity);
            commands.Add(new AddComponentCommand<AngularVelocity>(aEntity, new AngularVelocity(aAngVel.Value + tangentSpeed * RotationFactor)));
        }

        if (view.HasComponent<AngularVelocity>(bEntity))
        {
            var bAngVel = view.GetComponent<AngularVelocity>(bEntity);
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
        Vector2 asteroidVel = view.HasComponent<Velocity>(asteroidEntity)
            ? view.GetComponent<Velocity>(asteroidEntity).Value
            : Vector2.Zero;

        var relVel = ammoVel - asteroidVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);
        if (velAlongNormal > 0f) return;

        float asteroidMass = MathF.PI * asteroid.Radius * asteroid.Radius;
        float totalInvMass = 1f / AmmoMass + 1f / asteroidMass;

        float j = -(1 + AmmoRestitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        asteroidVel += impulse * (1f / asteroidMass);
        commands.Add(new AddComponentCommand<Velocity>(asteroidEntity, new Velocity(asteroidVel)));

        if (view.HasComponent<AngularVelocity>(asteroidEntity))
        {
            var tangentSpeed = relVel.Magnitude - Math.Abs(velAlongNormal);
            var angVel = view.GetComponent<AngularVelocity>(asteroidEntity);
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
        Vector2 playerVel = view.HasComponent<Velocity>(playerEntity)
            ? view.GetComponent<Velocity>(playerEntity).Value
            : Vector2.Zero;
        playerVel += normal * explosionForce;

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
        EnemyShip bShip,
        CommandBuffer commands)
    {
        var aPos = view.GetComponent<Position>(aEntity);
        var bPos = view.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
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
