using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

const float FixedDeltaTime = 1f / 60f;
const int WindowWidth = 1280;
const int WindowHeight = 720;
const int PlayerMaxHealth = 10;

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

// Spawn asteroids around the player
Random rand = new Random(42);

// 5 close asteroids within initial view range
for (int i = 0; i < 5; i++)
{
    var asteroid = em.CreateEntity();
    float angle = (float)(rand.NextDouble() * Math.PI * 2f);
    float dist = 150f + (float)rand.NextDouble() * 400f;
    float ax = (float)Math.Cos(angle) * dist;
    float ay = (float)Math.Sin(angle) * dist;
    float aw = 40f + (float)rand.NextDouble() * 60f;
    float ah = 30f + (float)rand.NextDouble() * 50f;
    float ar = Math.Max(aw, ah) / 2f;
    float aSpeed = 15f + (float)rand.NextDouble() * 35f;
    float aAngle = (float)(rand.NextDouble() * Math.PI * 2);
    em.AddComponent(asteroid, new Position(new Vector2(ax, ay)));
    em.AddComponent(asteroid, new Velocity(new Vector2((float)Math.Cos(aAngle) * aSpeed, (float)Math.Sin(aAngle) * aSpeed)));
    em.AddComponent(asteroid, new Rotation((float)(rand.NextDouble() * Math.PI * 2)));
    em.AddComponent(asteroid, new AngularVelocity((float)(rand.NextDouble() - 0.5) * 1.5f));
    em.AddComponent(asteroid, new Asteroid(aw, ah, ar));
}

// Remaining asteroids in a larger area (~5 screens away)
for (int i = 5; i < 105; i++)
{
    var asteroid = em.CreateEntity();
    float angle = (float)(rand.NextDouble() * Math.PI * 2f);
    float dist = 1000f + (float)rand.NextDouble() * 4000f;
    float ax = (float)Math.Cos(angle) * dist;
    float ay = (float)Math.Sin(angle) * dist;
    float aw = 40f + (float)rand.NextDouble() * 60f;
    float ah = 30f + (float)rand.NextDouble() * 50f;
    float ar = Math.Max(aw, ah) / 2f;
    float aSpeed = 10f + (float)rand.NextDouble() * 25f;
    float aAngle = (float)(rand.NextDouble() * Math.PI * 2);
    em.AddComponent(asteroid, new Position(new Vector2(ax, ay)));
    em.AddComponent(asteroid, new Velocity(new Vector2((float)Math.Cos(aAngle) * aSpeed, (float)Math.Sin(aAngle) * aSpeed)));
    em.AddComponent(asteroid, new Rotation((float)(rand.NextDouble() * Math.PI * 2)));
    em.AddComponent(asteroid, new AngularVelocity((float)(rand.NextDouble() - 0.5f) * 1.5f));
    em.AddComponent(asteroid, new Asteroid(aw, ah, ar));
}

// Spawn enemy mines around the player
for (int i = 0; i < 15; i++)
{
    var mine = em.CreateEntity();
    float angle = (float)(rand.NextDouble() * Math.PI * 2f);
    float dist = 300f + (float)rand.NextDouble() * 3000f;
    float mx = (float)Math.Cos(angle) * dist;
    float my = (float)Math.Sin(angle) * dist;
    float mRadius = 15f + (float)rand.NextDouble() * 10f;
    em.AddComponent(mine, new Position(new Vector2(mx, my)));
    em.AddComponent(mine, new Velocity(Vector2.Zero));
    em.AddComponent(mine, new EnemyMine(mRadius, 30f + (float)rand.NextDouble() * 20f, angle));
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
    em.AddComponent(ship, new Position(new Vector2(sx, sy)));
    em.AddComponent(ship, new Velocity(new Vector2((float)Math.Cos(sAngle) * sSpeed, (float)Math.Sin(sAngle) * sSpeed)));
    em.AddComponent(ship, new Rotation(sAngle));
    em.AddComponent(ship, new AngularVelocity((float)(rand.NextDouble() - 0.5f) * 1f));
    em.AddComponent(ship, new EnemyShip(
        Radius: 20f,
        Speed: 35f,
        TurnRate: 3.5f,
        Health: 3,
        DetectionRange: WindowHeight / 2f * 2f / 3f,
        FiringRange: 300f,
        TurretFireRate: 1.5f,
        TurretAmmoSpeed: 200f));
    em.AddComponent(ship, new Turret(
        FireRate: 1.5f,
        AmmoSpeed: 200f,
        KickbackForce: 0f,
        ArcAngle: MathF.PI / 8f,
        Range: WindowHeight / 2f * 2f / 3f,
        IsEnemy: true));
    em.AddComponent(ship, new Health(3));
}

