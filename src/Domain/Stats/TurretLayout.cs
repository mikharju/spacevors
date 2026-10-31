namespace Spacevors.Domain.Stats;

public enum TurretSize { Small, Large }

// One turret mount point on a hull.
// Offset is hull-local px: +x is the ship's right, -y is ahead of the hull centre.
// ArcCenter is the hull-relative direction a parked mount holds, ArcAngle the total span it may
// traverse; both radians, 0 = straight ahead, positive toward the ship's right.
public readonly record struct TurretSlotDefinition(
    string Name,
    TurretSize Size,
    Vector2 Offset,
    float ArcCenter,
    float ArcAngle)
{
    // Where this mount sits in world space for a hull at the given position and rotation.
    public Vector2 WorldPosition(Vector2 hullPosition, float hullRotation)
    {
        float cos = MathF.Cos(hullRotation);
        float sin = MathF.Sin(hullRotation);
        return hullPosition + new Vector2(Offset.X * cos - Offset.Y * sin, Offset.X * sin + Offset.Y * cos);
    }
}

// Slot names, shared by the layouts below and the starting loadouts that mount into them.
public static class TurretSlotName
{
    public const string MainGun = "main_gun";
    public const string SmallLeft = "small_left";
    public const string SmallRight = "small_right";
    public const string SmallBack = "small_back";
    public const string SmallNose = "small_nose";
    public const string HeavyLeft = "heavy_left";
    public const string HeavyRight = "heavy_right";
}

// Turret layout per ship. The arcs are the tuning surface: the mount screen shows them, and the
// weapon mounted into a slot fires inside them. A large slot takes any weapon; a large weapon needs one.
public static class TurretLayouts
{
    public static TurretSlotDefinition[] Scout { get; } =
    [
        Slot(TurretSlotName.MainGun, TurretSize.Large, 0f, -10f, 0f, 60f),
        Slot(TurretSlotName.SmallLeft, TurretSize.Small, -14f, 6f, -90f, 120f),
        Slot(TurretSlotName.SmallRight, TurretSize.Small, 14f, 6f, 90f, 120f),
    ];

    public static TurretSlotDefinition[] Fighter { get; } =
    [
        Slot(TurretSlotName.MainGun, TurretSize.Large, 0f, -14f, 0f, 60f),
        Slot(TurretSlotName.SmallLeft, TurretSize.Small, -18f, -2f, 0f, 45f),
        Slot(TurretSlotName.SmallRight, TurretSize.Small, 18f, -2f, 0f, 45f),
        Slot(TurretSlotName.SmallBack, TurretSize.Small, 0f, 26f, 180f, 270f),
    ];

    // Falcon: one heavy gun ahead and a small mount on each flank pod, each sweeping 180°. The two
    // flanks together cover the whole circle, which is why this hull has no aft mount at all. The offsets
    // sit on the painted pods, which are at ±0.50 R — ±37 px of this 74 px hull.
    public static TurretSlotDefinition[] Falcon { get; } =
    [
        Slot(TurretSlotName.MainGun, TurretSize.Large, 0f, -18f, 0f, 60f),
        Slot(TurretSlotName.SmallLeft, TurretSize.Small, -37f, 10f, -90f, 180f),
        Slot(TurretSlotName.SmallRight, TurretSize.Small, 37f, 10f, 90f, 180f),
    ];

    public static TurretSlotDefinition[] Shadow { get; } =
    [
        Slot(TurretSlotName.MainGun, TurretSize.Large, 0f, -14f, 0f, 60f),
        Slot(TurretSlotName.SmallLeft, TurretSize.Small, -18f, 6f, -90f, 120f),
        Slot(TurretSlotName.SmallRight, TurretSize.Small, 18f, 6f, 90f, 120f),
    ];

    // Eagle: the big-wings hull. One heavy gun ahead, a mount on each wing (painted out to ±0.93 R)
    // sweeping 150° down the flank, and an aft mount sweeping the whole 180° behind: the slow hull
    // that trades retreat speed for never having to turn to shoot backwards.
    public static TurretSlotDefinition[] Eagle { get; } =
    [
        Slot(TurretSlotName.MainGun, TurretSize.Large, 0f, -20f, 0f, 60f),
        Slot(TurretSlotName.SmallLeft, TurretSize.Small, -45f, 0f, -90f, 150f),
        Slot(TurretSlotName.SmallRight, TurretSize.Small, 45f, 0f, 90f, 150f),
        Slot(TurretSlotName.SmallBack, TurretSize.Small, 0f, 26f, 180f, 180f),
    ];

    // Manta: the crab. Every mount points ahead — the main gun at 60° and two narrow 60° mounts
    // angled 30° off the nose — because this hull aims by sliding sideways, not by turning. The
    // offsets sit on the painted wing roots at ±0.54 R.
    public static TurretSlotDefinition[] Manta { get; } =
    [
        Slot(TurretSlotName.MainGun, TurretSize.Large, 0f, -30f, 0f, 60f),
        Slot(TurretSlotName.SmallLeft, TurretSize.Small, -40f, 4f, -30f, 60f),
        Slot(TurretSlotName.SmallRight, TurretSize.Small, 40f, 4f, 30f, 60f),
    ];

    // Raptor: the broadside hull. One gun ahead and a heavy mount on each wing pod, each sweeping
    // 160° along its flank — the two together leave only a sliver dead astern. Both big mounts take
    // any large gun; the hull starts with one of them empty on purpose.
    public static TurretSlotDefinition[] Raptor { get; } =
    [
        Slot(TurretSlotName.MainGun, TurretSize.Large, 0f, -26f, 0f, 60f),
        Slot(TurretSlotName.HeavyLeft, TurretSize.Large, -33f, 20f, -90f, 160f),
        Slot(TurretSlotName.HeavyRight, TurretSize.Large, 33f, 20f, 90f, 160f),
    ];

    public static TurretSlotDefinition[] Heavy { get; } =
    [
        Slot(TurretSlotName.MainGun, TurretSize.Large, 0f, -22f, 0f, 60f),
        Slot(TurretSlotName.HeavyLeft, TurretSize.Large, -30f, 8f, -60f, 120f),
        Slot(TurretSlotName.HeavyRight, TurretSize.Large, 30f, 8f, 60f, 120f),
        Slot(TurretSlotName.SmallLeft, TurretSize.Small, -18f, -6f, -90f, 120f),
        Slot(TurretSlotName.SmallRight, TurretSize.Small, 18f, -6f, 90f, 120f),
        Slot(TurretSlotName.SmallBack, TurretSize.Small, 0f, 34f, 180f, 270f),
        Slot(TurretSlotName.SmallNose, TurretSize.Small, 0f, -38f, 0f, 60f),
    ];

    // Layouts are written in degrees for readability; the domain works in radians.
    static TurretSlotDefinition Slot(string name, TurretSize size, float x, float y, float directionDeg, float arcDeg) =>
        new(name, size, new Vector2(x, y), Angle.FromDeg(directionDeg), Angle.FromDeg(arcDeg));
}
