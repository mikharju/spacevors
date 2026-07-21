using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

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

    public override void Update(EntityManager em, float deltaTime)
    {
        var asteroids = em.GetEntitiesWithComponents<Asteroid>().ToList();
        var playerTuple = em.GetEntitiesWithComponents<Player>().FirstOrDefault();
        Entity playerEntity = playerTuple.Entity;
        bool hasPlayer = playerEntity.Value >= 0;

        if (hasPlayer)
        {
            foreach (var (entity, asteroid) in asteroids)
            {
                ResolveCircleVsCircle(em, entity, asteroid, playerEntity);
            }
        }

        for (int i = 0; i < asteroids.Count; i++)
        {
            for (int j = i + 1; j < asteroids.Count; j++)
            {
                var (aEntity, aAsteroid) = asteroids[i];
                var (bEntity, bAsteroid) = asteroids[j];
                ResolveCircleVsCircle(em, aEntity, aAsteroid, bEntity, bAsteroid);
            }
        }

        var ammoList = em.GetEntitiesWithComponents<Ammo>().ToList();
        var entitiesToDestroy = new List<Entity>();
        var effectsToSpawn = new List<(Vector2 Position, MineSize Size)>();

        foreach (var (ammoEntity, ammo) in ammoList)
        {
            if (!em.HasComponent<Ammo>(ammoEntity)) continue;

            var ammoPos = em.GetComponent<Position>(ammoEntity);
            float ammoRadius = ammo.Radius;

            foreach (var (asteroidEntity, asteroid) in asteroids)
            {
                var asteroidPos = em.GetComponent<Position>(asteroidEntity);
                var diff = asteroidPos.Value - ammoPos.Value;
                float distSq = diff.X * diff.X + diff.Y * diff.Y;
                float radiusSum = ammoRadius + asteroid.Radius;

                if (distSq >= radiusSum * radiusSum || distSq < 0.001f) continue;

                ResolveAmmoVsAsteroid(em, ammoEntity, ammo, asteroidEntity, asteroid);
                effectsToSpawn.Add((ammoPos.Value, MineSize.Small));
                entitiesToDestroy.Add(ammoEntity);
                break;
            }
        }

        foreach (var entity in entitiesToDestroy.Distinct())
        {
            em.DestroyEntity(entity);
        }

        var mines = em.GetEntitiesWithComponents<EnemyMine>().ToList();

        if (hasPlayer)
        {
            foreach (var (mineEntity, mine) in mines)
            {
                if (!em.HasComponent<EnemyMine>(mineEntity)) continue;
                ResolveMineVsPlayer(em, mineEntity, mine, playerEntity);
            }

            _mineCollisionPositions.Clear();
        }

        foreach (var (mineEntity, mine) in mines)
        {
            if (!em.HasComponent<EnemyMine>(mineEntity)) continue;
            if (!em.HasComponent<Position>(mineEntity)) continue;
            foreach (var (asteroidEntity, asteroid) in asteroids)
            {
                ResolveCircleVsCircle(em, mineEntity, mine, asteroidEntity, asteroid);
            }
        }

        var ammoToMineHits = new List<(Entity ammoEntity, Entity mineEntity)>();

        foreach (var (ammoEntity, ammo) in ammoList)
        {
            if (!em.HasComponent<Ammo>(ammoEntity)) continue;

            var ammoPos = em.GetComponent<Position>(ammoEntity);
            float ammoRadius = ammo.Radius;

            foreach (var (mineEntity, mine) in mines)
            {
                if (!em.HasComponent<Position>(mineEntity)) continue;
                var minePos = em.GetComponent<Position>(mineEntity);
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
            if (!em.HasComponent<Health>(mineEntity)) continue;
            var health = em.GetComponent<Health>(mineEntity);
            if (health.Current <= 1)
            {
                em.DestroyEntity(mineEntity);
            }
            else
            {
                em.AddComponent(mineEntity, new Health(health.Current - 1));
            }
            entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var entity in entitiesToDestroy.Distinct())
        {
            if (!ammoToMineHits.Any(h => h.ammoEntity == entity))
            {
                em.DestroyEntity(entity);
            }
        }

        var enemyShips = em.GetEntitiesWithComponents<EnemyShip>().ToList();

        foreach (var (enemyShipEntity, enemyShip) in enemyShips)
        {
            if (!em.HasComponent<EnemyShip>(enemyShipEntity)) continue;
            if (!em.HasComponent<Position>(enemyShipEntity)) continue;
            if (hasPlayer)
            {
                ResolveEnemyShipVsPlayer(em, enemyShipEntity, enemyShip, playerEntity);
            }
        }

        for (int i = 0; i < enemyShips.Count; i++)
        {
            var (aEntity, aShip) = enemyShips[i];
            if (!em.HasComponent<EnemyShip>(aEntity)) continue;
            if (!em.HasComponent<Position>(aEntity)) continue;
            foreach (var (asteroidEntity, asteroid) in asteroids)
            {
                ResolveEnemyShipVsAsteroid(em, aEntity, aShip, asteroidEntity, asteroid);
            }

            for (int j = i + 1; j < enemyShips.Count; j++)
            {
                var (bEntity, bShip) = enemyShips[j];
                if (!em.HasComponent<EnemyShip>(bEntity)) continue;
                if (!em.HasComponent<Position>(bEntity)) continue;
                ResolveEnemyShipVsEnemyShip(em, aEntity, aShip, bEntity, bShip);
            }

            foreach (var (mineEntity, mine) in mines)
            {
                if (!em.HasComponent<EnemyMine>(mineEntity)) continue;
                if (!em.HasComponent<Position>(mineEntity)) continue;
                ResolveEnemyShipVsMine(em, aEntity, aShip, mineEntity, mine);
            }
        }

        var ammoToEnemyShipHits = new List<(Entity ammoEntity, Entity enemyShipEntity)>();

        foreach (var (ammoEntity, ammo) in ammoList)
        {
            if (!em.HasComponent<Ammo>(ammoEntity)) continue;

            var ammoPos = em.GetComponent<Position>(ammoEntity);
            float ammoRadius = ammo.Radius;

            foreach (var (enemyShipEntity, enemyShip) in enemyShips)
            {
                if (!em.HasComponent<Position>(enemyShipEntity)) continue;
                var shipPos = em.GetComponent<Position>(enemyShipEntity);
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
            if (!em.HasComponent<Health>(enemyShipEntity)) continue;
            var health = em.GetComponent<Health>(enemyShipEntity);
            if (health.Current <= 1)
            {
                em.DestroyEntity(enemyShipEntity);
            }
            else
            {
                em.AddComponent(enemyShipEntity, new Health(health.Current - 1));
            }
            entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var entity in entitiesToDestroy.Distinct())
        {
            if (!ammoToEnemyShipHits.Any(h => h.ammoEntity == entity) && !ammoToMineHits.Any(h => h.ammoEntity == entity))
            {
                em.DestroyEntity(entity);
            }
        }

        var ammoToPlayerHits = new List<Entity>();

        foreach (var (ammoEntity, ammo) in ammoList)
        {
            if (!em.HasComponent<Ammo>(ammoEntity)) continue;
            if (!ammo.IsEnemy) continue;
            if (ammoToEnemyShipHits.Any(h => h.ammoEntity == ammoEntity)) continue;
            if (ammoToMineHits.Any(h => h.ammoEntity == ammoEntity)) continue;

            var ammoPos = em.GetComponent<Position>(ammoEntity);
            float ammoRadius = ammo.Radius;

            if (!hasPlayer) continue;

            var playerPos = em.GetComponent<Position>(playerEntity);
            var diff = playerPos.Value - ammoPos.Value;
            float distSq = diff.X * diff.X + diff.Y * diff.Y;
            float radiusSum = ammoRadius + 18f;

            if (distSq >= radiusSum * radiusSum || distSq < 0.001f) continue;

            ammoToPlayerHits.Add(ammoEntity);
            effectsToSpawn.Add((playerPos.Value, MineSize.Small));
        }

        foreach (var ammoEntity in ammoToPlayerHits.Distinct())
        {
            if (!em.HasComponent<Health>(playerEntity)) continue;
            var playerHealth = em.GetComponent<Health>(playerEntity);
            if (playerHealth.Current <= 1)
            {
                em.AddComponent(playerEntity, new Dead());
            }
            else
            {
                em.AddComponent(playerEntity, new Health(playerHealth.Current - 1));
            }
            entitiesToDestroy.Add(ammoEntity);
        }

        foreach (var entity in entitiesToDestroy.Distinct())
        {
            if (ammoToPlayerHits.Contains(entity) || ammoToEnemyShipHits.Any(h => h.ammoEntity == entity) || ammoToMineHits.Any(h => h.ammoEntity == entity))
            {
                em.DestroyEntity(entity);
            }
        }

        foreach (var (position, mineSize) in effectsToSpawn.Distinct())
        {
            SpawnExplosion(em, position, mineSize);
            int sparkCount = mineSize == MineSize.Large ? 7 : 3;
            for (int i = 0; i < sparkCount; i++)
            {
                SpawnSpark(em, position);
            }
        }
    }

    private void SpawnExplosion(EntityManager em, Vector2 position, MineSize mineSize)
    {
        var explosionEntity = em.CreateEntity();
        em.AddComponent(explosionEntity, new Position(position));
        float radius = mineSize == MineSize.Large ? 30f : 15f;
        em.AddComponent(explosionEntity, new Explosion(radius, 0.5f));
    }

    private void SpawnSpark(EntityManager em, Vector2 position)
    {
        var sparkEntity = em.CreateEntity();
        float angle = (float)(new Random().NextDouble() * MathF.PI * 2f);
        float speed = 50f + (float)new Random().NextDouble() * 100f;
        Vector2 velocity = new Vector2((float)Math.Cos(angle) * speed, (float)Math.Sin(angle) * speed);
        em.AddComponent(sparkEntity, new Position(position));
        em.AddComponent(sparkEntity, new Velocity(velocity));
        em.AddComponent(sparkEntity, new Spark(0.8f + (float)new Random().NextDouble() * 0.6f));
    }

    private void ResolveCircleVsCircle(
        EntityManager em,
        Entity aEntity,
        Asteroid aAst,
        Entity bEntity,
        Asteroid? bAst = null)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(bEntity);

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
            var playerComp = em.GetComponent<Player>(bEntity);
            bRadius = playerComp.Radius;
            isAsteroidVsAsteroid = false;
        }

        ResolveCollision(em, aPos, bPos, aEntity, bEntity, aRadius, bRadius, isAsteroidVsAsteroid);
    }

    private void ResolveCircleVsCircle(
        EntityManager em,
        Entity aEntity,
        EnemyMine aMine,
        Entity bEntity,
        Asteroid bAst)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(bEntity);

        float aRadius = aMine.Radius;
        float bRadius = bAst.Radius;
        bool isAsteroidVsAsteroid = true;

        ResolveCollision(em, aPos, bPos, aEntity, bEntity, aRadius, bRadius, isAsteroidVsAsteroid);
    }

    private void ResolveCollision(
        EntityManager em,
        Position aPos,
        Position bPos,
        Entity aEntity,
        Entity bEntity,
        float aRadius,
        float bRadius,
        bool isAsteroidVsAsteroid)
    {
        var diff = bPos.Value - aPos.Value;
        float distSq = diff.X * diff.X + diff.Y * diff.Y;
        float radiusSum = aRadius + bRadius;

        if (distSq >= radiusSum * radiusSum || distSq < 0.001f) return;

        float dist = (float)Math.Sqrt(distSq);
        var normal = diff / dist;

        float penetration = radiusSum - dist;

        float aMass = MathF.PI * aRadius * aRadius;
        float bMass = isAsteroidVsAsteroid ? MathF.PI * bRadius * bRadius : MathF.PI * bRadius * bRadius;
        float invMassA = 1f / aMass;
        float invMassB = 1f / bMass;
        float totalInvMass = invMassA + invMassB;

        Vector2 aVel = em.HasComponent<Velocity>(aEntity)
            ? em.GetComponent<Velocity>(aEntity).Value
            : Vector2.Zero;
        Vector2 bVel = em.HasComponent<Velocity>(bEntity)
            ? em.GetComponent<Velocity>(bEntity).Value
            : Vector2.Zero;

        var relVel = bVel - aVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);

        float correctionMagnitude = Math.Max(penetration - Slop, 0f) * CorrectionPercent;
        if (correctionMagnitude > 0f && totalInvMass > 0f)
        {
            var correctionPerInvMass = normal * (correctionMagnitude / totalInvMass);
            em.AddComponent(aEntity, new Position(aPos.Value - correctionPerInvMass * invMassA));
            em.AddComponent(bEntity, new Position(bPos.Value + correctionPerInvMass * invMassB));
        }

        if (velAlongNormal > 0f) return;

        float restitution = isAsteroidVsAsteroid ? AsteroidAsteroidRestitution : PlayerRestitution;
        float j = -(1 + restitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        aVel -= impulse * invMassA;
        bVel += impulse * invMassB;

        em.AddComponent(aEntity, new Velocity(aVel));
        em.AddComponent(bEntity, new Velocity(bVel));

        var correctedRelVel = bVel - aVel;
        var velNormalComponent = Vector2.Dot(correctedRelVel, normal);
        var tangentVel = correctedRelVel - normal * velNormalComponent;
        float tangentSpeed = tangentVel.Magnitude;

        if (em.HasComponent<AngularVelocity>(aEntity))
        {
            var aAngVel = em.GetComponent<AngularVelocity>(aEntity);
            em.AddComponent(aEntity, new AngularVelocity(aAngVel.Value + tangentSpeed * RotationFactor));
        }

        if (em.HasComponent<AngularVelocity>(bEntity))
        {
            var bAngVel = em.GetComponent<AngularVelocity>(bEntity);
            em.AddComponent(bEntity, new AngularVelocity(bAngVel.Value - tangentSpeed * RotationFactor));
        }
    }

    private void ResolveAmmoVsAsteroid(
        EntityManager em,
        Entity ammoEntity,
        Ammo ammo,
        Entity asteroidEntity,
        Asteroid asteroid)
    {
        var ammoPos = em.GetComponent<Position>(ammoEntity);
        var asteroidPos = em.GetComponent<Position>(asteroidEntity);

        var diff = asteroidPos.Value - ammoPos.Value;
        float dist = (float)Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
        var normal = diff / dist;

        Vector2 ammoVel = ammo.Velocity;
        Vector2 asteroidVel = em.HasComponent<Velocity>(asteroidEntity)
            ? em.GetComponent<Velocity>(asteroidEntity).Value
            : Vector2.Zero;

        var relVel = ammoVel - asteroidVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);
        if (velAlongNormal > 0f) return;

        float asteroidMass = MathF.PI * asteroid.Radius * asteroid.Radius;
        float totalInvMass = 1f / AmmoMass + 1f / asteroidMass;

        float j = -(1 + AmmoRestitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        asteroidVel += impulse * (1f / asteroidMass);
        em.AddComponent(asteroidEntity, new Velocity(asteroidVel));

        if (em.HasComponent<AngularVelocity>(asteroidEntity))
        {
            var tangentSpeed = relVel.Magnitude - Math.Abs(velAlongNormal);
            var angVel = em.GetComponent<AngularVelocity>(asteroidEntity);
            em.AddComponent(asteroidEntity, new AngularVelocity(angVel.Value + tangentSpeed * 0.01f));
        }
    }

    private void ResolveMineVsPlayer(EntityManager em, Entity mineEntity, EnemyMine mine, Entity playerEntity)
    {
        var minePos = em.GetComponent<Position>(mineEntity);
        var playerPos = em.GetComponent<Position>(playerEntity);

        var diff = playerPos.Value - minePos.Value;
        float distSq = diff.X * diff.X + diff.Y * diff.Y;
        float radiusSum = mine.Radius + 18f;

        if (distSq >= radiusSum * radiusSum || distSq < 0.001f) return;

        _mineCollisionPositions.Add(minePos.Value);

        em.DestroyEntity(mineEntity);

        var playerHealth = em.GetComponent<Health>(playerEntity);
        if (playerHealth.Current <= 3)
        {
            em.AddComponent(playerEntity, new Dead());
        }
        else
        {
            em.AddComponent(playerEntity, new Health(playerHealth.Current - 3));
        }

        var normal = diff / (float)Math.Sqrt(distSq);
        float explosionForce = mine.Size == MineSize.Large ? 240f : 120f;
        Vector2 playerVel = em.HasComponent<Velocity>(playerEntity)
            ? em.GetComponent<Velocity>(playerEntity).Value
            : Vector2.Zero;
        playerVel += normal * explosionForce;

        em.AddComponent(playerEntity, new Velocity(playerVel));

        int sparkCount = mine.Size == MineSize.Large ? 10 : 5;
        for (int i = 0; i < sparkCount; i++)
        {
            SpawnSpark(em, minePos.Value);
        }
    }

    private void ResolveEnemyShipVsPlayer(
        EntityManager em,
        Entity aEntity,
        EnemyShip aShip,
        Entity bEntity)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
        float bRadius = em.GetComponent<Player>(bEntity).Radius;

        ResolveCollisionWithMass(em, aPos, bPos, aEntity, bEntity, aRadius, bRadius, 3000f, false);
    }

    private void ResolveEnemyShipVsAsteroid(
        EntityManager em,
        Entity aEntity,
        EnemyShip aShip,
        Entity bEntity,
        Asteroid bAst)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
        float bRadius = bAst.Radius;

        ResolveCollisionWithMass(em, aPos, bPos, aEntity, bEntity, aRadius, bRadius, 3000f, true);
    }

    private void ResolveEnemyShipVsEnemyShip(
        EntityManager em,
        Entity aEntity,
        EnemyShip aShip,
        Entity bEntity,
        EnemyShip bShip)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
        float bRadius = bShip.Radius;

        ResolveCollisionWithMass(em, aPos, bPos, aEntity, bEntity, aRadius, bRadius, 3000f, true);
    }

    private void ResolveEnemyShipVsMine(
        EntityManager em,
        Entity aEntity,
        EnemyShip aShip,
        Entity bEntity,
        EnemyMine bMine)
    {
        var aPos = em.GetComponent<Position>(aEntity);
        var bPos = em.GetComponent<Position>(bEntity);

        float aRadius = aShip.Radius;
        float bRadius = bMine.Radius;

        ResolveCollisionWithMass(em, aPos, bPos, aEntity, bEntity, aRadius, bRadius, 3000f, true);
    }

    private void ResolveCollisionWithMass(
        EntityManager em,
        Position aPos,
        Position bPos,
        Entity aEntity,
        Entity bEntity,
        float aRadius,
        float bRadius,
        float? aOverrideMass = null,
        bool isAsteroidVsAsteroid = false)
    {
        var diff = bPos.Value - aPos.Value;
        float distSq = diff.X * diff.X + diff.Y * diff.Y;
        float radiusSum = aRadius + bRadius;

        if (distSq >= radiusSum * radiusSum || distSq < 0.001f) return;

        float dist = (float)Math.Sqrt(distSq);
        var normal = diff / dist;

        float penetration = radiusSum - dist;

        float aMass = aOverrideMass ?? (MathF.PI * aRadius * aRadius);
        float bMass = isAsteroidVsAsteroid ? MathF.PI * bRadius * bRadius : MathF.PI * bRadius * bRadius;
        float invMassA = 1f / aMass;
        float invMassB = 1f / bMass;
        float totalInvMass = invMassA + invMassB;

        Vector2 aVel = em.HasComponent<Velocity>(aEntity)
            ? em.GetComponent<Velocity>(aEntity).Value
            : Vector2.Zero;
        Vector2 bVel = em.HasComponent<Velocity>(bEntity)
            ? em.GetComponent<Velocity>(bEntity).Value
            : Vector2.Zero;

        var relVel = bVel - aVel;
        float velAlongNormal = Vector2.Dot(relVel, normal);

        float correctionMagnitude = Math.Max(penetration - Slop, 0f) * CorrectionPercent;
        if (correctionMagnitude > 0f && totalInvMass > 0f)
        {
            var correctionPerInvMass = normal * (correctionMagnitude / totalInvMass);
            em.AddComponent(aEntity, new Position(aPos.Value - correctionPerInvMass * invMassA));
            em.AddComponent(bEntity, new Position(bPos.Value + correctionPerInvMass * invMassB));
        }

        if (velAlongNormal > 0f) return;

        float restitution = isAsteroidVsAsteroid ? AsteroidAsteroidRestitution : PlayerRestitution;
        float j = -(1 + restitution) * velAlongNormal / totalInvMass;
        var impulse = normal * j;

        aVel -= impulse * invMassA;
        bVel += impulse * invMassB;

        em.AddComponent(aEntity, new Velocity(aVel));
        em.AddComponent(bEntity, new Velocity(bVel));

        var correctedRelVel = bVel - aVel;
        var velNormalComponent = Vector2.Dot(correctedRelVel, normal);
        var tangentVel = correctedRelVel - normal * velNormalComponent;
        float tangentSpeed = tangentVel.Magnitude;

        if (em.HasComponent<AngularVelocity>(aEntity))
        {
            var aAngVel = em.GetComponent<AngularVelocity>(aEntity);
            em.AddComponent(aEntity, new AngularVelocity(aAngVel.Value + tangentSpeed * RotationFactor));
        }

        if (em.HasComponent<AngularVelocity>(bEntity))
        {
            var bAngVel = em.GetComponent<AngularVelocity>(bEntity);
            em.AddComponent(bEntity, new AngularVelocity(bAngVel.Value - tangentSpeed * RotationFactor));
        }
    }
}

public readonly record struct Dead();