// Spawn two enemy ships at screen edges just inside view range
for (int side = -1; side <= 1; side += 2)
{
    var edgeShip = em.CreateEntity();
    float ex = side * (WindowWidth / 2f - 50f);
    float ey = 80f * side;
    float eAngle = (float)(Math.PI / 4f * side);
    em.AddComponent(edgeShip, new Position(new Vector2(ex, ey)));
    em.AddComponent(edgeShip, new Velocity(Vector2.Zero));
    em.AddComponent(edgeShip, new Rotation(eAngle + MathF.PI));
    em.AddComponent(edgeShip, new AngularVelocity(0f));
    em.AddComponent(edgeShip, new EnemyShip(
        Radius: 20f,
        Speed: 35f,
        TurnRate: 3.5f,
        Health: 3,
        DetectionRange: WindowHeight / 2f * 2f / 3f,
        FiringRange: 300f,
        TurretFireRate: 1.5f,
        TurretAmmoSpeed: 200f));
    em.AddComponent(edgeShip, new Turret(
        FireRate: 1.5f,
        AmmoSpeed: 200f,
        KickbackForce: 0f,
        ArcAngle: MathF.PI / 8f,
        Range: WindowHeight / 2f * 2f / 3f,
        IsEnemy: true));
    em.AddComponent(edgeShip, new Health(3));
}

bool gameOver = false;

var turretEntity = em.CreateEntity();
em.AddComponent(turretEntity, new Position(new Vector2(0f, 0f)));
em.AddComponent(turretEntity, new Rotation(0f));
    em.AddComponent(turretEntity, new Turret(FireRate: 6f, AmmoSpeed: 350f, KickbackForce: 10f, ArcAngle: MathF.PI / 4f, Range: WindowHeight / 2f, IsEnemy: false));

var systems = new GameSystem[] { new FiringSystem(), new PhysicsSystem(), new CollisionSystem(), new AmmoLifetimeSystem(), new MineDriftSystem(), new EnemyShipSpawnSystem(), new EnemyShipSystem(), new CameraSystem(), new TurretFiringSystem(), new EffectSystem() };

Raylib.InitWindow(WindowWidth, WindowHeight, "SpaceVors");

float accumulator = 0f;

