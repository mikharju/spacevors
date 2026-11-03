using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Who deals an upgrade hand, and what a hand may contain.
//
// The rules live here, in one place, because the same hand is dealt twice over: `LevelUpSystem` deals it
// when the player levels up, and a collected salvage capsule re-deals it (plans/REROLL_TOKENS.md). A
// re-deal that re-implemented the pool would drift the first time an offer rule was fixed, so a reroll
// cannot invent a card a level-up would have withdrawn — no card naming a cargo gun, no projectile-speed
// card on a hitscan beam, no "+1 shot" past the guided volley budget (R17/R18), no missile-health card
// past its ceiling (R19), no weapon the ship already owns, at most three new-weapon cards in a hand, and
// a damage card sized by the mount's class (R33), and no Armor or ArmorPierce card below level 5
// (plans/ARMOR_AND_PIERCE.md). Balance rules R6/R10/R18/R19/R29/R33 all say a card
// must never be a lie; that applies to a re-deal too.
//
// What the hand is *made of* is the other half of the job (plans/UPGRADE_HAND_COMPOSITION.md). The legal
// cards are sorted into kinds and the hand is dealt one card per slot: the hull card, one engine/utility
// card and three weapon cards, or at a milestone the hull card, up to three new guns and a damage card.
// One shuffled pool used to be able to deal five engine cards to a hull carrying one gun — 40% of a
// one-gun hull's hands held at most one weapon card — which is the hand players call unfair. A slot whose
// kind has nothing legal left falls through `SpillOne`'s preference order rather than inventing a card
// the rules would refuse, and a hand with nothing left to offer stays short, as it always could.
public static class UpgradeOffer
{
    const int HandSize = 5;
    const int MilestoneLevelInterval = 5;
    // Armor and its counter unlock with tier 2 — the same beat as the `UnlockLevel: 5` guns and the
    // first milestone hand (plans/ARMOR_AND_PIERCE.md). Before it, no hand may deal either card.
    const int ArmorUnlockLevel = 5;
    // The shield pool unlocks a tier later than armor (plans/SHIELDS.md): the same beat as the `UnlockLevel:
    // 10` guided guns, which is where a run starts meeting fire heavy enough for a pool to be worth banking.
    const int ShieldUnlockLevel = 10;
    // A hand never carries more than three new weapons. Whether they fit a free hull mount is the
    // player's problem, not the hand's: a weapon that does not fit rides in the cargo until swapped in.
    const int MaxNewWeaponCardsPerHand = 3;

    // The kinds a hand is dealt from. The two weapon kinds are the balance rules' two classes
    // (R24/R25), keyed on the gun (`WeaponType.IsSupport`), never on the mount it happens to sit in.
    enum CardKind { Hull, Utility, SupportWeapon, KillWeapon, NewWeapon }

    // One entry per slot, so the hand's shape is one readable line per hand shape.
    static readonly CardKind[] StandardSlots = [CardKind.Hull, CardKind.Utility, CardKind.SupportWeapon, CardKind.SupportWeapon, CardKind.KillWeapon];
    static readonly CardKind[] MilestoneSlots = [CardKind.Hull, CardKind.NewWeapon, CardKind.NewWeapon, CardKind.NewWeapon, CardKind.KillWeapon];

    public static bool IsMilestoneLevel(int level) => level % MilestoneLevelInterval == 0;

    /// The hand for `level`, or null when nothing can be offered (a ship with no turret at all), which is
    /// the case where a level-up must not be consumed. `why` labels the diagnostic row.
    public static UpgradableOption[]? Roll(WorldView view, Entity playerEntity, int level, string why)
    {
        var turrets = PlayerTurrets(view);
        if (turrets.Count == 0) return null;

        var buckets = SortIntoKinds(view, playerEntity, turrets, level);
        var hand = DealHand(buckets, IsMilestoneLevel(level), view, turrets);

        DiagnosticLogger.LogEvent("UPGRADE", $"{why} level={level} choices: {FormatChoices(hand)}");
        return [.. hand];
    }

    /// Spends one salvage charge and re-deals the hand that is on screen. False — and nothing spent —
    /// when there is no charge or no hand to re-deal. Called from the app layer while the game is paused,
    /// the same sanctioned direct-write path that applies a chosen upgrade.
    public static bool TryReroll(EntityManager em, Entity playerEntity)
    {
        if (!em.TryGetComponent<RerollStock>(playerEntity, out var stock) || stock.Count == 0) return false;
        if (!em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pending)) return false;

        var view = new WorldView(em);
        int level = em.GetComponent<Player>(playerEntity).Level;
        var dealt = Roll(view, playerEntity, level, "reroll");
        if (dealt is null) return false;

