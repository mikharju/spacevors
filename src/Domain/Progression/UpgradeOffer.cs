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
// past its ceiling (R19), no damage card on a support gun and no range card past a support gun's class
// roof (R29), no weapon the ship already owns, at most three new-weapon cards in a milestone
// hand. Balance rules R6/R10/R18/R19/R29 all say a card must never be a lie; that applies to a re-deal too.
public static class UpgradeOffer
{
    const int HandSize = 5;
    const int MilestoneLevelInterval = 5;
    // A milestone hand carries up to three new weapons. Whether they fit a free hull mount is the
    // player's problem, not the hand's: a weapon that does not fit rides in the cargo until swapped in.
    const int MilestoneNewWeaponCards = 3;

    public static bool IsMilestoneLevel(int level) => level % MilestoneLevelInterval == 0;

    /// The hand for `level`, or null when nothing can be offered (a ship with no turret at all), which is
    /// the case where a level-up must not be consumed. `why` labels the diagnostic row.
    public static UpgradableOption[]? Roll(WorldView view, Entity playerEntity, int level, string why)
    {
        var turrets = new List<Turret>();
        foreach (var t in view.GetEntitiesWithComponents<Turret>())
        {
            if (!t.Value1.IsEnemy) turrets.Add(t.Value1);
        }

        if (turrets.Count == 0) return null;

        var weaponNames = turrets.Select(t => t.WeaponName).Distinct().ToList();

        // Cargo weapons are owned but inactive: they are not offered stat upgrades (those come from
        // the turrets on the hull above) and they are not offered back as new weapons.
        var ownedNames = new List<string>(weaponNames);
        if (view.TryGetComponent<WeaponCargo>(playerEntity, out var cargo))
            ownedNames.AddRange(cargo.Weapons.Select(w => w.WeaponName));

        var allOptions = new List<UpgradableOption>();
        allOptions.Add(new UpgradableOption("", UpgradeOption.Hp));

        if (IsMilestoneLevel(level))
        {
            allOptions.AddRange(NewWeaponOptions(ownedNames, level, view.Rng));

            // R29: `Damage` is a kill-gun card. It is a flat +1 (`UpgradeStats.cs:21`), so on a 1-damage
            // support chip it behaves as a multiplier — one card would lift a support mount over the class
            // line the weapon table just drew, and R10's "+15-35%" guidance would be broken in the one
            // place it matters. A support gun buys growth with rate and reach instead.
            foreach (var turret in turrets.Where(t => !t.IsSupport))
                allOptions.Add(new UpgradableOption(turret.WeaponName, UpgradeOption.Damage));
        }
        else
        {
            foreach (var weapon in weaponNames)
            {
                var mounted = turrets.First(t => t.WeaponName == weapon);
                var stats = mounted.Weapon;

                // R18: a guided volley that has reached its round budget (R17) would be offered a
                // "+1 shot" card it cannot honour, so the card is withdrawn and the gun keeps the
                // other two. Guns whose volleys never grow always have a working rate card.
                if (!SalvoCardIsDead(stats))
                    allOptions.Add(new UpgradableOption(weapon, UpgradeOption.FireRate));

                // A hitscan beam has no travelling projectile: the card would multiply AmmoSpeed 0
                // by 1.3 and be a dead choice.
                if (!stats.Hitscan)
                    allOptions.Add(new UpgradableOption(weapon, UpgradeOption.ProjectileSpeed));

                // R19: only a guided round has hit points to raise, and only up to the ceiling, so the
                // card exists only for the guns that fire them and only while they can still take it.
                if (stats.CanToughenRound)
                    allOptions.Add(new UpgradableOption(weapon, UpgradeOption.MissileHealth));

                // R29: a support gun stops at its class roof (R24/R26). The Range card stretches the shot's
                // lifetime as well as its range, so withdrawing it at the roof caps the chip's live rounds
                // too — which is the same reason the card is withdrawn at R18/R19: never a card the rules
                // would refuse to honour.
                if (!mounted.IsSupport || mounted.Range * UpgradeDefinition.Range.Multiplier <= WeaponType.SupportRangeCeiling)
                    allOptions.Add(new UpgradableOption(weapon, UpgradeOption.Range));
            }

            allOptions.Add(new UpgradableOption("", UpgradeOption.PickupRadius));

            allOptions.Add(new UpgradableOption("", UpgradeOption.ForwardAcceleration));
            allOptions.Add(new UpgradableOption("", UpgradeOption.TurnSpeed));
            allOptions.Add(new UpgradableOption("", UpgradeOption.SideThrust));
            allOptions.Add(new UpgradableOption("", UpgradeOption.BackThrust));
        }

        Shuffle(allOptions, view.Rng);
        var shuffled = allOptions.ToArray();
        var choices = shuffled[..Math.Min(HandSize, shuffled.Length)];

        DiagnosticLogger.LogEvent("UPGRADE", $"{why} level={level} choices: {FormatChoices(choices)}");
        return choices;
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

    static string FormatChoices(UpgradableOption[] options) =>
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

    // New-weapon cards for a milestone hand: anything the ship does not already own, mounted or in
    // cargo, drawn from the pool that this level has unlocked.
    private static List<UpgradableOption> NewWeaponOptions(List<string> ownedNames, int level, Random rng)
    {
        var pool = WeaponType.NewWeaponPoolFor(level).Where(w => !ownedNames.Contains(w.Name)).ToList();
        Shuffle(pool, rng);

        var cards = new List<UpgradableOption>();
        foreach (var weapon in pool)
        {
            if (cards.Count >= MilestoneNewWeaponCards) break;
            cards.Add(new UpgradableOption(weapon.Name, UpgradeOption.Damage, IsNewWeapon: true));
        }

        return cards;
    }
}
