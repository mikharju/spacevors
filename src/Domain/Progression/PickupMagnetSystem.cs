using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Every pickup the hull can collect, plus the two field capsules that change what the rest of the field
// does (plans/ECM_PULSE_AND_SALVAGE_FIELD.md).
//
// One tracking rule for every pickup, orbs and capsules alike: it lies where it fell until the hull comes
// inside its range, then it commits (`Chased`) and closes at `LootStats.ChaseSpeedFor`, re-aimed at the hull
// every tick. The commit is one-way. A capsule that only accelerated and topped out under hull speed could be
// outflown by a boosting hull right after being touched: it coasted off in whatever direction it was already
// going, stopped being pulled once the hull left the magnet radius, and was lost. That is what this rule
// rules out — no pickup escapes a hull that has been inside its range even briefly.
//
// A committed pickup also stops aging: it is on its way, and a 90 s clock running out mid-flight would read
// as loot the game stole.
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

        float playerSpeed = view.TryGetComponent<Velocity>(playerEntity, out var playerVel) ? playerVel.Value.Magnitude : 0f;
        float chaseSpeed = LootStats.ChaseSpeedFor(playerSpeed);

        // The field capsules go first: their sweep decision has to be in hand while the loot passes below
        // still write each pickup's component exactly once this tick.
        SalvageSweep sweep = CollectSalvageFields(view, playerEntity, player, playerPos, deltaTime, commands, chaseSpeed);
        CollectEcmPulses(view, player, playerPos, deltaTime, commands, chaseSpeed);

        ProcessXpPickups(view, playerEntity, player, playerPos, deltaTime, commands, sweep.XpRadius, chaseSpeed);
        ProcessHealthOrbs(view, playerEntity, player, playerPos, deltaTime, commands, sweep.HealthRadius, chaseSpeed);
        ProcessRerollTokens(view, playerEntity, player, playerPos, deltaTime, commands, sweep.PullTokens, chaseSpeed);
    }

    // The many-entity pass: a constant speed toward the hull rather than an acceleration, which is what
    // makes an orb grabbable at any ship speed.
    private void ProcessXpPickups(WorldView view, Entity playerEntity, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands, float magnetRadius, float chaseSpeed)
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

            float dist = ChaseTowardsPlayer(view, commands, pickupEntity, playerPos, magnetRadius, deltaTime, pickup.Chased, chaseSpeed);

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

    // Health orbs use the same rule as every other pickup; what they add is the heal and the sparks.
    private void ProcessHealthOrbs(WorldView view, Entity playerEntity, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands, float magnetRadius, float chaseSpeed)
    {
        int totalHeal = 0;

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

    // Ship salvage: one charge per capsule collected. The stock is a component of its
    // own rather than a `Player` field — `ProcessXpPickups` ref-writes `Player.Xp` in this phase, so a
    // queued `Player` write built from a pre-phase copy would drop it.
    private void ProcessRerollTokens(WorldView view, Entity playerEntity, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands, bool swept, float chaseSpeed)
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

            // A capsule the hull has never been near lies where it fell; one the hull has touched, or a
            // salvage field called in, commits and closes on the hull like every other pickup.
            bool committed = token.Chased || swept;
            float dist = ChaseTowardsPlayer(view, commands, tokenEntity, playerPos, player.PickupRadius, deltaTime, committed, chaseSpeed);

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
                tokenEntity, token with { Lifetime = newLifetime, Spin = token.Spin + LootStats.CapsuleSpinRate * deltaTime, Chased = committed || dist <= player.PickupRadius }));
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
    // come back to full. An ECM capsule is not currency and is never swept: it waits where it fell until the
    // hull comes close enough to take it itself.
    private SalvageSweep CollectSalvageFields(WorldView view, Entity playerEntity, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands, float chaseSpeed)
    {
        SalvageSweep sweep = SalvageSweep.NoSweep(player.PickupRadius);

        foreach (var (capsuleEntity, capsule, _) in view.GetEntitiesWithComponents<SalvageField, Position>())
        {
            float newLifetime = capsule.Chased ? capsule.Lifetime : capsule.Lifetime - deltaTime;
            if (newLifetime <= 0f)
            {
                commands.Add(new DestroyEntityCommand(capsuleEntity));
                continue;
            }

            float dist = ChaseTowardsPlayer(view, commands, capsuleEntity, playerPos, player.PickupRadius, deltaTime, capsule.Chased, chaseSpeed);
            if (dist < player.Radius + capsule.Radius)
            {
                commands.Add(new DestroyEntityCommand(capsuleEntity));
                float healthRadius = MathF.Max(player.PickupRadius, HealthSweepRadius(view, playerEntity, playerPos));
                sweep = new SalvageSweep(float.PositiveInfinity, healthRadius, true);
                continue;
            }

            commands.Add(new AddComponentCommand<SalvageField>(capsuleEntity, capsule with
            {
                Lifetime = newLifetime,
                Spin = capsule.Spin + LootStats.CapsuleSpinRate * deltaTime,
                Chased = capsule.Chased || dist <= player.PickupRadius,
            }));
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
    // has to go and take — until the hull comes inside its range, after which it closes on the hull like
    // every other pickup. Touching it jams the whole world at once and fires the ring out from here.
    private void CollectEcmPulses(WorldView view, Player player, Vector2 playerPos, float deltaTime, CommandBuffer commands, float chaseSpeed)
    {
        foreach (var (capsuleEntity, capsule, pos) in view.GetEntitiesWithComponents<EcmPulse, Position>())
        {
            float dist = ChaseTowardsPlayer(view, commands, capsuleEntity, playerPos, player.PickupRadius, deltaTime, capsule.Chased, chaseSpeed);
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
                Chased = capsule.Chased || dist <= player.PickupRadius,
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

    // The one motion every pickup makes, and the only one: an uncommitted pickup outside `magnetRadius` is
    // left exactly where it lies, and anything else closes on the hull at a constant speed re-aimed every
    // tick, so it neither stalls short of a fast hull nor orbits it. Pass an infinite radius for a salvage
    // field's "from anywhere". It reports the distance so the caller can decide whether the hull has already
    // swallowed the pickup, and whether this tick is the one that commits it.
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
