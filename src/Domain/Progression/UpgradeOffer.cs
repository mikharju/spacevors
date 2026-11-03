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
// What the hand is *made of* is the other half of the job (plans/UPGRADE_BUNDLES.md, which supersedes the
// hand-shape half of plans/UPGRADE_HAND_COMPOSITION.md). The legal cards are sorted into kinds and a hand
// is dealt from them by **priority**: the hull card, an engine/utility card, a small-gun upgrade and a
// large-gun upgrade — the pair a level is built around, so choosing between the classes is never a
// question of missing out — plus a fifth card for a gun the hand has not named yet. A milestone hand deals
// a new large gun, a new small gun and a damage card per class. Every priority is a wish, not a promise: a
// slot whose kind has nothing legal left **leaks** (`LeakOne`) rather than staying empty — cards for the
// guns the ship is shooting with first, then filler, and the strong cards last, with a defence card behind a
// rare roll. A hand with nothing left to offer stays short.
public static class UpgradeOffer
{
    // A level spends two of the five cards it is dealt (plans/UPGRADE_BUNDLES.md §2). The XP cost is
    // deliberately unchanged, so this is a buff, and the buff is the thing playtest is judging.
    public const int CardsPerLevel = 2;

    const int HandSize = 5;
    const int MilestoneLevelInterval = 5;
    // Armor and its counter unlock with tier 2 — the same beat as the `UnlockLevel: 5` guns and the
    // first milestone hand (plans/ARMOR_AND_PIERCE.md). Before it, no hand may deal either card.
    const int ArmorUnlockLevel = 5;
    // The shield pool unlocks a tier later than armor (plans/SHIELDS.md): the same beat as the `UnlockLevel:
    // 10` guided guns, which is where a run starts meeting fire heavy enough for a pool to be worth banking.
    const int ShieldUnlockLevel = 10;
    // A hand never carries more than three new weapons of one class. Whether they fit a free hull mount is
    // the player's problem, not the hand's: a weapon that does not fit rides in the cargo until swapped in.
    const int MaxNewWeaponCardsPerClass = 3;
    // How often a defence card leaks into a slot that was not dealt for it. `Hp` and the engine cards are
    // the ordinary filler; armour and shield should turn up as a find (plans/UPGRADE_BUNDLES.md §2).
    const int RareDefenceLeakPercent = 20;

    // The kinds a hand is dealt from. The two weapon kinds are the balance rules' two classes
    // (R24/R25), keyed on the gun (`WeaponType.IsSupport`), never on the mount it happens to sit in.
    // `Damage*` holds the milestone-only damage cards (R33), split by class so a milestone hand grows both
    // classes; the new-weapon kinds are split by mount size because a milestone hand owes the player a
    // large gun to switch into even when the hull has exactly one large mount.
    enum CardKind { Hull, Utility, SupportWeapon, KillWeapon, DamageKill, DamageSupport, NewLargeWeapon, NewSmallWeapon }

    // One entry per slot, in priority order. The fifth card of an ordinary hand comes from
    // `TakeUpgradeForAnUnnamedGun`, and whatever is still missing leaks.
    static readonly CardKind[] StandardSlots = [CardKind.Hull, CardKind.Utility, CardKind.SupportWeapon, CardKind.KillWeapon];
    static readonly CardKind[] MilestoneSlots = [CardKind.Hull, CardKind.NewLargeWeapon, CardKind.NewSmallWeapon, CardKind.DamageKill, CardKind.DamageSupport];

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

    /// Spends one pick of the level's bundle: the card leaves the hand and the pick count drops. True while
    /// the level still owes a pick and has cards left to owe it from; false when the level is done, in which
    /// case the hand is left for the caller to clear. The bookkeeping lives here rather than in the app layer
    /// so the two-pick rule is testable without a window (plans/UPGRADE_BUNDLES.md §4) and a re-deal sees the
    /// same state.
    public static bool SpendPick(EntityManager em, Entity playerEntity, UpgradableOption taken)
    {
        if (!em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pending)) return false;

        var hand = pending.Value2;
        if (hand.PicksLeft <= 1 || hand.Options.Length <= 1) return false;

