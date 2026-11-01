using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// Cargo keeps a gun off the hull without losing what was upgraded on it, and the mount it lands in
// still decides the firing arc.
public class CargoTest
{
    [Fact]
    public void CarriedTurretComesBackWithItsUpgrades()
    {
        var (em, _, _) = WorldFactory.Create(ShipType.Fighter, WorldView.DefaultViewportSize, seed: 5);
        var view = new WorldView(em);
        Assert.True(TurretMount.TryFindSlot(view, TurretSlotName.MainGun, out var mainGun));

        string mountedName = em.GetComponent<Turret>(mainGun).WeaponName;
        var upgraded = em.GetComponent<Turret>(mainGun);
        var cargo = WeaponCargo.Empty.With(upgraded with { Weapon = upgraded.Weapon with { Damage = 40 } });

        Assert.True(cargo.Holds(mountedName));
        Assert.True(cargo.TryFind(mountedName, out var carried));
        Assert.False(cargo.Holds(WeaponType.RailGun.Name));

        TurretMount.Unmount(em, mainGun);
        Assert.False(em.HasComponent<Turret>(mainGun));

        var slot = view.GetComponent<TurretSlot>(mainGun).Definition;
        TurretMount.Mount(em, mainGun, slot, carried, Vector2.Zero, hullRotation: 0f);

        var back = em.GetComponent<Turret>(mainGun);
        Assert.Equal(40, back.Weapon.Damage);
        Assert.Equal(slot.ArcAngle, back.ArcAngle);
        Assert.False(back.IsEnemy);

        Assert.False(cargo.Without(mountedName).Holds(mountedName));
    }

    [Fact]
    public void FreshlyCarriedWeaponStartsAtItsBaseStats()
    {
        var carried = TurretMount.CarriedTurret(WeaponType.RailGun);

        Assert.Equal(WeaponType.RailGun.Name, carried.WeaponName);
        Assert.Equal(WeaponType.RailGun.Stats.Damage, carried.Weapon.Damage);
        Assert.Equal(WeaponType.RailGun.Range, carried.Range);
        Assert.Equal(WeaponType.RailGun.AutoTarget, carried.AutoTarget);
    }

    // R23 — one gun of each type aboard — has to see both halves of the ship: the turrets bolted to the
    // hull and the guns riding in cargo. A cargo↔mount move is a relocation, so it never reads as a
    // second copy; only a fresh weapon from a card can add one.
    [Fact]
    public void AlreadyAboard_SeesMountedGunsAndCargoAlike()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Fighter, WorldView.DefaultViewportSize, seed: 5);
        var view = new WorldView(em);

        string mounted = TurretMount.MountedSlots(view)[0].WeaponName;
        Assert.True(TurretMount.AlreadyAboard(view, mounted));
        Assert.False(TurretMount.AlreadyAboard(view, WeaponType.RailGun.Name));

        em.AddComponent(player, WeaponCargo.Empty.With(TurretMount.CarriedTurret(WeaponType.RailGun)));
        Assert.True(TurretMount.AlreadyAboard(view, WeaponType.RailGun.Name));

        // Relocating the cargo gun onto the hull keeps the ship holding exactly one of it.
        Assert.True(TurretMount.TryFindSlot(view, TurretSlotName.MainGun, out var mainGun));
        Assert.True(view.GetComponent<WeaponCargo>(player).TryFind(WeaponType.RailGun.Name, out var rail));
        TurretMount.Mount(em, mainGun, view.GetComponent<TurretSlot>(mainGun).Definition, rail, Vector2.Zero, hullRotation: 0f);

        Assert.True(TurretMount.AlreadyAboard(view, WeaponType.RailGun.Name));
        Assert.Single(TurretMount.MountedSlots(view), m => m.WeaponName == WeaponType.RailGun.Name);
    }
}
