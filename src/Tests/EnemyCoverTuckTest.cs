using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// What a crippled hull does instead of fleeing (plans/ENEMY_BEHAVIORS.md B5): it looks for the biggest
// hull still fit to fight that sits within its own bearing from the player, and slips behind that hull,
// where the player's rounds spend themselves on the shield's body before they reach it. With no such
// hull in reach it keeps fighting at its own ring — a wounded enemy that runs off the field is the one
// thing that would make a fight take longer without making it more interesting.
public class EnemyCoverTuckTest
{
    const float Dt = 1f / 60f;

    // Carmine is the hull this behaviour is for: 88 hp means it has a crippled phase worth protecting,
    // and 62 px of body means it is worth hiding behind. Its ring at tier 0 is 640 px.
    private const float CrippledHp = 10f;
    private const float FullHp = 88f;

    private static EntityManager World()
    {
        var em = new EntityManager();
        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 2.5f, MaxHealth: 100, Radius: 18f, PickupRadius: 150f));
        em.AddComponent(player, new Health(100, 100));
        return em;
    }

    private static Entity Hull(EntityManager em, Vector2 at, EnemyBehavior behavior, float hp)
    {
        var hull = em.CreateEntity();
        EnemyShipFactory.AddComponents(em, hull, at, Vector2.Zero, 0f, 0f, EnemyShipType.Carmine, EnemyArmament.Guns);
        em.AddComponent(hull, new Health((int)hp, (int)FullHp));

        var ship = em.GetComponent<EnemyShip>(hull);
        em.AddComponent(hull, ship with { Behavior = behavior, HoldBearing = (at - Vector2.Zero).Normalized });
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

    // The pair that prices the tuck: the same crippled hull, same place, with and without a fit hull on
    // its bearing. With one it moves away from the player, behind the shield; without one it parks at its
    // own ring and keeps shooting.
    [Fact]
    public void ACrippledHullSlipsBehindAFitFriend()
    {
        var em = World();
        Hull(em, new Vector2(60f, -420f), EnemyBehavior.Chase, FullHp);
        Entity tucker = Hull(em, new Vector2(30f, -500f), EnemyBehavior.CoverTuck, CrippledHp);

        var thrust = Thrust(em, tucker);
        var awayFromPlayer = new Vector2(30f, -500f).Normalized;

        Assert.True(thrust.Magnitude > 10f, $"a crippled hull with cover should be moving, got {thrust}");
        Assert.True(Vector2.Dot(thrust, awayFromPlayer) > 0f, $"a crippled hull should slip behind its friend, away from the player, got {thrust}");
    }

    [Fact]
    public void TheSameHullAtFullHpKeepsFighting()
    {
        var em = World();
        Hull(em, new Vector2(60f, -420f), EnemyBehavior.Chase, FullHp);
        Entity hull = Hull(em, new Vector2(30f, -500f), EnemyBehavior.CoverTuck, FullHp);

        // 500 px out, its ring is 640: a healthy hull with a gun in reach parks and shoots. Tucking is a
        // wounded-hull rule, not a personality.
        Assert.Equal(0f, Thrust(em, hull).Magnitude, 3);
    }

    [Fact]
    public void AHullWithNobodyFitToHideBehindKeepsFighting()
    {
        var em = World();
        Entity tucker = Hull(em, new Vector2(0f, -900f), EnemyBehavior.CoverTuck, CrippledHp);

        var thrust = Thrust(em, tucker);
        Assert.True(thrust.Y > 35f, $"with no cover in reach a wounded hull closes on the player instead of running, got {thrust}");
    }

    [Fact]
    public void ACrippledFriendIsNoCover()
    {
        var em = World();
        Hull(em, new Vector2(60f, -420f), EnemyBehavior.Chase, CrippledHp);
        Entity tucker = Hull(em, new Vector2(30f, -500f), EnemyBehavior.CoverTuck, CrippledHp);

        // Two wounded hulls do not shelter each other: a shield that is about to die has nothing to spare.
        Assert.Equal(0f, Thrust(em, tucker).Magnitude, 3);
    }

    // The cone is what keeps a tuck from routing across the field: a friend on the far side of the player
    // is not cover, it is a trip the player would happily wait out.
    [Fact]
    public void AFriendOnTheFarSideIsNotCover()
    {
        var em = World();
        Hull(em, new Vector2(400f, -400f), EnemyBehavior.Chase, FullHp);
        Entity tucker = Hull(em, new Vector2(0f, -700f), EnemyBehavior.CoverTuck, CrippledHp);

        var thrust = Thrust(em, tucker);
        Assert.True(thrust.Y > 35f && thrust.X < 1f, $"a friend 45 degrees off the bearing should not be worth a crossing, got {thrust}");
    }

    [Fact]
    public void AJammedHullStopsTucking()
    {
        var em = World();
        Hull(em, new Vector2(60f, -420f), EnemyBehavior.Chase, FullHp);
        Entity tucker = Hull(em, new Vector2(30f, -500f), EnemyBehavior.CoverTuck, CrippledHp);
        em.AddComponent(tucker, new EcmDisabled(EcmStats.Duration, MathF.PI / 2f));

        Assert.Equal(0f, Thrust(em, tucker).Magnitude, 3);
    }

    // The cover is the biggest fit hull, not the nearest one: hiding behind a swarmer buys a 20 px shield,
    // hiding behind a heavy buys 78 px of body between the tucker and every round coming in.
    [Fact]
    public void TheBiggestFitHullIsTheOneWorthHidingBehind()
    {
        var covered = EnemySteering.TryFindCover(
            new Vector2(30f, -500f), Vector2.Zero,
            new List<(Vector2 Position, float Radius, float HpFraction)>
            {
                (new Vector2(40f, -460f), 20f, 1f),
                (new Vector2(60f, -420f), 78f, 1f),
                (new Vector2(20f, -380f), 62f, 0.2f),
            },
            out var coverPosition, out float coverRadius);

        Assert.True(covered);
        Assert.Equal(78f, coverRadius, 3);
        Assert.Equal(new Vector2(60f, -420f), coverPosition);
    }
}
