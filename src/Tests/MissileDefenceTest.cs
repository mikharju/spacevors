using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

// R28: missile defence is the support class's job. The collision system has always been able to kill a
// guided round — what was missing was anything *aiming* at one. These tests run the firing pass over a
// world holding a hostile round and nothing else, so any shot fired can only have been aimed at the round.
// Turret sits at the origin facing -Y; a round at negative Y is ahead of it.
public class MissileDefenceTest
{
    // Both mounts fire the same stats, so the only difference between them is the class flags a real mount
    // gets from its WeaponType: a support gun screens, a kill gun does not (R24/R28).
    private static (EntityManager em, Entity Player, Entity Turret) World(bool support, float range = 900f)
    {
        var em = new EntityManager();

        var player = em.CreateEntity();
        em.AddComponent(player, new Position(Vector2.Zero));
        em.AddComponent(player, new Velocity(Vector2.Zero));
        em.AddComponent(player, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 40f, Boost: 2.5f, MaxHealth: 10, Radius: 18f, Xp: 0, Level: 1, PickupRadius: 60f, RotationSpeed: 5f));

        var turret = em.CreateEntity();
        em.AddComponent(turret, new Position(Vector2.Zero));
        em.AddComponent(turret, new Rotation(0f));
        em.AddComponent(turret, new Turret(
            Weapon: new WeaponStats(FireRate: 3f, AmmoSpeed: 1200f, KickbackForce: 0f, PelletCount: 1, Scatter: 0f, ShotLifetime: 2f, Damage: 1),
            WeaponName: "ClassTestGun",
            ArcAngle: MathF.PI,
            Range: range,
            AutoTarget: true,
            IsSupport: support,
            InterceptsRounds: support));

