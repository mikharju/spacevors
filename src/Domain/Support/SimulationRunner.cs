using Spacevors.Domain.Systems;

namespace Spacevors.Domain;

public static class SimulationRunner
{
    public static readonly GameSystem[] MovementSystems =
    {
        new PhysicsSystem(),
        new BlueSparkHomeSystem(),
        new PositionIntegrationSystem(),
        new AmmoLifetimeSystem()
    };

    public static readonly GameSystem[] ActionSystems =
    {
        new TurretFiringSystem(),
        new EnemyShipSpawnSystem()
    };

    public static readonly GameSystem[] ResolutionSystems =
    {
        new CollisionSystem(),
        new PickupMagnetSystem(),
        new LevelUpSystem(),
        new EffectSystem()
    };

    public static readonly GameSystem[] CleanupSystems =
    {
        new MineDriftSystem(),
        new MineRespawnSystem(),
        new EnemyShipSystem(),
        new CameraSystem()
    };

    public static void RunPhase(
        WorldView view,
        CommandBuffer commands,
        GameSystem[] phaseSystems,
        float deltaTime,
        Action<string, long>? onSystemComplete = null)
    {
        foreach (var system in phaseSystems)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            system.Update(view, deltaTime, commands);
            sw.Stop();
            onSystemComplete?.Invoke(system.GetType().Name, sw.ElapsedTicks);
        }
    }
}
