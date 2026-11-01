namespace Spacevors.Domain.Stats;

// What one enemy-ship death leaves behind (CollisionSystem.SpawnShipLootOnDeath).
public enum ShipDrop { None, HealthOrb, RerollToken, SalvageField, EcmPulse }

// The band arithmetic for LootStats' drop chances — the way WaveSchedule is WaveStats' arithmetic.
//
// One roll decides the whole drop, so a ship death consumes exactly one RNG number, which is what it
// consumed before salvage existed: the world's RNG stream does not shift, so seeded runs and the perf
// baselines stay comparable (plans/REROLL_TOKENS.md D3). Further kinds join these bands rather than
// adding a second `if`, so one death never drops everything and the total rare-drop budget stays one
// number — which is also where the open reward-side knob (plans/DIFFICULTY_SCALING.md R2.6 item 7)
// will scale the whole band width down late in a run.
public static class LootRoll
{
    public static ShipDrop RollShipDrop(Random rng)
    {
        double roll = rng.NextDouble();

        if (roll < LootStats.HealthOrbChance) return ShipDrop.HealthOrb;
        if (roll < LootStats.HealthOrbChance + LootStats.RerollTokenChance) return ShipDrop.RerollToken;
        if (roll < LootStats.HealthOrbChance + LootStats.RerollTokenChance + LootStats.SalvageFieldChance)
            return ShipDrop.SalvageField;
        if (roll < LootStats.HealthOrbChance + LootStats.RerollTokenChance + LootStats.SalvageFieldChance + LootStats.EcmPulseChance)
            return ShipDrop.EcmPulse;
        return ShipDrop.None;
    }
}
