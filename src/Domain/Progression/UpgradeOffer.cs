using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Who deals a level's upgrade offer, and what it may contain.
//
// The rules live here, in one place, because the same offer is dealt twice over: `LevelUpSystem` deals it
// when the player levels up, and a collected salvage capsule re-deals it (plans/REROLL_TOKENS.md). A re-deal
// that re-implemented the pool would drift the first time an offer rule was fixed, so a reroll cannot invent
// a card a level-up would have withdrawn — no card naming a cargo gun, no projectile-speed card on a hitscan
// beam, no "+1 shot" past the guided volley budget (R17/R18), no missile-health card past its ceiling (R19),
// no weapon the ship already owns, at most three new-weapon cards of one class, a damage card sized by the
// mount's class (R33), and no Armor or ArmorPierce card below level 5 (plans/ARMOR_AND_PIERCE.md). Balance
// rules R6/R10/R18/R19/R29/R33 all say a card must never be a lie; that applies to a re-deal too.
//
// What the offer is *made of* is the other half of the job (plans/UPGRADE_BUNDLE_PAIRS.md, which supersedes
// the hand-shape half of plans/UPGRADE_BUNDLES.md). The player is dealt five bundles and takes one; a bundle
// is two cards taken together — one **powerful** (a kill gun's upgrade, a new large gun, a defence card) and
// one **weak** (a chip gun's upgrade, a new small gun, an engine or utility card, `Hp`). The split is R25's
// kill/support axis made visible, so every level grows both classes and the player chooses which to grow
// more. The pools are built in the order the promises are made, so "a bundle carries a new large gun" and
// "both classes grow" fall out of the ordering instead of a special case, and a pool that runs dry simply
// deals fewer bundles — never a powerful+powerful or weak+weak pair.
public static class UpgradeOffer
{
    // A level is dealt five bundles and takes one of them (plans/UPGRADE_BUNDLE_PAIRS.md §1). The XP cost is
    // deliberately unchanged, so two cards a level is the thing playtest is judging.
    public const int BundlesPerLevel = 5;

    const int MilestoneLevelInterval = 5;
    // Armor and its counter unlock with tier 2 — the same beat as the `UnlockLevel: 5` guns and the first
    // milestone hand (plans/ARMOR_AND_PIERCE.md). Before it, no bundle may deal either card.
    const int ArmorUnlockLevel = 5;
    // The shield pool unlocks a tier later than armor (plans/SHIELDS.md): the same beat as the `UnlockLevel:
    // 10` guided guns, which is where a run starts meeting fire heavy enough for a pool to be worth banking.
    const int ShieldUnlockLevel = 10;
    // A hand never carries more than three new weapons of one class. Whether they fit a free hull mount is
    // the player's problem, not the hand's: a weapon that does not fit rides in the cargo until swapped in.
    const int MaxNewWeaponCardsPerClass = 3;
    // How often one gun may be named on one side of the five bundles. The strong side may name a kill gun
    // three times, because its four legal stat cards are exactly the variety a build decision is made of;
    // two cards on one chip gun is the most the weak side will deal, since three was the hand this shape
    // exists not to deal (plans/UPGRADE_HAND_COMPOSITION.md).
    const int MaxStrongCardsPerGun = 3;
    const int MaxWeakCardsPerGun = 2;
    // Engine and vacuum cards are the weak half's filler. Two of them in a hand is enough, and they never
    // crowd out a card for a gun the ship is actually shooting with.
    const int MaxUtilityCards = 2;
    // The hull grows in one way per bundle: health is the weak half's step, armour and shield the powerful
    // half's, and neither is dealt twice across the five bundles.
    const int MaxHullHpCards = 1;
    const int MaxDefenceCards = 2;

