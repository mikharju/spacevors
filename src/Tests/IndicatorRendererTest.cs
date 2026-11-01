using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Spacevors.Game;
using Xunit;

namespace Tests;

// IndicatorRenderer.Collect and its two pure helpers, headless: the same world must produce the
// same markers whatever the pop/swap storage order, and the caps keep the HUD bounded however much
// a scenario inflates a count (TROUBLE_SHOOTING #61).
public class IndicatorRendererTest
{
    private static readonly Vector2 Center = Vector2.Zero;
    private static readonly Vector2 Viewport = new(1920f, 1024f);

    private static Entity AddShip(EntityManager em, float x, float y)
    {
        var e = em.CreateEntity();
        em.AddComponent(e, new Position(new Vector2(x, y)));
        em.AddComponent(e, new EnemyShip(14f, 60f, 2f, 300f, 1f, 200f, 40f, 0));
        return e;
    }

    private static void AddMissile(EntityManager em, float x, float y, bool isEnemy)
    {
        var e = em.CreateEntity();
        em.AddComponent(e, new Position(new Vector2(x, y)));
        em.AddComponent(e, new Ammo(new Vector2(0f, 200f), 4f, 3f, isEnemy));
        em.AddComponent(e, new Missile(default, 150f, 300f, 100f, 2f, 0f));
    }

    [Fact]
    public void Threats_FiltersOnScreenDeadAndPlayerOwned()
    {
        var em = new EntityManager();
        AddShip(em, 0f, -2000f); // off-screen: collected
        AddShip(em, 100f, 50f);  // on-screen: none
        var wreck = AddShip(em, 0f, 2000f); // dead wreck: none
        em.AddComponent(wreck, new Dead());
        AddMissile(em, 0f, -3000f, isEnemy: true);
        AddMissile(em, 0f, -3200f, isEnemy: false); // the player's own round points at nothing

        IndicatorRenderer.Collect(em, Center, Viewport);

        Assert.Equal(1, IndicatorRenderer.EnemyCount);
        Assert.Equal(new Vector2(0f, -2000f), IndicatorRenderer.EnemyMarkers[0]);
        Assert.Equal(1, IndicatorRenderer.MissileCount);
        Assert.Equal(new Vector2(0f, -3000f), IndicatorRenderer.MissileMarkers[0]);
    }

    [Fact]
    public void Loot_CollectsOnlyOffScreenPickups()
    {
        var em = new EntityManager();
        var far = em.CreateEntity();
        em.AddComponent(far, new Position(new Vector2(0f, -2000f)));
        em.AddComponent(far, new XpPickup(5));
        var near = em.CreateEntity();
        em.AddComponent(near, new Position(new Vector2(10f, 10f)));
        em.AddComponent(near, new XpPickup(5));
        var orb = em.CreateEntity();
        em.AddComponent(orb, new Position(new Vector2(2000f, 0f)));
        em.AddComponent(orb, new HealthOrb());
        var token = em.CreateEntity();
        em.AddComponent(token, new Position(new Vector2(-3000f, 500f)));
        em.AddComponent(token, new RerollToken());

        IndicatorRenderer.Collect(em, Center, Viewport);

        Assert.Equal(1, IndicatorRenderer.XpCount);
        Assert.Equal(new Vector2(0f, -2000f), IndicatorRenderer.XpMarkers[0]);
        Assert.Equal(1, IndicatorRenderer.OrbCount);
        Assert.Equal(1, IndicatorRenderer.SalvageCount);
    }

    [Fact]
    public void Cap_KeepsTheNearestWhateverTheOrder()
    {
        var em = new EntityManager();
        // 30 ships at 1000..2450 north: the cap (24) must survive as the nearest 24.
        for (int i = 0; i < 30; i++) AddShip(em, 0f, -(1000f + i * 50f));

        IndicatorRenderer.Collect(em, Center, Viewport);

        Assert.Equal(IndicatorRenderer.EnemyCap, IndicatorRenderer.EnemyCount);
        float min = float.MaxValue, max = 0f;
        for (int i = 0; i < IndicatorRenderer.EnemyCount; i++)
        {
            float d = IndicatorRenderer.EnemyMarkers[i].Magnitude;
            min = Math.Min(min, d);
            max = Math.Max(max, d);
        }
        Assert.Equal(1000f, min, 1);
        Assert.Equal(2150f, max, 1); // 1000 + 23 * 50 — the 24 nearest end here
    }

    [Fact]
    public void Collect_TwiceProducesTheSameCounts()
    {
        var em = new EntityManager();
        AddShip(em, 0f, -2000f);
        AddMissile(em, 900f, -900f, isEnemy: true);

        IndicatorRenderer.Collect(em, Center, Viewport);
        int ships = IndicatorRenderer.EnemyCount, missiles = IndicatorRenderer.MissileCount;
        IndicatorRenderer.Collect(em, Center, Viewport);

        Assert.Equal(ships, IndicatorRenderer.EnemyCount);
        Assert.Equal(missiles, IndicatorRenderer.MissileCount);
    }

    [Fact]
    public void EdgePoint_AxisAndCornerDirectionsLandOnTheInsetRect()
    {
        // Viewport 1920x1024 inset 28: half-extents 932x484 about the centre (960, 512).
        var up = IndicatorRenderer.EdgePoint(Viewport, new Vector2(0f, -1f));
        Assert.Equal(960f, up.X, 1);
        Assert.Equal(28f, up.Y, 1);

        var right = IndicatorRenderer.EdgePoint(Viewport, new Vector2(1f, 0f));
        Assert.Equal(1892f, right.X, 1);
        Assert.Equal(512f, right.Y, 1);

        var diag = new Vector2(1f, 1f) / MathF.Sqrt(2f);
        var corner = IndicatorRenderer.EdgePoint(Viewport, diag);
        Assert.Equal(960f + 484f, corner.X, 1);
        Assert.Equal(512f + 484f, corner.Y, 1);
    }

    [Fact]
    public void HalfWidth_WideNearThinFarAndCappedBeyondFar()
    {
        Assert.Equal(IndicatorRenderer.WideHalfWidth, IndicatorRenderer.HalfWidthFor(500f));
        Assert.Equal(IndicatorRenderer.ThinHalfWidth, IndicatorRenderer.HalfWidthFor(4000f), 1);
        Assert.True(IndicatorRenderer.HalfWidthFor(1200f) > IndicatorRenderer.HalfWidthFor(1800f));

        float mid = IndicatorRenderer.HalfWidthFor((IndicatorRenderer.WideDistance + IndicatorRenderer.FarDistance) * 0.5f);
        Assert.Equal((IndicatorRenderer.WideHalfWidth + IndicatorRenderer.ThinHalfWidth) * 0.5f, mid, 1);
    }
}
