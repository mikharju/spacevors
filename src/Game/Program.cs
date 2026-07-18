using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Spacevors.Game.Infrastructure;

const float FixedDeltaTime = 1f / 60f;
const int WindowWidth = 1280;
const int WindowHeight = 720;

var em = new EntityManager();
var playerEntity = em.CreateEntity();
em.AddComponent(playerEntity, new Position(new Vector2(WindowWidth / 2f, WindowHeight / 2f)));

var systems = new GameSystem[] { new PhysicsSystem() };

var input = new InputAdapter();
Raylib.InitWindow(WindowWidth, WindowHeight, "SpaceVors");

float accumulator = 0f;

while (!Raylib.WindowShouldClose())
{
    float frameTime = (float)Raylib.GetFrameTime();
    accumulator += frameTime;

    // Handle input - update position directly
    var playerPos = em.GetComponent<Position>(playerEntity);
    var speed = 300f;

    if (input.IsKeyDown(KeyboardKey.W))
        em.AddComponent(playerEntity, new Position(playerPos.Value + new Vector2(0, -speed * frameTime)));
    if (input.IsKeyDown(KeyboardKey.S))
        em.AddComponent(playerEntity, new Position(playerPos.Value + new Vector2(0, speed * frameTime)));
    if (input.IsKeyDown(KeyboardKey.A))
        em.AddComponent(playerEntity, new Position(playerPos.Value + new Vector2(-speed * frameTime, 0)));
    if (input.IsKeyDown(KeyboardKey.D))
        em.AddComponent(playerEntity, new Position(playerPos.Value + new Vector2(speed * frameTime, 0)));

    // Fixed timestep simulation
    while (accumulator >= FixedDeltaTime)
    {
        foreach (var system in systems)
        {
            system.Update(em, FixedDeltaTime);
        }
        accumulator -= FixedDeltaTime;
    }

    // Render
    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(30, 30, 50, 255));

    foreach (var (entity, pos) in em.GetEntitiesWithComponents<Position>())
    {
        var rect = new Rectangle(
            (float)pos.Value.X - 16f,
            (float)pos.Value.Y - 16f,
            32f,
            32f
        );
        Raylib.DrawRectangleRec(rect, new Color(100, 200, 255, 255));
    }

    Raylib.EndDrawing();
}

Raylib.CloseWindow();
