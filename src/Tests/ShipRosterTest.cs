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
        Assert.Equal(WeaponType.GravitonBurst.Name, Assert.Single(ShipType.Shadow.Loadout.Mounts).Weapon.Name);
    }

    [Fact]
    public void DiagnosticShips_AreShadowClonesWithOneWeaponEach()
    {
        string[] expectedWeapons =
        [
            WeaponType.LaserLance.Name, WeaponType.PrismLance.Name, WeaponType.RippleCutter.Name,
            WeaponType.Cyclotron.Name, WeaponType.GravitonBurst.Name, WeaponType.FlakCurtain.Name,
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
    public void EveryShipHasAtLeastOneFreeMountForANewWeapon()
    {
        // Progression promises a free slot for most new weapons, so no hull may start fully booked.
        foreach (var ship in ShipType.All)
            Assert.True(ship.Slots.Count > ship.Loadout.Mounts.Count, $"{ship.Name} starts with every mount filled");
    }

    [Fact]
    public void DiagnosticShips_FireTheirWeaponInAStartedWorld()
    {
        foreach (var ship in ShipType.DiagnosticShips)
        {
            var (em, _, _, _, _) = GameInitializer.Initialize(ship, new Vector2(1920f, 1024f));

            // One target straight ahead: inside every weapon's arc and range.
            var target = em.CreateEntity();
            em.AddComponent(target, new Position(new Vector2(0f, -100f)));
            em.AddComponent(target, new Velocity(Vector2.Zero));
            em.AddComponent(target, new Asteroid(IsSmall: false, Radius: 30f));

            var commands = new CommandBuffer();
            new TurretFiringSystem().Update(new WorldView(em), 1f / 120f, commands);
            commands.Apply(em);

            bool fired = em.GetEntitiesWithComponents<Ammo>().Count() > 0
                || em.GetEntitiesWithComponents<LaserBeam>().Count() > 0;
            Assert.True(fired, $"{ship.Name} started a game but fired nothing");
        }
    }
}
