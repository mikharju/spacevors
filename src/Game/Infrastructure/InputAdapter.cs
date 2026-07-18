using Raylib_cs;

namespace Spacevors.Game.Infrastructure;

public class InputAdapter
{
    public bool IsKeyPressed(KeyboardKey key) => Raylib.IsKeyPressed(key);
    public bool IsKeyDown(KeyboardKey key) => Raylib.IsKeyDown(key);
    public bool IsKeyReleased(KeyboardKey key) => Raylib.IsKeyReleased(key);
}
