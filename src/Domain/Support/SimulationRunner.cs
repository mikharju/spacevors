using Spacevors.Domain.Systems;

namespace Spacevors.Domain;

public sealed class SimulationRunner
{
    public GameSystem[] MovementSystems { get; }
    public GameSystem[] ActionSystems { get; }
    public GameSystem[] ResolutionSystems { get; }
    public GameSystem[] IntentSystems { get; }

    public SimulationRunner()
    {
        MovementSystems = new GameSystem[]
        {
            new PhysicsSystem(),
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

        IntentSystems = new GameSystem[]
        {
            new MineDriftSystem(),
            new MineRespawnSystem(),
            new EnemyShipSystem(),
            new CameraSystem(),
            new DamageEffectSystem()
        };
    }

    public void RunPhase(
        WorldView view,
        CommandBuffer commands,
        GameSystem[] phaseSystems,
        float deltaTime,
        Action<string, long>? onSystemComplete = null)
    {
        // Timing (Stopwatch + type-name strings per system) only runs when a callback is
        // supplied; callers pass null with diagnostics off to keep the hot path allocation-free.
        if (onSystemComplete is null)
        {
            foreach (var system in phaseSystems)
                system.Update(view, deltaTime, commands);
            return;
        }

        foreach (var system in phaseSystems)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            system.Update(view, deltaTime, commands);
            sw.Stop();
            onSystemComplete(system.GetType().Name, sw.ElapsedTicks);
        }
    }
}
