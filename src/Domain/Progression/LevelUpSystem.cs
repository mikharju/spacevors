using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Turns collected XP into a level and a level into an upgrade hand. What a hand may contain is
// UpgradeOffer's business — the same offer code re-deals a hand when a salvage capsule is spent.
public class LevelUpSystem : GameSystem
{
    // Diagnostics only: fixed upgrade sequence consumed one entry per level-up.
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

        commands.AddEntity(playerTuple.Value2, new PendingChoice(), new PendingUpgradeOptions(hand));
        commands.Add(new AddComponentCommand<Player>(playerEntity, playerStats with { Level = newLevel }));
    }

    // A scripted entry wins over the random pool and over the empty-hull rule: the script is a bench.
    private UpgradableOption[]? NextHand(WorldView view, Entity playerEntity, int newLevel)
    {
        if (_scriptedUpgrades is { } script && _scriptIndex < script.Length)
        {
            var scripted = script[_scriptIndex++];
            DiagnosticLogger.LogEvent("UPGRADE", $"level={newLevel} scripted choice: {UpgradeOffer.FormatChoice(scripted)}");
            return [scripted];
        }

        return UpgradeOffer.Roll(view, playerEntity, newLevel, "level-up");
    }
}
