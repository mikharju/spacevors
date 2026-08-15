using Spacevors.Domain.Systems;

namespace Spacevors.Domain;

public sealed class SimulationRunner
{
    public GameSystem[] MovementSystems { get; }
    public GameSystem[] ActionSystems { get; }
    public GameSystem[] ResolutionSystems { get; }
    public GameSystem[] CleanupSystems { get; }

    public SimulationRunner()
    {
        MovementSystems = new GameSystem[]
        {
            new PhysicsSystem(),
            new BlueSparkHomeSystem(),
            new PositionIntegrationSystem(),
            new AmmoLifetimeSystem()
        };

        ActionSystems = new GameSystem[]
        {
            new TurretFiringSystem(),
            new EnemyShipSpawnSystem()
        };

        ResolutionSystems = new GameSystem[]
        {
            new CollisionSystem(),
            new PickupMagnetSystem(),
            new LevelUpSystem(),
            new ShipDeathExplosionSystem(),
            new EffectSystem()
        };

        CleanupSystems = new GameSystem[]
        {
            new MineDriftSystem(),
            new MineRespawnSystem(),
            new EnemyShipSystem(),
            new CameraSystem()
        };
    }

    public void RunPhase(
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
