using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

namespace Spacevors.Game;

public static class GameInitializer
{
    public const int PlayerMaxHealth = 10;

    public static (EntityManager em, Entity playerEntity, Entity cameraEntity, List<Entity> turretEntities, List<(Vector2 Position, float Size, Color Color, float Parallax)> stars, List<(Vector2 Position, float Width, float Height, Color Color)> clutter) Initialize(Loadout loadout)
    {
        var em = new EntityManager();

        // Create player ship
        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(new Vector2(0f, 0f)));
        em.AddComponent(playerEntity, new Velocity(Vector2.Zero));
        em.AddComponent(playerEntity, new Rotation(0f));
        em.AddComponent(playerEntity, new AngularVelocity(0f));
        em.AddComponent(playerEntity, new Player(Thrust: 400f, Boost: 2.5f));
        em.AddComponent(playerEntity, new Weapon(FireRate: 8f, AmmoSpeed: 350f, KickbackForce: 15f));
        em.AddComponent(playerEntity, new Health(PlayerMaxHealth));

        // Create camera
        var cameraEntity = em.CreateEntity();
        em.AddComponent(cameraEntity, new Camera(new Vector2(0f, 0f)));

        Random rand = new Random(42);

        // 5 close asteroids within initial view range
        for (int i = 0; i < 5; i++)
        {
            var asteroid = em.CreateEntity();
            float angle = (float)(rand.NextDouble() * Math.PI * 2f);
            float dist = 150f + (float)rand.NextDouble() * 400f;
            float ax = (float)Math.Cos(angle) * dist;
            float ay = (float)Math.Sin(angle) * dist;
            float aSpeed = 15f + (float)rand.NextDouble() * 35f;
            float aAngle = (float)(rand.NextDouble() * Math.PI * 2);
            AsteroidFactory.AddAsteroidComponents(em, asteroid, new Vector2(ax, ay), aSpeed, aAngle, rand);
        }

        // Remaining asteroids in a larger area (~5 screens away)
        for (int i = 5; i < 105; i++)
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

        // Spawn enemy mines around the player
        for (int i = 0; i < 15; i++)
        {
            var mine = em.CreateEntity();
            float angle = (float)(rand.NextDouble() * Math.PI * 2f);
            float dist = 300f + (float)rand.NextDouble() * 3000f;
            float mx = (float)Math.Cos(angle) * dist;
            float my = (float)Math.Sin(angle) * dist;
            MineSize mSize = rand.NextDouble() < 0.5f ? MineSize.Large : MineSize.Small;
            em.AddComponent(mine, new Position(new Vector2(mx, my)));
            em.AddComponent(mine, new Velocity(Vector2.Zero));
            em.AddComponent(mine, new EnemyMine(mSize, 30f + (float)rand.NextDouble() * 20f, angle));
            em.AddComponent(mine, new Health(2));
        }

        // Spawn enemy ships around the player
        for (int i = 0; i < 4; i++)
        {
            var ship = em.CreateEntity();
            float angle = (float)(rand.NextDouble() * Math.PI * 2f);
            float dist = 1500f + (float)rand.NextDouble() * 2000f;
            float sx = (float)Math.Cos(angle) * dist;
            float sy = (float)Math.Sin(angle) * dist;
            float sSpeed = 20f + (float)rand.NextDouble() * 15f;
            float sAngle = (float)(rand.NextDouble() * Math.PI * 2);
            EnemyShipFactory.AddEnemyShipComponents(em, ship, new Vector2(sx, sy), new Vector2((float)Math.Cos(sAngle) * sSpeed, (float)Math.Sin(sAngle) * sSpeed), sAngle, (float)(rand.NextDouble() - 0.5f) * 1f);
        }

        // Spawn two enemy ships at screen edges just inside view range
        for (int side = -1; side <= 1; side += 2)
        {
            var edgeShip = em.CreateEntity();
            float ex = side * 900f;
            float ey = 150f * side;
            float eAngle = (float)(Math.PI / 4f * side);
            EnemyShipFactory.AddEnemyShipComponents(em, edgeShip, new Vector2(ex, ey), Vector2.Zero, eAngle + MathF.PI, 0f);
        }

        // Turret entities based on loadout choice
        var turretEntities = new List<Entity>();

        if (loadout == Loadout.Forward)
        {
            var turretEntity = em.CreateEntity();
            em.AddComponent(turretEntity, new Position(new Vector2(0f, 0f)));
            em.AddComponent(turretEntity, new Rotation(0f));
            em.AddComponent(turretEntity, new Turret(FireRate: 8f, AmmoSpeed: 420f, KickbackForce: 10f, ArcAngle: MathF.PI / 4f, Range: 360f, IsEnemy: false));
            em.AddComponent(turretEntity, new TurretOffset(Vector2.Zero));
            em.AddComponent(turretEntity, new ArcOffset(0f));

            turretEntities.Add(turretEntity);
        }
        else
        {
            var leftTurret = em.CreateEntity();
            em.AddComponent(leftTurret, new Position(new Vector2(0f, 0f)));
            em.AddComponent(leftTurret, new Rotation(-MathF.PI / 2f));
            em.AddComponent(leftTurret, new Turret(FireRate: 6f, AmmoSpeed: 350f, KickbackForce: 10f, ArcAngle: MathF.PI / 4f, Range: 360f, IsEnemy: false));
            em.AddComponent(leftTurret, new TurretOffset(new Vector2(-35f, 0f)));
            em.AddComponent(leftTurret, new ArcOffset(-MathF.PI / 2f));

            var rightTurret = em.CreateEntity();
            em.AddComponent(rightTurret, new Position(new Vector2(0f, 0f)));
            em.AddComponent(rightTurret, new Rotation(MathF.PI / 2f));
            em.AddComponent(rightTurret, new Turret(FireRate: 6f, AmmoSpeed: 350f, KickbackForce: 10f, ArcAngle: MathF.PI / 4f, Range: 360f, IsEnemy: false));
            em.AddComponent(rightTurret, new TurretOffset(new Vector2(35f, 0f)));
            em.AddComponent(rightTurret, new ArcOffset(MathF.PI / 2f));

            turretEntities.Add(leftTurret);
            turretEntities.Add(rightTurret);
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

        // Spawn two weapon upgrades near player start
        UpgradeType[] allTypes = { UpgradeType.FireRate, UpgradeType.ProjectileSpeed };
        int idx1 = rand.Next(allTypes.Length);
        int idx2 = (idx1 + 1) % allTypes.Length;

        var upgradeBelow = em.CreateEntity();
        em.AddComponent(upgradeBelow, new Position(new Vector2(0f, -300f)));
        em.AddComponent(upgradeBelow, new Upgrade(allTypes[idx1]));

        var upgradeAbove = em.CreateEntity();
        em.AddComponent(upgradeAbove, new Position(new Vector2(0f, 300f)));
        em.AddComponent(upgradeAbove, new Upgrade(allTypes[idx2]));

        return (em, playerEntity, cameraEntity, turretEntities, stars, clutter);
    }
}
