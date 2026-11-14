using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// The grant of a new-weapon card — what the player actually receives when they take a bundle that offered a gun.
//
// In Domain rather than left in the app because the same grant runs twice over: the paused mount screen the
// player uses, and a perf scenario that never pauses and fits the gun into the first free mount. It is also the
// half of plans/PREUPGRADED_NEW_WEAPONS.md a test can reach without a window.
public static class NewWeaponGrant
{
    /// The gun as its card describes it — the table's gun carrying the upgrades the card printed — put into cargo,
    /// and those upgrades counted on the ship so the stats screen shows them and the next arriving gun is measured
    /// against them (D2). False and nothing written when the ship already owns that type: R23 lets it hold one gun
    /// of each type, on the hull or in cargo, and a dealt hand never names an owned gun, so this only bites for a
    /// scripted bench hand.
    ///
    /// Cargo rather than straight into a mount: the gun is already paid for, so backing out of the mount screen
    /// must not lose it, and one visit to that screen can then place every gun the level paid for.
    public static bool TryGrant(EntityManager em, Entity playerEntity, UpgradableOption card, out Turret carried)
    {
        carried = default;
        if (WeaponType.FromName(card.WeaponName) is not { } gun) return false;

        var view = new WorldView(em);
        if (TurretMount.AlreadyAboard(view, gun.Name)) return false;

        carried = WeaponUpgrade.ArrivingTurret(gun, card.PreApplied);

        var counts = em.TryGetComponent<UpgradeCounts>(playerEntity, out var held) ? held : UpgradeCounts.Empty;
        em.AddComponent(playerEntity, WeaponUpgrade.WithArrivingUpgrades(counts, card.PreApplied, gun.Name));

        var cargo = em.TryGetComponent<WeaponCargo>(playerEntity, out var weapons) ? weapons : WeaponCargo.Empty;
        em.AddComponent(playerEntity, cargo.With(carried));
        return true;
    }
}
