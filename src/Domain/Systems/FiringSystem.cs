using Spacevors.Domain.Components;

namespace Spacevors.Domain.Systems;

public class FiringSystem : GameSystem
{
    public override void Update(EntityManager em, float deltaTime)
    {
        foreach (var (entity, weapon, rotation) in em.GetEntitiesWithComponents<Weapon, Rotation>())
        {
            var cooldown = CooldownHelper.GetCooldown(em, entity);

            if (cooldown < 0f)
            {
                FireAmmo(em, entity, weapon, rotation);
                CooldownHelper.SetCooldown(em, entity, 1f / weapon.EffectiveFireRate);
            }
            else if (cooldown > 0f)
            {
                var newCooldown = cooldown - deltaTime;
                if (newCooldown <= 0f)
                {
                    CooldownHelper.SetCooldown(em, entity, 0f);
                }
                else
                {
                    CooldownHelper.SetCooldown(em, entity, newCooldown);
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

        int pelletCount = weapon.PelletCount;
        float scatterAngle = pelletCount > 1 ? 0.1f : 0.033f;

        for (int i = 0; i < pelletCount; i++)
        {
            float angleOffset = (i - (pelletCount - 1) / 2f) * scatterAngle;
            float cosOff = (float)Math.Cos(angleOffset);
            float sinOff = (float)Math.Sin(angleOffset);

            Vector2 pelletDir = new Vector2(
                ammoDir.X * cosOff - ammoDir.Y * sinOff,
                ammoDir.X * sinOff + ammoDir.Y * cosOff
            );

            float speedVariation = 1f + (Random.Shared.NextSingle() - 0.5f) * 0.15f;
            Vector2 ammoVel = pelletDir * weapon.EffectiveAmmoSpeed * speedVariation;

            if (em.HasComponent<Velocity>(shooterEntity))
            {
                var shooterVel = em.GetComponent<Velocity>(shooterEntity).Value;
                ammoVel += shooterVel;
            }

            var ammoEntity = em.CreateEntity();
            em.AddComponent(ammoEntity, new Position(spawnPos));
            em.AddComponent(ammoEntity, new Velocity(ammoVel));
            em.AddComponent(ammoEntity, new Ammo(ammoVel, 2.5f, 3f, IsEnemy: false));
        }

        Vector2 kickback = new Vector2(-sin, cos) * weapon.KickbackForce;
        if (em.HasComponent<Velocity>(shooterEntity))
        {
            var currentVel = em.GetComponent<Velocity>(shooterEntity).Value;
            em.AddComponent(shooterEntity, new Velocity(currentVel + kickback));
        }
    }
}
