using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain;

// Mounting a weapon into a hull slot. The rules live here so world generation, level-up offers and
// the mount screen cannot disagree about what fits where.
public static class TurretMount
{
    // A small weapon fits any slot; a large weapon needs a large one.
    public static bool Fits(TurretSize slotSize, TurretSize weaponSize) =>
        slotSize == TurretSize.Large || weaponSize == TurretSize.Small;

    public static bool Fits(TurretSlotDefinition slot, WeaponType weapon) => Fits(slot.Size, weapon.Size);

    // Empty slots, in layout order. Optionally only the ones a given weapon size could use.
    public static List<TurretSlotDefinition> FreeSlots(WorldView view, TurretSize? forSize = null)
    {
        var free = new List<TurretSlotDefinition>();
        foreach (var (entity, slot) in view.GetEntitiesWithComponents<TurretSlot>())
        {
            if (view.HasComponent<Turret>(entity)) continue;
            if (forSize is { } size && !Fits(slot.Definition.Size, size)) continue;
            free.Add(slot.Definition);
        }

        return free;
    }

    // Slots carrying a weapon, in layout order — what the mount screen offers for replacement.
    public static List<(Entity SlotEntity, TurretSlotDefinition Slot, string WeaponName)> MountedSlots(WorldView view)
    {
        var mounted = new List<(Entity, TurretSlotDefinition, string)>();
        foreach (var (entity, slot, turret) in view.GetEntitiesWithComponents<TurretSlot, Turret>())
            mounted.Add((entity, slot.Definition, turret.WeaponName));

        return mounted;
    }

    public static bool TryFindSlot(WorldView view, string slotName, out Entity slotEntity)
    {
        foreach (var (entity, slot) in view.GetEntitiesWithComponents<TurretSlot>())
        {
            if (slot.Definition.Name != slotName) continue;
            slotEntity = entity;
            return true;
        }

        slotEntity = default;
        return false;
    }

    // Put a weapon in a slot: the turret plus its aim state, parked at the middle of the slot's arc
    // so a freshly mounted gun is ready and inside its own firing rules.
    public static void Mount(EntityManager em, Entity slotEntity, TurretSlotDefinition slot, WeaponType weapon, Vector2 hullPosition, float hullRotation)
    {
        em.AddComponent(slotEntity, new Turret(
            Weapon: weapon.Stats,
            WeaponName: weapon.Name,
            ArcAngle: slot.ArcAngle,
            Range: weapon.Range,
            AutoTarget: weapon.AutoTarget));
        em.AddComponent(slotEntity, new TurretAim(slot.ArcCenter, Parked: true));
        em.AddComponent(slotEntity, new Position(slot.WorldPosition(hullPosition, hullRotation)));
        em.AddComponent(slotEntity, new Rotation(hullRotation + slot.ArcCenter));
    }

    // Take the weapon out, leaving the empty mount behind.
    public static void Unmount(EntityManager em, Entity slotEntity)
    {
        em.RemoveComponent<Turret>(slotEntity);
        em.RemoveComponent<TurretAim>(slotEntity);
        em.RemoveComponent<FireCooldown>(slotEntity);
        em.RemoveComponent<Position>(slotEntity);
        em.RemoveComponent<Rotation>(slotEntity);
    }
}
