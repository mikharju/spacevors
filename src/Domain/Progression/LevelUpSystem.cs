using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Domain.Systems;

// Turns collected XP into a level and a level into an upgrade hand. What a hand may contain is
// UpgradeOffer's business — the same offer code re-deals a hand when a salvage capsule is spent.
public class LevelUpSystem : GameSystem
{
    // Diagnostics only: fixed upgrade sequence consumed two entries per level — the bundle's strong card,
    // then its weak one — so a run scripted `Damage:MachineGun,SideThrust` benches exactly that pair.
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
        var bundles = NextBundles(view, playerEntity, newLevel);

        // Do not consume the level-up when no choice can be offered (e.g. zero turrets).
        if (bundles is not { Length: > 0 }) return;

        commands.AddEntity(playerTuple.Value2, new PendingChoice(), new PendingUpgradeOptions(bundles));
        commands.Add(new AddComponentCommand<Player>(playerEntity, playerStats with { Level = newLevel }));
    }

    // Scripted entries win over the random pool and over the empty-hull rule: the script is a bench, and a
    // bench that cannot fill the level it is benching is not benching the real thing. Fewer than two entries
    // left is an exhausted bench, so the level falls back to the dealt offer.
    private UpgradeBundle[]? NextBundles(WorldView view, Entity playerEntity, int newLevel)
    {
        if (_scriptedUpgrades is { } script && _scriptIndex + 2 <= script.Length)
        {
            var strong = UpgradeOffer.StampDefenceTier(script[_scriptIndex++], newLevel);
            var weak = UpgradeOffer.StampDefenceTier(script[_scriptIndex++], newLevel);
            DiagnosticLogger.LogEvent("UPGRADE",
                $"level={newLevel} scripted bundle: {UpgradeOffer.FormatChoice(strong)} + {UpgradeOffer.FormatChoice(weak)}");

            return [new UpgradeBundle(strong, weak)];
        }

        return UpgradeOffer.Roll(view, playerEntity, newLevel, "level-up");
    }
}
