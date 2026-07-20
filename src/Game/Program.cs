using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;

const float FixedDeltaTime = 1f / 60f;
const int WindowWidth = 1280;
const int WindowHeight = 720;

var em = new EntityManager();

// Create player ship
var playerEntity = em.CreateEntity();
em.AddComponent(playerEntity, new Position(new Vector2(0f, 0f)));
em.AddComponent(playerEntity, new Velocity(Vector2.Zero));
em.AddComponent(playerEntity, new Rotation(0f));
em.AddComponent(playerEntity, new AngularVelocity(0f));
em.AddComponent(playerEntity, new Player(Thrust: 400f, Boost: 2.5f));

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

var systems = new GameSystem[] { new PhysicsSystem(), new CollisionSystem(), new CameraSystem() };

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

    // Fixed timestep simulation
    while (accumulator >= FixedDeltaTime)
    {
        foreach (var system in systems)
        {
            system.Update(em, FixedDeltaTime);
        }
        accumulator -= FixedDeltaTime;
    }

    // Get camera offset for rendering
    var cam = em.GetComponent<Camera>(cameraEntity);
    float camX = (float)cam.Target.X;
    float camY = (float)cam.Target.Y;

    // Render
    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(15, 15, 25, 255));

    // Draw asteroids as rotated gray rectangles
    foreach (var (entity, asteroid) in em.GetEntitiesWithComponents<Asteroid>())
    {
        var pos = em.GetComponent<Position>(entity);
        float cx = (float)pos.Value.X - camX + WindowWidth / 2f;
        float cy = (float)pos.Value.Y - camY + WindowHeight / 2f;

        // Debug: draw AABB collision bounds in red
        Raylib.DrawRectangle((int)(cx - asteroid.Width / 2f), (int)(cy - asteroid.Height / 2f), (int)asteroid.Width, (int)asteroid.Height, new Color(255, 0, 0, 60));

        if (em.HasComponent<Rotation>(entity))
        {
            var rot = em.GetComponent<Rotation>(entity);

            float angleDeg = rot.Angle * 180f / MathF.PI;
            Raylib.DrawRectanglePro(
                new Rectangle((int)cx, (int)cy, (int)asteroid.Width, (int)asteroid.Height), // do not try to set origin to middle here, next line covers it
                new System.Numerics.Vector2(asteroid.Width / 2f, asteroid.Height / 2f), // Only set origin here
                angleDeg,
                new Color(200, 200, 210, 255)
            );
        }
        else
        {
            float rx = cx - asteroid.Width / 2f;
            float ry = cy - asteroid.Height / 2f;
            Raylib.DrawRectangle((int)rx, (int)ry, (int)asteroid.Width, (int)asteroid.Height, new Color(120, 120, 130, 255));
        }
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

        // Debug: draw player collision circle in green (radius = 18f)
        float shipCx = (float)shipPos.Value.X - camX + WindowWidth / 2f;
        float shipCy = (float)shipPos.Value.Y - camY + WindowHeight / 2f;
        Raylib.DrawCircle((int)shipCx, (int)shipCy, 18, new Color(0, 255, 0, 60));
    }

    Raylib.EndDrawing();
}

Raylib.CloseWindow();
