# Plan: Pop/Swap Compact ECS Storage

## Problem

Current `ComponentStorage<T>` uses entity ID as direct array index into a sparse `List<T>`. This causes:

- **Memory waste**: `_data` grows monotonically with highest entity ID ever created, filled with `default!` placeholders
- **O(N log N) iteration**: `.OrderBy()` on every enumeration
- **Unbounded growth**: Entity IDs never recycled, so storage only grows over a game session

For 10k-100k objects this becomes significant.

## Design Decision: Compact SoA with Pop/Swap

Each `ComponentStorage<T>` stores active components contiguously at indices `0..count-1`. Deletion uses the swap-pop pattern (swap deleted element with last, then pop) for O(1) removal.

**Per-storage fields (all private):**

```
T[]              _data        -- component values at slot index 0..count-1
Entity[]         _entityIds   -- maps slot index -> Entity struct (stable IDs)
Dictionary<int,int>  _slotMap -- entity.Value -> slot index (int keys for efficient hashing)
int              _count       -- number of active entities
```

**Key invariant:** `_slotMap[_entityIds[i].Value] == i` for all `0 <= i < _count`.

Entity IDs never change. Swap-pop moves data between slots but keeps the Entity struct stable.

### Operations

**Add(entity, value):**
- Append `_data[_count] = value`, `_entityIds[_count] = entity`
- `_slotMap[entity.Value] = _count`
- Increment `_count`

**Remove(entity):**
- Look up `slot = _slotMap[entity.Value]`
- If `slot < --_count`: swap `_data[slot]` with `_data[_count]`, swap `_entityIds[slot]` with `_entityIds[_count]`
- Update `_slotMap[_entityIds[slot].Value] = slot` (the entity that was moved)
- Remove `entity.Value` from `_slotMap`

**GetEnumerator():**
- Iterate `for (int i = 0; i < _count; i++)` → yield `(_entityIds[i], _data[i])`
- No `.OrderBy()`, no HashSet, O(count)

### Public API — Unchanged Except GetEnumerator Gets Faster

| Method | Behavior |
|--------|----------|
| `Add(entity, value)` | Append to end, increment count |
| `Get(entity)` | Lookup via `_slotMap[entity.Value]`, return `_data[slot]` |
| `Has(entity)` | `_slotMap.ContainsKey(entity.Value)` |
| `Remove(entity)` | Swap-pop with last element, update slot map for moved entity |
| `GetEnumerator()` | Iterate `0.._count-1`, yield `(_entityIds[i], _data[i])` — no `.OrderBy()`, O(count) |
| `Count` | `_count` |
| `GetEntityIds()` | Return `_slotMap.Keys` (existing base method, used by join queries) |

**No new public methods.** No slot accessor. The only way to iterate is via the existing enumerator — which systems already use. Storage internals stay private.

## EntityManager Changes

### FindSmallest — Replace LINQ with Simple Loop

Current:
```csharp
private ComponentStorageBase FindSmallest(params ComponentStorageBase[] storages)
    => storages.OrderBy(s => s.Count).First();
```

New:
```csharp
private static ComponentStorageBase FindSmallest(ComponentStorageBase first, params ComponentStorageBase[] rest)
{
    var smallest = first;
    foreach (var s in rest)
        if (s.Count < smallest.Count)
            smallest = s;
    return smallest;
}
```

No LINQ, no allocations. Handles 2-4 storages cleanly.

### Multi-Component Join Queries — No Changes Needed

Current join iterates entity IDs from the smallest storage:
```csharp
foreach (var id in smallest.GetEntityIds()) {
    var entity = new Entity(id);
    if (!storage1.Has(entity)) continue;
    ...
}
```

This already works with compact storage. `GetEntityIds()` returns `_slotMap.Keys` which are the stable entity IDs. No slot exposure needed.

Alternatively, iterate via enumerator directly:
```csharp
foreach (var (entity, _) in (ComponentStorage<T1>)smallest) {
    if (!storage2.Has(entity)) continue;
    ...
}
```

Either approach is clean — the enumerator version is slightly cleaner since it doesn't require `new Entity(id)` construction. Both keep storage internals private.

## Files to Change

| File | Changes |
|------|---------|
| `src/Domain/ComponentStorage.cs` | Rewrite: sparse List + HashSet → compact arrays + swap-pop + int-keyed Dictionary. ~60 → ~85 lines. GetEnumerator drops `.OrderBy()`. |
| `src/Domain/EntityManager.cs` | Replace LINQ `.OrderBy()` in `FindSmallest` with simple loop (~3 line change). Join queries unchanged (iterate via existing enumerator or GetEntityIds()). |
| `src/Tests/EcsTests.cs` | Add tests: destroy middle entity compacts correctly, iteration after mixed adds/deletes returns correct pairs, re-adding after destroy works. |

## Execution Order

1. Rewrite `ComponentStorage.cs` with compact storage + swap-pop
2. Update `FindSmallest` in `EntityManager.cs` (drop LINQ)
3. Add swap-behavior tests to `EcsTests.cs`
4. Run existing tests (`dotnet test`)
5. Build + brief visual smoke test

## Summary of What Changes vs Current

| Aspect | Before | After |
|--------|--------|-------|
| Storage layout | Sparse List indexed by entity ID, HashSet for active IDs | Compact arrays at slot indices 0..count-1 |
| Delete | O(1) remove from HashSet only (leaves stale data in list) | O(1) swap-pop + update slot map |
| Iterate | O(N log N) due to `.OrderBy()` on HashSet | O(N) sequential array access |
| Memory growth | Unbounded (highest entity ID ever created) | Bounded by active entity count × component types |
| Entity IDs | Stable (never change) | Still stable — swap moves data, not IDs |
| Public API | Unchanged | Unchanged — no new methods, no slot exposure |

## Risk Assessment

| Risk | Mitigation |
|------|-----------|
| Stale Entity refs after swap | No code holds Entity refs across frames (verified in Program.cs + all systems) |
| 3 long-lived entities (player, camera, turret) | Never destroyed, always at slot 0 — unaffected by compaction of other entities |
| `_slotMap` dictionary overhead on every add/remove | Acceptable for 10k-100k objects; O(1) lookup. Could switch to `Dictionary<int,int>` later if measured as bottleneck. |
| Iteration order changes | No code depends on iteration order (systems are commutative over entities) |
