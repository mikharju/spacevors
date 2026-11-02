using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// The weapon balance rules from plans/WEAPON_BALANCE.md, read straight off the weapon table so the
// next gun added cannot quietly break them. LoadTestWeapon is not a gun — it is the perf scenarios'
// 8000-pellet spray — so it is outside every rule here.
public class WeaponBalanceTest
{
    static WeaponType[] Guns => [.. WeaponType.All.Where(w => w.Name != WeaponType.LoadTestWeapon.Name)];

    static float Dps(WeaponType w) => w.Stats.FireRate * w.Stats.PelletCount * w.Stats.Damage;

    // Delivery class: how much a shot can lose on the way to the target. A guided round is its own
    // class: it cannot lose the target, so it is priced by R15 instead of by the speed ladder.
    static string Delivery(WeaponType w) =>
        w.Stats.Hitscan ? "hitscan"
        : w.Stats.Homing ? "guided"
        : w.Stats.AmmoSpeed >= 2000f ? "fast"
        : w.Stats.AmmoSpeed >= 600f ? "medium"
        : "slow";

    // R6: the range on a card must be what the slowest round that gun fires can actually cross.
    [Fact]
    public void CardRangeIsReachableByTheSlowestAmmoItFires()
    {
        foreach (var w in Guns.Where(w => !w.Stats.Hitscan))
        {
            float worstReach = w.Stats.AmmoSpeed * (1f - TurretFiringSystem.AmmoSpeedVariation / 2f) * w.Stats.ShotLifetime;
            Assert.True(worstReach >= w.Range, $"{w.Name} promises {w.Range:0} px but its slowest round dies at {worstReach:0} px");
        }
    }

    // The reach floor: a gun shorter than the enemy's own firing ring cannot answer what shoots it.
    [Fact]
    public void NoGunReachesLessThanTheEnemyFiringRing()
    {
        foreach (var w in Guns)
            Assert.True(w.Range >= 600f, $"{w.Name} reaches only {w.Range:0} px");
    }

    // R7: rounds spawned per second from one mount stay bounded, so a loadout cannot blow up the
    // entity count the 10k-object goal is priced against.
    [Fact]
    public void EveryGunStaysInsideTheRoundRateBudget()
    {
        foreach (var w in Guns)
        {
            float rounds = w.Stats.FireRate * w.Stats.PelletCount;
            Assert.True(rounds <= 12f, $"{w.Name} fires {rounds:0.#} rounds a second from one mount");
        }
    }

    // R17: a guided round stays live for seconds instead of a blink, so the same mount may keep far
    // fewer of them in the air than of dumb ammo.
    [Fact]
    public void GuidedGunStaysInsideTheGuidedRoundBudget()
    {
        foreach (var w in Guns.Where(w => w.Stats.Homing))
        {
            float rounds = w.Stats.FireRate * w.Stats.PelletCount;
            Assert.True(rounds <= WeaponStats.HomingRoundBudget,
                $"{w.Name} puts {rounds:0.#} guided rounds a second into the air from one mount");
        }
    }

    // R15: a guided round cannot lose its target on the way, which is the most certain delivery in the
    // game — better than a beam's — so it must buy less than the cheapest gun it competes with **for the
    // same mount class**. The old version priced a missile against the band's cheapest *anything*, which
    // chained taxes: nerfing a support chip re-priced every missile in that band, and a chip is already
    // taxed by R1/R2. A missile competes with the guns of its own class, which is the comparison R3 makes.
    [Fact]
    public void GuidedGunDpsStaysUnderItsOwnBand()
    {
        foreach (var missile in Guns.Where(w => w.Stats.Homing))
        {
            var peers = Guns.Where(w => !w.Stats.Homing && w.Size == missile.Size
                && MathF.Abs(w.Range - missile.Range) <= 0.15f * missile.Range).ToList();
            Assert.NotEmpty(peers);

            var cheapest = peers[0];
            foreach (var peer in peers)
                if (Dps(peer) < Dps(cheapest)) cheapest = peer;

            Assert.True(Dps(missile) <= 0.8f * Dps(cheapest),
                $"{missile.Name} at {Dps(missile):0.#} dps is too certain beside {cheapest.Name} ({Dps(cheapest):0.#} dps)");
        }
    }

