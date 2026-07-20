using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class FiringSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        foreach (var (entity, weapon, rotation) in em.GetEntitiesWithComponents<Weapon, Rotation>())
        {
            var cooldown = GetCooldown(em, entity);

            if (cooldown < 0f)
            {
                // Ready to fire: spawn ammo and start cooldown
                FireAmmo(em, entity, weapon, rotation);
                SetCooldown(em, entity, 1f / weapon.FireRate);
            }
            else if (cooldown > 0f)
            {
                // Decrement active cooldown
                var newCooldown = cooldown - deltaTime;
                if (newCooldown <= 0f)
                {
                    SetCooldown(em, entity, 0f);
                }
                else
                {
                    SetCooldown(em, entity, newCooldown);
                }
            }
        }
    }

    private void FireAmmo(EntityManager em, Entity shooterEntity, Weapon weapon, Rotation rotation)
    {
        var pos = em.GetComponent<Position>(shooterEntity);
        float cos = (float)Math.Cos(rotation.Angle);
        float sin = (float)Math.Sin(rotation.Angle);

        Vector2 ammoDir = new Vector2(sin, -cos).Normalized;

        float spawnDist = 20f;
        var spawnPos = pos.Value + ammoDir * spawnDist;

        Vector2 ammoVel = ammoDir * weapon.AmmoSpeed;

        var ammoEntity = em.CreateEntity();
        em.AddComponent(ammoEntity, new Position(spawnPos));
        em.AddComponent(ammoEntity, new Velocity(ammoVel));
        em.AddComponent(ammoEntity, new Ammo(ammoVel, 5f, 3f));

        Vector2 kickback = new Vector2(-sin, cos) * weapon.KickbackForce;
        if (em.HasComponent<Velocity>(shooterEntity))
        {
            var currentVel = em.GetComponent<Velocity>(shooterEntity).Value;
            em.AddComponent(shooterEntity, new Velocity(currentVel + kickback));
        }
    }

    private static float GetCooldown(EntityManager em, Entity entity)
    {
        if (em.HasComponent<FireCooldown>(entity))
        {
            return em.GetComponent<FireCooldown>(entity).Timer;
        }
        return 0f;
    }

    private static void SetCooldown(EntityManager em, Entity entity, float value)
    {
        em.AddComponent(entity, new FireCooldown(value));
    }
}
