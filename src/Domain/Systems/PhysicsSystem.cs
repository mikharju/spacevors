using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class PhysicsSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        foreach (var (entity, position) in em.GetEntitiesWithComponents<Position>())
        {
            if (!em.HasComponent<Velocity>(entity)) continue;

            var velocity = em.GetComponent<Velocity>(entity);
            var newPos = position.Value + velocity.Value * deltaTime;
            em.AddComponent(entity, new Position(newPos));
        }
    }
}
