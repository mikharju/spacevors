using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Systems;
using Xunit;

namespace Tests;

public class CameraDriftTest
{
    private const float Dt = 1f / 60f;

    // Default viewport is 1920x1024, so half-viewport is (960, 512) and center is (960, 512).
    private static readonly Vector2 Center = new(960f, 512f);

    [Fact]
    public void MouseAtCenter_CameraStaysOnPlayer()
    {
        var (em, cameraEntity) = CreateWorld(new Vector2(100f, -50f));

        var target = SettleCamera(em, cameraEntity, Center);

        Assert.True((target - new Vector2(100f, -50f)).Magnitude < 0.01f, $"target {target}");
    }

    [Fact]
    public void MouseInDeadZone_NoDrift()
    {
        var (em, cameraEntity) = CreateWorld(Vector2.Zero);

        // 40px / 960 and 20px / 512 are both inside the 0.05 dead zone.
        var target = SettleCamera(em, cameraEntity, Center + new Vector2(40f, 20f));

        Assert.True((target - Vector2.Zero).Magnitude < 0.01f, $"target {target}");
    }

    [Fact]
    public void MouseAtRightEdge_DriftsByMaxDriftXOnly()
    {
        var (em, cameraEntity) = CreateWorld(new Vector2(100f, -50f));

        var target = SettleCamera(em, cameraEntity, new Vector2(1920f, 512f));

        // Max drift is 0.5 of the half-viewport: (480, 0).
        Assert.True(Math.Abs(target.X - (100f + 480f)) < 0.01f, $"target {target}");
        Assert.True(Math.Abs(target.Y - (-50f)) < 0.01f, $"target {target}");
    }

    [Fact]
    public void MouseAtCorner_DriftClampedToMaxMagnitude()
    {
        var (em, cameraEntity) = CreateWorld(Vector2.Zero);

        var target = SettleCamera(em, cameraEntity, new Vector2(1920f, 1024f));

        // Corner normalizes to (sqrt(2)/2, sqrt(2)/2), then the dead-zone remap scales each axis
        // by (sqrt(2)/2 - DeadZone) / (1 - DeadZone) ~ 0.6917: drift (332.01, 177.07), magnitude ~376.28.
        Assert.True(Math.Abs(target.X - 332.012f) < 0.1f, $"target {target}");
        Assert.True(Math.Abs(target.Y - 177.073f) < 0.1f, $"target {target}");
        Assert.True(Math.Abs(target.Magnitude - 376.28f) < 0.1f, $"magnitude {target.Magnitude}");

        // Corner drift must not exceed edge drift on either axis.
        Assert.True(target.X < 480f && target.Y < 256f, $"target {target}");
    }

    [Fact]
    public void MouseMovedFast_DriftEasesInInsteadOfSnapping()
    {
        var (em, cameraEntity) = CreateWorld(Vector2.Zero);

        // 15 ticks (0.25s) after a jump to the right edge: drift must be moving but well short of final.
        var target = SettleCamera(em, cameraEntity, new Vector2(1920f, 512f), ticks: 15);

        Assert.True(target.X > 0f, $"target {target}");
        Assert.True(target.X < 480f * 0.5f, $"target {target}");
    }

    [Fact]
    public void FullDeflection_PlayerStaysInsideViewport()
    {
        var (em, cameraEntity) = CreateWorld(Vector2.Zero);

        foreach (var mouse in new[]
        {
            new Vector2(1920f, 512f), new Vector2(0f, 512f),
            new Vector2(960f, 1024f), new Vector2(960f, 0f)
        })
        {
            var target = SettleCamera(em, cameraEntity, mouse);

            Assert.True(Math.Abs(target.X) <= WorldView.DefaultViewportSize.X / 2f, $"target {target} for mouse {mouse}");
            Assert.True(Math.Abs(target.Y) <= WorldView.DefaultViewportSize.Y / 2f, $"target {target} for mouse {mouse}");
        }
    }

    private static (EntityManager em, Entity cameraEntity) CreateWorld(Vector2 playerPos)
    {
        var em = new EntityManager();

        var playerEntity = em.CreateEntity();
        em.AddComponent(playerEntity, new Position(playerPos));
        em.AddComponent(playerEntity, new Player(Thrust: 100f, SideThrust: 80f, BackThrust: 50f, Boost: 1.5f, MaxHealth: 10));

        var cameraEntity = em.CreateEntity();
        em.AddComponent(cameraEntity, new Camera(Vector2.Zero, Vector2.Zero));

        return (em, cameraEntity);
    }

    // 600 ticks (10s) lets the lazy drift easing converge to within ~1e-4 px of its target.
    private static Vector2 SettleCamera(EntityManager em, Entity cameraEntity, Vector2 mouseScreen, int ticks = 600)
    {
        var view = new WorldView(em) { MouseScreenPosition = mouseScreen };

        for (int i = 0; i < ticks; i++)
        {
            var commands = new CommandBuffer();
            new CameraSystem().Update(view, Dt, commands);
            commands.Apply(em);
        }

        return em.GetComponent<Camera>(cameraEntity).Target;
    }
}
