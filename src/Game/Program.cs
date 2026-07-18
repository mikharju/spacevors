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
for (int i = 0; i < 15; i++)
{
    var asteroid = em.CreateEntity();
    float angle = (float)(rand.NextDouble() * Math.PI * 2f);
    float dist = 200f + (float)rand.NextDouble() * 600f;
    float ax = (float)Math.Cos(angle) * dist;
    float ay = (float)Math.Sin(angle) * dist;
    float aw = 40f + (float)rand.NextDouble() * 60f;
    float ah = 30f + (float)rand.NextDouble() * 50f;
    em.AddComponent(asteroid, new Position(new Vector2(ax, ay)));
    em.AddComponent(asteroid, new Asteroid(aw, ah));
}

var systems = new GameSystem[] { new PhysicsSystem(), new CameraSystem() };

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

    // Draw asteroids as gray rectangles
    foreach (var (entity, asteroid) in em.GetEntitiesWithComponents<Asteroid>())
    {
        var pos = em.GetComponent<Position>(entity);
        float rx = (float)pos.Value.X - camX - asteroid.Width / 2f + WindowWidth / 2f;
        float ry = (float)pos.Value.Y - camY - asteroid.Height / 2f + WindowHeight / 2f;
        Raylib.DrawRectangle((int)rx, (int)ry, (int)asteroid.Width, (int)asteroid.Height, new Color(120, 120, 130, 255));
    }

    // Draw player ship as a light blue triangle
    {
        var shipPos = em.GetComponent<Position>(playerEntity);
        var shipRot = em.GetComponent<Rotation>(playerEntity);
        float angle = shipRot.Angle;
        float size = 20f;

        // Triangle vertices (pointing forward, which is -Y in local space)
        var tip = new System.Numerics.Vector2((float)Math.Sin(angle) * size, -(float)Math.Cos(angle) * size);
        var left = new System.Numerics.Vector2(-(float)Math.Cos(angle + Math.PI / 6) * size * 0.7f, (float)Math.Sin(angle + Math.PI / 6) * size * 0.7f);
        var right = new System.Numerics.Vector2((float)Math.Cos(angle - Math.PI / 6) * size * 0.7f, -(float)Math.Sin(angle - Math.PI / 6) * size * 0.7f);

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
    }

    Raylib.EndDrawing();
}

Raylib.CloseWindow();
