using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

// Emits smoke puffs and sparks from damaged ships (PLAN.md "Graphical damage indicators").
public class DamageEffectSystem : GameSystem
{
    // Emission priority tiers.
    private const int PlayerTier = 0;
    private const int ManualTargetTier = 1;
    private const int FreshAutoMarkTier = 2;
    private const int OtherTier = 3;

    // hp ratio below which a ship starts emitting smoke puffs.
    private const float LightSmokeHpRatio = 2f / 3f;

    // hp ratio below which emission intensifies and sparks are added.
    private const float HeavySmokeHpRatio = 1f / 3f;

    // Expected particles per second at each tier.
    private const float SmokeRateLightPerSecond = 2f;
    private const float SmokeRateHeavyPerSecond = 6f;
    private const float SparkRateHeavyPerSecond = 3f;

    // Per-tick spawn budget, allocated in priority order. Particles live exactly one second, so the
    // steady-state live count is bounded by budget x ticks-per-second - no separate culling pass needed.
    private const int MaxSmokePerTick = 1;
    private const int MaxSparksPerTick = 1;

    // Requirement: smoke and sparks fade away after 1 second.
    private const float EffectLifetime = 1f;

    // Puffs inherit this fraction of the ship's velocity, so they trail behind moving ships.
    private const float FollowFactor = 0.5f;

    // Random per-axis jitter added to puff velocity at spawn (px/s).
    private const float PuffJitterSpeed = 20f;

    // Spark speed range (px/s).
    private const float SparkSpeedMin = 60f;
    private const float SparkSpeedMax = 140f;

    // Ships within this many pixels past the viewport edge still emit, so smoke does not pop at screen borders.
    private const float EmitMargin = 100f;

    // Puff radius scales with hull size, clamped to keep Heavy puffs from becoming blobs.
    private const float PuffRadiusFactor = 0.35f;
    private const float MinPuffRadius = 4f;
    private const float MaxPuffRadius = 14f;

    // A damaged ship that may emit this tick, with everything emission needs.
    public readonly record struct DamageCandidate(
        Entity Entity, Vector2 Position, Vector2 Velocity, float HpRatio, int Tier, float DistanceToCamera, float SourceRadius);

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        var candidates = OrderCandidates(view);
        if (candidates.Count == 0) return;

        int smokeBudget = MaxSmokePerTick;
        int sparkBudget = MaxSparksPerTick;

