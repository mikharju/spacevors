using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

// Ship stats panel: every upgradeable stat with its current value and how many times it was upgraded.
public static class StatsScreenRenderer
{
    public const int PanelWidth = 420;
    public const int PanelMargin = 32;
    public static int SidePanelReservedWidth => PanelWidth + 2 * PanelMargin;

    const int PaddingX = 16;
    const int PaddingY = 16;
    const int TitleFontSize = 24;
    const int TitleGap = 18;
    const int HeaderFontSize = 18;
    const int HeaderHeight = 30;
    const int RowFontSize = 16;
    const int RowPitch = 24;
    const int SectionGap = 10;
    const int CountColumnWidth = 48;

    static readonly Color PanelBackground = new(28, 28, 40, 255);
    static readonly Color PanelBorder = new(90, 90, 120, 255);
    static readonly Color HeaderColor = new(50, 150, 255, 255);
    static readonly Color LabelColor = new(220, 220, 230, 255);
    static readonly Color ValueColor = new(255, 255, 255, 255);
    static readonly Color CountUpgraded = new(120, 255, 140, 255);
    static readonly Color CountNone = new(110, 110, 130, 255);

    readonly record struct StatRow(string Label, string Value, int Count); // Count < 0: no count column
    readonly record struct StatsSection(string Title, StatRow[] Rows);