    // R16: a guided round's turn radius is what makes it dodgeable. Tighter than the turn radius of
    // what it hunts and a lock is a death sentence rather than a challenge. It is measured at the **launch**
    // speed: a round builds to its top speed, so its radius is tightest the moment it leaves the rack, and
    // that is the end of the flight the guarantee has to hold at.
    [Fact]
    public void GuidedRoundTurnsWiderThanAnythingItHunts()
    {
        float tightest = float.MaxValue;
        foreach (var type in EnemyShipType.All)
        {
            float radius = EnemyShipStats.SpeedFor(type, EnemyShipStats.MaxTier) / EnemyShipStats.TurnRateFor(type, EnemyShipStats.MaxTier);
            if (radius < tightest) tightest = radius;
        }

        foreach (var w in Guns.Where(w => w.Stats.Homing))
        {
            float launchRadius = w.Stats.AmmoSpeed * WeaponStats.MissileLaunchFraction / w.Stats.HomingTurnRate;
            float topRadius = w.Stats.AmmoSpeed / w.Stats.HomingTurnRate;
            Assert.True(launchRadius > tightest,
                $"{w.Name} turns inside {launchRadius:0} px just after launch, tighter than the {tightest:0} px a tier-{EnemyShipStats.MaxTier} hull can out-turn");
            Assert.True(topRadius > launchRadius, $"{w.Name}'s turn radius must widen as its speed builds");
        }
    }

    // R18: growing the volley is a privilege a guided gun has to be given, not the default — every
    // guided gun growing its salvo is how one loadout ends up with a swarm on screen. The growth also
    // has to have somewhere to go, so the card is never offered already dead.
    [Fact]
    public void OnlySomeGunsMayGrowTheirVolley()
    {
        var guided = Guns.Where(w => w.Stats.Homing).ToList();

        Assert.True(guided.Count(w => w.Stats.SalvoGrows) < guided.Count,
            "every guided gun grows its volley, so a missile loadout becomes a swarm by default");

        foreach (var w in guided.Where(w => w.Stats.SalvoGrows))
            Assert.True(w.Stats.CanGrowSalvo,
                $"{w.Name}'s +1 shot card starts life past the {WeaponStats.HomingRoundBudget:0.#} guided rounds/s budget");
    }

    // R1: instant delivery is the cheapest delivery, so a beam buys less damage than the slowest
    // travelling round that covers the same band. Guided rounds are excluded from the peer set: they
    // are the delivery class *above* beams in certainty, and R15 prices them against it.
    // Several guns can share a band's slowest ammo speed — ScatterCannon, Shotgun and AcidBubbleSpray
    // all throw 400 px/s rounds across the close band — so the benchmark is the *weakest* of them: a
    // beam must lose to every gun whose round travels less surely than its own, and the answer cannot
    // depend on the order the guns happen to be listed in `WeaponType.All`.
    [Fact]
    public void BeamGunDpsStaysUnderItsOwnBand()
    {
        foreach (var beam in Guns.Where(w => w.Stats.Hitscan))
        {
            var peers = Guns.Where(w => !w.Stats.Hitscan && !w.Stats.Homing && MathF.Abs(w.Range - beam.Range) <= 0.15f * beam.Range).ToList();
            Assert.NotEmpty(peers);

            float slowestAmmo = peers.Min(peer => peer.Stats.AmmoSpeed);
            var benchmark = peers.Where(peer => peer.Stats.AmmoSpeed <= slowestAmmo).OrderBy(Dps).First();

            Assert.True(Dps(beam) <= 0.8f * Dps(benchmark),
                $"{beam.Name} at {Dps(beam):0.#} dps is too strong beside {benchmark.Name} ({Dps(benchmark):0.#} dps with {benchmark.Stats.AmmoSpeed:0} px/s ammo)");
        }
    }

    // R3: a large mount is scarce, so a large gun of the same tier and delivery class must out-DPS
    // the best small gun of that class. Beams are exempt — R1 caps them from the other side.
    [Fact]
    public void LargeGunsEarnTheirMountWithDamage()
    {
        foreach (var group in Guns.Where(w => Delivery(w) != "hitscan").GroupBy(w => (w.UnlockLevel, Delivery(w))))
        {
            var small = group.Where(w => w.Size == TurretSize.Small).ToList();
            var large = group.Where(w => w.Size == TurretSize.Large).ToList();
            if (small.Count == 0 || large.Count == 0) continue;

            float weakestLarge = large.Min(Dps);
            float bestSmall = small.Max(Dps);
            Assert.True(weakestLarge >= 1.25f * bestSmall,
                $"{group.Key}: the weakest large gun is at {weakestLarge:0.#} dps, the best small one at {bestSmall:0.#}");
        }
    }