        foreach (var candidate in candidates)
        {
            bool heavy = candidate.HpRatio < HeavySmokeHpRatio;

            if (heavy && sparkBudget > 0 && view.Rng.NextDouble() < SparkRateHeavyPerSecond * deltaTime)
            {
                SpawnSpark(commands, candidate, view.Rng);
                sparkBudget--;
            }

            float smokeRate = heavy ? SmokeRateHeavyPerSecond : SmokeRateLightPerSecond;
            if (smokeBudget > 0 && view.Rng.NextDouble() < smokeRate * deltaTime)
            {
                SpawnSmokePuff(commands, candidate, view.Rng);
                smokeBudget--;
            }

            if (smokeBudget == 0 && sparkBudget == 0) break;
        }
    }

    // Collects visible, non-dead, damaged ships and orders them by emission priority:
    // player > live manual target > fresh auto-mark > others; ties broken by distance to camera, then entity id.
    public static List<DamageCandidate> OrderCandidates(WorldView view)
    {
        var candidates = new List<DamageCandidate>();

        Vector2 camTarget = GetCameraTarget(view);
        float halfWidth = view.ViewportSize.X * 0.5f + EmitMargin;
        float halfHeight = view.ViewportSize.Y * 0.5f + EmitMargin;

        Entity? manualTarget = null;
        if (view.GetEntitiesWithComponents<Player, PrimaryTarget>().TryFirst(out var playerTuple))
            manualTarget = playerTuple.Value2.Target;

        foreach (var (entity, _, pos) in view.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            var candidate = TryGetCandidate(view, entity, pos.Value, camTarget, halfWidth, halfHeight);
            if (candidate is null) continue;

            int tier = manualTarget.HasValue && entity == manualTarget.Value ? ManualTargetTier : GetAutoMarkTier(view, entity);
            candidates.Add(candidate.Value with { Tier = tier });
        }

        if (view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var posTuple))
        {
            var candidate = TryGetCandidate(view, posTuple.Entity, posTuple.Value2.Value, camTarget, halfWidth, halfHeight);
            if (candidate is not null)
                candidates.Add(candidate.Value with { Tier = PlayerTier });
        }

        candidates.Sort(CompareCandidates);
        return candidates;
    }

    private static Vector2 GetCameraTarget(WorldView view)
        => view.GetEntitiesWithComponents<Camera>().TryFirst(out var camTuple) ? camTuple.Value1.Target : Vector2.Zero;

    // Returns null when the ship is dead, undamaged, or outside the viewport plus margin.
    private static DamageCandidate? TryGetCandidate(
        WorldView view, Entity entity, Vector2 position, Vector2 camTarget, float halfWidth, float halfHeight)
    {
        if (view.HasComponent<Dead>(entity)) return null;
        if (!view.TryGetComponent<Health>(entity, out var health)) return null;

        float hpRatio = health.Current / (float)health.Max;
        if (hpRatio >= LightSmokeHpRatio) return null;

        if (Math.Abs(position.X - camTarget.X) > halfWidth || Math.Abs(position.Y - camTarget.Y) > halfHeight) return null;

        Vector2 velocity = view.TryGetComponent<Velocity>(entity, out var vel) ? vel.Value : Vector2.Zero;
        float distance = (position - camTarget).Magnitude;
        float sourceRadius = GetSourceRadius(view, entity);

        return new DamageCandidate(entity, position, velocity, hpRatio, OtherTier, distance, sourceRadius);
    }

    private static int GetAutoMarkTier(WorldView view, Entity entity)
    {
        if (!view.TryGetComponent<AutoTargetMark>(entity, out var mark)) return OtherTier;
        return view.ElapsedTime - mark.LastTargetedAt <= AutoTargetMark.FreshWindow ? FreshAutoMarkTier : OtherTier;
    }

    private static float GetSourceRadius(WorldView view, Entity entity)
    {
        if (view.TryGetComponent<EnemyShip>(entity, out var ship)) return ship.Radius;
        if (view.TryGetComponent<Player>(entity, out var player)) return player.Radius;
        return MinPuffRadius;
    }

    private static int CompareCandidates(DamageCandidate a, DamageCandidate b)
    {
        if (a.Tier != b.Tier) return a.Tier.CompareTo(b.Tier);
        if (a.DistanceToCamera != b.DistanceToCamera) return a.DistanceToCamera.CompareTo(b.DistanceToCamera);
        return a.Entity.Value.CompareTo(b.Entity.Value);
    }

    private static void SpawnSmokePuff(CommandBuffer commands, DamageCandidate candidate, Random rng)
    {
        Vector2 offset = new Vector2(
            (float)(rng.NextDouble() * 2f - 1f),
            (float)(rng.NextDouble() * 2f - 1f)) * candidate.SourceRadius;

        Vector2 jitter = new Vector2(
            (float)(rng.NextDouble() * 2f - 1f) * PuffJitterSpeed,
            (float)(rng.NextDouble() * 2f - 1f) * PuffJitterSpeed);

        float radius = Math.Clamp(candidate.SourceRadius * PuffRadiusFactor, MinPuffRadius, MaxPuffRadius);
        Vector2 velocity = candidate.Velocity * FollowFactor + jitter;

        commands.AddEntity(
            new Position(candidate.Position + offset),
            new Velocity(velocity),
            new SmokePuff(EffectLifetime, EffectLifetime, radius));
    }

    private static void SpawnSpark(CommandBuffer commands, DamageCandidate candidate, Random rng)
    {
        Vector2 offset = new Vector2(
            (float)(rng.NextDouble() * 2f - 1f),
            (float)(rng.NextDouble() * 2f - 1f)) * candidate.SourceRadius;

        float angle = (float)(rng.NextDouble() * MathF.PI * 2f);
        float speed = SparkSpeedMin + (float)rng.NextDouble() * (SparkSpeedMax - SparkSpeedMin);
        Vector2 velocity = new Vector2((float)MathF.Cos(angle), (float)MathF.Sin(angle)) * speed + candidate.Velocity * FollowFactor;

        commands.AddEntity(
            new Position(candidate.Position + offset),
            new Velocity(velocity),
            new DamageSpark(EffectLifetime, EffectLifetime));
    }
}
