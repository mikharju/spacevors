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
// "both classes grow" fall out of the ordering instead of a special case. A side that runs dry repeats a card
// it has already dealt rather than dealing a short hand (plans/UPGRADE_BUNDLE_DUPLICATES.md) — never a
// powerful+powerful or weak+weak pair, never the same pair twice, and never the same **build** of a new gun
// twice. A bundle may carry two new guns at once, a large one and a small one, and a new gun dealt to a hull
// that cannot fit one arrives carrying upgrades — which is what makes it a second build of that gun rather than
// the same card again (plans/PREUPGRADED_NEW_WEAPONS.md).
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
    // A hand never carries more than three new weapons of one class. Whether they fit a free hull mount used to
    // be the player's problem and nothing else: a weapon that does not fit rides in the cargo until swapped in.
    // On a hull that cannot fit one it now also arrives carrying upgrades (plans/PREUPGRADED_NEW_WEAPONS.md), so
    // the same unowned gun can be on offer two or three times as two different builds of itself.
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

    // Whether pierce cards may be dealt at all — the same beat as tier-2 hulls and the first milestone hand
    // (plans/ARMOR_AND_PIERCE.md). One home for the gate, because a gun's legal upgrade set is asked about both by
    // the stat cards and by the upgrades an arriving gun carries.
    public static bool PierceUnlockedAt(int level) => level >= ArmorUnlockLevel;

    /// The bundles for `level`, or null when nothing can be offered (a ship with no turret at all), which is
    /// the case where a level-up must not be consumed. `why` labels the diagnostic row.
    public static UpgradeBundle[]? Roll(WorldView view, Entity playerEntity, int level, string why)
    {
        var turrets = PlayerTurrets(view);
        if (turrets.Count == 0) return null;

        var buckets = SortIntoKinds(view, playerEntity, turrets, level);
        var bundles = DealBundles(buckets, IsMilestoneLevel(level), view, playerEntity, turrets, level);

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

    /// Defence cards carry the step of the level that dealt them (plans/UPGRADE_BUNDLE_PAIRS.md §2): the doubled
    /// step at a milestone, half of it otherwise. A milestone defence card is never also super, because the super
    /// step and the milestone step are the same number and a card that prints one ladder twice is a lie
    /// (plans/SUPER_UPGRADES.md D3). The scripted bench stamps the same way, so a benched `Armor:` entry at a
    /// milestone grants what it prints, exactly like a dealt card.
    public static UpgradableOption StampDefenceTier(UpgradableOption card, int level)
    {
        if (card.Stat is not (UpgradeOption.Armor or UpgradeOption.Shield)) return card;

        bool milestone = IsMilestoneLevel(level);
        return card with
        {
            Tier = milestone ? DefenceTier.Milestone : DefenceTier.Ordinary,
            Power = milestone ? CardPower.Base : card.Power
        };
    }

    // The hand: the distinct pairs the two pools allow, topped up from its own halves when a side ran dry,
    // then spiked and shuffled — which is why a promised card is never a promised position.
    static UpgradeBundle[] DealBundles(Dictionary<CardKind, List<UpgradableOption>> buckets, bool milestone, WorldView view, Entity playerEntity, List<Turret> turrets, int level)
    {
        var powerful = PowerfulPool(buckets, milestone, view, turrets);
        var weak = WeakPool(buckets, milestone, view, turrets);

        // A hull with nothing powerful to offer — no kill-class gun to upgrade and no free large mount for a
        // new one — still has to be dealt something, or the level it just earned would never resolve. Its first
        // bundle is two ordinary cards, taken together like any other.
        var bundles = powerful.Count == 0 ? FallbackPair(weak).ToList() : DealDistinct(powerful, weak);

        TopUp(bundles, powerful, weak);
        MarkSuperHalves(bundles, view, playerEntity, turrets, level);
        Shuffle(bundles, view.Rng);
        return [.. bundles];
    }

    // One bundle per pair of fresh cards, in pool order: the promises are already in the order the pools were
    // built, so a strong half that finds no compatible weak half is dropped rather than paired with something it
    // would disable. Whatever the pass did not reach stays in the two pools for the fill pass below.
    static List<UpgradeBundle> DealDistinct(List<UpgradableOption> powerful, List<UpgradableOption> weak)
    {
        var bundles = new List<UpgradeBundle>();
        while (bundles.Count < BundlesPerLevel && powerful.Count > 0)
        {
            var strong = powerful[0];
            powerful.RemoveAt(0);
            if (TryTakeWeak(weak, out var weakCard)) bundles.Add(new UpgradeBundle(strong, weakCard));
        }

        return bundles;
    }

    // Fill out the rest of the hand (plans/UPGRADE_BUNDLE_DUPLICATES.md §3). A card still sitting in a pool is
    // offered before one already on the table, so a genuinely new pair always beats a repeat, and a half only
    // repeats once the pools hold nothing that can still pair with it. That is the §3.1 formula — the hand is
    // five whenever the two sides can name five different pairs — and there is no copy cap: the refusals below
    // are the whole bound.
    static void TopUp(List<UpgradeBundle> hand, List<UpgradableOption> strongLeft, List<UpgradableOption> weakLeft)
    {
        while (hand.Count < BundlesPerLevel)
        {
            if (NextPair(hand, strongLeft, weakLeft) is not { } bundle) return;
            hand.Add(bundle);
            TakeFrom(strongLeft, bundle.Powerful);
            TakeFrom(weakLeft, bundle.Weak);
        }
    }

    static UpgradeBundle? NextPair(List<UpgradeBundle> hand, List<UpgradableOption> strongLeft, List<UpgradableOption> weakLeft)
    {
        foreach (var strong in Candidates(hand, strongLeft, powerful: true))
            foreach (var weak in Candidates(hand, weakLeft, powerful: false))
            {
                if (strong == weak) continue;                                     // one card, granted twice
                if (strong.IsNewWeapon && IsDealt(hand, strong)) continue;        // one build of a gun per hand
                if (weak.IsNewWeapon && IsDealt(hand, weak)) continue;            // — per build, not per gun
                if (hand.Any(dealt => dealt.Powerful == strong && dealt.Weak == weak)) continue;

                return new UpgradeBundle(strong, weak);
            }

        return null;
    }

    // The cards still in a pool come first — they carry no copies yet — then the hand's own halves from the
    // least offered upward, ties keeping the order they were dealt in. That order is what makes a new pair win
    // over a repeat, and what spreads a repeat over a side (2 + 2 + 1) instead of stacking five copies of the
    // first card. `OrderBy` is a stable sort, so the whole pass is deterministic and draws no RNG.
    static IOrderedEnumerable<UpgradableOption> Candidates(
        List<UpgradeBundle> hand, List<UpgradableOption> leftovers, bool powerful)
    {
        List<UpgradableOption> cards = [.. leftovers, .. hand.Select(bundle => powerful ? bundle.Powerful : bundle.Weak)];
        return cards.OrderBy(card => hand.Count(dealt => (powerful ? dealt.Powerful : dealt.Weak) == card));
    }

    static bool IsDealt(List<UpgradeBundle> hand, UpgradableOption card) =>
        hand.Any(dealt => dealt.Powerful == card || dealt.Weak == card);

    static void TakeFrom(List<UpgradableOption> pool, UpgradableOption card)
    {
        int index = pool.IndexOf(card);
        if (index >= 0) pool.RemoveAt(index);
    }

    static UpgradeBundle[] FallbackPair(List<UpgradableOption> weak)
    {
        if (weak.Count < 2 || weak[0].IsNewWeapon && weak[1].IsNewWeapon) return [];

        DiagnosticLogger.LogEvent("UPGRADE", $"nothing powerful to offer: {FormatChoice(weak[0])} + {FormatChoice(weak[1])}");
        return [new UpgradeBundle(weak[0], weak[1])];
    }

    // Spike the hand (plans/SUPER_UPGRADES.md D1/D7). Two halves of it are super — the same upgrade at twice the
    // step — one on the powerful side and one on the weak side, and never both in one bundle, which is what
    // makes the spike a trade instead of a free bigger bundle. The pass runs on the finished hand, walking it in
    // deal order, so every pool rule above (promises, per-class and per-gun caps, repeats) is untouched and the
    // pass needs no new pool. It draws no RNG of its own: the shuffle after it is what hides which bundle was
    // spiked. A side whose halves are all illegal at the doubled step simply gets no spike — never a card dealt
    // as a fraction of one (D6).
    static void MarkSuperHalves(List<UpgradeBundle> hand, WorldView view, Entity playerEntity, List<Turret> turrets, int level)
    {
        int strong = MarkOneBundleSuper(hand, view, playerEntity, turrets, level, powerful: true, from: 0);

        // A one-bundle hand cannot satisfy "in two different bundles", so it carries its one spike (the fallback
        // hull is the weakest case in the game and deserves it) and nothing else.
        if (hand.Count < 2) return;

        // Start past the spiked bundle and wrap, so the two spikes land on different bundles by construction.
        MarkOneBundleSuper(hand, view, playerEntity, turrets, level, powerful: false, from: strong + 1);
    }

    static int MarkOneBundleSuper(
        List<UpgradeBundle> hand, WorldView view, Entity playerEntity, List<Turret> turrets, int level, bool powerful, int from)
    {
        for (int step = 0; step < hand.Count; step++)
        {
            int at = (from + step) % hand.Count;
            var bundle = hand[at];
            var card = powerful ? bundle.Powerful : bundle.Weak;

            // Grading a card makes it a **different** card, so spiking a half the fill pass had already repeated
            // would give one gun a fourth distinct card on a side capped at three, and would hide the repeat a
            // thin pool is supposed to show. The spike goes to a half that stands alone on its side.
            if (CopiesOnSide(hand, card, powerful) > 1) continue;
            if (AsSuper(card, view, playerEntity, turrets, level) is not { } spiked) continue;

            hand[at] = powerful ? bundle with { Powerful = spiked } : bundle with { Weak = spiked };
            return at;
        }

        return -1;
    }

    static int CopiesOnSide(List<UpgradeBundle> hand, UpgradableOption card, bool powerful) =>
        hand.Count(bundle => (powerful ? bundle.Powerful : bundle.Weak) == card);

    /// This card at twice the step, or null when the doubled step is not one the rules could honour: no bigger
    /// arriving set for a new gun, a volley with no room for two more shots, a chip whose doubled range would go
    /// through its class roof, a round or a hull at its ceiling. Skipping is the shipped rule for a step that
    /// does not fit — withdraw rather than relabel (plans/UPGRADE_BUNDLE_PAIRS.md Shipped deviation 2).
    static UpgradableOption? AsSuper(UpgradableOption card, WorldView view, Entity playerEntity, List<Turret> turrets, int level)
    {
        // A super never stacks on a card that already carries a milestone-sized step (D3): a `Damage` card, which
        // R33 keeps milestone-only, and defence at a milestone. An ordinary level's defence card doubles into the
        // milestone step, so the two step ladders meet at one number and the ceilings (12 / 32) still bound it.
        if (card.IsSuper || (card.Stat == UpgradeOption.Damage && !card.IsNewWeapon)) return null;

        if (card.Stat is UpgradeOption.Armor or UpgradeOption.Shield)
        {
            if (IsMilestoneLevel(level)) return null;
            var graded = card with { Power = CardPower.Super };
            return DefenceFitsHeadroom(graded, view, playerEntity) ? graded : null;
        }

        if (card.IsNewWeapon) return WithBiggerArrivingSet(card, view, playerEntity, level);

        // `Hp` and the engine and vacuum cards have no ceiling to break, so they are always eligible.
        if (card.WeaponName.Length == 0) return card with { Power = CardPower.Super };

        var live = turrets.Find(t => t.WeaponName == card.WeaponName);
        if (live.WeaponName != card.WeaponName) return null;     // a card naming a gun that is no longer aboard

        return WeaponUpgrade.LegalStats(WeaponType.FromName(card.WeaponName), live, PierceUnlockedAt(level), CardPower.Super)
            .Contains(card.Stat)
                ? card with { Power = CardPower.Super }
                : null;
    }

    // The same new gun, arriving with as much as the weakest thing it would displace instead of half of it
    // (plans/SUPER_UPGRADES.md D5). Only worth marking when that is strictly more upgrades: a gun that already
    // arrived carrying everything it may carry cannot be spiked, and "super" over the same set would be a lie.
    static UpgradableOption? WithBiggerArrivingSet(UpgradableOption card, WorldView view, Entity playerEntity, int level)
    {
        if (WeaponType.FromName(card.WeaponName) is not { } gun) return null;

        int carried = WeaponUpgrade.StatsOf(card.PreApplied).Length;
        int room = ArrivingUpgrades(view, playerEntity, gun.Size, CardPower.Super);
        if (room <= carried) return null;

        // Re-sampled rather than extended: a super gun is a **bigger build** of that gun, and the card prints
        // its own set and its own numbers, so the two offers of one gun can never be read as the same one.
        var bigger = ArrivingSet(gun, room, level, view.Rng);
        return WeaponUpgrade.StatsOf(bigger).Length > carried
            ? card with { Power = CardPower.Super, PreApplied = bigger }
            : null;
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

    // The weak half a strong half pairs with, in pool order — the order is the promise, so the first card still
    // in the weak pool is the one that pairs. A bundle may carry two new guns (plans/PREUPGRADED_NEW_WEAPONS.md
    // D5): a new large gun is always the strong half and a new small one always the weak, so a pair of two new
    // guns is still one of each side, and both are granted — the mount screen places one and leaves the other in
    // cargo, where the player may pick it up in the same visit because moving guns is free.
    static bool TryTakeWeak(List<UpgradableOption> weak, out UpgradableOption taken)
    {
        if (weak.Count == 0)
        {
            taken = default;
            return false;
        }

        taken = weak[0];
        weak.RemoveAt(0);
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
            [CardKind.NewLargeWeapon] = NewGunOptions(owned, level, TurretSize.Large, view, playerEntity, turrets),
            [CardKind.NewSmallWeapon] = NewGunOptions(owned, level, TurretSize.Small, view, playerEntity, turrets)
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

        if (level >= ArmorUnlockLevel)
        {
            var armor = new UpgradableOption("", UpgradeOption.Armor, Tier: tier);
            if (DefenceFitsHeadroom(armor, view, playerEntity)) cards.Add(armor);
        }
        if (level >= ShieldUnlockLevel)
        {
            var shield = new UpgradableOption("", UpgradeOption.Shield, Tier: tier);
            if (DefenceFitsHeadroom(shield, view, playerEntity)) cards.Add(shield);
        }

        return cards;
    }

    // A defence card is dealt only while its **whole** step fits under its ceiling — at partial headroom it would
    // deliver less than it prints, which is the lie R10 refuses, and the honest fix is to withdraw it rather than
    // deal a smaller card (the same rule R19 applies to pierce and round health). The step is read off the card,
    // so the rule is identical for the tier a level deals and for the super grade (plans/SUPER_UPGRADES.md D3).
    static bool DefenceFitsHeadroom(UpgradableOption defenceCard, WorldView view, Entity playerEntity) =>
        defenceCard.Stat == UpgradeOption.Armor
            ? CurrentArmor(view, playerEntity) <= ArmorRules.ArmorCeiling - defenceCard.Step.Additive
            : CurrentShieldMax(view, playerEntity) <= ShieldRules.ShieldCeiling - defenceCard.Step.Additive;

    static int CurrentArmor(WorldView view, Entity playerEntity) =>
        view.TryGetComponent<Armor>(playerEntity, out var armor) ? armor.Value : 0;

    // A hull with no shield has a pool of 0, so the first card is always offerable at the gate.
    static int CurrentShieldMax(WorldView view, Entity playerEntity) =>
        view.TryGetComponent<Shield>(playerEntity, out var shield) ? shield.Max : 0;

    // The per-gun stat cards, gated by `WeaponUpgrade.LegalStats` — the same legality an arriving upgrade is
    // sampled from, so a card the rules would refuse to honour is never dealt and an empty pool is honest rather
    // than a bug (R18 volley budget, R19 ceilings, R29 support roof, pierce from level 5). `Damage` is not in
    // this list: R33 keeps it milestone-only, and `AddDamageCards` builds it into its own buckets.
    static void AddStatCards(Dictionary<CardKind, List<UpgradableOption>> buckets, Turret turret, int level)
    {
        var cards = buckets[KindFor(turret)];

        foreach (var stat in WeaponUpgrade.LegalStats(WeaponType.FromName(turret.WeaponName), turret, PierceUnlockedAt(level), CardPower.Base))
            cards.Add(new UpgradableOption(turret.WeaponName, stat));
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

    // New-weapon cards for one mount class: anything the ship does not already own, mounted or in cargo, drawn
    // from the pool this level has unlocked. R23 cuts both ways here (plans/PREUPGRADED_NEW_WEAPONS.md D8): a gun
    // the ship owns is never dealt back as a new weapon in any other build — it reaches a hand as its own upgrade
    // card instead, and at a milestone as its `Damage` card. Capping the bucket is what makes "a hand never
    // carries more than three new guns of a class" structural rather than a matter of luck. Splitting it by size
    // is what lets a milestone hand promise a large gun to switch into on a hull with one large mount, and which
    // half of the bundle the gun belongs to.
    //
    // A gun dealt to a hull that cannot fit it arrives carrying upgrades (`ArrivingUpgrades` below), and the set
    // is part of the card — so the same unowned gun may be dealt again as a second build of itself while an
    // identical build is never dealt twice.
    private static List<UpgradableOption> NewGunOptions(
        List<string> ownedNames, int level, TurretSize size, WorldView view, Entity playerEntity, List<Turret> turrets)
    {
        var pool = WeaponType.NewWeaponPoolFor(level).Where(w => w.Size == size && !ownedNames.Contains(w.Name)).ToList();
        Shuffle(pool, view.Rng);

        int arriving = ArrivingUpgrades(view, playerEntity, size, CardPower.Base);
        var cards = new List<UpgradableOption>();
        for (int round = 0; round < MaxNewWeaponCardsPerClass && cards.Count < MaxNewWeaponCardsPerClass; round++)
        {
            foreach (var gun in pool)
            {
                if (cards.Count >= MaxNewWeaponCardsPerClass) break;

                var preApplied = ArrivingSet(gun, arriving, level, view.Rng);
                if (cards.Exists(card => card.WeaponName == gun.Name && card.PreApplied == preApplied)) continue;
                cards.Add(new UpgradableOption(gun.Name, UpgradeOption.Damage, IsNewWeapon: true, PreApplied: preApplied));
            }

            // A plain gun is dealt once per gun, exactly as it always was: only an arriving set makes a second
            // card for the same weapon a different offer.
            if (arriving == 0) break;
        }

        return cards;
    }

    // How many upgrades a new gun of this size arrives with (plans/PREUPGRADED_NEW_WEAPONS.md D1/D2): none while a
    // mount the gun fits is free, because a new gun that is pure addition is not a replacement and the punishing
    // case is only the one where it must displace something. Otherwise half, rounded up, of the upgrades on the
    // **least** upgraded gun standing in a mount this gun could take. "Least" is what stops a run of swap-after-
    // swap from compounding, and only mounted guns count: a cargo gun does not fire and is never offered upgrades.
    // A super card's gun arrives with the whole of that number instead of half (plans/SUPER_UPGRADES.md D5) —
    // which is what makes a spiked new-weapon card "this gun shows up as built as the weakest thing you own",
    // and the gun's own legal-stat count is still the bound on it.
    static int ArrivingUpgrades(WorldView view, Entity playerEntity, TurretSize size, CardPower power)
    {
        if (TurretMount.FreeSlots(view, size).Count > 0) return 0;

        var counts = view.TryGetComponent<UpgradeCounts>(playerEntity, out var held) ? held : UpgradeCounts.Empty;
        var displaceable = TurretMount.MountedSlots(view).Where(slot => TurretMount.Fits(slot.Slot.Size, size)).ToList();
        if (displaceable.Count == 0) return 0;

        int least = displaceable.Min(slot => WeaponUpgrade.CountOn(counts, slot.WeaponName));
        return power == CardPower.Super ? least : (least + 1) / 2;
    }

    // Which upgrades they are (D3): a random sample, without replacement, of the gun's own legal stat cards at its
    // table stats — the same gates that withdraw a bought card the rules could not honour (R18/R19/R29, pierce
    // from level 5). `Damage` joins the sample only at a milestone level, which keeps R33 whole: a damage step is a
    // milestone step, and a new gun with no free mount of its size is only ever dealt by a milestone hand. A gun
    // has a handful of legal stats, so a big count is capped by that set rather than repeating a stat: 0.5 × N is a
    // target, and R10 is the bound.
    static WeaponPreUpgrade ArrivingSet(WeaponType gun, int count, int level, Random rng)
    {
        if (count <= 0) return WeaponPreUpgrade.None;

        var legal = WeaponUpgrade.LegalStats(gun, TurretMount.CarriedTurret(gun), PierceUnlockedAt(level), CardPower.Base);
        if (IsMilestoneLevel(level)) legal.Add(UpgradeOption.Damage);
        Shuffle(legal, rng);

        return legal.Take(Math.Min(count, legal.Count))
            .Aggregate(WeaponPreUpgrade.None, (mask, stat) => mask | WeaponUpgrade.BitOf(stat));
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

    /// One `SPACEVORS_DIAG_UPGRADES` entry: a gun name (`RailGun`), a gun with the upgrades it arrives with
    /// (`RailGun+FireRate+Damage`), or a stat card (`FireRate:MachineGun`). On a stat card a trailing `*` benches
    /// the **super** grade — twice the step, exactly as a dealt super card grants it (plans/SUPER_UPGRADES.md D9);
    /// a benched new gun spells its arriving set with `+Stat`, so it needs no grade of its own. Public so a test
    /// can pin the syntax the bench depends on without setting process-wide environment variables.
    public static UpgradableOption? ParseEntry(string entry)
    {
        // A trailing `*` asks for the **super** grade of the card after it — the grade a dealt hand stamps on
        // two of its halves. A `Damage` card is never super (plans/SUPER_UPGRADES.md D3) and a bench that asks
        // for one is a bench of a card the deal would never show, so it is refused rather than quietly granted
        // at the base step.
        bool super = entry.EndsWith('*');
        string card = (super ? entry[..^1] : entry).Trim();

        // A script names a gun directly and ignores its unlock level: the script is a test bench. With `+Stat`
        // after the name it also says what that gun arrives carrying, which is how a scripted hand can put an
        // arriving build in front of the mount screen (plans/PREUPGRADED_NEW_WEAPONS.md).
        string[] pieces = card.Split('+', StringSplitOptions.TrimEntries);
        if (pieces[0] != WeaponType.LoadTestWeapon.Name && WeaponType.FromName(pieces[0]) is not null)
        {
            var mask = WeaponPreUpgrade.None;
            foreach (string piece in pieces[1..])
            {
                if (!Enum.TryParse<UpgradeOption>(piece, ignoreCase: true, out var arriving)) return null;
                if (WeaponUpgrade.BitOf(arriving) == WeaponPreUpgrade.None) return null;   // not an upgrade a gun carries
                mask |= WeaponUpgrade.BitOf(arriving);
            }

            return new UpgradableOption(pieces[0], UpgradeOption.Damage, IsNewWeapon: true, PreApplied: mask);
        }

        string[] statAndWeapon = card.Split(':');
        if (statAndWeapon.Length != 2 || !Enum.TryParse<UpgradeOption>(statAndWeapon[0], ignoreCase: true, out var stat))
            return null;
        if (super && stat == UpgradeOption.Damage) return null;

        return new UpgradableOption(statAndWeapon[1].Trim(), stat, Power: super ? CardPower.Super : CardPower.Base);
    }

    public static string FormatChoice(UpgradableOption option)
    {
        // The grade is printed first, so a `[UPGRADE]` row shows which halves of the hand were spiked, and a
        // defence row still prints the step it actually grants — the milestone tier and a super ordinary card
        // land on the same number, and the row says which one it was (R10).
        string grade = option.IsSuper ? "super " : "";
        if (option.IsNewWeapon) return $"{grade}new:{option.WeaponName}{FormatArriving(option.PreApplied)}";
        if (option.Stat is UpgradeOption.Armor or UpgradeOption.Shield)
            return $"{grade}{FormatStat(option)}(+{option.Step.Additive} {option.Tier.ToString().ToLowerInvariant()})";

        return grade + FormatStat(option);
    }

    // What a new gun arrives with, spelled for the `[UPGRADE]` rows and the mount log: nothing for a plain gun,
    // the stat names otherwise. No numbers — the card and the stats screen print those, from the stats themselves.
    public static string FormatArriving(WeaponPreUpgrade mask) =>
        mask == WeaponPreUpgrade.None
            ? ""
            : $"({string.Join("+", WeaponUpgrade.StatsOf(mask).Select(stat => stat.ToString()))})";

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
