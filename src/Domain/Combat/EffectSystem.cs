using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EffectSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        foreach (var (entity, spark) in view.GetEntitiesWithComponents<DamageSpark>())
        {
            var newLifetime = spark.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<DamageSpark>(entity, new DamageSpark(newLifetime, spark.InitialLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: damage spark", sw.ElapsedTicks);

        sw.Restart();

        // The pieces of a broken shield field (plans/SHIELDS.md §7). They move, so PositionSystem runs
        // them; only their lifetime is ticked here, exactly as with a spark.
        foreach (var (entity, shard) in view.GetEntitiesWithComponents<ShieldShard>())
        {
            var newLifetime = shard.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<ShieldShard>(entity, new ShieldShard(newLifetime, shard.InitialLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: shield shard", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, impact) in view.GetEntitiesWithComponents<BeamImpact>())
        {
            var newLifetime = impact.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<BeamImpact>(entity, new BeamImpact(impact.Radius, impact.Color, newLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: beam impact", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, explosion) in view.GetEntitiesWithComponents<Explosion>())
        {
            var newLifetime = explosion.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<Explosion>(entity, new Explosion(explosion.Radius, newLifetime, explosion.InitialLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: explosion", sw.ElapsedTicks);

        sw.Restart();

        // The ECM pulse's ring: it grows out to its max radius as it fades, the inverse of the explosion
        // above, so the component holds the far radius and the renderer reads `CurrentRadius`.
        foreach (var (entity, wave) in view.GetEntitiesWithComponents<EcmWave>())
        {
            var newLifetime = wave.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<EcmWave>(entity, wave with { Lifetime = newLifetime }));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: ecm wave", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, spark) in view.GetEntitiesWithComponents<HealSpark>())
        {
            var newLifetime = spark.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<HealSpark>(entity, new HealSpark(newLifetime, spark.InitialLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: heal spark", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, puff) in view.GetEntitiesWithComponents<SmokePuff>())
        {
            var newLifetime = puff.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<SmokePuff>(entity, new SmokePuff(newLifetime, puff.InitialLifetime, puff.Radius)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: smoke puff", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, beam) in view.GetEntitiesWithComponents<LaserBeam>())
        {
            // CollisionSystem resolves beams in this same phase and rewrites their component (hit
            // length, DamagePending cleared). Commands apply once per phase, so writing this
            // pre-resolution copy back would undo that: the beam would draw at full length, damage
            // again every tick, and flash its impact glow again every tick. An unresolved beam is
            // left alone; its lifetime starts ticking the next tick.
            if (beam.DamagePending) continue;

            var newLifetime = beam.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<LaserBeam>(entity, beam with { Lifetime = newLifetime }));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: laser beam", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, marker) in view.GetEntitiesWithComponents<DebugMarker>())
        {
            var newLifetime = marker.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<DebugMarker>(entity, new DebugMarker(newLifetime, marker.InitialLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: debug marker", sw.ElapsedTicks);
    }
}
