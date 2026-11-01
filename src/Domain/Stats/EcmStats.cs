namespace Spacevors.Domain.Stats;

// The jam one picked-up ECM capsule puts on the whole world (plans/ECM_PULSE_AND_SALVAGE_FIELD.md).
// Sim numbers only: how the jam looks — arcs, rings, the capsule's flash — lives with the renderer that
// draws it and with `LootStats` for the capsule's own tumble.
public static class EcmStats
{
    // How long a jammed hull stays jammed. Long enough to cross a screen and shoot a few hulls down,
    // short enough that it is a window rather than a reprieve.
    public const float Duration = 5f;

    // How fast a jammed hull turns toward the heading it was given when the pulse fired, rad/s. Well
    // under a hull's own turn rate (2–4 rad/s), so it reads as a dead ship being turned by its
    // emergency thrusters rather than as a ship still flying.
    public const float WanderTurnRate = 0.8f;

    // The first stunned round's fuse, and the gap between the next ones: a volley pops in a chain
    // instead of one simultaneous flash, which is the only way a player can count how many it took.
    public const float MissileDetonateDelay = 0.25f;
    public const float MissileDetonateStagger = 0.12f;

    // The pop that replaces a stunned round. Under a small mine's blast on purpose: this is a round's
    // own warhead going off, not a mine, and it damages nothing.
    public const float DetonationRadius = 20f;
    public const float DetonationLifetime = 0.5f;

    // The ring that fires out of the picked-up capsule. Past the on-screen half-diagonal at 1080p, so
    // the wave is seen to leave the screen rather than stop at the edge; it is a cue, not the effect,
    // which is world-wide.
    public const float WaveMaxRadius = 900f;
    public const float WaveLifetime = 0.6f;
}
