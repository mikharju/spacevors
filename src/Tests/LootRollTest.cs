using Spacevors.Domain.Stats;
using Xunit;

namespace Tests;

// The ship-death drop table: one roll decides the drop, so a death spends one RNG number (the world's
// stream must not shift — plans/REROLL_TOKENS.md D3) and never drops two things at once.
public class LootRollTest
{
    [Fact]
    public void RollShipDrop_ConsumesExactlyOneRandomNumber()
    {
        var rolling = new Random(7);
        LootRoll.RollShipDrop(rolling);
        double afterRoll = rolling.NextDouble();

        var stepped = new Random(7);
        stepped.NextDouble();
        double afterOneStep = stepped.NextDouble();

        Assert.Equal(afterRoll, afterOneStep);
    }

    [Fact]
    public void RollShipDrop_LandsInItsBandsAndNeverDropsBoth()
    {
        var rng = new Random(42);
        int health = 0, token = 0, field = 0, ecm = 0, none = 0;
        const int rolls = 20_000;

        for (int i = 0; i < rolls; i++)
        {
            switch (LootRoll.RollShipDrop(rng))
            {
                case ShipDrop.HealthOrb: health++; break;
                case ShipDrop.RerollToken: token++; break;
                case ShipDrop.SalvageField: field++; break;
                case ShipDrop.EcmPulse: ecm++; break;
                case ShipDrop.None: none++; break;
            }
        }

        Assert.InRange((double)health / rolls, 0.04, 0.06);
        Assert.InRange((double)token / rolls, 0.05, 0.07);
        Assert.InRange((double)field / rolls, 0.02, 0.04);
        Assert.InRange((double)ecm / rolls, 0.015, 0.035);

        // Every roll landed in exactly one band: five counters, one roll each, nothing double-counted.
        Assert.Equal(rolls, health + token + field + ecm + none);
    }

    // The whole rare-drop budget is the bands added together: a run's loot density is one number, and
    // the reward-side knob (plans/DIFFICULTY_SCALING.md R2.6 item 7) scales that number, not each kind.
    // The ECM band is the smallest of them (plans/ECM_PULSE_AND_SALVAGE_FIELD.md D1).
    [Fact]
    public void RareDropBudget_IsTheSumOfTheBands()
    {
        double total = LootStats.HealthOrbChance + LootStats.RerollTokenChance
            + LootStats.SalvageFieldChance + LootStats.EcmPulseChance;

        Assert.InRange(total, 0.05, 0.25);
        Assert.True(LootStats.EcmPulseChance < LootStats.SalvageFieldChance);
        Assert.True(LootStats.SalvageFieldChance < LootStats.RerollTokenChance);
    }
}
