namespace Spacevors.Domain.Stats;

// Difficulty and spawn tuning for enemy content (plans/DIFFICULTY_SCALING.md).
public static class SpawningStats
{
    // World contents at game start (GameInitializer).
    public static class InitialWorld
    {
        public const int MineCount = 9;
        public const int EnemyShipCount = 6;

        // Initial ships spawn in a band around the player so they arrive one at a time over ~90 s.
        // The max must stay below Chase.CullDistance so initial ships survive their spawn.
        public const float EnemyShipMinDistance = 2400f;
        public const float EnemyShipMaxDistance = 5000f;
    }

    // Continuous enemy ship spawning (EnemyShipSpawnSystem).
    public static class EnemyShips
    {
        // Grace period before the first spawn.
        public const float InitialDelay = 9f;

        // Spawn interval ramps from random [StartMinInterval, StartMaxInterval] s down to random
        // [MinInterval, MaxInterval] s over RampDuration (EnemyShipSpawnSystem.NextInterval).
        public const float StartMinInterval = 5f;
        public const float StartMaxInterval = 10f;
        public const float MinInterval = 2f;
        public const float MaxInterval = 4f;

        // Time over which the interval ramp completes.
        public const float RampDuration = 180f;

        // Hard cap on live enemy ships before spawning pauses.
        public const int MaxEnemyShips = 100;

        // Minimum distance from any existing ship to a new spawn point.
        public const float MinSpawnDistance = 300f;

        // Initial velocity follows the player by this fraction, plus drift toward the player.
        public const float FollowFactor = 0.5f;

        // Threat density per unit distance stays roughly constant at any speed (P3): a fast player covers
        // more ground per second, so spawns must arrive faster to keep pressure up.
        public const float ReferenceSpeed = 100f;
        public const float MaxSpeedFactor = 3f;
    }

    // Mine respawning (MineRespawnSystem).
    public static class Mines
    {
        // Grace period before the first respawn.
        public const float InitialDelay = 20f;

        // Respawn interval ramps from random [StartMinInterval, StartMaxInterval] s up to random
        // [LateMinInterval, LateMaxInterval] s over RampDuration.
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
}