while (!Raylib.WindowShouldClose())
{
    float frameTime = (float)Raylib.GetFrameTime();
    accumulator += frameTime;

    // Handle player input
    var playerPos = em.GetComponent<Position>(playerEntity);
    var playerRot = em.GetComponent<Rotation>(playerEntity);
    var playerStats = em.GetComponent<Player>(playerEntity);
    float thrustForce = playerStats.Thrust;
    if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift))
        thrustForce *= playerStats.Boost;

    // Thrust: apply acceleration in direction of ship rotation
    if (Raylib.IsKeyDown(KeyboardKey.W))
    {
        float cos = (float)Math.Cos(playerRot.Angle);
        float sin = (float)Math.Sin(playerRot.Angle);
        var thrustAccel = new Vector2(sin * thrustForce, -cos * thrustForce);
        em.AddComponent(playerEntity, new Acceleration(thrustAccel));
    }
    else
    {
        em.AddComponent(playerEntity, new Acceleration(Vector2.Zero));
    }

    // Rotation: A/D changes angular velocity
    if (Raylib.IsKeyDown(KeyboardKey.A))
    {
        var angVel = em.GetComponent<AngularVelocity>(playerEntity);
        em.AddComponent(playerEntity, new AngularVelocity(angVel.Value - 5f * frameTime));
    }
    else if (Raylib.IsKeyDown(KeyboardKey.D))
    {
        var angVel = em.GetComponent<AngularVelocity>(playerEntity);
        em.AddComponent(playerEntity, new AngularVelocity(angVel.Value + 5f * frameTime));
    }

    // Firing: Space key sets negative cooldown to signal "ready to fire"
    if (Raylib.IsKeyDown(KeyboardKey.Space))
    {
        var hasCooldown = em.HasComponent<FireCooldown>(playerEntity);
        var currentCooldown = hasCooldown ? em.GetComponent<FireCooldown>(playerEntity).Timer : -1f;

        if (!hasCooldown || currentCooldown <= 0f)
        {
            em.AddComponent(playerEntity, new FireCooldown(-1f));
        }
    }

    em.AddComponent(turretEntity, new Position(playerPos.Value));
    em.AddComponent(turretEntity, new Rotation(playerRot.Angle));

    // Fixed timestep simulation
    while (accumulator >= FixedDeltaTime)
    {
        foreach (var system in systems)
        {
            system.Update(em, FixedDeltaTime);
        }
        accumulator -= FixedDeltaTime;
    }

    if (!gameOver && em.HasComponent<Dead>(playerEntity))
    {
        gameOver = true;
    }

    // Get camera offset for rendering
    var cam = em.GetComponent<Camera>(cameraEntity);
    float camX = (float)cam.Target.X;
    float camY = (float)cam.Target.Y;

    // Render
    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(15, 15, 25, 255));

    // Draw asteroids with rotation as rotated gray rectangles
    foreach (var (entity, asteroid, rot) in em.GetEntitiesWithComponents<Asteroid, Rotation>())
    {
        var pos = em.GetComponent<Position>(entity);
        float cx = (float)pos.Value.X - camX + WindowWidth / 2f;
        float cy = (float)pos.Value.Y - camY + WindowHeight / 2f;

        // Debug: draw circle collision bounds in red
        Raylib.DrawCircle((int)cx, (int)cy, (int)asteroid.Radius, new Color(255, 0, 0, 60));

        float angleDeg = rot.Angle * 180f / MathF.PI;
        Raylib.DrawRectanglePro(
            new Rectangle((int)cx, (int)cy, (int)asteroid.Width, (int)asteroid.Height),
            new System.Numerics.Vector2(asteroid.Width / 2f, asteroid.Height / 2f),
            angleDeg,
            new Color(200, 200, 210, 255)
        );
    }

    // Draw asteroids without rotation as unrotated gray rectangles
    foreach (var (entity, asteroid) in em.GetEntitiesWithComponents<Asteroid>())
    {
        if (em.HasComponent<Rotation>(entity)) continue;

        var pos = em.GetComponent<Position>(entity);
        float cx = (float)pos.Value.X - camX + WindowWidth / 2f;
        float cy = (float)pos.Value.Y - camY + WindowHeight / 2f;

        // Debug: draw circle collision bounds in red
        Raylib.DrawCircle((int)cx, (int)cy, (int)asteroid.Radius, new Color(255, 0, 0, 60));

        float rx = cx - asteroid.Width / 2f;
        float ry = cy - asteroid.Height / 2f;
        Raylib.DrawRectangle((int)rx, (int)ry, (int)asteroid.Width, (int)asteroid.Height, new Color(120, 120, 130, 255));
    }

    // Draw ammo as small yellow circles
    foreach (var (entity, ammo) in em.GetEntitiesWithComponents<Ammo>())
    {
        var pos = em.GetComponent<Position>(entity);
        float cx = (float)pos.Value.X - camX + WindowWidth / 2f;
        float cy = (float)pos.Value.Y - camY + WindowHeight / 2f;
        Raylib.DrawCircle((int)cx, (int)cy, (int)ammo.Radius, new Color(255, 230, 100, 255));
    }

    // Draw explosions as growing yellow circles with fading alpha
    foreach (var (entity, explosion) in em.GetEntitiesWithComponents<Explosion>())
    {
        var pos = em.GetComponent<Position>(entity);
        float cx = (float)pos.Value.X - camX + WindowWidth / 2f;
        float cy = (float)pos.Value.Y - camY + WindowHeight / 2f;

        float lifeRatio = explosion.Lifetime / 0.25f;
        float currentRadius = explosion.Radius * (1f - lifeRatio);
        int alpha = (int)(255f * lifeRatio);

        Raylib.DrawCircle((int)cx, (int)cy, (int)Math.Max(currentRadius, 1f), new Color(255, 230, 50, alpha));
    }

    // Draw sparks as small colored circles that fade from orange to red to black
    foreach (var (entity, spark) in em.GetEntitiesWithComponents<Spark>())
    {
        var pos = em.GetComponent<Position>(entity);
        float cx = (float)pos.Value.X - camX + WindowWidth / 2f;
        float cy = (float)pos.Value.Y - camY + WindowHeight / 2f;

        float lifeRatio = spark.Lifetime / 0.7f;
        int size = (int)Math.Max(lifeRatio * 5f, 1f);
        int r = (int)(lifeRatio * 255);
        int g = (int)(lifeRatio * lifeRatio * 80);
        int b = 0;
        int alpha = (int)(lifeRatio * 255);

        Raylib.DrawCircle((int)cx, (int)cy, size, new Color(r, g, b, alpha));
    }

    // Draw player ship as a light blue triangle
    {
        var shipPos = em.GetComponent<Position>(playerEntity);
        var shipRot = em.GetComponent<Rotation>(playerEntity);
        float angle = shipRot.Angle;
        float size = 20f;

        // Triangle vertices: apply rotation matrix to local-space points
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        // Tip in local space: (0, -size), pointing forward (-Y)
        var tipLocal = new System.Numerics.Vector2(0f, -size);
        var tip = new System.Numerics.Vector2(tipLocal.X * cos - tipLocal.Y * sin, tipLocal.X * sin + tipLocal.Y * cos);

        // Left base corner in local space: bottom-left of ship
        var leftLocal = new System.Numerics.Vector2(-size * 0.4f, size * 0.6f);
        var left = new System.Numerics.Vector2(leftLocal.X * cos - leftLocal.Y * sin, leftLocal.X * sin + leftLocal.Y * cos);

        // Right base corner in local space: bottom-right of ship
        var rightLocal = new System.Numerics.Vector2(size * 0.4f, size * 0.6f);
        var right = new System.Numerics.Vector2(rightLocal.X * cos - rightLocal.Y * sin, rightLocal.X * sin + rightLocal.Y * cos);

        // Apply camera offset and center
        float tx1 = (float)shipPos.Value.X + tip.X - camX + WindowWidth / 2f;
        float ty1 = (float)shipPos.Value.Y + tip.Y - camY + WindowHeight / 2f;
        float tx2 = (float)shipPos.Value.X + left.X - camX + WindowWidth / 2f;
        float ty2 = (float)shipPos.Value.Y + left.Y - camY + WindowHeight / 2f;
        float tx3 = (float)shipPos.Value.X + right.X - camX + WindowWidth / 2f;
        float ty3 = (float)shipPos.Value.Y + right.Y - camY + WindowHeight / 2f;

        Raylib.DrawTriangle(
            new System.Numerics.Vector2(tx1, ty1),
            new System.Numerics.Vector2(tx2, ty2),
            new System.Numerics.Vector2(tx3, ty3),
            new Color(100, 200, 255, 255)
        );

        // Debug: draw player collision circle in green
        float shipCx = (float)shipPos.Value.X - camX + WindowWidth / 2f;
        float shipCy = (float)shipPos.Value.Y - camY + WindowHeight / 2f;
        Raylib.DrawCircle((int)shipCx, (int)shipCy, (int)playerStats.Radius, new Color(0, 255, 0, 60));

        // Draw turret as a small rectangle at ship center
        float turretSize = 8f;
        Raylib.DrawRectangle(
            (int)(shipCx - turretSize / 2f),
            (int)(shipCy - turretSize / 2f),
            (int)turretSize,
            (int)turretSize,
            new Color(255, 180, 50, 255)
        );
    }

    // Draw enemy ships as red triangles pointing opposite to player ship
    foreach (var (entity, enemyShip) in em.GetEntitiesWithComponents<EnemyShip>())
    {
        var shipPos = em.GetComponent<Position>(entity);
        var shipRot = em.GetComponent<Rotation>(entity);
        float angle = shipRot.Angle;
        float size = 20f;

        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        // Tip in local space: (0, size), pointing backward (+Y) — opposite to player
        var tipLocal = new System.Numerics.Vector2(0f, size);
        var tip = new System.Numerics.Vector2(tipLocal.X * cos - tipLocal.Y * sin, tipLocal.X * sin + tipLocal.Y * cos);

        // Left base corner in local space: bottom-left of ship
        var leftLocal = new System.Numerics.Vector2(-size * 0.4f, -size * 0.6f);
        var left = new System.Numerics.Vector2(leftLocal.X * cos - leftLocal.Y * sin, leftLocal.X * sin + leftLocal.Y * cos);

        // Right base corner in local space: bottom-right of ship
        var rightLocal = new System.Numerics.Vector2(size * 0.4f, -size * 0.6f);
        var right = new System.Numerics.Vector2(rightLocal.X * cos - rightLocal.Y * sin, rightLocal.X * sin + rightLocal.Y * cos);

        float tx1 = (float)shipPos.Value.X + tip.X - camX + WindowWidth / 2f;
        float ty1 = (float)shipPos.Value.Y + tip.Y - camY + WindowHeight / 2f;
        float tx2 = (float)shipPos.Value.X + right.X - camX + WindowWidth / 2f;
        float ty2 = (float)shipPos.Value.Y + right.Y - camY + WindowHeight / 2f;
        float tx3 = (float)shipPos.Value.X + left.X - camX + WindowWidth / 2f;
        float ty3 = (float)shipPos.Value.Y + left.Y - camY + WindowHeight / 2f;

        Raylib.DrawTriangle(
            new System.Numerics.Vector2(tx1, ty1),
            new System.Numerics.Vector2(tx2, ty2),
            new System.Numerics.Vector2(tx3, ty3),
            new Color(255, 80, 80, 255)
        );

        // Draw enemy turret as small orange rectangle at ship center
        float cx = (float)shipPos.Value.X - camX + WindowWidth / 2f;
        float cy = (float)shipPos.Value.Y - camY + WindowHeight / 2f;
        float enemyTurretSize = 8f;
        Raylib.DrawRectangle(
            (int)(cx - enemyTurretSize / 2f),
            (int)(cy - enemyTurretSize / 2f),
            (int)enemyTurretSize,
            (int)enemyTurretSize,
            new Color(255, 140, 30, 255)
        );

        // Debug: draw enemy ship collision circle in orange
        Raylib.DrawCircle((int)cx, (int)cy, (int)enemyShip.Radius, new Color(255, 165, 0, 60));
    }

    // Draw enemy mines as red circles with pulsing effect
    foreach (var (entity, mine) in em.GetEntitiesWithComponents<EnemyMine>())
    {
        var pos = em.GetComponent<Position>(entity);
        float cx = (float)pos.Value.X - camX + WindowWidth / 2f;
        float cy = (float)pos.Value.Y - camY + WindowHeight / 2f;

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

        // Draw inner core
        Raylib.DrawCircle((int)cx, (int)cy, (int)(mine.Radius * 0.4f), new Color(255, 200, 200, 255));
    }

    // Draw player health bar at top left
    {
        var playerHealth = em.GetComponent<Health>(playerEntity);
        int barWidth = 160;
        int barHeight = 14;
        int padding = 16;
        float healthPercent = (float)playerHealth.Current / PlayerMaxHealth;

        Raylib.DrawRectangle(padding, padding, barWidth, barHeight, new Color(50, 50, 50, 255));
        int filledWidth = (int)(barWidth * Math.Max(healthPercent, 0f));
        Color healthColor = filledWidth > barWidth / 3 ? new Color(80, 255, 80, 255) : new Color(255, 60, 60, 255);
        Raylib.DrawRectangle(padding, padding, filledWidth, barHeight, healthColor);
        Raylib.DrawRectangleLines(padding, padding, barWidth, barHeight, new Color(180, 180, 180, 255));

        string text = $"{playerHealth.Current}/{PlayerMaxHealth}";
        int textWidth = Raylib.MeasureText(text, 14);
        Raylib.DrawText(text, padding + (barWidth - textWidth) / 2, padding + (barHeight - 14) / 2, 14, new Color(255, 255, 255, 255));
    }

    if (gameOver)
    {
        Raylib.DrawText("GAME OVER", WindowWidth / 2 - 80, WindowHeight / 2 - 20, 40, new Color(255, 255, 255, 255));
    }

    Raylib.EndDrawing();
}

Raylib.CloseWindow();
