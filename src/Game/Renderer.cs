using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public static class Renderer
{
    public static void Render(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        bool gameOver,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        int playerMaxHealth)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(15, 15, 25, 255));

        DrawStarfield(stars, camX, camY, windowWidth, windowHeight);
        DrawClutter(clutter, camX, camY, windowWidth, windowHeight);
        DrawAsteroids(em, camX, camY, windowWidth, windowHeight);
        DrawAmmo(em, camX, camY, windowWidth, windowHeight);
        DrawExplosions(em, camX, camY, windowWidth, windowHeight);
        DrawSparks(em, camX, camY, windowWidth, windowHeight);
        DrawGreenSparks(em, camX, camY, windowWidth, windowHeight);
        DrawPlayerShip(em, playerEntity, camX, camY, windowWidth, windowHeight);
        DrawEnemyShips(em, camX, camY, windowWidth, windowHeight);
        DrawMines(em, camX, camY, windowWidth, windowHeight);
        DrawXpPickups(em, camX, camY, windowWidth, windowHeight);
        DrawHealthOrbs(em, camX, camY, windowWidth, windowHeight);
        DrawTurrets(em, camX, camY, windowWidth, windowHeight);
        DrawHealthBar(em, playerEntity, playerMaxHealth, windowWidth, windowHeight);

        if (gameOver)
        {
            Raylib.DrawText("GAME OVER", windowWidth / 2 - 80, windowHeight / 2 - 20, 40, new Color(255, 255, 255, 255));
        }

        Raylib.EndDrawing();
    }

    private static void DrawStarfield(
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (pos, size, color, parallax) in stars)
        {
            float cx = pos.X - camX * parallax + windowWidth / 2f;
            float cy = pos.Y - camY * parallax + windowHeight / 2f;

            cx = ((cx % windowWidth) + windowWidth) % windowWidth;
            cy = ((cy % windowHeight) + windowHeight) % windowHeight;

            Raylib.DrawCircle((int)cx, (int)cy, (int)size, color);
        }
    }

    private static void DrawClutter(
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (pos, width, height, color) in clutter)
        {
            float cx = pos.X - camX + windowWidth / 2f;
            float cy = pos.Y - camY + windowHeight / 2f;

            if (cx < -width || cx > windowWidth + width || cy < -height || cy > windowHeight + height) continue;

            Raylib.DrawRectangle((int)(cx - width / 2f), (int)(cy - height / 2f), (int)width, (int)height, color);
        }
    }

    private static void DrawAsteroids(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, asteroid, rot) in em.GetEntitiesWithComponents<Asteroid, Rotation>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            Raylib.DrawCircle((int)cx, (int)cy, (int)asteroid.Radius, new Color(255, 0, 0, 60));

            float angleDeg = rot.Angle * 180f / MathF.PI;
            Raylib.DrawRectanglePro(
                new Rectangle((int)cx, (int)cy, (int)asteroid.Width, (int)asteroid.Height),
                new System.Numerics.Vector2(asteroid.Width / 2f, asteroid.Height / 2f),
                angleDeg,
                new Color(200, 200, 210, 255)
            );
        }

        foreach (var (entity, asteroid) in em.GetEntitiesWithComponents<Asteroid>())
        {
            if (em.HasComponent<Rotation>(entity)) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            Raylib.DrawCircle((int)cx, (int)cy, (int)asteroid.Radius, new Color(255, 0, 0, 60));

            float rx = cx - asteroid.Width / 2f;
            float ry = cy - asteroid.Height / 2f;
            Raylib.DrawRectangle((int)rx, (int)ry, (int)asteroid.Width, (int)asteroid.Height, new Color(120, 120, 130, 255));
        }
    }

    private static void DrawAmmo(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, ammo) in em.GetEntitiesWithComponents<Ammo>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;
            Raylib.DrawCircle((int)cx, (int)cy, (int)ammo.Radius, new Color(255, 230, 100, 255));
        }
    }

    private static void DrawExplosions(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, explosion) in em.GetEntitiesWithComponents<Explosion>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float lifeRatio = explosion.Lifetime / 0.5f;
            float currentRadius = explosion.Radius * (1f - lifeRatio);
            int alpha = (int)(255f * lifeRatio);

            Raylib.DrawCircle((int)cx, (int)cy, (int)Math.Max(currentRadius, 1f), new Color(255, 230, 50, alpha));
        }
    }

    private static void DrawSparks(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, spark) in em.GetEntitiesWithComponents<Spark>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float lifeRatio = spark.Lifetime / 1.4f;
            int size = (int)Math.Max(lifeRatio * 5f, 1f);
            int r = (int)(lifeRatio * 255);
            int g = (int)(lifeRatio * lifeRatio * 80);
            int b = 0;
            int alpha = (int)(lifeRatio * 255);

            Raylib.DrawCircle((int)cx, (int)cy, size, new Color(r, g, b, alpha));
        }
    }

    private static void DrawPlayerShip(
        EntityManager em, Entity playerEntity, float camX, float camY, int windowWidth, int windowHeight)
    {
        var shipPos = em.GetComponent<Position>(playerEntity);
        var shipRot = em.GetComponent<Rotation>(playerEntity);
        var playerStats = em.GetComponent<Player>(playerEntity);

        float angle = shipRot.Angle;
        float size = 20f;

        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        var tipLocal = new System.Numerics.Vector2(0f, -size);
        var tip = new System.Numerics.Vector2(tipLocal.X * cos - tipLocal.Y * sin, tipLocal.X * sin + tipLocal.Y * cos);

        var leftLocal = new System.Numerics.Vector2(-size * 0.4f, size * 0.6f);
        var left = new System.Numerics.Vector2(leftLocal.X * cos - leftLocal.Y * sin, leftLocal.X * sin + leftLocal.Y * cos);

        var rightLocal = new System.Numerics.Vector2(size * 0.4f, size * 0.6f);
        var right = new System.Numerics.Vector2(rightLocal.X * cos - rightLocal.Y * sin, rightLocal.X * sin + rightLocal.Y * cos);

        float tx1 = (float)shipPos.Value.X + tip.X - camX + windowWidth / 2f;
        float ty1 = (float)shipPos.Value.Y + tip.Y - camY + windowHeight / 2f;
        float tx2 = (float)shipPos.Value.X + left.X - camX + windowWidth / 2f;
        float ty2 = (float)shipPos.Value.Y + left.Y - camY + windowHeight / 2f;
        float tx3 = (float)shipPos.Value.X + right.X - camX + windowWidth / 2f;
        float ty3 = (float)shipPos.Value.Y + right.Y - camY + windowHeight / 2f;

        Raylib.DrawTriangle(
            new System.Numerics.Vector2(tx1, ty1),
            new System.Numerics.Vector2(tx2, ty2),
            new System.Numerics.Vector2(tx3, ty3),
            new Color(100, 200, 255, 255)
        );

        float shipCx = (float)shipPos.Value.X - camX + windowWidth / 2f;
        float shipCy = (float)shipPos.Value.Y - camY + windowHeight / 2f;
        Raylib.DrawCircle((int)shipCx, (int)shipCy, (int)playerStats.Radius, new Color(0, 255, 0, 60));
    }

    public static void DrawTurrets(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, turret, rot) in em.GetEntitiesWithComponents<Turret, Rotation>())
        {
            if (turret.IsEnemy) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float turretSize = 8f;
            Raylib.DrawRectangle(
                (int)(cx - turretSize / 2f),
                (int)(cy - turretSize / 2f),
                (int)turretSize,
                (int)turretSize,
                new Color(255, 180, 50, 255)
            );
        }
    }

    private static void DrawEnemyShips(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
        {
            var shipPos = em.GetComponent<Position>(entity);
            var shipRot = em.GetComponent<Rotation>(entity);

            float angle = shipRot.Angle;
            float size = 20f;

            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            var tipLocal = new System.Numerics.Vector2(0f, size);
            var tip = new System.Numerics.Vector2(tipLocal.X * cos - tipLocal.Y * sin, tipLocal.X * sin + tipLocal.Y * cos);

            var leftLocal = new System.Numerics.Vector2(-size * 0.4f, -size * 0.6f);
            var left = new System.Numerics.Vector2(leftLocal.X * cos - leftLocal.Y * sin, leftLocal.X * sin + leftLocal.Y * cos);

            var rightLocal = new System.Numerics.Vector2(size * 0.4f, -size * 0.6f);
            var right = new System.Numerics.Vector2(rightLocal.X * cos - rightLocal.Y * sin, rightLocal.X * sin + rightLocal.Y * cos);

            float tx1 = (float)shipPos.Value.X + tip.X - camX + windowWidth / 2f;
            float ty1 = (float)shipPos.Value.Y + tip.Y - camY + windowHeight / 2f;
            float tx2 = (float)shipPos.Value.X + right.X - camX + windowWidth / 2f;
            float ty2 = (float)shipPos.Value.Y + right.Y - camY + windowHeight / 2f;
            float tx3 = (float)shipPos.Value.X + left.X - camX + windowWidth / 2f;
            float ty3 = (float)shipPos.Value.Y + left.Y - camY + windowHeight / 2f;

            Raylib.DrawTriangle(
                new System.Numerics.Vector2(tx1, ty1),
                new System.Numerics.Vector2(tx2, ty2),
                new System.Numerics.Vector2(tx3, ty3),
                new Color(255, 80, 80, 255)
            );

            float cx = (float)shipPos.Value.X - camX + windowWidth / 2f;
            float cy = (float)shipPos.Value.Y - camY + windowHeight / 2f;
            float enemyTurretSize = 8f;
            Raylib.DrawRectangle(
                (int)(cx - enemyTurretSize / 2f),
                (int)(cy - enemyTurretSize / 2f),
                (int)enemyTurretSize,
                (int)enemyTurretSize,
                new Color(255, 140, 30, 255)
            );

            Raylib.DrawCircle((int)cx, (int)cy, (int)enemyShip.Radius, new Color(255, 165, 0, 60));
        }
    }

    private static void DrawMines(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, mine) in em.GetEntitiesWithComponents<EnemyMine>())
        {
            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            if (em.HasComponent<Health>(entity))
            {
                var health = em.GetComponent<Health>(entity);
                int alpha = health.Current >= 2 ? 180 : 255;
                Raylib.DrawCircle((int)cx, (int)cy, (int)mine.Radius, new Color(255, 60, 60, alpha));
            }
            else
            {
                Raylib.DrawCircle((int)cx, (int)cy, (int)mine.Radius, new Color(255, 100, 100, 200));
            }

            Raylib.DrawCircle((int)cx, (int)cy, (int)(mine.Radius * 0.4f), new Color(255, 200, 200, 255));
        }
    }

    private static void DrawXpPickups(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, pickup) in em.GetEntitiesWithComponents<XpPickup>())
        {
            if (!em.HasComponent<Position>(entity)) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            int alpha = pickup.Chased ? 255 : 180;
            Raylib.DrawCircle((int)cx, (int)cy, (int)pickup.Radius, new Color(50, 150, 255, alpha));
        }
    }

    private static void DrawHealthOrbs(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, orb) in em.GetEntitiesWithComponents<HealthOrb>())
        {
            if (!em.HasComponent<Position>(entity)) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            Raylib.DrawCircle((int)cx, (int)cy, (int)orb.Radius, new Color(50, 255, 100, 200));
        }
    }

    private static void DrawGreenSparks(EntityManager em, float camX, float camY, int windowWidth, int windowHeight)
    {
        foreach (var (entity, spark) in em.GetEntitiesWithComponents<GreenSpark>())
        {
            if (!em.HasComponent<Position>(entity)) continue;

            var pos = em.GetComponent<Position>(entity);
            float cx = (float)pos.Value.X - camX + windowWidth / 2f;
            float cy = (float)pos.Value.Y - camY + windowHeight / 2f;

            float lifeRatio = spark.Lifetime / 0.6f;
            int size = (int)Math.Max(lifeRatio * 5f, 1f);
            int alpha = (int)(lifeRatio * 255);

            Raylib.DrawCircle((int)cx, (int)cy, size, new Color(50, 255, 100, alpha));
        }
    }

    private static void DrawHealthBar(EntityManager em, Entity playerEntity, int playerMaxHealth, int windowWidth, int windowHeight)
    {
        var playerHealth = em.GetComponent<Health>(playerEntity);
        int barWidth = 160;
        int barHeight = 14;
        int padding = 16;
        float healthPercent = (float)playerHealth.Current / playerMaxHealth;

        Raylib.DrawRectangle(padding, padding, barWidth, barHeight, new Color(50, 50, 50, 255));
        int filledWidth = (int)(barWidth * Math.Max(healthPercent, 0f));
        Color healthColor = filledWidth > barWidth / 3 ? new Color(80, 255, 80, 255) : new Color(255, 60, 60, 255);
        Raylib.DrawRectangle(padding, padding, filledWidth, barHeight, healthColor);
        Raylib.DrawRectangleLines(padding, padding, barWidth, barHeight, new Color(180, 180, 180, 255));

        string text = $"{playerHealth.Current}/{playerMaxHealth}";
        int textWidth = Raylib.MeasureText(text, 14);
        Raylib.DrawText(text, padding + (barWidth - textWidth) / 2, padding + (barHeight - 14) / 2, 14, new Color(255, 255, 255, 255));
    }

    public static void DrawUpgradeCards(int windowWidth, int windowHeight, PendingUpgradeOptions? options = null)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, new Color(15, 15, 25, 180));

        int cardW = 220;
        int cardH = 140;
        int spacing = 60;
        int totalW = cardW * 2 + spacing;
        int startX = (windowWidth - totalW) / 2;
        int startY = windowHeight / 2 - cardH / 2;

        if (options.HasValue)
        {
            var optA = options.Value.OptionA;
            var optB = options.Value.OptionB;
            DrawCard(startX, startY, GetUpgradeTitle(optA), GetUpgradeValue(optA), new Color(50, 150, 255, 255), "1");
            DrawCard(startX + cardW + spacing, startY, GetUpgradeTitle(optB), GetUpgradeValue(optB), new Color(50, 150, 255, 255), "2");
        }
        else
        {
            DrawCard(startX, startY, "Fire Rate", "+15%", new Color(50, 150, 255, 255), "1");
            DrawCard(startX + cardW + spacing, startY, "Projectile Speed", "+30%", new Color(50, 150, 255, 255), "2");
        }

        Raylib.DrawText("Press 1 or 2 to choose", windowWidth / 2 - 90, windowHeight / 2 + cardH / 2 + 30, 16, new Color(200, 200, 200, 255));
    }

    private static string GetUpgradeTitle(UpgradeOption option) => option switch
    {
        UpgradeOption.FireRate => "Fire Rate",
        UpgradeOption.ProjectileSpeed => "Projectile Speed",
        UpgradeOption.PickupRadius => "Pickup Radius",
        _ => "Unknown"
    };

    private static string GetUpgradeValue(UpgradeOption option) => option switch
    {
        UpgradeOption.FireRate => "+15%",
        UpgradeOption.ProjectileSpeed => "+30%",
        UpgradeOption.PickupRadius => "+20%",
        _ => "?"
    };

    private static void DrawCard(int x, int y, string title, string value, Color borderColor, string key)
    {
        Raylib.DrawRectangle(x, y, 220, 140, new Color(35, 35, 45, 255));
        Raylib.DrawRectangleLines(x, y, 220, 140, borderColor);

        int keyWidth = Raylib.MeasureText(key, 18);
        Raylib.DrawText(key, x + 10, y + 10, 18, new Color(200, 200, 200, 255));

        int titleWidth = Raylib.MeasureText(title, 24);
        Raylib.DrawText(title, x + 110 - titleWidth / 2, y + 35, 24, new Color(255, 255, 255, 255));

        int valueWidth = Raylib.MeasureText(value, 36);
        Raylib.DrawText(value, x + 110 - valueWidth / 2, y + 75, 36, borderColor);
    }

    public static void DrawLoadoutCards(int windowWidth, int windowHeight)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, new Color(15, 15, 25, 180));

        int cardW = 340;
        int cardH = 160;
        int spacing = 60;
        int totalW = cardW * 2 + spacing;
        int startX = (windowWidth - totalW) / 2;
        int startY = windowHeight / 2 - cardH / 2;

        DrawLoadoutCard(startX, startY, "Forward", 
            "1 turret · 90° forward arc\nFire Rate: 8 · Ammo Speed: 420",
            new Color(50, 150, 255, 255), "1");

        DrawLoadoutCard(startX + cardW + spacing, startY, "Broadside", 
            "2 turrets · Shotgun · 3 pellets\nFire Rate: 2 · Ammo Speed: 350",
            new Color(50, 150, 255, 255), "2");

        Raylib.DrawText("Press 1 or 2 to choose", windowWidth / 2 - 90, windowHeight / 2 + cardH / 2 + 30, 16, new Color(200, 200, 200, 255));
    }

    private static void DrawLoadoutCard(int x, int y, string title, string details, Color borderColor, string key)
    {
        Raylib.DrawRectangle(x, y, 340, 160, new Color(35, 35, 45, 255));
        Raylib.DrawRectangleLines(x, y, 340, 160, borderColor);

        int keyWidth = Raylib.MeasureText(key, 18);
        Raylib.DrawText(key, x + 10, y + 10, 18, new Color(200, 200, 200, 255));

        int titleWidth = Raylib.MeasureText(title, 24);
        Raylib.DrawText(title, x + 170 - titleWidth / 2, y + 35, 24, new Color(255, 255, 255, 255));

        string[] lines = details.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineW = Raylib.MeasureText(lines[i], 16);
            Raylib.DrawText(lines[i], x + 170 - lineW / 2, y + 75 + i * 20, 16, new Color(200, 200, 200, 255));
        }
    }
}
