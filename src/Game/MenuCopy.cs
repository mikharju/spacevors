using Spacevors.Domain.Stats;

namespace Spacevors.Game;

// The words every menu screen teaches with, in one file: copy is content, not layout, and keeping it
// here means PLAN.md's Controls section can be checked against a single source instead of against
// scattered DrawText calls (plans/START_SCREEN_ONBOARDING.md).
//
// The rule for a line of copy here: it must be true of the current game and short enough to read while
// the player is deciding something. Boost is deliberately absent: it is a candidate for removal, so the
// screen does not teach a control the game may stop having.
//
// The default raylib font draws "?" for an em dash, so these lines use a colon, a comma or "·".
// (Every other on-screen string in the Game layer follows the same rule.)
public static class MenuCopy
{
    // One row of a control list: the keys, then what they do.
    public readonly record struct ControlRow(string[] Keys, string What);

    public static ControlRow[] FlyingRows { get; } =
    [
        new(["Mouse"], "turns the hull to face the cursor"),
        new(["W", "S"], "thrust ahead · push backwards"),
        new(["A", "D"], "slide sideways"),
    ];

    public static ControlRow[] FightingRows { get; } =
    [
        new(["Click"], "lock a target: every gun prefers it"),
        new(["Tab"], "your ship, guns and mounts (pauses)"),
        new(["1-5"], "choose an upgrade when you level up"),
    ];

    // Plain-spoken lines under the control list. The drift line is the one a new player has never
    // had to think about before: nothing stops them, so stopping is a manoeuvre, not a key.
    public static string[] StartNotes { get; } =
    [
        "You drift. Nothing brakes you: to stop, turn and fire the engines back.",
        "Kills give XP, levels give upgrades. Survive as long as you can.",
    ];

    public static string StartRecommendation => "First flight? The Scout forgives mistakes.";

    // The level-up screen, above the five bundles. The one-choice rule is the thing a new player
    // cannot guess from looking at the cards, and neither is the super grade: the green half of a bundle is the
    // same upgrade at twice the step, and the two green halves of a hand are never in the same bundle
    // (plans/SUPER_UPGRADES.md D1/D8).
    public static string UpgradeSuperLine => "A green half is SUPER: that upgrade at twice the step, never both halves of one bundle.";

    public static string[] UpgradeLines { get; } =
    [
        "Take ONE bundle: you get both cards inside it.",
        "The card above upgrades your heavy guns, the one below your small ones.",
        UpgradeSuperLine,
        "Tab reads your ship, guns and mounts. The game waits while you do.",
    ];

    public static string UpgradeWeaponLine => "A new gun is yours at once: it waits in cargo until you mount it.";

    // The mount screen, whose one hint line is about the click in front of the player. These are the
    // rules around it.
    public static ControlRow[] MountingRows { get; } =
    [
        new(["Click"], "a ringed mount: the ring means it fits"),
        new(["1-8"], "or press that mount's number"),
        new(["C"], "store the gun in cargo: it stops firing"),
        new(["Right"], "put down the gun in hand"),
        new(["Esc"], "back"),
    ];

    public static string MountingNote => "A cargo gun does not fight. A large gun needs a large mount.";

    // What a hull carries, as one line: "Machine Gun ahead · Interceptor Turret left flank".
    public static string GunsLine(ShipType ship)
    {
        string line = "";
        foreach (var mount in ship.Loadout.Mounts)
            line += (line.Length == 0 ? "" : "  ·  ") + $"{FriendlyWeaponName(mount.Weapon.Name)} {MountPosition(mount.SlotName)}";
        return line;
    }

    public static string HullLine(ShipType ship) =>
        ship.Armor > 0 ? $"{ship.MaxHealth} hp · armor {ship.Armor}" : $"{ship.MaxHealth} hp";

    public static string EngineLine(ShipType ship) => $"{ship.Engine.Name}: {ship.Engine.Blurb}";

