using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Spacevors.Game;
using Xunit;

// Ship roster: Shadow's own weapon, plus the diagnostics-only Shadow copies on the select screen.
public class ShipRosterTest
{
    [Fact]
    public void Shadow_CarriesARealWeaponNotTheLoadTestSpray()
    {
        Assert.NotEmpty(ShipType.Shadow.Loadout.Mounts);
        Assert.All(ShipType.Shadow.Loadout.Mounts, m => Assert.Equal(WeaponType.RippleCutter.Name, m.Weapon.Name));
        Assert.DoesNotContain(ShipType.Shadow.Loadout.Mounts, m => m.Weapon.Name == WeaponType.LoadTestWeapon.Name);
    }

    [Fact]
    public void DiagnosticShips_AreShadowClonesWithOneWeaponEach()
    {
        string[] expectedWeapons =
        [
            WeaponType.LaserLance.Name, WeaponType.PrismLance.Name, WeaponType.RippleCutter.Name,
            WeaponType.Cyclotron.Name, WeaponType.GravitonBurst.Name, WeaponType.FlakCurtain.Name,
            WeaponType.MachineGun.Name, WeaponType.Shotgun.Name, WeaponType.RailGun.Name,
            WeaponType.AcidBubbleSpray.Name, WeaponType.InterceptorTurret.Name,
            WeaponType.SeekerPod.Name, WeaponType.Swarmlet.Name, WeaponType.HavocTorpedo.Name,
            WeaponType.LoadTestWeapon.Name,
        ];

        Assert.Equal(expectedWeapons, ShipType.DiagnosticShips.Select(s => Assert.Single(s.Loadout.Mounts).Weapon.Name));

        foreach (var ship in ShipType.DiagnosticShips)
        {
            // Only the weapon differs: same graphics, engines and hull as Shadow.
            Assert.Equal(ShipType.Shadow.TextureKey, ship.TextureKey);
            Assert.Equal(ShipType.Shadow.Engine, ship.Engine);
            Assert.Equal(ShipType.Shadow.Radius, ship.Radius);
            Assert.Equal(ShipType.Shadow.MaxHealth, ship.MaxHealth);
        }
    }

    [Fact]
    public void DiagnosticShips_AreNamedAfterTheirWeapon()
    {
        // The select screen and the stats screen show Name, so the copies must tell each other apart.
        var names = ShipType.DiagnosticShips.Select(s => s.Name).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names, name => Assert.StartsWith("Shadow ", name));
    }

    [Fact]
    public void EveryLoadoutFitsTheShipsOwnSlots()
    {
        foreach (var ship in ShipType.All)
            foreach (var mount in ship.Loadout.Mounts)
            {
                var slot = Assert.Single(ship.Slots, s => s.Name == mount.SlotName);
                Assert.True(TurretMount.Fits(slot, mount.Weapon), $"{ship.Name}: {mount.Weapon.Name} does not fit {slot.Name}");
            }
    }

    [Fact]
    public void AtMostOneHullStartsFullyBooked()
    {
        // A new weapon card is never a dead pick: a booked mount can be freed for free through the
        // cargo. Even so, only the brawler hull may fill every mount, so most hulls can take a new
        // gun straight into a slot.
        int fullyBooked = ShipType.All.Count(s => s.Loadout.Mounts.Count >= s.Slots.Count);

        Assert.True(fullyBooked <= 1, $"{fullyBooked} hulls start with every mount filled");
    }

    [Fact]
    public void DiagnosticShips_FireTheirWeaponInAStartedWorld()
    {
        foreach (var ship in ShipType.DiagnosticShips)
        {
            var (em, playerEntity, _, _, _) = GameInitializer.Initialize(ship, new Vector2(1920f, 1024f));

            // One rock straight ahead, clicked: inside every weapon's arc and range, and a rock is
            // only ever a target once the player picks it.
            var target = em.CreateEntity();
            em.AddComponent(target, new Position(new Vector2(0f, -100f)));
            em.AddComponent(target, new Velocity(Vector2.Zero));
            em.AddComponent(target, new Asteroid(IsSmall: false, Radius: 30f));
            em.AddComponent(playerEntity, new PrimaryTarget(target));

            var commands = new CommandBuffer();
            new TurretFiringSystem().Update(new WorldView(em), 1f / 120f, commands);
            commands.Apply(em);

            bool fired = em.GetEntitiesWithComponents<Ammo>().Count() > 0
                || em.GetEntitiesWithComponents<LaserBeam>().Count() > 0;
            Assert.True(fired, $"{ship.Name} started a game but fired nothing");

            // A guided gun's rounds leave with their lock on the rock the bench clicked: that lock is
            // the whole of its guidance, so a missile fired without one is a dumb round in a fancy skin.
            var gun = Assert.Single(ship.Loadout.Mounts).Weapon;
            if (gun.Stats.Homing)
            {
                int lockedOnTarget = 0;
                foreach (var (missileEntity, missile) in em.GetEntitiesWithComponents<Missile>())
                {
                    Assert.Equal(target, missile.LockedTarget);
                    lockedOnTarget++;
                }

                Assert.Equal(gun.Stats.PelletCount, lockedOnTarget);
            }
        }
    }
}
