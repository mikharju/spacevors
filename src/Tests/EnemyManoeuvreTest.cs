using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What each manoeuvre does to a hull's thrust (plans/ENEMY_BEHAVIORS.md B1-B4, B6). Every case is the
// pair that prices it: the same hull, same place, same tick, chasing and manoeuvring. A behaviour that
// changed nothing about the thrust would be invisible to the player, and these tests say so.
public class EnemyManoeuvreTest
{
    const float Dt = 1f / 60f;

    // Just on the swarmer's own firing ring (700 px at tier 0), so a chasing hull brakes and parks there.
    private static readonly Vector2 OnTheRing = new(0f, -700f);

    // Well outside it, where even a cut-off hull still has ground to cross.
    private static readonly Vector2 BeyondTheRing = new(-900f, 0f);

    private static (EntityManager Em, Entity Player) World(Vector2 playerVelocity)
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(playerVelocity));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(100, 100));
        return (em, player);
    }

    // The one thing a behaviour is: a field on the hull written at spawn. A test sets it the same way the
    // spawn system does, so the manoeuvre is exercised through the same code path the game uses.
    private static Entity Hull(EntityManager em, Vector2 at, EnemyBehavior behavior = EnemyBehavior.Chase, Vector2 holdBearing = default)
    {
        var hull = em.CreateEntity();
        EnemyShipFactory.AddComponents(em, hull, at, Vector2.Zero, 0f, 0f, EnemyShipType.Default, EnemyArmament.Guns);
        var ship = em.GetComponent<EnemyShip>(hull);
        em.AddComponent(hull, ship with { Behavior = behavior, HoldBearing = holdBearing });
        return hull;
    }

    private static Vector2 Thrust(EntityManager em, Entity hull)
    {
        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new EnemyShipSystem().Update(view, Dt, commands);
        commands.Apply(em);
        return em.TryGetComponent<Acceleration>(hull, out var accel) ? accel.Value : Vector2.Zero;
    }

    // B1: a spreading hull leaves the chase line and holds the lane its spawn bearing names. It is already
    // on its ring, where a chaser brakes and parks; the spread hull slides along to its own lane.
    [Fact]
    public void ASpreadHullSlidesToItsOwnLaneWhereAChaserParks()
    {
        var (em, _) = World(Vector2.Zero);
        Entity hull = Hull(em, OnTheRing, EnemyBehavior.Spread, holdBearing: new Vector2(1f, 0f));

        var thrust = Thrust(em, hull);
        Assert.True(thrust.X > 15f && thrust.Y > 30f, $"spread thrust {thrust} should slide toward the lane its bearing names");

        var (chaseEm, _) = World(Vector2.Zero);
        Entity chaser = Hull(chaseEm, OnTheRing);
        Assert.Equal(0f, Thrust(chaseEm, chaser).Magnitude, 3);
    }

    // The lane is **inside** the ring, which is the whole 2026-11-08 change: holding the ring put a spread
    // hull at the same 650-780 px as the rest of the swarm, and it read as a hull that had stopped to
    // think. Here the bearing points at the player, so the only way to the lane is inward — and the chaser
    // in the same place does not move at all.
    [Fact]
    public void ASpreadHullClosesIntoGunRangeWhereAChaserParks()
    {
        var (em, _) = World(Vector2.Zero);
        Entity hull = Hull(em, OnTheRing, EnemyBehavior.Spread, holdBearing: new Vector2(0f, -1f));

        var thrust = Thrust(em, hull);
        Assert.True(thrust.Y > 40f, $"a spread hull should close to its lane inside the ring, got {thrust}");

        var (chaseEm, _) = World(Vector2.Zero);
        Assert.Equal(0f, Thrust(chaseEm, Hull(chaseEm, OnTheRing)).Magnitude, 3);
    }

    // A flanker cannot outrun the player, so it must not have to: leading the lane is what keeps a hull
    // capped below the player's speed on its bearing instead of trailing their exhaust forever. The pair
    // here is the same hull with a still player and with one running crosswise — the moving case must put
    // more of its thrust along the player's heading.
    [Fact]
    public void ASpreadHullLeadsThePlayerIntoItsLane()
    {
        var crosswise = new Vector2(0f, 250f);

        var (stillEm, _) = World(Vector2.Zero);
        var still = Thrust(stillEm, Hull(stillEm, OnTheRing, EnemyBehavior.Spread, holdBearing: new Vector2(1f, 0f))).Normalized;

        var (leadEm, _) = World(crosswise);
        var lead = Thrust(leadEm, Hull(leadEm, OnTheRing, EnemyBehavior.Spread, holdBearing: new Vector2(1f, 0f))).Normalized;

        Assert.True(lead.Y > still.Y, $"a leading flanker should aim further along the player's heading: still {still}, leading {lead}");
        Assert.True(lead.X < still.X, $"and less of it should be spent on the bearing itself: still {still}, leading {lead}");
    }

    // B3: the dart never parks. Inside its own ring a chaser stops; a strafing hull keeps circling, and
    // the thrust is along the ring rather than at the player, which is what a hull whose gun *is* its
    // hull can afford only because the circle keeps the player in its firing line.
    [Fact]
    public void AStrafingHullKeepsMovingAlongItsRing()
    {
        var (em, _) = World(Vector2.Zero);
        Entity hull = Hull(em, OnTheRing, EnemyBehavior.Strafe);

        var thrust = Thrust(em, hull);
        Assert.True(thrust.X > 40f, $"strafe thrust {thrust} should run around the ring, not at the player");
        Assert.True(MathF.Abs(thrust.Y) < 12f, $"strafe thrust {thrust} should not be closing on or fleeing the player");
    }

    // B4: the cut-off hull aims where the player is going. Across the player's path it is the difference
    // between trailing their exhaust and meeting them.
    [Fact]
    public void ACutOffHullLeadsWhereThePlayerIsGoing()
    {
        var crosswise = new Vector2(0f, 250f);

        var (leadEm, _) = World(crosswise);
        Entity lead = Hull(leadEm, BeyondTheRing, EnemyBehavior.CutOff);
        var leadThrust = Thrust(leadEm, lead);
        Assert.True(leadThrust.Y > 20f, $"a cut-off hull should aim ahead of the player's path, got {leadThrust}");

        var (chaseEm, _) = World(crosswise);
        Entity chaser = Hull(chaseEm, BeyondTheRing);
        var chaseThrust = Thrust(chaseEm, chaser);
        Assert.True(chaseThrust.X > 40f, "a chaser closes on where the player is");
        Assert.True(MathF.Abs(chaseThrust.Y) < 1f, $"a chaser does not lead the player at all, got {chaseThrust}");
    }

    // B6: the ram never brakes. It is the one behaviour with no ring to hold, which is what makes it a
    // charge rather than a gunner that wandered inward.
    [Fact]
    public void ARammerNeverBrakesInsideItsRing()
    {
        var (em, _) = World(Vector2.Zero);
        Entity hull = Hull(em, new Vector2(0f, 400f), EnemyBehavior.Rammer);

        var thrust = Thrust(em, hull);
        Assert.True(thrust.Y < -40f, $"a ram charges straight in, got {thrust}");

        var (chaseEm, _) = World(Vector2.Zero);
        Entity chaser = Hull(chaseEm, new Vector2(0f, 400f));
        Assert.Equal(0f, Thrust(chaseEm, chaser).Magnitude, 3);
    }

    // B2: the gap is a behaviour property, not a property of ships. Behaving hulls keep room from each
    // other; the swarm's tight ball is left alone on purpose, because that ball is what the player's
    // PrismLance, FlakCurtain and RippleCutter are paid for.
    [Fact]
    public void BehavingHullsKeepAGapAndTheSwarmDoesNot()
    {
        var leftAt = new Vector2(0f, -1200f);
        var rightAt = new Vector2(60f, -1200f);

        // Each hull on its own, at the very same places, so the difference below is only the neighbour.
        var (leftAloneEm, _) = World(Vector2.Zero);
        var leftAlone = Thrust(leftAloneEm, Hull(leftAloneEm, leftAt, EnemyBehavior.Strafe));
        var (rightAloneEm, _) = World(Vector2.Zero);
        var rightAlone = Thrust(rightAloneEm, Hull(rightAloneEm, rightAt, EnemyBehavior.Strafe));

        var (togetherEm, _) = World(Vector2.Zero);
        Entity left = Hull(togetherEm, leftAt, EnemyBehavior.Strafe);
        Entity right = Hull(togetherEm, rightAt, EnemyBehavior.Strafe);
        // Together, 60 px apart with 20 px hulls and a 40 px wanted gap, they shove apart.
        var leftTogether = Thrust(togetherEm, left);
        var rightTogether = Thrust(togetherEm, right);

        Assert.True(leftTogether.X < leftAlone.X, $"the left hull should be pushed left: alone {leftAlone}, together {leftTogether}");
        Assert.True(rightTogether.X > rightAlone.X, $"the right hull should be pushed right: alone {rightAlone}, together {rightTogether}");
    }

    [Fact]
    public void TheSwarmStaysAClumpWhenHullsCrowd()
    {
        var leftAt = new Vector2(0f, -1200f);
        var rightAt = new Vector2(60f, -1200f);

        var (leftAloneEm, _) = World(Vector2.Zero);
        var leftAlone = Thrust(leftAloneEm, Hull(leftAloneEm, leftAt));
        var (rightAloneEm, _) = World(Vector2.Zero);
        var rightAlone = Thrust(rightAloneEm, Hull(rightAloneEm, rightAt));

        var (pairEm, _) = World(Vector2.Zero);
        var leftTogether = Thrust(pairEm, Hull(pairEm, leftAt));
        var rightTogether = Thrust(pairEm, Hull(pairEm, rightAt));

        // Crowding changes nothing for a chasing hull: the clump is the formation, not an accident.
        Assert.Equal(leftAlone, leftTogether);
        Assert.Equal(rightAlone, rightTogether);
    }

    // A jam outranks every manoeuvre: the hull that was circling or tucking has lost its throttles, and
    // no behaviour brings them back (plans/ECM_PULSE_AND_SALVAGE_FIELD.md).
    [Theory]
    [InlineData(EnemyBehavior.Spread)]
    [InlineData(EnemyBehavior.Strafe)]
    [InlineData(EnemyBehavior.CutOff)]
    [InlineData(EnemyBehavior.CoverTuck)]
    [InlineData(EnemyBehavior.Rammer)]
    public void AJamBeatsAManoeuvre(EnemyBehavior behavior)
    {
        var (em, _) = World(Vector2.Zero);
        Entity hull = Hull(em, BeyondTheRing, behavior, holdBearing: new Vector2(1f, 0f));
        em.AddComponent(hull, new EcmDisabled(EcmStats.Duration, MathF.PI / 2f));

        Assert.Equal(0f, Thrust(em, hull).Magnitude, 3);
    }

    // The separation rule as a pure function: a hull inside the wanted gap is pushed straight away from
    // its neighbour, one outside it is not pushed at all.
    [Fact]
    public void SeparationPushesOnlyHullsInsideTheWantedGap()
    {
        var neighbours = new List<(Vector2 Position, float Radius)> { (new Vector2(60f, 0f), 20f) };

        var crowded = EnemySteering.SeparationThrust(Vector2.Zero, 20f, neighbours, 45f);
        Assert.True(crowded.X < -20f && crowded.X > -22.6f, $"expected a half-thrust push away, got {crowded}");
        Assert.Equal(0f, crowded.Y, 3);

        var clear = new List<(Vector2 Position, float Radius)> { (new Vector2(200f, 0f), 20f) };
        Assert.Equal(0f, EnemySteering.SeparationThrust(Vector2.Zero, 20f, clear, 45f).Magnitude, 3);
    }

    // A behaviour may not buy speed: whatever it is doing, a hull still cannot exceed the cap the chase
    // rule already gives it (plans/DIFFICULTY_SCALING.md D3).
    [Theory]
    [InlineData(EnemyBehavior.Spread)]
    [InlineData(EnemyBehavior.Strafe)]
    [InlineData(EnemyBehavior.CutOff)]
    [InlineData(EnemyBehavior.Rammer)]
    public void AManoeuvreNeverThrustsHarderThanTheHullsEngines(EnemyBehavior behavior)
    {
        var (em, _) = World(new Vector2(0f, 250f));
        Entity hull = Hull(em, BeyondTheRing, behavior, holdBearing: new Vector2(1f, 0f));

        Assert.True(Thrust(em, hull).Magnitude <= EnemyShipType.Default.Acceleration + 1e-3f);
    }

    // B6's price: the charge is the hull's whole payload, so the contact that reaches the player spends it
    // (plans/ENEMY_BEHAVIORS.md). Left in, a rammer would bounce off the hull and charge again without
    // paying for the hit it landed — the one part of the ram the player could not answer.
    [Fact]
    public void ARamIsSpentByTheContactItLands()
    {
        var (em, player) = World(Vector2.Zero);
        Entity rammer = Hull(em, new Vector2(0f, 30f), EnemyBehavior.Rammer);
        em.AddComponent(rammer, new Velocity(new Vector2(0f, -700f)));

        Contact(em);

        Assert.True(em.HasComponent<Dead>(rammer), "the ram that reaches the player is spent");
        Assert.True(em.GetComponent<Health>(player).Current < 100, "and the player feels it");
    }

    [Fact]
    public void AnOrdinaryHullSurvivesTheSameContact()
    {
        var (em, _) = World(Vector2.Zero);
        Entity hull = Hull(em, new Vector2(0f, 30f));
        em.AddComponent(hull, new Velocity(new Vector2(0f, -700f)));

        Contact(em);

        Assert.False(em.HasComponent<Dead>(hull), "an ordinary hull bounces off, as it always did");
    }

    private static void Contact(EntityManager em)
    {
        var view = new WorldView(em);
        var commands = new CommandBuffer();
        new CollisionSystem().Update(view, Dt, commands);
        commands.Apply(em);
    }
}