    // The empty mounts are the interesting half of the mount count: what a ship *does not* carry is
    // the room it has to grow. "" when every mount is filled.
    public static string FreeMountLine(ShipType ship)
    {
        int free = 0;
        bool bigFree = false;
        foreach (var slot in ship.Slots)
        {
            if (ship.Loadout.Mounts.Any(m => m.SlotName == slot.Name)) continue;
            free++;
            if (slot.Size == TurretSize.Large) bigFree = true;
        }

        if (free == 0) return "";
        string guns = bigFree ? "a large gun" : "another gun";
        return free == 1 ? $"1 empty mount: room for {guns}" : $"{free} empty mounts: room for more guns";
    }

    // Weapon ids are PascalCase (`MachineGun`) because they are also the upgrade card's key. Only the
    // display splits them into words, so no weapon type needs a second name to keep in step.
    public static string FriendlyWeaponName(string name)
    {
        var spaced = new System.Text.StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            bool wordStart = i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]) && name[i - 1] != ' ';
            if (wordStart) spaced.Append(' ');
            spaced.Append(name[i]);
        }

        return spaced.ToString();
    }

    // Where a mount points, in the player's words, read off the slot names the hull declares — so the
    // card cannot drift from the layout.
    public static string MountPosition(string slotName) => slotName switch
    {
        TurretSlotName.MainGun => "ahead",
        TurretSlotName.SmallLeft or TurretSlotName.HeavyLeft => "left flank",
        TurretSlotName.SmallRight or TurretSlotName.HeavyRight => "right flank",
        TurretSlotName.SmallBack => "aft",
        TurretSlotName.SmallNose => "nose",
        _ => "",
    };

    public static string SizeWord(TurretSize size) => size == TurretSize.Large ? "large" : "small";

    // A mount as the player refers to it: where it sits and how big it is. Two mounts of one hull can
    // share a position word (`small_left` and `heavy_left` are both the left flank), so the size is part of
    // the name — and the same string is read out on the hull, in the mount legend and on the stats screen,
    // where the slot's internal id (`heavy_left`) used to stand instead.
    public static string MountLabel(TurretSlotDefinition slot) => $"{MountPosition(slot.Name)} · {SizeWord(slot.Size)}";

    // The stats a hull and its guns are upgraded with, in one vocabulary: the upgrade card's label, the
    // caps that list what a new gun arrives carrying, and the stats screen's rows all read these words.
    // A card used to say `rate` where the same card said `attack speed` two lines above and the stats
    // screen said `Fire rate` — three spellings of one stat is three things to learn.
    //
    // `FireRate` is the one stat whose word depends on the gun: a gun whose volley grows buys another shot,
    // and the game's own word for that is the volley. The caller passes the same predicate the grant
    // applies, so the word cannot drift from the effect (R10).
    public static string StatWord(UpgradeOption stat, bool growsVolley = false) => stat switch
    {
        UpgradeOption.FireRate => growsVolley ? "volley size" : "attack speed",
        UpgradeOption.ProjectileSpeed => "projectile speed",
        UpgradeOption.Range => "range",
        UpgradeOption.Damage => "damage",
        UpgradeOption.ArmorPierce => "armor pierce",
        // R19: the card says whose rounds get tougher; "missile health" read like a stat of the hull.
        UpgradeOption.MissileHealth => "round hit points",
        UpgradeOption.Hp => "hit points",
        UpgradeOption.Armor => "hull armor",
        UpgradeOption.Shield => "hull shield",
        UpgradeOption.PickupRadius => "pickup radius",
        UpgradeOption.ForwardAcceleration => "forward acceleration",
        UpgradeOption.TurnSpeed => "turn speed",
        UpgradeOption.SideThrust => "side thrust",
        UpgradeOption.BackThrust => "back thrust",
        _ => stat.ToString().ToLowerInvariant(),
    };

    // The same words for the tight spots — a row of caps on a card that also carries a gun's name. Only the
    // two-word stats shorten, and no third vocabulary is invented: each abbreviation is the word above with
    // its qualifier dropped.
    public static string StatAbbrev(UpgradeOption stat, bool growsVolley = false) => stat switch
    {
        UpgradeOption.FireRate => growsVolley ? "volley" : "rate",
        UpgradeOption.ProjectileSpeed => "proj speed",
        UpgradeOption.MissileHealth => "round hp",
        UpgradeOption.ArmorPierce => "pierce",
        _ => StatWord(stat, growsVolley),
    };
}
