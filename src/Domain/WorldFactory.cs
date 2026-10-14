using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;

namespace Spacevors.Domain;

// Builds the starting world: player, camera, turrets, asteroids, mines and enemy ships.
// Presentation-only data (the starfield and world clutter) stays in the Game layer, which draws it.
// Generation order is contractual: the caller may keep drawing from em.Rng afterwards (GameInitializer
// generates the starfield that way) and must get the same world for the same seed.
public static class WorldFactory
{
    // testAsteroid pins the first asteroid directly ahead of the ship, so a weapon always has the
    // same target. Diagnostics mode does this through the env var; scenarios ask for it directly
    // rather than depending on an env var being set.
    public static (EntityManager em, Entity playerEntity, Entity cameraEntity) Create(
        ShipType shipType, Vector2 viewportSize, int seed = EntityManager.DefaultRngSeed, bool testAsteroid = false)
    {
        var em = new EntityManager(seed);

        // Create player ship
        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(new Vector2(0f, 0f)));
        em.AddComponent(playerEntity, new Velocity(Vector2.Zero));
        em.AddComponent(playerEntity, new Rotation(0f));
        em.AddComponent(playerEntity, new AngularVelocity(0f));

        em.AddComponent(playerEntity, new Player(Thrust: shipType.Engine.ForwardThrust, SideThrust: shipType.Engine.SideThrust, BackThrust: shipType.Engine.BackThrust, Boost: PlayerShipStats.Boost, Radius: shipType.Radius, Xp: 0, Level: 1, PickupRadius: shipType.PickupRadius + shipType.Radius, RotationSpeed: shipType.Engine.TurnRate, MaxHealth: shipType.MaxHealth));
        em.AddComponent(playerEntity, new Health(shipType.MaxHealth, shipType.MaxHealth));

        // A slot is one distinct weapon type (matches LevelUpSystem and AddNewWeaponTurret).
        int usedSlots = shipType.Weapon.Turrets.Select(t => t.Weapon.Name).Distinct().Count();
        em.AddComponent(playerEntity, new WeaponSlots(usedSlots, shipType.MaxWeaponSlots));

        bool pinnedAsteroid = testAsteroid || Environment.GetEnvironmentVariable(DiagnosticLogger.EnvVar) == "1";

        // Create camera
        var cameraEntity = em.CreateEntity();
        em.AddComponent(cameraEntity, new Camera(Vector2.Zero, Vector2.Zero));

        var rand = em.Rng;

        // One close asteroid within initial view range.
        // Pinned (diagnostics, scenarios): fixed position directly ahead of the ship, so weapon
        // behaviour is testable deterministically.
        {
            var asteroid = em.CreateEntity();
            float angle = pinnedAsteroid ? 3f * MathF.PI / 2f : (float)(rand.NextDouble() * Math.PI * 2f);
            float dist = pinnedAsteroid ? 300f : 150f + (float)rand.NextDouble() * 1500f;
            float ax = (float)Math.Cos(angle) * dist;
            float ay = (float)Math.Sin(angle) * dist;
            float aSpeed = pinnedAsteroid ? 10f : 15f + (float)rand.NextDouble() * 35f;
            float aAngle = angle;
            AsteroidFactory.AddAsteroidComponents(em, asteroid, new Vector2(ax, ay), aSpeed, aAngle, rand);
        }

        // Remaining asteroids in a larger area (~5 screens away)
        for (int i = 1; i < 35; i++)
        {
            var asteroid = em.CreateEntity();
            float angle = (float)(rand.NextDouble() * Math.PI * 2f);
            float dist = 1000f + (float)rand.NextDouble() * 4000f;
            float ax = (float)Math.Cos(angle) * dist;
            float ay = (float)Math.Sin(angle) * dist;
            float aSpeed = 10f + (float)rand.NextDouble() * 25f;
            float aAngle = (float)(rand.NextDouble() * Math.PI * 2);
            AsteroidFactory.AddAsteroidComponents(em, asteroid, new Vector2(ax, ay), aSpeed, aAngle, rand);
        }

        // Spawn enemy mines just outside the screen around the player
        for (int i = 0; i < SpawningStats.InitialWorld.MineCount; i++)
        {
            var mine = em.CreateEntity();
            Vector2 dir = SpawnPlacement.AnyDirection(rand);
            Vector2 minePos = SpawnPlacement.OutsideScreen(Vector2.Zero, viewportSize, dir);
            MineSize mSize = rand.NextDouble() < 0.5f ? MineSize.Large : MineSize.Small;
            var mineType = MineType.FromSize(mSize);
            em.AddComponent(mine, new Position(minePos));
            em.AddComponent(mine, new Velocity(Vector2.Zero));
            em.AddComponent(mine, new EnemyMine(mSize, MineStats.SpawnSpeedMin + (float)rand.NextDouble() * (MineStats.SpawnSpeedMax - MineStats.SpawnSpeedMin), (float)(rand.NextDouble() * Math.PI * 2)));
            em.AddComponent(mine, new Health(mineType.Health, mineType.Health));
        }

        // Spawn enemy ships well outside the screen (beyond firing range), drifting in toward the player
        for (int i = 0; i < SpawningStats.InitialWorld.EnemyShipCount; i++)
        {
            var ship = em.CreateEntity();
            Vector2 dir = SpawnPlacement.AnyDirection(rand);
            float dist = SpawningStats.InitialWorld.EnemyShipMinDistance + (float)rand.NextDouble() * (SpawningStats.InitialWorld.EnemyShipMaxDistance - SpawningStats.InitialWorld.EnemyShipMinDistance);
            Vector2 spawnPos = SpawnPlacement.OutsideScreen(Vector2.Zero, viewportSize, dir);
            if (spawnPos.Magnitude < dist) spawnPos = dir * dist;
            Vector2 initialVel = (Vector2.Zero - spawnPos).Normalized * SpawnPlacement.DriftSpeed;

            var enemyShipType = EnemyShipFactory.PickRandomType(rand);
            EnemyShipFactory.AddComponents(em, ship, spawnPos, initialVel, SpawnPlacement.AngleFromTo(spawnPos, Vector2.Zero), 0f, enemyShipType);
        }

        // Create turret entities from the ship's weapons
        foreach (var def in shipType.Weapon.Turrets)
        {
            var turretEntity = em.CreateEntity();
            em.AddComponent(turretEntity, new Position(new Vector2(0f, 0f)));
            em.AddComponent(turretEntity, new Rotation(def.ArcOffset));
            em.AddComponent(turretEntity, new Turret(Weapon: def.Weapon.Stats, WeaponName: def.Weapon.Name, ArcAngle: def.ArcAngle, Range: def.Range));
            em.AddComponent(turretEntity, new TurretOffset(def.Offset));
            em.AddComponent(turretEntity, new ArcOffset(def.ArcOffset));
        }

        return (em, playerEntity, cameraEntity);
    }
}
