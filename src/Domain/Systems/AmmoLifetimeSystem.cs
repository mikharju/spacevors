using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class AmmoLifetimeSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        var ammoEntities = em.GetEntitiesWithComponents<Ammo>().ToList();

        foreach (var (entity, ammo) in ammoEntities)
        {
            if (ammo.Lifetime - deltaTime <= 0f)
            {
                em.DestroyEntity(entity);
            }
            else
            {
                em.AddComponent(entity, new Ammo(ammo.Velocity, ammo.Radius, ammo.Lifetime - deltaTime, ammo.IsEnemy));
            }
        }
    }
}
