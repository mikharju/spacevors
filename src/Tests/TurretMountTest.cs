using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// Hull mounts: what fits where, how many are free, and what mounting/unmounting does to a slot.
public class TurretMountTest
{
    [Fact]
    public void LargeSlotsTakeAnyWeapon_LargeWeaponsNeedALargeSlot()
    {
        Assert.True(TurretMount.Fits(TurretSize.Large, TurretSize.Large));
        Assert.True(TurretMount.Fits(TurretSize.Large, TurretSize.Small));
        Assert.False(TurretMount.Fits(TurretSize.Small, TurretSize.Large));
    }

    [Fact]
    public void StartedWorld_HasOneMountEntityPerSlotOfTheHull()
    {
        foreach (var ship in ShipType.All)
        {
            var (em, _, _) = WorldFactory.Create(ship, WorldView.DefaultViewportSize, seed: 7);

            Assert.Equal(ship.Slots.Count, em.GetEntitiesWithComponents<TurretSlot>().Count());
            int mounted = 0;
            foreach (var _ in em.GetEntitiesWithComponents<TurretSlot, Turret>()) mounted++;
            Assert.Equal(ship.Loadout.Mounts.Count, mounted);
        }
    }

    [Fact]
    public void FreeSlots_CountWhatTheHullStillHasPerSize()
    {
        var (em, _, _) = WorldFactory.Create(ShipType.Fighter, WorldView.DefaultViewportSize, seed: 7);
        var view = new WorldView(em);

        // Fighter: four mounts, one large. The starting loadout takes the large one and a small one.
        Assert.Equal(2, TurretMount.FreeSlots(view).Count);
        Assert.Equal(2, TurretMount.FreeSlots(view, TurretSize.Small).Count);
        Assert.Empty(TurretMount.FreeSlots(view, TurretSize.Large));

        Assert.Equal(2, TurretMount.MountedSlots(view).Count);
    }

    [Fact]
    public void MountedSlotCarriesAimAndPosition_EmptySlotCarriesNeither()
    {
        var (em, player, _) = WorldFactory.Create(ShipType.Fighter, WorldView.DefaultViewportSize, seed: 7);
        var view = new WorldView(em);
        Assert.True(TurretMount.TryFindSlot(view, TurretSlotName.MainGun, out var mainGun));

        TurretMount.Unmount(em, mainGun);

        Assert.True(em.HasComponent<TurretSlot>(mainGun), "the mount point itself stays");
        Assert.False(em.HasComponent<Turret>(mainGun));
        Assert.False(em.HasComponent<TurretAim>(mainGun));
        Assert.False(em.HasComponent<Position>(mainGun));
        Assert.False(em.HasComponent<Rotation>(mainGun));

        var hullPos = em.GetComponent<Position>(player).Value;
        float hullRotation = em.GetComponent<Rotation>(player).Angle;
        var slotDefinition = em.GetComponent<TurretSlot>(mainGun).Definition;
        TurretMount.Mount(em, mainGun, slotDefinition, WeaponType.RailGun, hullPos, hullRotation);

        Assert.True(em.HasComponent<Turret>(mainGun));
        // A freshly mounted gun rests in the middle of its arc, not aimed at anything.
        Assert.True(em.GetComponent<TurretAim>(mainGun).Parked);
        Assert.Equal(0f, em.GetComponent<TurretAim>(mainGun).Yaw, 3);
        Assert.Equal(Angle.Normalize(hullRotation + slotDefinition.ArcCenter), em.GetComponent<Rotation>(mainGun).Angle, 3);
        Assert.Equal(TurretSize.Large, slotDefinition.Size);
    }
}
