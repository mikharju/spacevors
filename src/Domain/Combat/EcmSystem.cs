using System.Diagnostics;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// The ECM jam's own clock (plans/ECM_PULSE_AND_SALVAGE_FIELD.md). It runs right after
// `PickupMagnetSystem`, which is what puts these two components on entities: because a phase reads
// pre-phase state, a capsule collected this tick is jammed for its full `Duration` and its rounds get
// their full fuses, with nothing aged twice.
//
// What the components *mean* lives with the systems that read them — no thrust in `EnemyShipSystem`, no
// shot in the firing systems, no seeker in `MissileGuidanceSystem`, a bounce instead of a detonation in
// `CollisionSystem`. This system only keeps time and pops the stunned rounds.
public class EcmSystem : GameSystem
{
    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var sw = Stopwatch.StartNew();
        int expired = 0;

        foreach (var (entity, disabled) in view.GetEntitiesWithComponents<EcmDisabled>())
        {
            float remaining = disabled.TimeRemaining - deltaTime;
            if (remaining <= 0f)
            {
                commands.Add(new RemoveComponentCommand<EcmDisabled>(entity));
                expired++;
            }
            else
            {
                commands.Add(new AddComponentCommand<EcmDisabled>(entity, disabled with { TimeRemaining = remaining }));
            }
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("ECM: jam timers", sw.ElapsedTicks, expired);

        sw.Restart();
        int detonated = 0;

        foreach (var (entity, stunned, pos) in view.GetEntitiesWithComponents<MissileStunned, Position>())
        {
            float remaining = stunned.DetonateIn - deltaTime;
            if (remaining > 0f)
            {
                commands.Add(new AddComponentCommand<MissileStunned>(entity, stunned with { DetonateIn = remaining }));
                continue;
            }

            // The round is spent where it stands, with the same shoot-down visual a player round gets.
            // The explosion is a light and nothing else: it carries no Health, so it cannot hurt the hull
            // it went off beside.
            commands.Add(new DestroyEntityCommand(entity));
            commands.AddEntity(new Position(pos.Value), new Explosion(EcmStats.DetonationRadius, EcmStats.DetonationLifetime, EcmStats.DetonationLifetime));
            detonated++;
        }

        sw.Stop();
        DiagnosticLogger.LogSystem("ECM: stunned rounds", sw.ElapsedTicks, detonated);
    }
}