    // The kinds the pools are filled from. The two weapon kinds are the balance rules' two classes
    // (R24/R25), keyed on the gun (`WeaponType.IsSupport`), never on the mount it happens to sit in.
    // `Damage*` holds the milestone-only damage cards (R33), split by class. The new-weapon kinds are split by
    // mount size because a milestone hand owes the player a large gun to switch into even when the hull has
    // exactly one large mount — and because size decides which half of a bundle the gun belongs to.
    enum CardKind { HullHp, HullDefence, Utility, SupportWeapon, KillWeapon, DamageKill, DamageSupport, NewLargeWeapon, NewSmallWeapon }

    public static bool IsMilestoneLevel(int level) => level % MilestoneLevelInterval == 0;

    /// The bundles for `level`, or null when nothing can be offered (a ship with no turret at all), which is
    /// the case where a level-up must not be consumed. `why` labels the diagnostic row.
    public static UpgradeBundle[]? Roll(WorldView view, Entity playerEntity, int level, string why)
    {
        var turrets = PlayerTurrets(view);
        if (turrets.Count == 0) return null;

        var buckets = SortIntoKinds(view, playerEntity, turrets, level);
        var bundles = DealBundles(buckets, IsMilestoneLevel(level), view, turrets);

        DiagnosticLogger.LogEvent("UPGRADE", $"{why} level={level} bundles: {FormatBundles(bundles)}");
        return [.. bundles];
    }

    /// Spends one salvage charge and re-deals the bundles that are on screen. False — and nothing spent —
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

        // A charge must never buy back exactly the bundles already on screen. One retry, then keep what came:
        // a pool smaller than the hand can genuinely re-deal the same cards, and looping on it would spend
        // unbounded RNG for nothing.
        if (SameBundles(dealt, pending.Value2.Bundles))
            dealt = Roll(view, playerEntity, level, "reroll retry") ?? dealt;

