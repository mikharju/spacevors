using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// How a hull mount aims: it slews inside its arc at the weapon's turn speed, holds fire until the
// aim is good enough, rests mid-arc with nothing to shoot, and is dragged off target by a hull that
// turns faster than the mount can follow.
public class TurretSlewTest
{
    const float Tick = 1f / 120f;

    // A full-turn arc keeps these cases about slewing; TurretFiringTest covers the real arcs.
    static readonly TurretSlotDefinition WideSlot = new("test_gun", TurretSize.Large, Vector2.Zero, ArcCenter: 0f, ArcAngle: Angle.FullTurn);

    [Fact]
    public void MountSlewsAtTheWeaponsTurnSpeed()
    {
        var (em, _, turret, _) = CreateMountedWorld(turnSpeedDeg: 60f);
        AddTargetAt(em, worldAngle: Angle.FromDeg(25f), distance: 300f);

        Step(em, ticks: 1);
        // 60°/s for a single 1/120 s tick.
        Assert.Equal(0.5f, Angle.ToDeg(em.GetComponent<TurretAim>(turret).Yaw), 3);

        Step(em, ticks: 120);
        var aim = em.GetComponent<TurretAim>(turret);
        Assert.True(MathF.Abs(Angle.Delta(aim.Yaw, Angle.FromDeg(25f))) <= Angle.FromDeg(3f), "mount should settle inside the acquire tolerance");
        Assert.True(aim.Parked);
    }

    [Fact]
    public void MountHoldsFireUntilItsAimIsGoodEnough()
    {
        var (em, _, turret, _) = CreateMountedWorld(turnSpeedDeg: 60f);
        AddTargetAt(em, worldAngle: Angle.FromDeg(25f), distance: 300f);

        Step(em, ticks: 10); // 5° of slew: still well outside the 8° firing band
        Assert.Equal(0, AmmoIn(em));
        Assert.False(em.GetComponent<TurretAim>(turret).Parked);

        Step(em, ticks: 60); // enough to walk the aim into the band
        Assert.True(AmmoIn(em) > 0, "the mount should have opened fire");
    }

    [Fact]
    public void HullTurningFasterThanTheMountDragsItsAimOffTarget()
    {
        var (em, player, turret, _) = CreateMountedWorld(turnSpeedDeg: 30f);
        AddTargetAt(em, worldAngle: 0f, distance: 300f);

        Spin(em, player, degreesPerTick: 0.75f, ticks: 120); // 90°/s hull vs a 30°/s gun

        float yaw = em.GetComponent<TurretAim>(turret).Yaw;
        Assert.True(MathF.Abs(yaw) > Angle.FromDeg(8f), $"a gun rated a third of the hull's turn rate should fall behind, yaw was {Angle.ToDeg(yaw):0.0}°");

        int ammoWhileSilenced = AmmoIn(em);
        Step(em, ticks: 20); // still spinning is not required: the aim is simply too far off
        Assert.Equal(ammoWhileSilenced, AmmoIn(em));

        Step(em, ticks: 260); // and once the hull holds still, the 30°/s mount walks the last 60° back onto target
        Assert.True(em.GetComponent<TurretAim>(turret).Parked);
        Assert.True(AmmoIn(em) > ammoWhileSilenced, "the mount should engage again once the hull stops");
    }

    [Fact]
    public void MountRatedAboveTheHullHoldsItsTargetThroughASpin()
    {
        var (em, player, turret, _) = CreateMountedWorld(turnSpeedDeg: 300f);
        AddTargetAt(em, worldAngle: 0f, distance: 300f);

        int fired = Spin(em, player, degreesPerTick: 0.75f, ticks: 120);

        Assert.True(em.GetComponent<TurretAim>(turret).Parked, "a fast mount should keep up with the hull instead of lagging");
        Assert.True(fired > 0, "a mount that keeps its aim fires even while the ship turns");
    }