        em.AddComponent(pending.Entity, hand with
        {
            Options = [.. hand.Options.Where(card => card != taken)],
            PicksLeft = hand.PicksLeft - 1,
            Taken = [.. hand.Taken, taken],
        });
        return true;
    }

    /// Drops the cards the hand still holds that can no longer be applied, and clamps the picks to what is
    /// left. A mount that displaces a gun into cargo takes that gun's cards with it — `ApplyUpgrade` only
    /// reaches guns that are actually firing — so a card left behind would be a card that does nothing
    /// (R10's rule: a card is never a lie). True while the hand still has something to pick.
    public static bool PruneCardsThatNoLongerApply(EntityManager em, Entity playerEntity)
    {
        if (!em.GetEntitiesWithComponents<PendingChoice, PendingUpgradeOptions>().TryFirst(out var pending)) return false;

        var hand = pending.Value2;
        var mounted = PlayerTurrets(new WorldView(em)).Select(turret => turret.WeaponName).ToList();
        var left = hand.Options.Where(card =>
            card.IsNewWeapon || card.WeaponName.Length == 0 || mounted.Contains(card.WeaponName)).ToArray();

        if (left.Length == hand.Options.Length) return left.Length > 0;

        em.AddComponent(pending.Entity, hand with { Options = left, PicksLeft = Math.Min(hand.PicksLeft, left.Length) });
        DiagnosticLogger.LogEvent("UPGRADE", $"hand pruned to {left.Length} applicable cards");
        return left.Length > 0;
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
        var dealt = WithoutTaken(Roll(view, playerEntity, level, "reroll"), pending.Value2.Taken);
        if (dealt is null) return false;

        // A charge must never buy back a card this level has already spent, nor exactly the hand already
        // on screen. One retry, then keep what came: a pool smaller than the hand can genuinely re-deal the
        // same cards, and looping on it would spend unbounded RNG for nothing.
        if (SameHand(dealt, pending.Value2.Options))
            dealt = WithoutTaken(Roll(view, playerEntity, level, "reroll retry"), pending.Value2.Taken) ?? dealt;

        em.AddComponent(playerEntity, stock.With(stock.Count - 1));
        em.AddComponent(pending.Entity, pending.Value2 with { Options = dealt });
        return true;
    }

    // A re-deal may not offer back what this level has already taken. `Roll` does not know about picks, so
    // the filter sits on this side of the call — and it never leaves an empty hand behind.
    static UpgradableOption[]? WithoutTaken(UpgradableOption[]? hand, UpgradableOption[] taken)
    {
        if (hand is null) return null;

        UpgradableOption[] left = [.. hand.Where(card => !taken.Contains(card))];
        return left.Length > 0 ? left : hand;
    }

    // Deal one card per priority slot, then leak whatever the kinds could not supply. The final shuffle is
    // why a guaranteed slot never becomes a guaranteed card position.
    static List<UpgradableOption> DealHand(Dictionary<CardKind, List<UpgradableOption>> buckets, bool milestone, WorldView view, List<Turret> turrets)
    {
        var hand = new List<UpgradableOption>();
        foreach (var kind in milestone ? MilestoneSlots : StandardSlots)
            Take(buckets, kind, hand);

        if (!milestone) TakeUpgradeForAnUnnamedGun(buckets, hand);

        while (hand.Count < HandSize)
        {
            if (!LeakOne(buckets, hand, view, turrets)) break;
        }

        Shuffle(hand, view.Rng);
        return hand;
    }

    // The fifth card of an ordinary hand: an upgrade for a gun the hand has not named yet, small class
    // first. It is what keeps a hand about the guns the player is shooting with.
    static bool TakeUpgradeForAnUnnamedGun(Dictionary<CardKind, List<UpgradableOption>> buckets, List<UpgradableOption> hand) =>
        Take(buckets, CardKind.SupportWeapon, hand) || Take(buckets, CardKind.KillWeapon, hand);

    // One leaked card for a slot whose kind has nothing legal left (plans/UPGRADE_BUNDLES.md §2). First the
    // cards that are about the guns the ship is actually shooting with — a gun the hand has not named, a gun
    // that fills a class the hull does not mount at all, then a second card on a gun it has named — because
    // a hand of filler is the hand players call short. Only after those comes the filler itself (engine
    // cards, then `Hp`), and last of all the strong cards: a `Damage` card, and a defence card behind a rare
    // roll. False means nothing legal is left, which is the honest short hand.
    static bool LeakOne(Dictionary<CardKind, List<UpgradableOption>> buckets, List<UpgradableOption> hand, WorldView view, List<Turret> turrets) =>
        TakeUpgradeForAnUnnamedGun(buckets, hand)
        || TakeGunForAMissingClass(buckets, hand, view, turrets)
        || Take(buckets, CardKind.SupportWeapon, hand, sameGunAllowed: true)
        || Take(buckets, CardKind.KillWeapon, hand, sameGunAllowed: true)
        || Take(buckets, CardKind.Utility, hand, sameGunAllowed: true)
        || TakeHullCard(buckets, hand, UpgradeOption.Hp)
        || Take(buckets, CardKind.DamageKill, hand)
        || Take(buckets, CardKind.DamageSupport, hand)
        || (RollsRareDefenceLeak(view.Rng) && TakeDefenceCard(buckets, hand))
        || Take(buckets, CardKind.DamageKill, hand, sameGunAllowed: true)
        || Take(buckets, CardKind.DamageSupport, hand, sameGunAllowed: true);

    static bool RollsRareDefenceLeak(Random rng) => rng.Next(100) < RareDefenceLeakPercent;

    // `Hp` leaks only while the hand has none — the hull slot may already have spent it — so a leaked
    // fourth card is health exactly when the hull card was a defence one.
    static bool TakeHullCard(Dictionary<CardKind, List<UpgradableOption>> buckets, List<UpgradableOption> hand, UpgradeOption stat) =>
        hand.All(card => card.Stat != stat)
        && TakeFromBucket(buckets[CardKind.Hull], hand, card => card.Stat == stat);

    static bool TakeDefenceCard(Dictionary<CardKind, List<UpgradableOption>> buckets, List<UpgradableOption> hand) =>
        TakeFromBucket(buckets[CardKind.Hull], hand, card => card.Stat is UpgradeOption.Armor or UpgradeOption.Shield);

    // Take the first card of `kind` into the hand, skipping a gun the hand already names while anything
    // else is on offer: three cards for one chip gun is the hand this shape exists not to deal. The bucket
    // is already shuffled, so "the first one" is a random one.
    static bool Take(Dictionary<CardKind, List<UpgradableOption>> buckets, CardKind kind, List<UpgradableOption> hand, bool sameGunAllowed = false) =>
        TakeFromBucket(buckets[kind], hand, card => sameGunAllowed || !NamesGunInHand(hand, card));

    static bool TakeFromBucket(List<UpgradableOption> bucket, List<UpgradableOption> hand, Func<UpgradableOption, bool> matches)
    {
        int index = bucket.FindIndex(card => matches(card));
        if (index < 0) return false;

        hand.Add(bucket[index]);
        bucket.RemoveAt(index);
        return true;
    }

    static bool NamesGunInHand(List<UpgradableOption> hand, UpgradableOption card) =>
        card.WeaponName.Length > 0 && hand.Any(other => other.WeaponName == card.WeaponName);

    // A leaked slot buys a new gun only when the hull mounts no gun of one class at all and has a free
    // mount of that size to put it in: a new gun that cannot be mounted is cargo, and cargo does not fire
    // (plans/UPGRADE_HAND_COMPOSITION.md D5). Milestone hands offer new guns regardless of mounts, because
    // there they are the swap-into option the player asked for.
    static bool TakeGunForAMissingClass(Dictionary<CardKind, List<UpgradableOption>> buckets, List<UpgradableOption> hand, WorldView view, List<Turret> turrets)
    {
        if (turrets.Any(t => t.IsSupport) && turrets.Any(t => !t.IsSupport)) return false;

        CardKind missingClass = turrets.All(t => t.IsSupport) ? CardKind.NewLargeWeapon : CardKind.NewSmallWeapon;
        return TurretMount.FreeSlots(view, SizeOf(missingClass)).Count > 0 && Take(buckets, missingClass, hand);
    }

    static TurretSize SizeOf(CardKind kind) => kind == CardKind.NewLargeWeapon ? TurretSize.Large : TurretSize.Small;

    // The legal cards for one hand, sorted into kinds and shuffled inside each kind: random within a slot,
    // never across slots, so a big pool of filler cannot crowd a slot out.
    static Dictionary<CardKind, List<UpgradableOption>> SortIntoKinds(WorldView view, Entity playerEntity, List<Turret> turrets, int level)
    {
        var owned = OwnedWeaponNames(view, playerEntity, turrets);
        var buckets = new Dictionary<CardKind, List<UpgradableOption>>
        {
            [CardKind.Hull] = HullOptions(view, playerEntity, level),
            [CardKind.Utility] = UtilityOptions(),
            [CardKind.SupportWeapon] = [],
            [CardKind.KillWeapon] = [],
            [CardKind.DamageKill] = [],
            [CardKind.DamageSupport] = [],
            [CardKind.NewLargeWeapon] = NewGunOptions(owned, level, TurretSize.Large, view.Rng),
            [CardKind.NewSmallWeapon] = NewGunOptions(owned, level, TurretSize.Small, view.Rng)
        };

        if (IsMilestoneLevel(level)) AddDamageCards(buckets, turrets);
        else
            foreach (var turret in turrets.DistinctBy(t => t.WeaponName))
                AddStatCards(buckets, turret, level);

        // The damage buckets keep the order they were built in — kill class before support class — which is
        // how a milestone hand is seen to grow both classes instead of leaving it to a draw.
        foreach (var (kind, bucket) in buckets)
            if (kind is not (CardKind.DamageKill or CardKind.DamageSupport))
                Shuffle(bucket, view.Rng);

        return buckets;
    }

    // The always-legal engine and vacuum cards. No ceiling can withdraw one, which is why the leak path can
    // lean on this bucket first and never runs dry.
    static List<UpgradableOption> UtilityOptions() =>
        [
            new UpgradableOption("", UpgradeOption.PickupRadius),
            new UpgradableOption("", UpgradeOption.ForwardAcceleration),
            new UpgradableOption("", UpgradeOption.TurnSpeed),
            new UpgradableOption("", UpgradeOption.SideThrust),
            new UpgradableOption("", UpgradeOption.BackThrust)
        ];

    // R33: the damage card is a percent of the gun's **base** damage sized by its mount class — a support
    // chip +25%, a kill gun +50% — so the class line widens across a run instead of being frozen by a flat
    // step, and it is dealt only by a milestone hand, which is what makes a step that big affordable. One
    // card per gun, one bucket per class: a milestone hand with both classes aboard grows both, and a hull
    // with no chip to spare spends its second damage slot on a second kill gun.
    static void AddDamageCards(Dictionary<CardKind, List<UpgradableOption>> buckets, List<Turret> turrets)
    {
        foreach (var turret in turrets.DistinctBy(t => t.WeaponName))
            buckets[turret.IsSupport ? CardKind.DamageSupport : CardKind.DamageKill]
                .Add(new UpgradableOption(turret.WeaponName, UpgradeOption.Damage));
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

    // New-weapon cards for one mount class: anything the ship does not already own, mounted or in cargo,
    // drawn from the pool this level has unlocked. Capping the bucket is what makes "a hand never carries
    // more than three new guns of a class" structural rather than a matter of luck. Splitting it by size is
    // what lets a milestone hand promise a large gun to switch into on a hull with one large mount.
    private static List<UpgradableOption> NewGunOptions(List<string> ownedNames, int level, TurretSize size, Random rng)
    {
        var pool = WeaponType.NewWeaponPoolFor(level).Where(w => w.Size == size && !ownedNames.Contains(w.Name)).ToList();
        Shuffle(pool, rng);

        return [.. pool.Take(MaxNewWeaponCardsPerClass).Select(w => new UpgradableOption(w.Name, UpgradeOption.Damage, IsNewWeapon: true))];
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
