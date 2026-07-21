using Spacevors.Domain.Components;

namespace Spacevors.Domain;

public static class CooldownHelper
{
    public static float GetCooldown(EntityManager em, Entity entity) =>
        em.HasComponent<FireCooldown>(entity)
            ? em.GetComponent<FireCooldown>(entity).Timer
            : 0f;

    public static void SetCooldown(EntityManager em, Entity entity, float value) =>
        em.AddComponent(entity, new FireCooldown(value));
}
