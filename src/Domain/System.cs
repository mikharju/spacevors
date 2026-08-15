namespace Spacevors.Domain;

public abstract class GameSystem
{
    public virtual void Update(WorldView view, float deltaTime, CommandBuffer commands) { }
}
