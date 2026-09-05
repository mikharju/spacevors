using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class CameraSystem : GameSystem
{
    // The camera tracks the player directly (its motion is already smooth per tick);
    // only the mouse-driven drift eases in, so fast mouse moves don't yank the screen.
    private const float DriftFollowSpeed = 1.5f;

    // At full mouse deflection the camera shifts this fraction of the half-viewport,
    // so the player stays at least (1 - MaxDriftFraction) of a half-screen from the edge.
    private const float MaxDriftFraction = 0.5f;

    // Mouse offset below this fraction of the half-viewport produces no drift.
    private const float DeadZone = 0.05f;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        if (!view.GetEntitiesWithComponents<Position, Player>().TryFirst(out var playerTuple)) return;

        var desiredDrift = ComputeDrift(view.MouseScreenPosition, view.ViewportSize);

        foreach (var (entity, camera) in view.GetEntitiesWithComponents<Camera>())
        {
            var drift = camera.Drift + (desiredDrift - camera.Drift) * Math.Min(DriftFollowSpeed * deltaTime, 1f);
            commands.Add(new AddComponentCommand<Camera>(entity, new Camera(playerTuple.Value1.Value + drift, drift)));
        }
    }

    // World-space drift from the mouse's screen offset: zero inside the dead zone,
    // linear to MaxDriftFraction of the half-viewport at the window edge.
    private static Vector2 ComputeDrift(Vector2 mouseScreen, Vector2 viewport)
    {
        var half = viewport * 0.5f;
        var n = new Vector2(
            (mouseScreen.X - half.X) / half.X,
            (mouseScreen.Y - half.Y) / half.Y);

        if (n.Magnitude > 1f) n /= n.Magnitude;

        return new Vector2(DriftAxis(n.X) * half.X, DriftAxis(n.Y) * half.Y) * MaxDriftFraction;
    }

    // Maps a normalized axis offset [-1..1] to [0..1], zero inside the dead zone.
    private static float DriftAxis(float value)
    {
        float t = Math.Clamp((Math.Abs(value) - DeadZone) / (1f - DeadZone), 0f, 1f);
        return MathF.Sign(value) * t;
    }
}
