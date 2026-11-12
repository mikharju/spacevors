namespace Spacevors.Domain.Stats;

// Difficulty and spawn tuning for enemy content (plans/DIFFICULTY_SCALING.md).
public static class SpawningStats
{
    // World contents at game start (GameInitializer).
    public static class InitialWorld
    {
        public const int MineCount = 9;
        public const int EnemyShipCount = 6;

        // Initial ships spawn in a band around the player so they arrive one at a time over ~90 s. The
        // band's far edge must leave real slack against Chase.CullDistance: the cull is measured from the
        // player every tick, so a ship placed near it dies the moment the player runs the other way —
        // which deletes the staggered opening, and the XP in it. The slack here is ~6 s of running away
        // at 300 px/s, asserted by InitialSpawnBandTest.
        public const float EnemyShipMinDistance = 2400f;
        public const float EnemyShipMaxDistance = 3800f;
    }

    // Enemy ship spawning (EnemyShipSpawnSystem). Cadence is the wave clock (WaveStats/WaveSchedule);
    // what stays here is the per-spawn placement and velocity rule set.
    public static class EnemyShips
    {
        // Hard cap on live enemy ships before spawning pauses.
        public const int MaxEnemyShips = 100;

        // Minimum distance from any existing ship to a new spawn point.
        public const float MinSpawnDistance = 300f;

        // Initial velocity follows the player by this fraction, plus drift toward the player.
        public const float FollowFactor = 0.5f;

        // Threat density per unit distance stays roughly constant at any speed (P3): a fast player covers
        // more ground per wave, so waves bring proportionally more ships. The factor scales the wave's
        // quota (EnemyShipSpawnSystem.WaveQuota), not the in-wave interval — speed has to buy more ships,
        // not the same ships sooner and then a dead window.
        public const float ReferenceSpeed = 100f;
        public const float MaxSpeedFactor = 3f;
    }

    // Mine respawning (MineRespawnSystem). Paused during the opening and full lulls; keeps running
    // (slowly) through quiet gaps.
    public static class Mines
    {
        // Respawn interval ramps from random [StartMinInterval, StartMaxInterval] s up to random
        // [LateMinInterval, LateMaxInterval] s over RampDuration, then stays at the late interval: the
        // interval grows, so mine pressure falls over the ramp rather than rising. The direction is
        // pinned by MineRespawnIntervalTest.
        public const float StartMinInterval = 4f;
        public const float StartMaxInterval = 8f;
        public const float LateMinInterval = 10f;
        public const float LateMaxInterval = 20f;

        // Time over which the interval ramp completes.
        public const float RampDuration = 180f;

        // Hard cap on live mines before respawning pauses.
        public const int MaxMines = 23;
    }

    // Enemy ship chase behavior (EnemyShipSystem).
    public static class Chase
    {
        // Ships farther than this from the player are culled: stale tailers would otherwise persist forever,
        // count against the spawn cap, and thin out fresh threats (P1).
        public const float CullDistance = 5500f;

        // Effective top-speed cap (P2): enemies chase at PlayerSpeedFactor of the player's current speed so
        // fast movement no longer guarantees escape, but never exceed MaxSpeedMultiplier times their base
        // speed — it should read as pursuit, not rubber-banding. Slow play is unchanged (max with base).
        public const float PlayerSpeedFactor = 0.75f;
        public const float MaxSpeedMultiplier = 2f;
    }

    // Non-chase manoeuvres (EnemySteering, plans/ENEMY_BEHAVIORS.md). Every one of them picks a point to
    // head for inside the hull's own firing ring: no behaviour changes a range, so the D1 reach ceiling
    // and the grown rings stay exactly what the difficulty tests assert.
    public static class Manoeuvre
    {
        // How close to its chosen point a hull must get before it stops thrusting there.
        public const float HoldTolerance = 60f;

        // Spread: the hull holds a **lane** partway in from its firing ring rather than the ring itself.
        // The original held the ring, and play (2026-11-08) found that inert: the whole swarm already parks
        // at 650-780 px, so the manoeuvre was a second of radial shuffle at the same distance as everything
        // else, threatening nothing. A fraction of the ring puts the flank inside gun range, where holding a
        // bearing means something — and holding *closer* can never extend anyone's reach (D1).
        public const float SpreadHoldFraction = 0.55f;

        // And the lane is aimed where the player is **going**. A flanker's speed is capped by
        // EffectiveSpeed — 188 px/s for an Interceptor against a ~290 px/s player — so it cannot hold a
        // frozen bearing by chasing a point that outruns it; it can only cut toward where that point will be.
        // A fixed lead, bounded by the player's own speed rather than by a distance solved from it.
        public const float SpreadLeadSeconds = 1.2f;

        // Strafe: the point it aims at sits this far *around* the ring from its own bearing, so the hull
        // keeps chasing a point that keeps moving — a circle it never finishes.
        public const float StrafeLeadAngle = 0.35f;

        // Behaving hulls keep this much clear of each other, so the fleet reads as a formation instead
        // of a knot. Plain-chase hulls are excluded on purpose: the swarm's tight ball is what the
        // player's area guns are paid for.
        public const float SeparationGap = 40f;
        public const float SeparationThrustFraction = 0.5f;

        // Where a spreading hull may be born. Wider than the wave's own ±45° cone, because a flanker that
        // spawns inside the chase line has to run around the player to reach its lane and loses it; wider
        // than a right angle, because a hull born behind a fast player simply falls behind and never
        // arrives. ±60° of the heading is the arc a hull can cross in a couple of seconds.
        public const float SpreadSpawnCone = MathF.PI / 3f;

        // Cut-off: the lead is solved with the hull's own effective top speed, capped so a hull 3000 px
        // behind a moving player does not aim at where the player will be in a minute.
        public const float MaxCutOffLeadTime = 3f;

        // Cover-tuck: the crippled hull follows the biggest hull that still has this much of its hp and
        // sits within this bearing cone of its own, as seen from the player. The cone is what keeps a
        // tuck from routing across the field to reach a friend on the far side.
        public const float CrippleHpFraction = 0.25f;
        public const float HealthyHpFraction = 0.6f;
        public const float CoverCone = 0.35f;

        // The tuck point is this far behind the cover hull, measured from its outer edge.
        public const float CoverGap = 30f;
    }
}