        // A charge must never buy back exactly the hand already on screen. One retry, then keep what
        // came: a pool smaller than the hand can genuinely re-deal the same five, and looping on it
        // would spend unbounded RNG for nothing.
        if (SameHand(dealt, pending.Value2.Options))
            dealt = Roll(view, playerEntity, level, "reroll retry") ?? dealt;

        em.AddComponent(playerEntity, stock.With(stock.Count - 1));
        em.AddComponent(pending.Entity, new PendingUpgradeOptions(dealt));
        return true;
    }

    // Deal one card per slot, then fill whatever the kinds could not supply. The final shuffle is why a
    // guaranteed slot never becomes a guaranteed card position.
    static List<UpgradableOption> DealHand(Dictionary<CardKind, List<UpgradableOption>> buckets, bool milestone, WorldView view, List<Turret> turrets)
    {
        var hand = new List<UpgradableOption>();
        foreach (var kind in milestone ? MilestoneSlots : StandardSlots)
            Take(buckets, kind, hand, sameGunAllowed: false);

        bool starvedForANewGun = StarvedForANewGun(view, turrets);
        while (hand.Count < HandSize)
        {
            if (!SpillOne(buckets, hand, starvedForANewGun)) break;
        }

        Shuffle(hand, view.Rng);
        return hand;
    }

    // One spilled card, in preference order: a weapon card for a gun the hand does not name yet, a new
    // gun when the hull cannot field one of the two classes at all, a second card on a gun the hand
    // already names, and only then an engine/utility card — a weapon upgrade beats filler, which is the
    // whole point of the slots. False means nothing legal is left, which is the honest short hand.
    static bool SpillOne(Dictionary<CardKind, List<UpgradableOption>> buckets, List<UpgradableOption> hand, bool starvedForANewGun) =>
        Take(buckets, CardKind.SupportWeapon, hand, sameGunAllowed: false)
        || Take(buckets, CardKind.KillWeapon, hand, sameGunAllowed: false)
        || (starvedForANewGun && Take(buckets, CardKind.NewWeapon, hand, sameGunAllowed: false))
        || Take(buckets, CardKind.SupportWeapon, hand, sameGunAllowed: true)
        || Take(buckets, CardKind.KillWeapon, hand, sameGunAllowed: true)
        || Take(buckets, CardKind.Utility, hand, sameGunAllowed: true);

    // Take the first card of `kind` into the hand, skipping a gun the hand already names while anything
    // else is on offer: three cards for one chip gun is the hand this change exists to stop dealing. The
    // bucket is already shuffled, so "the first one" is a random one.
    static bool Take(Dictionary<CardKind, List<UpgradableOption>> buckets, CardKind kind, List<UpgradableOption> hand, bool sameGunAllowed)
    {
        var bucket = buckets[kind];
        int index = bucket.FindIndex(card => sameGunAllowed || !NamesGunInHand(hand, card));
        if (index < 0) return false;

        hand.Add(bucket[index]);
        bucket.RemoveAt(index);
        return true;
    }

    static bool NamesGunInHand(List<UpgradableOption> hand, UpgradableOption card) =>
        card.WeaponName.Length > 0 && hand.Any(other => other.WeaponName == card.WeaponName);

    // A new gun is worth spilling a slot for only when the hull mounts no gun of one class at all and has
    // a free mount to put it in (plans/WEAPON_IDENTITY_CELLS.md §8 has kept "prefer a gun that fits a free
    // mount" a nice-to-have, and here it stays one: milestone hands offer new guns regardless of mounts).
    static bool StarvedForANewGun(WorldView view, List<Turret> turrets) =>
        TurretMount.FreeSlots(view).Count > 0 &&
        (turrets.All(t => t.IsSupport) || turrets.All(t => !t.IsSupport));

    // The legal cards for one hand, sorted into kinds and shuffled inside each kind: random within a slot,
    // never across slots, so a big pool of filler cannot crowd a slot out.
    static Dictionary<CardKind, List<UpgradableOption>> SortIntoKinds(WorldView view, Entity playerEntity, List<Turret> turrets, int level)
    {
        var buckets = new Dictionary<CardKind, List<UpgradableOption>>
        {
            [CardKind.Hull] = HullOptions(view, playerEntity, level),
            [CardKind.Utility] =
            [
                new UpgradableOption("", UpgradeOption.PickupRadius),
                new UpgradableOption("", UpgradeOption.ForwardAcceleration),
                new UpgradableOption("", UpgradeOption.TurnSpeed),
                new UpgradableOption("", UpgradeOption.SideThrust),
                new UpgradableOption("", UpgradeOption.BackThrust)
            ],
            [CardKind.SupportWeapon] = [],
            [CardKind.KillWeapon] = [],
            [CardKind.NewWeapon] = NewWeaponOptions(OwnedWeaponNames(view, playerEntity, turrets), level, view.Rng)
        };

        if (IsMilestoneLevel(level))
        {
            // R33: every mounted gun is dealt its damage card at a milestone. It used to be a kill-gun
            // card because a flat +1 on a 1-damage chip was a x2 multiplier; as a percent of the base the
            // support gun buys a quarter and the kill gun a half, so the class line holds without a ban.
            // The hand has one or two weapon slots left, so which gun is offered the card is chosen rather
            // than left to a shuffle over a pool bigger than the hand.
            foreach (var turret in turrets)
                buckets[KindFor(turret)].Add(new UpgradableOption(turret.WeaponName, UpgradeOption.Damage));
        }
        else
        {
            foreach (var turret in turrets.DistinctBy(t => t.WeaponName))
                AddStatCards(buckets, turret, level);
        }

        foreach (var bucket in buckets.Values)
            Shuffle(bucket, view.Rng);

        return buckets;
    }

    // The guaranteed hull-slot card. `Hp` is always there; `Armor` joins the draw from level 5
    // (plans/ARMOR_AND_PIERCE.md) while the hull still has room under the ceiling — at the ceiling
    // it is withdrawn, never dealt as a lie (R19's rule), and `Hp` goes back to guaranteed.
    // `Shield` joins two beats later, at tier 4's unlock level (plans/SHIELDS.md): a pool that drinks a
    // whole hit is worth more than +2 armor, so it competes at the point where a run has spare cards to
    // spend on defence, and it is withdrawn at its own ceiling the same way.
    static List<UpgradableOption> HullOptions(WorldView view, Entity playerEntity, int level)
    {
        var cards = new List<UpgradableOption> { new("", UpgradeOption.Hp) };
        if (level >= ArmorUnlockLevel && CurrentArmor(view, playerEntity) < ArmorRules.ArmorCeiling)
            cards.Add(new UpgradableOption("", UpgradeOption.Armor));
        if (level >= ShieldUnlockLevel && CurrentShieldMax(view, playerEntity) < ShieldRules.ShieldCeiling)
            cards.Add(new UpgradableOption("", UpgradeOption.Shield));

        return cards;
    }

    static int CurrentArmor(WorldView view, Entity playerEntity) =>
        view.TryGetComponent<Armor>(playerEntity, out var armor) ? armor.Value : 0;

    // A hull with no shield has a pool of 0, so the first card is always offerable at the gate.
    static int CurrentShieldMax(WorldView view, Entity playerEntity) =>
        view.TryGetComponent<Shield>(playerEntity, out var shield) ? shield.Max : 0;

    // The per-gun stat cards, gated exactly as they always were — a card the rules would refuse to honour
    // is never dealt, so an empty slot is honest rather than a bug.
    static void AddStatCards(Dictionary<CardKind, List<UpgradableOption>> buckets, Turret turret, int level)
    {
        var stats = turret.Weapon;
        var cards = buckets[KindFor(turret)];

        // R18: a guided volley that has reached its round budget (R17) would be offered a "+1 shot" card
        // it cannot honour, so the card is withdrawn and the gun keeps the others. Guns whose volleys
        // never grow always have a working rate card.
        if (!SalvoCardIsDead(stats))
            cards.Add(new UpgradableOption(turret.WeaponName, UpgradeOption.FireRate));

        // A hitscan beam has no travelling projectile: the card would multiply AmmoSpeed 0 by 1.3 and be
        // a dead choice.
        if (!stats.Hitscan)
            cards.Add(new UpgradableOption(turret.WeaponName, UpgradeOption.ProjectileSpeed));

        // R19: only a guided round has hit points to raise, and only while the next whole step still lands
        // at or under the ceiling. The step is the gun's percent of its **base** round health (R33's
        // shape), read off the table, so a mount that already bought a card is offered the same honest
        // step again.
        if (WeaponType.FromName(turret.WeaponName) is { } gun && stats.CanToughenRound(gun.MissileHealthStep))
            cards.Add(new UpgradableOption(turret.WeaponName, UpgradeOption.MissileHealth));

        // R29: a support gun stops at its class roof (R24/R26). The Range card stretches the shot's
        // lifetime as well as its range, so withdrawing it at the roof caps the chip's live rounds too —
        // which is the same reason the card is withdrawn at R18/R19: never a card the rules would refuse.
        if (!turret.IsSupport || turret.Range * UpgradeDefinition.Range.Multiplier <= WeaponType.SupportRangeCeiling)
            cards.Add(new UpgradableOption(turret.WeaponName, UpgradeOption.Range));

        // plans/ARMOR_AND_PIERCE.md: pierce is the level-5 answer to the armored hull. Against
        // unarmored targets it is worth nothing, so it cannot move R25's class line, and at the
        // ceiling it is withdrawn rather than dealt as a lie (R19's rule). Both classes may buy it —
        // a screened chip is the one that most often wants the armored heavy dead fast.
        if (level >= ArmorUnlockLevel && stats.CanPierceMore(UpgradeDefinition.ArmorPierce.Additive))
            cards.Add(new UpgradableOption(turret.WeaponName, UpgradeOption.ArmorPierce));
    }

    static CardKind KindFor(Turret turret) => turret.IsSupport ? CardKind.SupportWeapon : CardKind.KillWeapon;

    static List<Turret> PlayerTurrets(WorldView view)
    {
        var turrets = new List<Turret>();
        foreach (var turret in view.GetEntitiesWithComponents<Turret>())
            if (!turret.Value1.IsEnemy) turrets.Add(turret.Value1);

        return turrets;
    }

    // Cargo weapons are owned but inactive: they are not offered stat upgrades (those come from the
    // turrets on the hull above) and they are not offered back as new weapons.
    static List<string> OwnedWeaponNames(WorldView view, Entity playerEntity, List<Turret> turrets)
    {
        var owned = turrets.Select(t => t.WeaponName).Distinct().ToList();
        if (view.TryGetComponent<WeaponCargo>(playerEntity, out var cargo))
            owned.AddRange(cargo.Weapons.Select(w => w.WeaponName));

        return owned;
    }

    // New-weapon cards: anything the ship does not already own, mounted or in cargo, drawn from the pool
    // this level has unlocked. Capping the bucket is what makes "at most three new weapons in a hand"
    // structural rather than a matter of luck.
    private static List<UpgradableOption> NewWeaponOptions(List<string> ownedNames, int level, Random rng)
    {
        var pool = WeaponType.NewWeaponPoolFor(level).Where(w => !ownedNames.Contains(w.Name)).ToList();
        Shuffle(pool, rng);

        return [.. pool.Take(MaxNewWeaponCardsPerHand).Select(w => new UpgradableOption(w.Name, UpgradeOption.Damage, IsNewWeapon: true))];
    }

    static bool SameHand(UpgradableOption[] a, UpgradableOption[] b) =>
        a.Length == b.Length && a.Zip(b).All(pair => pair.First == pair.Second);

    // Diagnostics only: SPACEVORS_DIAG_UPGRADES="RailGun,Hp,FireRate:MachineGun" — one entry per level-up.
    public static UpgradableOption[]? ParseScriptedUpgrades()
    {
        if (Environment.GetEnvironmentVariable(DiagnosticLogger.EnvVar) != "1") return null;

        var raw = Environment.GetEnvironmentVariable(ScriptedUpgradesEnvVar);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var options = new List<UpgradableOption>();
        foreach (var entry in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseEntry(entry) is { } option) options.Add(option);
            else DiagnosticLogger.LogWarning($"SPACEVORS_DIAG_UPGRADES: ignoring unknown entry '{entry}'");
        }

        return options.Count > 0 ? [.. options] : null;
    }

    const string ScriptedUpgradesEnvVar = "SPACEVORS_DIAG_UPGRADES";

    private static UpgradableOption? ParseEntry(string entry)
    {
        // A script names a gun directly and ignores its unlock level: the script is a test bench.
        if (entry != WeaponType.LoadTestWeapon.Name && WeaponType.FromName(entry) is not null)
            return new UpgradableOption(entry, UpgradeOption.Damage, IsNewWeapon: true);

        var parts = entry.Split(':');
        if (parts.Length != 2 || !Enum.TryParse<UpgradeOption>(parts[0], ignoreCase: true, out var stat))
            return null;

        string weaponName = parts[1].Trim();
        return new UpgradableOption(weaponName, stat);
    }

    // R18: a growing guided volley that has reached the guided-round budget (R17) cannot take another
    // shot, so the "+1 shot" card is withdrawn instead of being offered as a lie. Guns whose volleys
    // never grow (SalvoGrows off) keep the card as a plain rate increase.
    static bool SalvoCardIsDead(WeaponStats weapon) => weapon.SalvoGrows && weapon.PelletCount > 1 && !weapon.CanGrowSalvo;

    public static string FormatChoice(UpgradableOption option) =>
        option.IsNewWeapon ? $"new:{option.WeaponName}" : FormatStat(option);

    static string FormatChoices(IEnumerable<UpgradableOption> options) =>
        string.Join(", ", options.Select(FormatChoice));

    static string FormatStat(UpgradableOption option) =>
        string.IsNullOrEmpty(option.WeaponName) ? option.Stat.ToString() : $"{option.Stat}:{option.WeaponName}";

    static void Shuffle<T>(List<T> items, Random rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
