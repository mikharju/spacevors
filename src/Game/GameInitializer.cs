using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class GameInitializer
{
    private const int InitialMineCount = 15;
    private const int InitialEnemyShipCount = 6;
    public const float InitialShipMinDistance = 1600f;
    public const float InitialShipMaxDistance = 3200f;

    public static (EntityManager em, Entity playerEntity, Entity cameraEntity, List<(Vector2 Position, float Size, Color Color, float Parallax)> stars, List<(Vector2 Position, float Width, float Height, Color Color)> clutter) Initialize(ShipType shipType, Vector2 viewportSize)
    {
        var em = new EntityManager();

        // Create player ship
        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(new Vector2(0f, 0f)));
        em.AddComponent(playerEntity, new Velocity(Vector2.Zero));
        em.AddComponent(playerEntity, new Rotation(0f));
        em.AddComponent(playerEntity, new AngularVelocity(0f));

        em.AddComponent(playerEntity, new Player(Thrust: shipType.Engine.ForwardThrust, SideThrust: shipType.Engine.SideThrust, BackThrust: shipType.Engine.BackThrust, Boost: 2.5f, Radius: shipType.Radius, Xp: 0, Level: 1, PickupRadius: shipType.PickupRadius + shipType.Radius, RotationSpeed: shipType.Engine.TurnRate, MaxHealth: shipType.MaxHealth));
        em.AddComponent(playerEntity, new Health(shipType.MaxHealth));

        // A slot is one distinct weapon type (matches LevelUpSystem and AddNewWeaponTurret).
        int usedSlots = shipType.Weapon.Turrets.Select(t => t.Weapon.Name).Distinct().Count();
        em.AddComponent(playerEntity, new WeaponSlots(usedSlots, shipType.MaxWeaponSlots));

        bool diagnostics = Environment.GetEnvironmentVariable("SPACEVORS_DIAGNOSTIC") == "1";

        // Create camera
        var cameraEntity = em.CreateEntity();
        em.AddComponent(cameraEntity, new Camera(new Vector2(0f, 0f)));

        var rand = em.Rng;

        // One close asteroid within initial view range.
        // Diagnostics: fixed position directly ahead of the ship so weapon firing is testable deterministically.
        {
            var asteroid = em.CreateEntity();
            float angle = diagnostics ? 3f * MathF.PI / 2f : (float)(rand.NextDouble() * Math.PI * 2f);
            float dist = diagnostics ? 300f : 150f + (float)rand.NextDouble() * 1500f;
            float ax = (float)Math.Cos(angle) * dist;
            float ay = (float)Math.Sin(angle) * dist;
            float aSpeed = diagnostics ? 10f : 15f + (float)rand.NextDouble() * 35f;
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
        for (int i = 0; i < InitialMineCount; i++)
        {
            var mine = em.CreateEntity();
            Vector2 dir = SpawnPlacement.AnyDirection(rand);
            Vector2 minePos = SpawnPlacement.OutsideScreen(Vector2.Zero, viewportSize, dir);
            MineSize mSize = rand.NextDouble() < 0.5f ? MineSize.Large : MineSize.Small;
            em.AddComponent(mine, new Position(minePos));
            em.AddComponent(mine, new Velocity(Vector2.Zero));
            em.AddComponent(mine, new EnemyMine(mSize, 30f + (float)rand.NextDouble() * 20f, (float)(rand.NextDouble() * Math.PI * 2)));
            em.AddComponent(mine, new Health(2));
        }

        // Spawn enemy ships well outside the screen (beyond firing range), drifting in toward the player
        for (int i = 0; i < InitialEnemyShipCount; i++)
        {
            var ship = em.CreateEntity();
            Vector2 dir = SpawnPlacement.AnyDirection(rand);
            float dist = InitialShipMinDistance + (float)rand.NextDouble() * (InitialShipMaxDistance - InitialShipMinDistance);
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

        // Background starfield with parallax layers
        var stars = new List<(Vector2 Position, float Size, Color Color, float Parallax)>();

        for (int layer = 0; layer < 3; layer++)
        {
            int count = layer == 0 ? 150 : layer == 1 ? 100 : 50;
            float parallax = layer switch { 0 => 0.2f, 1 => 0.5f, _ => 1.0f };
            float sizeMin = layer switch { 0 => 0.5f, 1 => 1f, _ => 1.5f };
            float sizeMax = layer switch { 0 => 1f, 1 => 1.5f, _ => 2f };

            for (int i = 0; i < count; i++)
            {
                float x = (float)rand.NextDouble() * 6000f - 3000f;
                float y = (float)rand.NextDouble() * 6000f - 3000f;
                float size = sizeMin + (float)rand.NextDouble() * (sizeMax - sizeMin);

                Color color;
                float roll = (float)rand.NextDouble();
                if (roll < 0.5f) color = new Color(140, 130, 100, 160);
                else if (roll < 0.75f) color = new Color(160, 90, 70, 160);
                else color = new Color(80, 100, 140, 160);

                stars.Add((new Vector2(x, y), size, color, parallax));
            }
        }

        // Stationary clutter fixed to world coordinates
        var clutter = new List<(Vector2 Position, float Width, float Height, Color Color)>();

        for (int i = 0; i < 40; i++)
        {
            float x = (float)rand.NextDouble() * 6000f - 3000f;
            float y = (float)rand.NextDouble() * 6000f - 3000f;
            float w = 5f + (float)rand.NextDouble() * 20f;
            float h = 3f + (float)rand.NextDouble() * 12f;

            Color color;
            float roll = (float)rand.NextDouble();
            if (roll < 0.6f) color = new Color(35, 35, 38, 140);
            else color = new Color(55, 45, 32, 140);

            clutter.Add((new Vector2(x, y), w, h, color));
        }

        return (em, playerEntity, cameraEntity, stars, clutter);
    }
}
