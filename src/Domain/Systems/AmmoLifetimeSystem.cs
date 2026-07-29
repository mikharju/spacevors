using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class AmmoLifetimeSystem : GameSystem
{
    public override void GenerateUpdateCommands(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        foreach (var (entity, ammo) in view.GetEntitiesWithComponents<Ammo>())
        {
            var newLifetime = ammo.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                commands.Add(new AddComponentCommand<Ammo>(entity, new Ammo(ammo.Velocity, ammo.Radius, newLifetime, ammo.IsEnemy, ammo.Damage)));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Ammo: lifetime", sw.ElapsedTicks);
    }
}
