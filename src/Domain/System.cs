namespace Spacevors.Domain;

public abstract class GameSystem
{
    private static float _accumulatedTime = 0f;
    public static void ResetElapsedTime() => _accumulatedTime = 0f;
    public static void AddElapsedTime(float amount) => _accumulatedTime += amount;
    public static float ElapsedTime => _accumulatedTime;

    public virtual void GenerateUpdateCommands(WorldView view, float deltaTime, CommandBuffer commands) { }

    public virtual void DirectMutationUpdate(WorldView view, float deltaTime) { }
}
