using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class EffectSystem : GameSystem
{
    public override void GenerateUpdateCommands(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        foreach (var (entity, spark) in view.GetEntitiesWithComponents<Spark>())
        {
            var newLifetime = spark.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<Spark>(entity, new Spark(newLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: spark", sw.ElapsedTicks);

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
                commands.Add(new AddComponentCommand<Explosion>(entity, new Explosion(explosion.Radius, newLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: explosion", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, spark) in view.GetEntitiesWithComponents<GreenSpark>())
        {
            var newLifetime = spark.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<GreenSpark>(entity, new GreenSpark(newLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: green spark", sw.ElapsedTicks);

        sw.Restart();

        foreach (var (entity, spark) in view.GetEntitiesWithComponents<BlueSpark>())
        {
            var newLifetime = spark.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<BlueSpark>(entity, new BlueSpark(newLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: blue spark", sw.ElapsedTicks);

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
                commands.Add(new AddComponentCommand<DebugMarker>(entity, new DebugMarker(newLifetime)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Effects: debug marker", sw.ElapsedTicks);
    }
}
