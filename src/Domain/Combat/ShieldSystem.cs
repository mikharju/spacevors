using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

// The shield's own timers (plans/SHIELDS.md): the shock counts down, the pool climbs back at
// ShieldRules.RechargeRate, and the two hull-borne flashes expire.
//
// It runs **last in the Intent phase**, and that placement is load-bearing. In Resolution it would read
// the Shield component as it was before CollisionSystem spent it and queue a write of that stale copy,
// which lands after the collision write in the same flush and undoes the hit (TROUBLE_SHOOTING #58 —
// the same trap the beam lifetime avoids by skipping unresolved beams). Running after the flush that
// applied the spend, it sees the pool as combat left it.
//
// The pool is a float so a sub-point-per-tick rate accumulates without a second counter to drift; a
// full pool writes nothing at all, so a shielded hull at rest costs one component read per tick.
public class ShieldSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        foreach (var (entity, shield) in view.GetEntitiesWithComponents<Shield>())
        {
            if (shield.Points >= shield.Max) continue;

            if (shield.Offline > 0f)
            {
                commands.Add(new AddComponentCommand<Shield>(entity,
                    shield with { Offline = MathF.Max(0f, shield.Offline - deltaTime) }));
                continue;
            }

            commands.Add(new AddComponentCommand<Shield>(entity, shield with
            {
                Points = MathF.Min(shield.Max, shield.Points + ShieldRules.RechargeRate * deltaTime)
            }));
        }

        foreach (var (entity, flash) in view.GetEntitiesWithComponents<ShieldFlash>())
        {
            if (flash.Lifetime - deltaTime <= 0f) commands.Add(new RemoveComponentCommand<ShieldFlash>(entity));
            else commands.Add(new AddComponentCommand<ShieldFlash>(entity, flash with { Lifetime = flash.Lifetime - deltaTime }));
        }

        foreach (var (entity, burst) in view.GetEntitiesWithComponents<ShieldBurst>())
        {
            if (burst.Lifetime - deltaTime <= 0f) commands.Add(new RemoveComponentCommand<ShieldBurst>(entity));
            else commands.Add(new AddComponentCommand<ShieldBurst>(entity, burst with { Lifetime = burst.Lifetime - deltaTime }));
        }
    }
}
