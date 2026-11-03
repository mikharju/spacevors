using Spacevors.Domain.Systems;
using Spacevors.Game;
using Xunit;

namespace Tests;

// The parts of the shield's look (plans/SHIELDS.md §7) that are pure geometry, tested headless the way
// IndicatorRendererTest tests its helpers — no window, no raylib init, just the functions the renderer
// draws from. The rest of the look is a screenshot judgement.
public class ShieldGraphicTest
{
    [Fact]
    public void ArcFor_SaysNothingWhenThePoolIsFull()
    {
        // The whole "invisible when full" rule in one line, so it lives in a pure function instead of a
        // branch scattered through the renderer.
        Assert.Equal(0f, ShieldRenderer.ArcFor(16f, 16));
        Assert.Equal(0f, ShieldRenderer.ArcFor(16.4f, 16));   // and never past 360° whatever Points says
        Assert.Equal(0f, ShieldRenderer.ArcFor(0f, 16));      // empty: the shatter already announced it
        Assert.Equal(180f, ShieldRenderer.ArcFor(8f, 16));
        Assert.Equal(337.5f, ShieldRenderer.ArcFor(15f, 16), 3);
        Assert.True(ShieldRenderer.ArcFor(15.999f, 16) < 360f);
    }

    [Fact]
    public void TheGlintFiresOncePerPeriod()
    {
        const uint hull = 7717;

        // Count the rising edges over six periods: a full shield shows itself exactly once every period,
        // which is the difference between a heartbeat and an aura.
        int sweeps = 0;
        bool on = false;
        for (float t = 0f; t < 30f; t += 0.01f)
        {
            bool nowOn = ShieldRenderer.GlintAngleDeg(hull, t) is not null;
            if (nowOn && !on) sweeps++;
            on = nowOn;
        }

        Assert.Equal(6, sweeps);
    }

    [Fact]
    public void TheGlintIsDeterministicAndNotInLockstep()
    {
        // The same hull at the same world clock answers the same, twice over: a seeded run draws the same
        // frame twice.
        Assert.Equal(ShieldRenderer.GlintAngleDeg(31u, 3.14f), ShieldRenderer.GlintAngleDeg(31u, 3.14f));

        // And two hulls do not pulse together, which is what will keep shielded elites apart.
        var starts = new uint[] { 1u, 2u, 3u, 4u, 5u, 6u }.Select(SweepStartSeconds).ToList();
        Assert.True(starts.Distinct().Count() > 1,
            $"every hull starts its glint at {starts[0]} s — a shielded fleet would flash in lockstep");

        static float SweepStartSeconds(uint hull)
        {
            for (float t = 0f; t < 5f; t += 0.001f)
                if (ShieldRenderer.GlintAngleDeg(hull, t) is not null) return (float)Math.Round(t, 2);
            return 0f;   // already mid-sweep when the clock starts
        }
    }

    [Fact]
    public void TheFieldIsDrawnOutsideTheHullsBoundingCircle()
    {
        // Draw-only geometry: the ring is a field, not a hurtbox, so it sits outside the circle the
        // diagnostic overlay draws (plans/SHIELDS.md §7).
        Assert.True(ShieldRules.RingRadius(18f) > 18f);
        Assert.Equal(18f * ShieldRules.RingRadiusFactor, ShieldRules.RingRadius(18f), 3);
    }
}
