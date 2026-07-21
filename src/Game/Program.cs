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
    em.AddComponent(ship, new Position(new Vector2(sx, sy)));
    em.AddComponent(ship, new Velocity(new Vector2((float)Math.Cos(sAngle) * sSpeed, (float)Math.Sin(sAngle) * sSpeed)));
    em.AddComponent(ship, new Rotation(sAngle));
    em.AddComponent(ship, new AngularVelocity((float)(rand.NextDouble() - 0.5f) * 1f));
    em.AddComponent(ship, new EnemyShip(
        Radius: 20f,
        Speed: 35f,
        TurnRate: 1.0f,
        Health: 3,
        DetectionRange: 1200f,
        FiringRange: 300f,
        TurretFireRate: 1.5f,
        TurretAmmoSpeed: 200f,
        Acceleration: 9.0f));
    em.AddComponent(ship, new Turret(
        FireRate: 1.5f,
        AmmoSpeed: 200f,
        KickbackForce: 0f,
        ArcAngle: MathF.PI / 8f,
        Range: 1200f,
        IsEnemy: true));
    em.AddComponent(ship, new Health(3));
}

// Spawn two enemy ships at screen edges just inside view range
for (int side = -1; side <= 1; side += 2)
{
    var edgeShip = em.CreateEntity();
    float ex = side * 900f;
    float ey = 150f * side;
    float eAngle = (float)(Math.PI / 4f * side);
    em.AddComponent(edgeShip, new Position(new Vector2(ex, ey)));
    em.AddComponent(edgeShip, new Velocity(Vector2.Zero));
    em.AddComponent(edgeShip, new Rotation(eAngle + MathF.PI));
    em.AddComponent(edgeShip, new AngularVelocity(0f));
    em.AddComponent(edgeShip, new EnemyShip(
        Radius: 20f,
        Speed: 35f,
        TurnRate: 1.0f,
        Health: 3,
        DetectionRange: 1200f,
        FiringRange: 300f,
        TurretFireRate: 1.5f,
        TurretAmmoSpeed: 200f,
        Acceleration: 9.0f));
    em.AddComponent(edgeShip, new Turret(
        FireRate: 1.5f,
        AmmoSpeed: 200f,
        KickbackForce: 0f,
        ArcAngle: MathF.PI / 8f,
        Range: 1200f,
        IsEnemy: true));
    em.AddComponent(edgeShip, new Health(3));
}

bool gameOver = false;

var turretEntity = em.CreateEntity();
em.AddComponent(turretEntity, new Position(new Vector2(0f, 0f)));
em.AddComponent(turretEntity, new Rotation(0f));
    em.AddComponent(turretEntity, new Turret(FireRate: 6f, AmmoSpeed: 350f, KickbackForce: 10f, ArcAngle: MathF.PI / 4f, Range: WindowHeight / 2f, IsEnemy: false));

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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            system.Update(em, FixedDeltaTime);
            sw.Stop();
            DiagnosticLogger.LogSystem(system.GetType().Name, sw.ElapsedTicks);
        }
        accumulator -= FixedDeltaTime;
    }

    if (!gameOver && em.HasComponent<Dead>(playerEntity))
    {
        gameOver = true;
    }

    var cam = em.GetComponent<Camera>(cameraEntity);
    float camX = (float)cam.Target.X;
    float camY = (float)cam.Target.Y;

    Renderer.Render(em, camX, camY, WindowWidth, WindowHeight, gameOver, stars, clutter, playerEntity, PlayerMaxHealth);
}

Raylib.CloseWindow();
