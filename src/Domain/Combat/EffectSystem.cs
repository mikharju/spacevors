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