        return (em, player, turret);
    }

    // The same components a rack puts on a launched round: health is what makes it shootable (R19).
    private static Entity AddRound(EntityManager em, Vector2 position, Vector2 velocity, bool isEnemy)
    {
        var lockOn = em.CreateEntity();
        var round = em.CreateEntity();
        em.AddComponent(round, new Position(position));
        em.AddComponent(round, new Velocity(velocity));
        em.AddComponent(round, new Ammo(velocity, Radius: 4f, Lifetime: 10f, IsEnemy: isEnemy, Damage: 3));
        em.AddComponent(round, new Missile(lockOn, Speed: velocity.Magnitude, MaxSpeed: velocity.Magnitude, Acceleration: WeaponStats.MissileLaunchAcceleration, TurnRate: 3f, Steering: 0f));
        em.AddComponent(round, new Health(2, 2));
        return round;
    }

    private static Entity AddEnemyShip(EntityManager em, Vector2 position)
    {
        var ship = em.CreateEntity();
        em.AddComponent(ship, new Position(position));
        em.AddComponent(ship, new Velocity(Vector2.Zero));
        em.AddComponent(ship, new Rotation(0f));
        em.AddComponent(ship, new EnemyShip(Radius: 20f, Speed: 65f, TurnRate: 1f, FiringRange: 700f, TurretFireRate: 1.5f, TurretAmmoSpeed: 200f, Acceleration: 45f, GraphicsId: 0));
        return ship;
    }

    // One firing tick, and the shots it spawned: entities with Ammo that did not exist before the pass and
    // belong to the player. The hostile round carries Ammo too, which is why the "new" half matters.
    private static List<Entity> FireOnce(EntityManager em)
    {
        var before = new HashSet<Entity>();
        foreach (var (entity, _) in em.GetEntitiesWithComponents<Ammo>()) before.Add(entity);

        var commands = new CommandBuffer();
        new TurretFiringSystem().Update(new WorldView(em), 1f / 60f, commands);
        commands.Apply(em);

        var shots = new List<Entity>();
        foreach (var (entity, ammo) in em.GetEntitiesWithComponents<Ammo>())
        {
            if (ammo.IsEnemy || before.Contains(entity)) continue;
            shots.Add(entity);
        }

        return shots;
    }

    [Fact]
    public void ASupportMountEngagesAClosingRound()
    {
        var (em, _, _) = World(support: true);
        AddRound(em, new Vector2(0f, -700f), new Vector2(0f, 150f), isEnemy: true);

        Assert.Single(FireOnce(em));
    }

    [Fact]
    public void AKillMountNeverVolunteersForARound()
    {
        var (em, _, _) = World(support: false);
        AddRound(em, new Vector2(0f, -700f), new Vector2(0f, 150f), isEnemy: true);

        Assert.Empty(FireOnce(em));
    }

    // A round with no closing component is not worth a chip: firing at it wastes the volley and drags the
    // mount's aim across the ship.
    [Fact]
    public void ASupportMountIgnoresARoundThatIsLeaving()
    {
        var (em, _, _) = World(support: true);
        AddRound(em, new Vector2(0f, -700f), new Vector2(0f, -150f), isEnemy: true);

        Assert.Empty(FireOnce(em));
    }

    // The screen is a defence, not a cleaning service: the ship's own rounds are not a threat, and the
    // faction gate is what stops a support mount emptying its chips into its own missile.
    [Fact]
    public void ASupportMountIgnoresItsOwnRounds()
    {
        var (em, _, _) = World(support: true);
        AddRound(em, new Vector2(0f, -700f), new Vector2(0f, 150f), isEnemy: false);

        Assert.Empty(FireOnce(em));
    }

    // A round outside the mount's reach is not a target either — a screen does not walk out to meet it.
    [Fact]
    public void ASupportMountIgnoresARoundBeyondItsReach()
    {
        var (em, _, _) = World(support: true, range: 500f);
        AddRound(em, new Vector2(0f, -700f), new Vector2(0f, 150f), isEnemy: true);

        Assert.Empty(FireOnce(em));
    }

    // A click is an order, and it outranks the screen — the same priority a clicked hull has always had.
    // The shot leaves to the left because that is where the clicked hull is, not ahead at the round.
    [Fact]
    public void AClickedHullOutranksAClosingRound()
    {
        var (em, player, _) = World(support: true);
        AddRound(em, new Vector2(0f, -700f), new Vector2(0f, 150f), isEnemy: true);
        var hull = AddEnemyShip(em, new Vector2(-600f, -100f));
        em.AddComponent(player, new PrimaryTarget(hull));

        var shot = Assert.Single(FireOnce(em));
        var ammo = em.GetComponent<Ammo>(shot);
        Assert.True(ammo.Velocity.X < 0f, $"the mount fired {ammo.Velocity} instead of at the clicked hull");
    }

    // A hostile round is clickable, and a click is an order: this is how a player tells a mount that never
    // volunteers for a missile to delete one anyway (R28 clause 3).
    [Fact]
    public void AHostileRoundCanBeClickedAndAnyMountFiresAtIt()
    {
        var (em, player, _) = World(support: false);
        var round = AddRound(em, new Vector2(0f, -700f), new Vector2(0f, 150f), isEnemy: true);

        Assert.Equal<Entity?>(round, PrimaryTargetPicker.Pick(em, new Vector2(0f, -700f)));

        em.AddComponent(player, new PrimaryTarget(round));
        Assert.Single(FireOnce(em));
    }

    // A player round is not clickable: it is not a target, it is what the shot is made of.
    [Fact]
    public void APlayersOwnRoundIsNotClickable()
    {
        var (em, _, _) = World(support: true);
        AddRound(em, new Vector2(0f, -700f), new Vector2(0f, 150f), isEnemy: false);

        Assert.Null(PrimaryTargetPicker.Pick(em, new Vector2(0f, -700f)));
    }

    // R26: the clicked-reach premium belongs to the kill class. A chip stream aimed past its own band would
    // be the long band bought with the wrong currency, so the click that reaches a hull at 800 px from a
    // kill mount does not reach it from a support mount.
    [Fact]
    public void AClickedTargetBuysReachOnlyForTheKillClass()
    {
        var (supportWorld, supportPlayer, _) = World(support: true, range: 300f);
        var supportHull = AddEnemyShip(supportWorld, new Vector2(0f, -800f));
        supportWorld.AddComponent(supportPlayer, new PrimaryTarget(supportHull));

        Assert.Empty(FireOnce(supportWorld));

        var (killWorld, killPlayer, _) = World(support: false, range: 300f);
        var killHull = AddEnemyShip(killWorld, new Vector2(0f, -800f));
        killWorld.AddComponent(killPlayer, new PrimaryTarget(killHull));

        Assert.Single(FireOnce(killWorld));
    }
}
