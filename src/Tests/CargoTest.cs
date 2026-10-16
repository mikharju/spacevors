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
}
