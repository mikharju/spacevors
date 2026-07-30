using System.Diagnostics;
using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class AmmoLifetimeSystem : GameSystem
{
    public override void DirectMutationUpdate(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();

        if (!view.TryGetStorage(out ComponentStorage<Ammo>? ammoStorage))
            return;

        for (int i = 0; i < ammoStorage.Count; i++)
        {
            Entity entity = ammoStorage.GetEntity(i);
            ref Ammo ammo = ref ammoStorage.GetComponent(i);

            var newLifetime = ammo.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(entity));
            }
            else
            {
                ammo = new Ammo(ammo.Velocity, ammo.Radius, newLifetime, ammo.IsEnemy, ammo.Damage);
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("Ammo: lifetime", sw.ElapsedTicks, ammoStorage.Count);
    }
}
