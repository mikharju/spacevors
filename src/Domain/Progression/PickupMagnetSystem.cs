using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Every pickup the hull can collect, plus the two field capsules that change what the rest of the field
// does (plans/ECM_PULSE_AND_SALVAGE_FIELD.md).
//
// Two pulls, on purpose:
//  - `PullTowardsPlayer` is the debris pull: accelerating, capped below hull speed, so a capsule that fell
//    inside the magnet radius drifts in like wreckage. Only uncommitted salvage capsules use it.
//  - `ChaseTowardsPlayer` is the committed pull: a constant speed re-aimed at the hull every tick, so a
//    pickup already hunting cannot be outrun. XP and health orbs use it, which is what stops a boosting
//    hull shrugging off a health orb it has already touched.
//
// A committed pickup (`Chased`) also stops aging: it is on its way, and a 90 s clock running out
// mid-flight would read as loot the game stole.
public class PickupMagnetSystem : GameSystem
{
    // Reused on the one tick a salvage field is collected, so the sweep decision allocates nothing.
    private readonly List<float> _healthOrbDistances = new();

    // What a salvage field decided, expressed as radii so each pass below decides with one comparison:
    // infinite for "everything on the field", a threshold for "the N nearest", the plain magnet radius when
    // nothing was collected. A set of chosen entities would have been a second write of the pickups' own
    // components in this phase, and the stale copy these passes write back would have won it
    // (TROUBLE_SHOOTING #58).
    private readonly record struct SalvageSweep(float XpRadius, float HealthRadius, bool PullTokens)
    {
        public static SalvageSweep NoSweep(float pickupRadius) => new(pickupRadius, pickupRadius, false);
    }

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        if (!view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple)) return;
        Entity playerEntity = playerTuple.Entity;
        Player player = playerTuple.Value1;
        Vector2 playerPos = playerTuple.Value2.Value;

        // The field capsules go first: their sweep decision has to be in hand while the loot passes below
        // still write each pickup's component exactly once this tick.
        SalvageSweep sweep = CollectSalvageFields(view, playerEntity, player, playerPos, deltaTime, commands);
        CollectEcmPulses(view, player, playerPos, deltaTime, commands);

        float playerSpeed = view.TryGetComponent<Velocity>(playerEntity, out var playerVel) ? playerVel.Value.Magnitude : 0f;

        ProcessXpPickups(view, playerEntity, player, playerPos, deltaTime, commands, sweep.XpRadius);
        ProcessHealthOrbs(view, playerEntity, player, playerPos, playerSpeed, deltaTime, commands, sweep.HealthRadius);
        ProcessRerollTokens(view, playerEntity, player, playerPos, deltaTime, commands, sweep.PullTokens);
    }

    // XP orbs are the many-entity pass: a constant magnet speed toward the hull rather than an
    // acceleration, which is what makes them grabbable at any ship speed.
    private void ProcessXpPickups(WorldView view, Entity playerEntity, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands, float magnetRadius)
    {
        int totalXpGained = 0;

        foreach (var (pickupEntity, pickup, _) in view.GetEntitiesWithComponents<XpPickup, Position>())
        {
            float newLifetime = pickup.Chased ? pickup.Lifetime : pickup.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(pickupEntity));
                continue;
            }

            float dist = ChaseTowardsPlayer(view, commands, pickupEntity, playerPos, magnetRadius, deltaTime, pickup.Chased, LootStats.CommittedChaseSpeed);

            if (dist < player.Radius + pickup.Radius)
            {
                totalXpGained += pickup.XpAmount;
                commands.Add(new DestroyEntityCommand(pickupEntity));
                continue;
            }

            commands.Add(new AddComponentCommand<XpPickup>(pickupEntity, pickup with { Lifetime = newLifetime, Chased = pickup.Chased || dist <= magnetRadius }));
        }

        if (totalXpGained > 0)
        {
            // Mutate directly so LevelUpSystem (same phase, runs later) reads the fresh value.
            ref Player playerRef = ref view.GetComponentRef<Player>(playerEntity);
            playerRef = playerRef with { Xp = playerRef.Xp + totalXpGained };
        }
    }

    // Health orbs hunt the hull at a speed that matches it, so a hull that boosts away cannot shake one
    // it has already touched — the complaint this pass exists to answer.
    private void ProcessHealthOrbs(WorldView view, Entity playerEntity, Player player, Vector2 playerPos, float playerSpeed, float deltaTime, CommandBuffer commands, float magnetRadius)
    {
        int totalHeal = 0;
        float chaseSpeed = LootStats.HealthOrbChaseSpeedFor(playerSpeed);

        foreach (var (orbEntity, orb, pos) in view.GetEntitiesWithComponents<HealthOrb, Position>())
        {
            float newLifetime = orb.Chased ? orb.Lifetime : orb.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(orbEntity));
                continue;
            }

            float dist = ChaseTowardsPlayer(view, commands, orbEntity, playerPos, magnetRadius, deltaTime, orb.Chased, chaseSpeed);

            if (dist < player.Radius + orb.Radius)
            {
                totalHeal += LootStats.HealthOrbHealAmount;
                SpawnHealSparks(position: pos.Value, commands);
                commands.Add(new DestroyEntityCommand(orbEntity));
                continue;
            }

            commands.Add(new AddComponentCommand<HealthOrb>(orbEntity, orb with { Lifetime = newLifetime, Chased = orb.Chased || dist <= magnetRadius }));
        }

        if (totalHeal > 0)
        {
            var health = view.GetComponent<Health>(playerEntity);
            int healed = Math.Min(health.Current + totalHeal, health.Max);
            commands.Add(new AddComponentCommand<Health>(playerEntity, new Health(healed, health.Max)));
        }
    }

    // Ship salvage: the debris pull, and one charge per capsule collected. The stock is a component of its
    // own rather than a `Player` field — `ProcessXpPickups` ref-writes `Player.Xp` in this phase, so a
    // queued `Player` write built from a pre-phase copy would drop it.
    private void ProcessRerollTokens(WorldView view, Entity playerEntity, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands, bool swept)
    {
        int collected = 0;

        foreach (var (tokenEntity, token, _) in view.GetEntitiesWithComponents<RerollToken, Position>())
        {
            float newLifetime = token.Chased ? token.Lifetime : token.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(tokenEntity));
                continue;
            }

            // A capsule nobody called in is wreckage and drifts; a swept one commits and flies, like
            // everything else a salvage field pulls.
            bool committed = token.Chased || swept;
            float dist = committed
                ? ChaseTowardsPlayer(view, commands, tokenEntity, playerPos, float.PositiveInfinity, deltaTime, committed, LootStats.CommittedChaseSpeed)
                : PullTowardsPlayer(view, commands, tokenEntity, playerPos, player.PickupRadius, deltaTime);

            if (dist < player.Radius + token.Radius)
            {
                collected++;
                commands.Add(new DestroyEntityCommand(tokenEntity));
                continue;
            }

            // The capsule tumbles where it lies. The angle is simulated, not read off a clock, so a
            // seeded run draws the same pose in a screenshot twice; a capsule that fell on a later tick
            // starts turning later, so capsules never sit parallel.
            commands.Add(new AddComponentCommand<RerollToken>(
                tokenEntity, token with { Lifetime = newLifetime, Spin = token.Spin + LootStats.CapsuleSpinRate * deltaTime, Chased = committed }));
        }

        if (collected > 0)
        {
            var stock = view.TryGetComponent<RerollStock>(playerEntity, out var held) ? held : RerollStock.Empty;
            commands.Add(new AddComponentCommand<RerollStock>(playerEntity, stock.With(stock.Count + collected)));
        }
    }

    // A salvage field is spent on the tick the hull touches it: the sweep is decided there and once, so
    // damage taken while the loot is in flight does not call in a second wave. It pulls every XP orb and
    // every currency capsule in the world and only as many of the nearest health orbs as the hull needs to
    // come back to full. An ECM capsule is not currency and is not pulled: it waits where it fell.
    private SalvageSweep CollectSalvageFields(WorldView view, Entity playerEntity, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands)
    {
        SalvageSweep sweep = SalvageSweep.NoSweep(player.PickupRadius);

        foreach (var (capsuleEntity, capsule, _) in view.GetEntitiesWithComponents<SalvageField, Position>())
        {
            float newLifetime = capsule.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(capsuleEntity));
                continue;
            }

            float dist = PullTowardsPlayer(view, commands, capsuleEntity, playerPos, player.PickupRadius, deltaTime);
            if (dist < player.Radius + capsule.Radius)
            {
                commands.Add(new DestroyEntityCommand(capsuleEntity));
                float healthRadius = MathF.Max(player.PickupRadius, HealthSweepRadius(view, playerEntity, playerPos));
                sweep = new SalvageSweep(float.PositiveInfinity, healthRadius, true);
                continue;
            }

            commands.Add(new AddComponentCommand<SalvageField>(
                capsuleEntity, capsule with { Lifetime = newLifetime, Spin = capsule.Spin + LootStats.CapsuleSpinRate * deltaTime }));
        }

        return sweep;
    }

    // How far out a salvage field reaches for health: the distance of the Nth nearest orb, N being what it
    // takes to fill the hull. Orbs past that threshold stay exactly where they are. An orb already hunting
    // the hull counts toward N — it is on its way and will heal — so the field does not over-call; ties at
    // the threshold pull both, which costs one orb of overshoot and is clamped at full hp anyway.
    // -1 means "no health pull": full hull, or nothing on the field to call.
    private float HealthSweepRadius(WorldView view, Entity playerEntity, Vector2 playerPos)
    {
        if (!view.TryGetComponent<Health>(playerEntity, out var health)) return -1f;

        int deficit = health.Max - health.Current;
        if (deficit <= 0) return -1f;

        _healthOrbDistances.Clear();
        foreach (var (_, _, pos) in view.GetEntitiesWithComponents<HealthOrb, Position>())
            _healthOrbDistances.Add((playerPos - pos.Value).Magnitude);

        if (_healthOrbDistances.Count == 0) return -1f;

        int needed = (deficit + LootStats.HealthOrbHealAmount - 1) / LootStats.HealthOrbHealAmount;
        _healthOrbDistances.Sort();
        return _healthOrbDistances[Math.Min(needed, _healthOrbDistances.Count) - 1];
    }

    // The ECM capsule tumbles and flashes where it lies and never expires, so it stays a choice the player
    // has to go and take. Touching it jams the whole world at once and fires the ring out from here.
    private void CollectEcmPulses(WorldView view, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands)
    {
        foreach (var (capsuleEntity, capsule, pos) in view.GetEntitiesWithComponents<EcmPulse, Position>())
        {
            float dist = PullTowardsPlayer(view, commands, capsuleEntity, playerPos, player.PickupRadius, deltaTime);
            if (dist < player.Radius + capsule.Radius)
            {
                commands.Add(new DestroyEntityCommand(capsuleEntity));
                commands.AddEntity(new Position(pos.Value), new EcmWave(EcmStats.WaveMaxRadius, EcmStats.WaveLifetime, EcmStats.WaveLifetime));
                JamTheField(view, commands);
                continue;
            }

            commands.Add(new AddComponentCommand<EcmPulse>(capsuleEntity, capsule with
            {
                Spin = capsule.Spin + LootStats.CapsuleSpinRate * deltaTime,
                Flash = capsule.Flash + LootStats.CapsuleFlashRate * deltaTime,
            }));
        }
    }

    // Every enemy hull and mine in the world gets the jam, and every enemy round in the air loses its
    // seeker. The wander heading is drawn here — once per hull, when the pulse fires — so the systems that
    // read the jam never touch the world RNG, and a seeded run replays the same wander.
    private static void JamTheField(WorldView view, CommandBuffer commands)
    {
        foreach (var (entity, _) in view.GetEntitiesWithComponents<EnemyShip>())
        {
            if (view.HasComponent<Dead>(entity)) continue;
            float heading = (float)(view.Rng.NextDouble() * MathF.Tau);
            commands.Add(new AddComponentCommand<EcmDisabled>(entity, new EcmDisabled(EcmStats.Duration, heading)));
        }

        // A jammed mine stops hunting and stops detonating on contact. It keeps its hp, so a pulse turns a
        // mine field into something the player can shoot clear while it cannot answer.
        foreach (var (entity, _) in view.GetEntitiesWithComponents<EnemyMine>())
            commands.Add(new AddComponentCommand<EcmDisabled>(entity, new EcmDisabled(EcmStats.Duration, 0f)));

        // The chain: each round's fuse is staggered from the last, so a volley pops one after another and
        // the player can count what the pulse took. No RNG draw — the stagger is a position in a list.
        int chain = 0;
        foreach (var (entity, _, ammo) in view.GetEntitiesWithComponents<Missile, Ammo>())
        {
            if (!ammo.IsEnemy) continue;
            commands.Add(new AddComponentCommand<MissileStunned>(entity, new MissileStunned(EcmStats.MissileDetonateDelay + chain++ * EcmStats.MissileDetonateStagger)));
        }
    }

    // A committed pickup closes on the hull at a constant speed and re-aims every tick, so it neither stalls
    // short of a fast hull nor orbits it. `magnetRadius` is the acquisition radius; pass infinity for a
    // salvage field's "from anywhere". It reports the distance so the caller can decide whether the hull has
    // already swallowed the pickup.
    private static float ChaseTowardsPlayer(WorldView view, CommandBuffer commands, Entity pickup, Vector2 playerPos, float magnetRadius, float deltaTime, bool committed, float speed)
    {
        var pos = view.GetComponent<Position>(pickup).Value;
        var diff = playerPos - pos;
        float dist = diff.Magnitude;

        if (dist <= 0f) return 0f;
        if (!committed && dist > magnetRadius) return dist;

        var newVel = diff / dist * speed;
        commands.Add(new AddComponentCommand<Position>(pickup, new Position(pos + newVel * deltaTime)));
        commands.Add(new AddComponentCommand<Velocity>(pickup, new Velocity(newVel)));
        return dist;
    }

    // Accelerates one capsule toward the hull while it is inside the magnet radius, and reports the
    // distance so the caller can decide whether it has been collected. This is the wreckage pull: capped
    // below hull speed on purpose, so loot that nobody called in still has to be flown to.
    private static float PullTowardsPlayer(WorldView view, CommandBuffer commands, Entity pickup, Vector2 playerPos, float magnetRadius, float deltaTime)
    {
        var pos = view.GetComponent<Position>(pickup);
        var diff = playerPos - pos.Value;
        float dist = (float)Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);

        if (dist <= 0f || dist >= magnetRadius) return dist;

        Vector2 currentVel = view.TryGetComponent<Velocity>(pickup, out var vel) ? vel.Value : Vector2.Zero;
        var newVel = currentVel + (diff / dist) * LootStats.MagnetAcceleration * deltaTime;

        float speed = (float)Math.Sqrt(newVel.X * newVel.X + newVel.Y * newVel.Y);
        if (speed > LootStats.MaxMagnetSpeed) newVel = newVel / speed * LootStats.MaxMagnetSpeed;

        commands.Add(new AddComponentCommand<Position>(pickup, new Position(pos.Value + newVel * deltaTime)));
        commands.Add(new AddComponentCommand<Velocity>(pickup, new Velocity(newVel)));
        return dist;
    }

    private void SpawnHealSparks(Vector2 position, CommandBuffer commands)
    {
        for (int i = 0; i < 6; i++)
        {
            float angleOffset = ((float)i - 2.5f) * 0.3f;
            var dir = new Vector2(0f, -1f);
            float cos = (float)Math.Cos(angleOffset);
            float sin = (float)Math.Sin(angleOffset);
            var sparkDir = new Vector2(dir.X * cos - dir.Y * sin, dir.X * sin + dir.Y * cos);

            float speed = 80f + i * 25f;
            var velocity = sparkDir * speed;

            commands.AddEntity(new Position(position), new Velocity(velocity), new HealSpark(0.6f));
        }
    }
}