        em.AddComponent(playerEntity, stock.With(stock.Count - 1));
        em.AddComponent(pending.Entity, pending.Value2 with { Bundles = dealt });
        return true;
    }

    /// Which half of a bundle a card belongs to — the §2 table in one place. The axis is R25's kill/support
    /// split, plus the judgement calls: `Hp` is weak while `Armor` and `Shield` are powerful, a new gun is
    /// powerful at large size and weak at small, and a `MissileHealth` card follows its own gun's class.
    public static bool IsPowerfulCard(UpgradableOption card)
    {
        if (card.Stat is UpgradeOption.Armor or UpgradeOption.Shield) return true;
        if (card.Stat == UpgradeOption.Hp || card.WeaponName.Length == 0) return false;

        return WeaponType.FromName(card.WeaponName) is { IsSupport: false };
    }

    /// Defence cards carry the step of the level that dealt them (plans/UPGRADE_BUNDLE_PAIRS.md §2): the
    /// doubled step at a milestone, half of it otherwise. The scripted bench stamps the same way, so a benched
    /// `Armor:` entry at a milestone grants what it prints, exactly like a dealt card.
    public static UpgradableOption StampDefenceTier(UpgradableOption card, int level) =>
        card.Stat is UpgradeOption.Armor or UpgradeOption.Shield
            ? card with { Tier = IsMilestoneLevel(level) ? DefenceTier.Milestone : DefenceTier.Ordinary }
            : card;

    // One bundle per pair taken from the two pools, in pool order: the promises are already in the order the
    // pools were built, so a strong half that finds no compatible weak half is dropped rather than paired with
    // something it would disable. The final shuffle is why a promised card is never a promised position.
    static UpgradeBundle[] DealBundles(Dictionary<CardKind, List<UpgradableOption>> buckets, bool milestone, WorldView view, List<Turret> turrets)
    {
        var powerful = PowerfulPool(buckets, milestone, view, turrets);
        var weak = WeakPool(buckets, milestone, view, turrets);

        // A hull with nothing powerful to offer — no kill-class gun to upgrade and no free large mount for a
        // new one — still has to be dealt something, or the level it just earned would never resolve. Its one
        // bundle is two ordinary cards, taken together like any other.
        if (powerful.Count == 0) return FallbackPair(weak);

        var bundles = new List<UpgradeBundle>();
        foreach (var strong in powerful)
        {
            if (bundles.Count >= BundlesPerLevel) break;
            if (TryTakeWeakFor(weak, strong, out var weakCard)) bundles.Add(new UpgradeBundle(strong, weakCard));
        }

        Shuffle(bundles, view.Rng);
        return [.. bundles];
    }

    static UpgradeBundle[] FallbackPair(List<UpgradableOption> weak)
    {
        if (weak.Count < 2 || weak[0].IsNewWeapon && weak[1].IsNewWeapon) return [];

        DiagnosticLogger.LogEvent("UPGRADE", $"nothing powerful to offer: {FormatChoice(weak[0])} + {FormatChoice(weak[1])}");
        return [new UpgradeBundle(weak[0], weak[1])];
    }

    // The strong half of a milestone hand is the build beat: a new large gun to switch into, the kill gun's
    // `Damage` card, and the doubled defence tier. An ordinary hand deals the guns aboard plus the half tier
    // (plans/UPGRADE_BUNDLE_PAIRS.md §4), and never a `Damage` card: R33 keeps those milestone-only.
    static List<UpgradableOption> PowerfulPool(Dictionary<CardKind, List<UpgradableOption>> buckets, bool milestone, WorldView view, List<Turret> turrets)
    {
        var pool = new List<UpgradableOption>();
        if (milestone || HasFreeMountForMissingClass(view, turrets, TurretSize.Large))
            Drain(pool, buckets[CardKind.NewLargeWeapon], MaxNewWeaponCardsPerClass, MaxStrongCardsPerGun);

        Drain(pool, buckets[milestone ? CardKind.DamageKill : CardKind.KillWeapon], BundlesPerLevel, MaxStrongCardsPerGun);
        Drain(pool, buckets[CardKind.HullDefence], MaxDefenceCards, MaxStrongCardsPerGun);
        return pool;
    }

    // The weak half at a milestone is a new small gun or the chip line's `Damage` card. When those run dry the
    // ordinary weak pool follows — a chip gun's upgrade, then engine cards, then `Hp` — rather than dealing a
    // pair of two powerful halves or a short hand. Order is the whole rule: what is promised comes first.
    static List<UpgradableOption> WeakPool(Dictionary<CardKind, List<UpgradableOption>> buckets, bool milestone, WorldView view, List<Turret> turrets)
    {
        var pool = new List<UpgradableOption>();
        if (milestone)
        {
            Drain(pool, buckets[CardKind.NewSmallWeapon], MaxNewWeaponCardsPerClass, MaxWeakCardsPerGun);
            Drain(pool, buckets[CardKind.DamageSupport], BundlesPerLevel, MaxWeakCardsPerGun);
        }
        else if (HasFreeMountForMissingClass(view, turrets, TurretSize.Small))
            Drain(pool, buckets[CardKind.NewSmallWeapon], MaxNewWeaponCardsPerClass, MaxWeakCardsPerGun);

        Drain(pool, buckets[CardKind.SupportWeapon], BundlesPerLevel, MaxWeakCardsPerGun);
        Drain(pool, buckets[CardKind.Utility], MaxUtilityCards, MaxWeakCardsPerGun);
        Drain(pool, buckets[CardKind.HullHp], MaxHullHpCards, MaxWeakCardsPerGun);
        return pool;
    }

    // Take cards of one kind into a pool, in bucket order (already shuffled), stopping at `maxFromKind` and
    // skipping a card that would take the same gun past `maxPerGun`. A skipped card leaves the deal: it is
    // over the cap, not waiting for a later pool.
    static void Drain(List<UpgradableOption> pool, List<UpgradableOption> bucket, int maxFromKind, int maxPerGun)
    {
        int taken = 0;
        while (bucket.Count > 0 && taken < maxFromKind)
        {
            var card = bucket[0];
            bucket.RemoveAt(0);
            if (NamesGunTimes(pool, card.WeaponName, maxPerGun)) continue;

            pool.Add(card);
            taken++;
        }
    }

    static bool NamesGunTimes(List<UpgradableOption> pool, string weaponName, int maxPerGun) =>
        weaponName.Length > 0 && pool.Count(other => other.WeaponName == weaponName) >= maxPerGun;

    // Take the first weak half that leaves the bundle carrying one new gun at most. Two new guns would need two
    // mount trips at once, and the mount screen settles one weapon. Nothing else can go wrong inside a pair:
    // the weak half is applied **before** the strong half is mounted, so an upgrade spent on a gun that the new
    // gun then displaces rides into cargo with that gun instead of vanishing (plans/UPGRADE_BUNDLE_PAIRS.md §3.7).
    static bool TryTakeWeakFor(List<UpgradableOption> weak, UpgradableOption powerful, out UpgradableOption taken)
    {
        int index = weak.FindIndex(candidate => !(powerful.IsNewWeapon && candidate.IsNewWeapon));
        if (index < 0)
        {
            taken = default;
            return false;
        }

        taken = weak[index];
        weak.RemoveAt(index);
        return true;
    }

    // plans/UPGRADE_HAND_COMPOSITION.md D5: on an ordinary level a new gun is dealt only when the hull mounts
    // no gun of that class at all and has a free mount of that size — a gun that cannot be mounted is cargo,
    // and cargo does not fire. Milestone hands deal new guns regardless of mounts, because there they are the
    // swap-into option the player asked for.
    static bool HasFreeMountForMissingClass(WorldView view, List<Turret> turrets, TurretSize size) =>
        (size == TurretSize.Large ? turrets.All(t => t.IsSupport) : turrets.All(t => !t.IsSupport))
        && TurretMount.FreeSlots(view, size).Count > 0;

    // The legal cards for one offer, sorted into kinds and shuffled inside each kind: random within a kind,
    // never across kinds, so a big pool of filler cannot crowd a promised card out of its pool. Stat cards are
    // built for both classes at every level — a milestone hand only falls back to them when its own two weak
    // kinds have run dry.
    static Dictionary<CardKind, List<UpgradableOption>> SortIntoKinds(WorldView view, Entity playerEntity, List<Turret> turrets, int level)
    {
        var owned = OwnedWeaponNames(view, playerEntity, turrets);
        var buckets = new Dictionary<CardKind, List<UpgradableOption>>
        {
            [CardKind.HullHp] = [new UpgradableOption("", UpgradeOption.Hp)],
            [CardKind.HullDefence] = HullDefenceOptions(view, playerEntity, level),
            [CardKind.Utility] = UtilityOptions(),
            [CardKind.SupportWeapon] = [],
            [CardKind.KillWeapon] = [],
            [CardKind.DamageKill] = [],
            [CardKind.DamageSupport] = [],
            [CardKind.NewLargeWeapon] = NewGunOptions(owned, level, TurretSize.Large, view.Rng),
            [CardKind.NewSmallWeapon] = NewGunOptions(owned, level, TurretSize.Small, view.Rng)
        };

        foreach (var turret in turrets.DistinctBy(t => t.WeaponName))
            AddStatCards(buckets, turret, level);

        if (IsMilestoneLevel(level)) AddDamageCards(buckets, turrets);

        // The damage buckets keep the order they were built in — kill class before support class — which is
        // how a milestone hand is seen to grow both classes instead of leaving it to a draw.
        foreach (var (kind, bucket) in buckets)
            if (kind is not (CardKind.DamageKill or CardKind.DamageSupport))
                Shuffle(bucket, view.Rng);

        return buckets;
    }

    // The always-legal engine and vacuum cards. No ceiling can withdraw one, which is why the weak pool can
    // lean on this bucket and never runs dry while a bundle needs filling.
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
    // card per gun, one bucket per class: the kill card is a bundle's strong half and the chip card its weak
    // half, which is how a milestone hand grows both classes.
    static void AddDamageCards(Dictionary<CardKind, List<UpgradableOption>> buckets, List<Turret> turrets)
    {
        foreach (var turret in turrets.DistinctBy(t => t.WeaponName))
            buckets[turret.IsSupport ? CardKind.DamageSupport : CardKind.DamageKill]
                .Add(new UpgradableOption(turret.WeaponName, UpgradeOption.Damage));
    }

    // The powerful half's hull cards. `Armor` joins from level 5 (plans/ARMOR_AND_PIERCE.md) and `Shield` two
    // beats later at tier 4's unlock level (plans/SHIELDS.md), each stamped with the step of the level that
    // deals it. A defence card is withdrawn when its whole step no longer fits under its ceiling: at partial
    // headroom it would deliver less than it prints, which is the lie R10 refuses — and the honest fix is to
    // withdraw rather than to deal a smaller card, the same rule R19 applies to pierce and round health.
    static List<UpgradableOption> HullDefenceOptions(WorldView view, Entity playerEntity, int level)
    {
        var cards = new List<UpgradableOption>();
        var tier = IsMilestoneLevel(level) ? DefenceTier.Milestone : DefenceTier.Ordinary;

        if (level >= ArmorUnlockLevel && CurrentArmor(view, playerEntity) <= ArmorRules.ArmorCeiling - Step(UpgradeOption.Armor, tier))
            cards.Add(new UpgradableOption("", UpgradeOption.Armor, Tier: tier));
        if (level >= ShieldUnlockLevel && CurrentShieldMax(view, playerEntity) <= ShieldRules.ShieldCeiling - Step(UpgradeOption.Shield, tier))
            cards.Add(new UpgradableOption("", UpgradeOption.Shield, Tier: tier));

        return cards;
    }

    static int Step(UpgradeOption stat, DefenceTier tier) => UpgradeDefinition.DefenceStep(stat, tier);

    static int CurrentArmor(WorldView view, Entity playerEntity) =>
        view.TryGetComponent<Armor>(playerEntity, out var armor) ? armor.Value : 0;

    // A hull with no shield has a pool of 0, so the first card is always offerable at the gate.
    static int CurrentShieldMax(WorldView view, Entity playerEntity) =>
        view.TryGetComponent<Shield>(playerEntity, out var shield) ? shield.Max : 0;

    // The per-gun stat cards, gated exactly as they always were — a card the rules would refuse to honour
    // is never dealt, so an empty pool is honest rather than a bug.
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
    // what lets a milestone hand promise a large gun to switch into on a hull with one large mount, and which
    // half of the bundle the gun belongs to.
    private static List<UpgradableOption> NewGunOptions(List<string> ownedNames, int level, TurretSize size, Random rng)
    {
        var pool = WeaponType.NewWeaponPoolFor(level).Where(w => w.Size == size && !ownedNames.Contains(w.Name)).ToList();
        Shuffle(pool, rng);

        return [.. pool.Take(MaxNewWeaponCardsPerClass).Select(w => new UpgradableOption(w.Name, UpgradeOption.Damage, IsNewWeapon: true))];
    }

    static bool SameBundles(UpgradeBundle[] a, UpgradeBundle[] b) =>
        a.Length == b.Length && a.Zip(b).All(pair => pair.First == pair.Second);

    // Diagnostics only: SPACEVORS_DIAG_UPGRADES="RailGun,Hp,FireRate:MachineGun" — one entry per card granted,
    // so a level's bundle is a pair of entries: the strong card, then the weak one.
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

    public static string FormatChoice(UpgradableOption option)
    {
        if (option.IsNewWeapon) return $"new:{option.WeaponName}";
        if (option.Stat is UpgradeOption.Armor or UpgradeOption.Shield)
            return $"{FormatStat(option)}(+{Step(option.Stat, option.Tier)} {option.Tier.ToString().ToLowerInvariant()})";

        return FormatStat(option);
    }

    static string FormatBundles(IEnumerable<UpgradeBundle> bundles) =>
        string.Join(" | ", bundles.Select(bundle => $"{FormatChoice(bundle.Powerful)} + {FormatChoice(bundle.Weak)}"));

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
