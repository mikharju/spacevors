using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// What the enemy manœuvres actually **delivered** over a run, as hull-seconds
// (plans/ENEMY_BEHAVIORS.md follow-up). The armament ladder is arithmetic: it says what share of spawns is
// dealt a behaviour. This says what the player met, because a behaviour dealt at 10 % of spawns and killed
// in two seconds is not a formation — and the only way to find that out is to time it.
//
// Sampled, not per frame: `Sample` integrates what it saw since its own previous call, so the caller's
// cadence sets the accuracy and the cost. Read-only over the world — it writes no state and draws no rng,
// so it cannot change what a seed replays.
public sealed class BehaviourTally
{
    public static readonly int BehaviorCount = Enum.GetValues<EnemyBehavior>().Length;

    // Indexed by (int)EnemyBehavior, so the writer and the reader cannot drift apart.
    public readonly int[] Live = new int[BehaviorCount];
    public readonly int[] OnScreen = new int[BehaviorCount];
    public readonly float[] AliveSeconds = new float[BehaviorCount];
    public readonly float[] OnScreenSeconds = new float[BehaviorCount];
    public readonly float[] PeakLive = new float[BehaviorCount];
    public readonly int[] Seen = new int[BehaviorCount];

    // A hull counts as new when its entity was not in the previous sample of the same behaviour. Entity ids
    // are recycled without a generation, so a hull that dies and is replaced by the same behaviour inside
    // one sample gap is counted once: `Seen` is a floor, never an overcount.
    private HashSet<Entity>[] _previous = NewSets();
    private HashSet<Entity>[] _current = NewSets();

    private float _lastSampleAt = -1f;

    public void Sample(WorldView view)
    {
        // The world's own clock, so the tally measures game seconds whether the caller runs at 120 fps or
        // at a stalled 4. The first sample only establishes the baseline.
        float now = view.ElapsedTime;
        float dt = _lastSampleAt < 0f ? 0f : now - _lastSampleAt;
        _lastSampleAt = now;

        Array.Clear(Live);
        Array.Clear(OnScreen);
        foreach (HashSet<Entity> set in _current) set.Clear();

        // Without a player there is no camera, so the on-screen box has no centre; a run whose hull has
        // burst has nothing left to meet anyway, and its death animation should not be counted as play.
        bool hasCamera = view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple);
        Vector2 playerPos = hasCamera ? view.GetComponent<Position>(playerTuple.Entity).Value : Vector2.Zero;
        var half = view.ViewportSize * 0.5f;

        foreach (var (entity, ship, position) in view.GetEntitiesWithComponents<EnemyShip, Position>())
        {
            // A wreck still carries its EnemyShip row while its explosion plays. It is not manœuvring.
            if (view.HasComponent<Dead>(entity)) continue;

            int behaviour = (int)ship.Behavior;
            Live[behaviour]++;
            _current[behaviour].Add(entity);

            // The same box SpawnPlacement spawns outside of, centred on the player as the camera is: a
            // flanker that has not arrived yet is alive but not yet something the player has met.
            Vector2 offset = position.Value - playerPos;
            if (hasCamera && Math.Abs(offset.X) <= half.X && Math.Abs(offset.Y) <= half.Y) OnScreen[behaviour]++;
        }

        for (int behaviour = 0; behaviour < BehaviorCount; behaviour++)
        {
            AliveSeconds[behaviour] += Live[behaviour] * dt;
            OnScreenSeconds[behaviour] += OnScreen[behaviour] * dt;
            if (Live[behaviour] > PeakLive[behaviour]) PeakLive[behaviour] = Live[behaviour];

            foreach (Entity entity in _current[behaviour])
                if (!_previous[behaviour].Contains(entity)) Seen[behaviour]++;
        }

        // The sets trade places instead of copying: each one is cleared at the top of the next sample, so a
        // sample allocates nothing once the run has warmed up.
        (_previous, _current) = (_current, _previous);
    }

    static HashSet<Entity>[] NewSets()
    {
        var sets = new HashSet<Entity>[BehaviorCount];
        for (int i = 0; i < BehaviorCount; i++) sets[i] = new HashSet<Entity>();
        return sets;
    }
}