    // R4: tier 2 is a step up, never a leap. The floor exempts beams for the reason R3 does: their
    // tier-2 edge is reach and extra beams, not damage.
    [Fact]
    public void TierTwoIsSlightlyStrongerNotTwiceAsStrong()
    {
        foreach (var w in Guns.Where(w => w.UnlockLevel > 1))
        {
            var tierOne = Guns.Where(g => g.UnlockLevel == 1 && g.Size == w.Size).ToList();

            Assert.True(Dps(w) <= 1.4f * tierOne.Max(Dps), $"{w.Name} is more than 1.4x every tier-1 {w.Size} gun");

            // Beams and guided rounds buy delivery, not a DPS step, so the floor is not theirs: R1 and
            // R15 price them from underneath instead.
            if (w.Stats.Hitscan || w.Stats.Homing) continue;

            bool aStepUp = tierOne.Any(g => Dps(w) >= 1.15f * Dps(g) || (Dps(w) >= Dps(g) && w.Range >= 1.3f * g.Range));
            Assert.True(aStepUp, $"{w.Name} neither out-damages nor clearly outranges a tier-1 {w.Size} gun");
        }
    }

    // R9: only the deliberate heavy snipers may slew slower than a Balanced hull turns, which is
    // what drags their aim off target while the ship spins.
    [Fact]
    public void OnlyTheHeavyGunsSlewSlowerThanTheHullTurns()
    {
        int slowerThanHull = Guns.Count(w => w.Stats.TurnSpeed < EngineLayout.Balanced.TurnRate);

        Assert.True(slowerThanHull <= 2, $"{slowerThanHull} guns lose their target while a Balanced hull spins");
    }

