using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

public class MineRespawnSystem : GameSystem
{
    private float _timer;

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        // Mines pause during full lulls and the opening lull; quiet gaps keep them coming, which is
        // what makes a gap "less pressure" rather than "none" (plans/WAVES_AND_BREATHERS.md).
        if (WaveSchedule.PhaseFor(view.ElapsedTime).MinesPaused) return;

        _timer -= deltaTime;

        if (_timer > 0f) return;

        int activeMines = view.GetEntitiesWithComponents<EnemyMine>().Count();
        if (activeMines >= SpawningStats.Mines.MaxMines) return;

        if (!view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple)) return;
        Entity playerEntity = playerTuple.Entity;

        var rng = view.Rng;
        var playerPos = view.GetComponent<Position>(playerEntity);
        view.TryGetComponent<Velocity>(playerEntity, out var playerVelComp);
        Vector2 playerVel = playerVelComp.Value;

        // No meaningful "front" while the player is stationary: spawn in any direction.
        Vector2 spawnDir = playerVel.Magnitude >= SpawnPlacement.MinDirectionalSpeed
            ? SpawnPlacement.ForwardDirection(playerVel / playerVel.Magnitude, rng)
            : SpawnPlacement.AnyDirection(rng);

        Vector2 minePos = SpawnPlacement.OutsideScreen(playerPos.Value, view.ViewportSize, spawnDir);
        float mineAngle = (float)(rng.NextDouble() * Math.PI * 2);

        // Bigger mines later as the field thins (MineStats.LargeChanceFor); the draw count is unchanged.
        MineSize mSize = rng.NextDouble() < MineStats.LargeChanceFor(EnemyShipStats.TierFor(view.ElapsedTime))
            ? MineSize.Large
            : MineSize.Small;
        var mineType = MineType.FromSize(mSize);

        commands.AddEntity(new Position(minePos), new Velocity(Vector2.Zero), new EnemyMine(mSize, MineStats.SpawnSpeedMin + (float)rng.NextDouble() * (MineStats.SpawnSpeedMax - MineStats.SpawnSpeedMin), mineAngle), new Health(mineType.Health, mineType.Health));

        _timer = NextInterval(view.ElapsedTime, rng);
    }

    // The mine field thins out as the run goes on: the respawn window widens from the start interval at
    // the opening to the late interval at RampDuration and stays there. Mines are the one content the
    // difficulty ladder makes weaker (plans/DIFFICULTY_SCALING.md R2.1) — they are the XP clock and the
    // heal-orb source, so refilling them faster over time would hand the player more of both exactly
    // where every other knob is trying to raise pressure. Pure: elapsed time and rng are the only inputs.
    public static float NextInterval(float elapsed, Random rng)
    {
        var (min, max) = IntervalRange(elapsed);
        return min + (float)rng.NextDouble() * (max - min);
    }

    // Random within this window, interpolated linearly over RampDuration and clamped at both ends.
    public static (float Min, float Max) IntervalRange(float elapsed)
    {
        float progress = Math.Clamp(elapsed / SpawningStats.Mines.RampDuration, 0f, 1f);
        return (
            Interpolate(SpawningStats.Mines.StartMinInterval, SpawningStats.Mines.LateMinInterval, progress),
            Interpolate(SpawningStats.Mines.StartMaxInterval, SpawningStats.Mines.LateMaxInterval, progress));
    }

    private static float Interpolate(float from, float to, float progress) => from + (to - from) * progress;
}