    [Fact]
    public void WithoutATargetTheMountWalksBackToTheMiddleOfItsArc()
    {
        var (em, _, turret, _) = CreateMountedWorld(turnSpeedDeg: 60f);
        em.AddComponent(turret, new TurretAim(Angle.FromDeg(20f), Parked: false));
        em.AddComponent(turret, new Rotation(Angle.FromDeg(20f)));

        Step(em, ticks: 60);

        // It stops at the acquire tolerance rather than exactly mid-arc: the hysteresis that keeps a
        // parked mount from twitching.
        var aim = em.GetComponent<TurretAim>(turret);
        Assert.True(MathF.Abs(aim.Yaw) <= Angle.FromDeg(3f), $"mount should settle near mid-arc, was {Angle.ToDeg(aim.Yaw):0.0}°");
        Assert.True(aim.Parked);
    }

    [Fact]
    public void ParkedMountWithNothingToShootIssuesNoAimChanges()
    {
        var (em, _, turret, _) = CreateMountedWorld(turnSpeedDeg: 60f);
        var settledAim = em.GetComponent<TurretAim>(turret);
        var settledRotation = em.GetComponent<Rotation>(turret);

        Step(em, ticks: 10);

        Assert.Equal(settledAim, em.GetComponent<TurretAim>(turret));
        Assert.Equal(settledRotation, em.GetComponent<Rotation>(turret));
    }

    [Fact]
    public void TargetOutsideTheArcIsNotEngagedAndTheMountStaysMidArc()
    {
        // A 60° arc pointing ahead: a target off the ship's right flank is nobody's problem.
        var narrowSlot = new TurretSlotDefinition("test_gun", TurretSize.Large, Vector2.Zero, ArcCenter: 0f, ArcAngle: Angle.FromDeg(60f));
        var (em, _, turret, _) = CreateMountedWorld(narrowSlot, turnSpeedDeg: 300f);
        AddTargetAt(em, worldAngle: Angle.FromDeg(90f), distance: 300f);

        Step(em, ticks: 60);

        Assert.Equal(0f, em.GetComponent<TurretAim>(turret).Yaw, 3);
        Assert.Equal(0, AmmoIn(em));
    }

    // A hull carrying one mount with a MachineGun whose turn rating the test sets.
    static (EntityManager Em, Entity Player, Entity Turret, TurretSlotDefinition Slot) CreateMountedWorld(float turnSpeedDeg)
        => CreateMountedWorld(WideSlot, turnSpeedDeg);

    static (EntityManager Em, Entity Player, Entity Turret, TurretSlotDefinition Slot) CreateMountedWorld(TurretSlotDefinition slot, float turnSpeedDeg)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Rotation(0f));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        var slotEntity = em.CreateEntity();
        em.AddComponent(slotEntity, new TurretSlot(slot));

        var weapon = WeaponType.MachineGun with { Stats = WeaponType.MachineGun.Stats with { TurnSpeed = Angle.FromDeg(turnSpeedDeg) } };
        TurretMount.Mount(em, slotEntity, slot, weapon, Vector2.Zero, hullRotation: 0f);

        return (em, player, slotEntity, slot);
    }

    // A stationary asteroid at `worldAngle` (0 = straight up, which is forward for an unturned hull),
    // clicked as the primary target: a rock the player has not clicked is never worth firing at.
    static void AddTargetAt(EntityManager em, float worldAngle, float distance)
    {
        var target = em.CreateEntity();
        var direction = new Vector2(MathF.Sin(worldAngle), -MathF.Cos(worldAngle));
        em.AddComponent(target, new Position(direction * distance));
        em.AddComponent(target, new Velocity(Vector2.Zero));
        em.AddComponent(target, new Asteroid(IsSmall: false, Radius: 30f));

        if (em.GetEntitiesWithComponents<Player>().TryFirst(out var playerTuple))
            em.AddComponent(playerTuple.Entity, new PrimaryTarget(target));
    }

    // Turn the hull and step the firing system tick by tick; returns how many shots went off.
    static int Spin(EntityManager em, Entity player, float degreesPerTick, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            em.AddComponent(player, new Rotation(Angle.Normalize(em.GetComponent<Rotation>(player).Angle + Angle.FromDeg(degreesPerTick))));
            Step(em, ticks: 1);
        }

        return AmmoIn(em);
    }

    static void Step(EntityManager em, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            var commands = new CommandBuffer();
            new TurretFiringSystem().Update(new WorldView(em), Tick, commands);
            commands.Apply(em);
        }
    }

    static int AmmoIn(EntityManager em)
    {
        int shots = 0;
        foreach (var _ in em.GetEntitiesWithComponents<Ammo>()) shots++;
        return shots;
    }
}