    // R13: one gun per cell of (range band, delivery, fire rate, whether one shot carries several
    // pellets, mount size).
    [Fact]
    public void NoTwoGunsShareAnIdentityCell()
    {
        var cells = Guns.Select(w => (
            Band: w.Range < 750f ? "aura" : w.Range < 1100f ? "close" : w.Range < 1500f ? "mid" : "long",
            Delivery: Delivery(w),
            Rate: w.Stats.FireRate >= 4f ? "stream" : w.Stats.FireRate >= 1.5f ? "burst" : "volley",
            Shot: w.Stats.PelletCount > 1 ? $"{w.Stats.PelletCount} pellets" : "single",
            w.Size)).ToList();

        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    // R5: a gun that acquires its own targets stays at ≤ 4 damage per shot. Mines are 2 hp and the
    // smallest enemy hull 5, so a fatter round throws the rest of it away on whatever the targeter
    // happened to grab. Two guns are licensed above the cap — RailGun's 15 and HavocTorpedo's 10 — and
    // both fire so slowly that nothing else in the game could carry a round that big. `AutoTarget: false`
    // is not the licence: it only turns lead prediction off, so the rule is written around damage.
    [Fact]
    public void OnlyTwoGunsMayCarryABigRound()
    {
        string[] bigRoundLicence = [WeaponType.RailGun.Name, WeaponType.HavocTorpedo.Name];

        foreach (var w in Guns.Where(w => !bigRoundLicence.Contains(w.Name)))
            Assert.True(w.Stats.Damage <= 4, $"{w.Name} deals {w.Stats.Damage} damage per shot without a licence");
    }

    // R12: a bigger hull is a worse dodge, so the big ship may carry more firepower from the start
    // and the light one starts slightly behind the mediums.
    [Fact]
    public void StarterFirepowerFollowsHullSize()
    {
        float Firepower(ShipType ship) => ship.Loadout.Mounts.Sum(m => Dps(m.Weapon));

        float scout = Firepower(ShipType.Scout);
        float fighter = Firepower(ShipType.Fighter);
        float shadow = Firepower(ShipType.Shadow);
        float heavy = Firepower(ShipType.Heavy);

        Assert.True(scout < fighter && scout < shadow, $"the 46 px Scout ({scout:0.#}) should start behind the 58 px hulls ({fighter:0.#}, {shadow:0.#})");
        Assert.True(heavy > fighter && heavy > shadow, $"the 84 px Heavy ({heavy:0.#}) should start ahead of the mediums ({fighter:0.#}, {shadow:0.#})");
    }

    // R24: the support envelope. A Small mount's gun is chips, screens and area denial — it gets medium
    // DPS *or* medium reach, never both. The ceiling is 8 dps in the aura and close bands and 6 in the mid
    // band, where a chip already sees far enough to matter; the reach roof is the class roof, and the long
    // band's beam cap (R1) keeps its own counsel from there up. A guided small gun is priced by R15.
    [Fact]
    public void SupportGunsStayInsideTheirClassEnvelope()
    {
        foreach (var w in Guns.Where(w => w.IsSupport && !w.Stats.Homing))
        {
            float ceiling = w.Range < 1100f ? 8f : 6f;
            Assert.True(Dps(w) <= ceiling, $"{w.Name} is a support gun at {Dps(w):0.#} dps, over its {ceiling:0.#} dps band ceiling");
            Assert.True(w.Range <= WeaponType.SupportRangeCeiling, $"{w.Name} reaches {w.Range:0} px from a small mount");
        }
    }

    // R25: the step between the classes is what the player is meant to see. The best support mount tops out
    // at 8 dps and the weakest kill gun starts at 10.4, so a large mount buys at least 1.25x the kill at its
    // cheapest — and the top of the DPS table always belongs to a kill gun.
    [Fact]
    public void KillGunsHoldTheTopOfTheTable()
    {
        float bestSupport = Guns.Where(w => w.IsSupport && !w.Stats.Homing).Max(Dps);
        float weakestKill = Guns.Where(w => !w.IsSupport && !w.Stats.Homing).Min(Dps);

        Assert.True(weakestKill >= 1.25f * bestSupport,
            $"the strongest support gun ({bestSupport:0.#} dps) sits too close to the weakest kill gun ({weakestKill:0.#} dps)");

        var top = Guns[0];
        foreach (var w in Guns)
            if (Dps(w) > Dps(top)) top = w;

        Assert.False(top.IsSupport, $"{top.Name} is a support gun on top of the DPS table at {Dps(top):0.#} dps");
    }

    // R26: the long band belongs to the kill class. Reach past the visible half-width is bought with a big
    // round, not with a chip stream. (The other half of the rule — a small mount never takes the 3x clicked
    // reach premium — is in TurretFiringSystem, where the click path is built.)
    [Fact]
    public void NoSupportGunReachesTheLongBand()
    {
        foreach (var w in Guns.Where(w => w.IsSupport))
            Assert.True(w.Range < 1500f, $"{w.Name} reaches {w.Range:0} px from a small mount");
    }

    // R27: a support gun fires chips. One round of it cannot kill anything, so it can never be the round
    // that "should have been" the kill. The cap is on a shot, not a volley: ScatterCannon's 2-damage slugs
    // are kill fire because three of them arrive together at 18 dps, which R24 denies a support gun.
    [Fact]
    public void SupportGunsFireChips()
    {
        foreach (var w in Guns.Where(w => w.IsSupport))
            Assert.True(w.Stats.Damage <= 2, $"{w.Name} deals {w.Stats.Damage} damage a shot from a support mount");
    }

    // R28: missile defence is the support class's job, keyed on the mount class rather than a per-gun list,
    // so a chip bolted into a heavy mount screens too. A kill gun never swerves off the hull that is
    // killing it to chase a pebble, and a rack does not slew, so it could not track one if it tried.
    [Fact]
    public void OnlySupportGunsAreMissileDefence()
    {
        foreach (var w in Guns)
            Assert.Equal(w.IsSupport && !w.Stats.Homing, w.InterceptsRounds);
    }

    // R31: a kill comes mostly from the kill class. Against the same hull, the mounts a ship starts with
    // that can kill must out-DPS its screens by 1.25x: the support half of a starter loadout is there to
    // make the killing possible, not to do it.
    [Fact]
    public void StarterLoadoutsKillMostlyFromTheirKillGuns()
    {
        foreach (var ship in ShipType.All)
        {
            float kill = ship.Loadout.Mounts.Where(m => !m.Weapon.IsSupport).Sum(m => Dps(m.Weapon));
            float support = ship.Loadout.Mounts.Where(m => m.Weapon.IsSupport).Sum(m => Dps(m.Weapon));
            if (support == 0f) continue;

            Assert.True(kill >= 1.25f * support, $"{ship.Name}: {kill:0.#} dps of kill fire against {support:0.#} dps of support");
        }
    }
}
