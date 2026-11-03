using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Turns collected XP into a level and a level into an upgrade hand. What a hand may contain is
// UpgradeOffer's business — the same offer code re-deals a hand when a salvage capsule is spent.
public class LevelUpSystem : GameSystem
{
    // Diagnostics only: fixed upgrade sequence consumed one entry per card taken, so a run scripted
    // `Hp,FireRate:MachineGun` spends both picks of its first level-up on those two cards.
    private readonly UpgradableOption[]? _scriptedUpgrades;
    private int _scriptIndex;

    public LevelUpSystem(UpgradableOption[]? scriptedUpgrades = null)
    {
        _scriptedUpgrades = scriptedUpgrades ?? UpgradeOffer.ParseScriptedUpgrades();
    }

    public override void Update(WorldView view, float deltaTime, CommandBuffer commands)
    {
        if (!view.GetEntitiesWithComponents<Player, Position>().TryFirst(out var playerTuple)) return;
        Entity playerEntity = playerTuple.Entity;
        if (view.TryGetComponent<Dead>(playerEntity, out _)) return;

        var playerStats = view.GetComponent<Player>(playerEntity);
        if (playerStats.Xp < UpgradeStats.XpForLevel(playerStats.Level)) return;

        int newLevel = playerStats.Level + 1;
        var hand = NextHand(view, playerEntity, newLevel);

        // Do not consume the level-up when no choice can be offered (e.g. zero turrets).
        if (hand is null) return;

        var options = PendingUpgradeOptions.Deal(hand, UpgradeOffer.CardsPerLevel);
        commands.AddEntity(playerTuple.Value2, new PendingChoice(), options);
        commands.Add(new AddComponentCommand<Player>(playerEntity, playerStats with { Level = newLevel }));
    }

    // Scripted entries win over the random pool and over the empty-hull rule: the script is a bench, and a
    // bench that cannot fill every pick of the level it is benching is not benching the real thing.
    private UpgradableOption[]? NextHand(WorldView view, Entity playerEntity, int newLevel)
    {
        if (_scriptedUpgrades is { } script && _scriptIndex < script.Length)
        {
            var scripted = TakeScripted(script);
            foreach (var card in scripted)
                DiagnosticLogger.LogEvent("UPGRADE", $"level={newLevel} scripted choice: {UpgradeOffer.FormatChoice(card)}");

            return scripted;
        }

        return UpgradeOffer.Roll(view, playerEntity, newLevel, "level-up");
    }

    UpgradableOption[] TakeScripted(UpgradableOption[] script)
    {
        var taken = new List<UpgradableOption>();
        while (taken.Count < UpgradeOffer.CardsPerLevel && _scriptIndex < script.Length)
            taken.Add(script[_scriptIndex++]);

        return [.. taken];
    }
}
