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
            new EnemyMissileFiringSystem(),
            new EnemyShipSpawnSystem()
        };

        ResolutionSystems = new GameSystem[]
        {
            new CollisionSystem(),
            new PickupMagnetSystem(),
            // Right after the magnet, which is what applies an ECM jam: a phase reads pre-phase state, so
            // a capsule collected this tick is jammed for its full duration (plans/ECM_PULSE_AND_SALVAGE_FIELD.md).
            new EcmSystem(),
            new LevelUpSystem(),
            new ShipDeathExplosionSystem(),
            new EffectSystem()
        };

        IntentSystems = new GameSystem[]
        {
            // First: it decides next tick's heading for every missile, and the round must move by it
            // before the collision sweep reads its velocity.
            new MissileGuidanceSystem(),
            new MineDriftSystem(),
            new MineRespawnSystem(),
            new EnemyShipSystem(),
            new CameraSystem(),
            new DamageEffectSystem(),
            // Last, deliberately: it refills the pool, so it has to see the pool as CollisionSystem left
            // it this tick. Anywhere earlier it would write a pre-hit copy back over the spend
            // (TROUBLE_SHOOTING #58). See ShieldSystem.
            new ShieldSystem()
        };
    }

    // One fixed-timestep tick: the four phases in contractual order, each followed by the command
    // flush. Timing callbacks are optional; callers must not reorder the phases.
    public void RunTick(
        WorldView view,
        EntityManager em,
        CommandBuffer commands,
        float deltaTime,
        Action<string, long>? onSystemComplete = null,
        Action<string, long>? onApplyComplete = null)
    {
        RunPhaseAndApply(view, em, commands, MovementSystems, "Movement", deltaTime, onSystemComplete, onApplyComplete);
        RunPhaseAndApply(view, em, commands, ActionSystems, "Action", deltaTime, onSystemComplete, onApplyComplete);
        RunPhaseAndApply(view, em, commands, ResolutionSystems, "Resolution", deltaTime, onSystemComplete, onApplyComplete);
        RunPhaseAndApply(view, em, commands, IntentSystems, "Intent", deltaTime, onSystemComplete, onApplyComplete);
    }

    private void RunPhaseAndApply(
        WorldView view,
        EntityManager em,
        CommandBuffer commands,
        GameSystem[] phaseSystems,
        string phaseName,
        float deltaTime,
        Action<string, long>? onSystemComplete,
        Action<string, long>? onApplyComplete)
    {
        RunPhase(view, commands, phaseSystems, deltaTime, onSystemComplete);

        if (onApplyComplete is null)
        {
            commands.Apply(em);
            return;
        }

        var applySw = System.Diagnostics.Stopwatch.StartNew();
        commands.Apply(em);
        applySw.Stop();
        onApplyComplete(phaseName, applySw.ElapsedTicks);
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