    public static void DrawOverlay(EntityManager em, Entity playerEntity, string shipName, int level, int windowWidth, int windowHeight)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, new Color(15, 15, 25, 215));

        var sections = BuildSections(em, playerEntity);
        int panelHeight = MeasurePanelHeight(sections, hasTitle: true);
        int x = (windowWidth - PanelWidth) / 2;
        int y = Math.Max(PanelMargin, (windowHeight - panelHeight) / 2);
        DrawPanel(x, y, $"{shipName} · Level {level}", sections);

        string hint = "Tab to close";
        Raylib.DrawText(hint, windowWidth / 2 - Raylib.MeasureText(hint, RowFontSize) / 2, windowHeight - 40, RowFontSize, new Color(200, 200, 200, 255));
    }

    public static void DrawSidePanel(EntityManager em, Entity playerEntity, int windowWidth, int windowHeight)
    {
        var sections = BuildSections(em, playerEntity);
        int panelHeight = MeasurePanelHeight(sections, hasTitle: false);
        int x = PanelMargin;
        int y = Math.Max(PanelMargin, (windowHeight - panelHeight) / 2);
        DrawPanel(x, y, null, sections);
    }

    static void DrawPanel(int x, int y, string? title, StatsSection[] sections)
    {
        int height = MeasurePanelHeight(sections, title != null);
        Raylib.DrawRectangle(x, y, PanelWidth, height, PanelBackground);
        Raylib.DrawRectangleLines(x, y, PanelWidth, height, PanelBorder);

        int cursorY = y + PaddingY;
        if (title is { } t)
        {
            Raylib.DrawText(t, x + PaddingX, cursorY, TitleFontSize, ValueColor);
            cursorY += TitleFontSize + TitleGap;
        }

        for (int i = 0; i < sections.Length; i++)
        {
            var section = sections[i];
            Raylib.DrawText(section.Title.ToUpper(), x + PaddingX, cursorY, HeaderFontSize, HeaderColor);
            cursorY += HeaderHeight;

            foreach (var row in section.Rows)
                cursorY = DrawRow(x, cursorY, row);

            if (i < sections.Length - 1) cursorY += SectionGap;
        }
    }

    static int DrawRow(int x, int y, StatRow row)
    {
        Raylib.DrawText(row.Label, x + PaddingX, y, RowFontSize, LabelColor);

        int valueEnd = x + PanelWidth - PaddingX - (row.Count >= 0 ? CountColumnWidth : 0);
        Raylib.DrawText(row.Value, valueEnd - Raylib.MeasureText(row.Value, RowFontSize), y, RowFontSize, ValueColor);

        if (row.Count >= 0)
        {
            string count = $"x{row.Count}";
            Color color = row.Count > 0 ? CountUpgraded : CountNone;
            int countEnd = x + PanelWidth - PaddingX;
            Raylib.DrawText(count, countEnd - Raylib.MeasureText(count, RowFontSize), y, RowFontSize, color);
        }

        return y + RowPitch;
    }

    static int MeasurePanelHeight(StatsSection[] sections, bool hasTitle)
    {
        int height = 2 * PaddingY;
        if (hasTitle) height += TitleFontSize + TitleGap;
        foreach (var section in sections)
            height += HeaderHeight + section.Rows.Length * RowPitch + SectionGap;
        return height - SectionGap;
    }

    static StatsSection[] BuildSections(EntityManager em, Entity playerEntity)
    {
        var sections = new List<StatsSection>();

        var player = em.GetComponent<Player>(playerEntity);
        int currentHp = em.TryGetComponent<Health>(playerEntity, out var health) ? health.Current : 0;

        var shipRows = new List<StatRow>
        {
            new("Health", $"{currentHp}/{player.MaxHealth}", CountOf(em, playerEntity, UpgradeOption.Hp)),
            new("Forward thrust", FormatInt(player.Thrust), CountOf(em, playerEntity, UpgradeOption.ForwardAcceleration)),
            new("Side thrust", FormatInt(player.SideThrust), CountOf(em, playerEntity, UpgradeOption.SideThrust)),
            new("Back thrust", FormatInt(player.BackThrust), CountOf(em, playerEntity, UpgradeOption.BackThrust)),
            new("Turn speed", $"{FormatDegPerSec(player.RotationSpeed)}°/s", CountOf(em, playerEntity, UpgradeOption.TurnSpeed)),
            new("Pickup radius", FormatInt(player.PickupRadius), CountOf(em, playerEntity, UpgradeOption.PickupRadius)),
        };

        if (em.TryGetComponent<WeaponSlots>(playerEntity, out var slots))
            shipRows.Add(new("Weapons", $"{slots.Used}/{slots.Max}", -1));

        sections.Add(new StatsSection("Ship", [.. shipRows]));

        foreach (var weaponName in GetPlayerWeaponNames(em))
        {
            var turret = FirstTurretOfWeapon(em, weaponName);
            var rows = new List<StatRow>
            {
                new("Damage", FormatInt(turret.Weapon.Damage), CountOf(em, playerEntity, UpgradeOption.Damage, weaponName)),
                new("Fire rate", $"{FormatRate(turret.Weapon.FireRate)}/s{PelletSuffix(turret.Weapon.PelletCount)}", CountOf(em, playerEntity, UpgradeOption.FireRate, weaponName)),
                new("Projectile speed", FormatInt(turret.Weapon.AmmoSpeed), CountOf(em, playerEntity, UpgradeOption.ProjectileSpeed, weaponName)),
                new("Range / bullet life", $"{FormatInt(turret.Range)} px · {turret.Weapon.ShotLifetime:0.0}s", CountOf(em, playerEntity, UpgradeOption.Range, weaponName)),
            };
            sections.Add(new StatsSection(weaponName.ToUpper(), [.. rows]));
        }

        return [.. sections];
    }

    static List<string> GetPlayerWeaponNames(EntityManager em)
    {
        var names = new List<string>();
        foreach (var (_, turret) in em.GetEntitiesWithComponents<Turret>())
            if (!turret.IsEnemy && !names.Contains(turret.WeaponName))
                names.Add(turret.WeaponName);
        return names;
    }

    static Turret FirstTurretOfWeapon(EntityManager em, string weaponName)
    {
        foreach (var (_, turret) in em.GetEntitiesWithComponents<Turret>())
            if (!turret.IsEnemy && turret.WeaponName == weaponName)
                return turret;
        throw new InvalidOperationException($"No player turret for weapon {weaponName}");
    }

    static int CountOf(EntityManager em, Entity playerEntity, UpgradeOption stat, string weaponName = "") =>
        em.TryGetComponent<UpgradeCounts>(playerEntity, out var counts) ? counts.GetCount(stat, weaponName) : 0;

    static string FormatInt(float value) => ((int)MathF.Round(value)).ToString();

    static string FormatRate(float rate) => rate < 10f ? $"{rate:0.0}" : ((int)MathF.Round(rate)).ToString();

    static string FormatDegPerSec(float radPerSec) => ((int)MathF.Round(radPerSec * 180f / MathF.PI)).ToString();

    static string PelletSuffix(int pelletCount) => pelletCount > 1 ? $" · {pelletCount} pellets" : "";
}
