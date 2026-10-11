using Spacevors.Domain.Components;

namespace Spacevors.Game;

// Maps hp ratio to the hull-damage shading scalar [0,1] carried in vertex color B.
// 0 (pristine hull) until the smoke threshold (2/3 hp, same as DamageEffectSystem),
// rising continuously to 1 at destruction. Rendering-only; the simulation never sees it.
public static class DamageShading
{
    public const float LightHpThreshold = 2f / 3f;

    public static float FromHpRatio(float hpRatio)
    {
        if (hpRatio >= LightHpThreshold) return 0f;
        return (LightHpThreshold - Math.Clamp(hpRatio, 0f, 1f)) / LightHpThreshold;
    }

    public static float FromHealth(Health health)
        => health.Max > 0 ? FromHpRatio((float)health.Current / health.Max) : 0f;
}
