using Spacevors.Domain.Components;

namespace Spacevors.Domain;

public static class CooldownHelper
{
    public static float GetCooldown(WorldView view, Entity entity) =>
        view.HasComponent<FireCooldown>(entity)
            ? view.GetComponent<FireCooldown>(entity).Timer
            : 0f;
}
