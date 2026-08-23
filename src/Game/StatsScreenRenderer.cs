using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

// Ship stats screen: ship/engine stats top-left, weapon stats top-right, upgrade cards along the bottom.
public static class StatsScreenRenderer
{
    const int BaseDesignWidth = 1920;
    const float MaxTextScale = 2f; // text grows with window width up to this multiple of base size

    const int OuterMargin = 32;
    const int PanelPaddingX = 16;
    const int PanelPaddingY = 16;
    const int ColumnGap = 32;
    const int LeftPanelWidth = 420;
    const int TitleFontSize = 20;
    const int TitleGap = 14;
    const int HeaderFontSize = 18;
    const int HeaderHeight = 30;
    const int RowFontSize = 16;
    const int RowPitch = 24;
    const int SectionGap = 10;
    const int CountColumnWidth = 48;

    static readonly Color DimColor = new(15, 15, 25, 215);
    static readonly Color PanelBackground = new(28, 28, 40, 255);
    static readonly Color PanelBorder = new(90, 90, 120, 255);
    static readonly Color HeaderColor = new(50, 150, 255, 255);
    static readonly Color LabelColor = new(220, 220, 230, 255);
    static readonly Color ValueColor = new(255, 255, 255, 255);
    static readonly Color CountUpgraded = new(120, 255, 140, 255);
    static readonly Color CountNone = new(110, 110, 130, 255);
    static readonly Color HintColor = new(200, 200, 200, 255);

    readonly record struct StatRow(string Label, string Value, int Count); // Count < 0: no count column
    readonly record struct StatsSection(string Title, StatRow[] Rows);
    readonly record struct StatsLayout(StatRow[] ShipRows, StatsSection[] WeaponSections);

    public static void Draw(EntityManager em, Entity playerEntity, string shipName, int level, int windowWidth, int windowHeight, PendingUpgradeOptions? options, int hoveredIndex)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, DimColor);

        var layout = BuildStats(em, playerEntity);
        bool hasCards = options is { Options.Length: > 0 };
        float scale = ComputeScale(windowWidth, windowHeight, layout, hasCards);

        DrawTopPanels(layout, shipName, level, windowWidth, scale);

        if (hasCards)
            UpgradeMenuRenderer.DrawCards(windowWidth, windowHeight, options!.Value, hoveredIndex, scale);
        else
            DrawHint("Tab to close", windowWidth, windowHeight, scale);
    }

    public static int GetHoveredCardIndex(EntityManager em, Entity playerEntity, PendingUpgradeOptions options, int mouseX, int mouseY, int windowWidth, int windowHeight)
    {
        if (options.Options.Length == 0) return -1;

        var layout = BuildStats(em, playerEntity);
        float scale = ComputeScale(windowWidth, windowHeight, layout, hasCards: true);

        for (int i = 0; i < options.Options.Length; i++)
        {
            var (topLeft, w, h) = UpgradeMenuRenderer.GetUpgradeCardRect(i, options.Options.Length, windowWidth, windowHeight, scale);
            if (mouseX >= topLeft.X && mouseX <= topLeft.X + w && mouseY >= topLeft.Y && mouseY <= topLeft.Y + h)
                return i;
        }

        return -1;
    }

    static float ComputeScale(int windowWidth, int windowHeight, StatsLayout layout, bool hasCards)
    {
        float scale = Math.Clamp(windowWidth / (float)BaseDesignWidth, 1f, MaxTextScale);

        // Shrink to fit vertically if the content would overflow a short window.
        float baseUnits = MeasureTopPanelsHeight(layout, 1f) + 2f * OuterMargin
            + (hasCards ? UpgradeMenuRenderer.UpgradeCardHeight + UpgradeMenuRenderer.HintGap + UpgradeMenuRenderer.HintFontSize : 0f);
        if (baseUnits * scale > windowHeight)
            scale = windowHeight / baseUnits;

        return scale;
    }

    static void DrawTopPanels(StatsLayout layout, string shipName, int level, int windowWidth, float scale)
    {
        int margin = (int)(OuterMargin * scale);
        int padX = (int)(PanelPaddingX * scale);
        int padY = (int)(PanelPaddingY * scale);

        float leftContentH = (TitleFontSize + TitleGap) * scale + layout.ShipRows.Length * RowPitch * scale;
        float rightContentH = MeasureWeaponSectionsHeight(layout, scale);
        int panelH = (int)MathF.Max(leftContentH, rightContentH) + 2 * padY;

        int leftW = (int)(LeftPanelWidth * scale);
        DrawBox(margin, margin, leftW, panelH);

        Raylib.DrawText($"{shipName} · Level {level}", margin + padX, margin + padY, (int)(TitleFontSize * scale), ValueColor);
        int y = margin + padY + (int)((TitleFontSize + TitleGap) * scale);
        foreach (var row in layout.ShipRows)
            y = DrawRow(margin + padX, y, leftW - 2 * padX, row, scale);

        int gap = (int)(ColumnGap * scale);
        int rightX = margin + leftW + gap;
        int rightW = windowWidth - 2 * margin - leftW - gap;
        DrawBox(rightX, margin, rightW, panelH);

        y = margin + padY;
        for (int i = 0; i < layout.WeaponSections.Length; i++)
        {
            var section = layout.WeaponSections[i];
            Raylib.DrawText(section.Title.ToUpper(), rightX + padX, y, (int)(HeaderFontSize * scale), HeaderColor);
            y += (int)(HeaderHeight * scale);
            foreach (var row in section.Rows)
                y = DrawRow(rightX + padX, y, rightW - 2 * padX, row, scale);
            if (i < layout.WeaponSections.Length - 1) y += (int)(SectionGap * scale);
        }
    }

    static float MeasureWeaponSectionsHeight(StatsLayout layout, float scale)
    {
        float height = 0f;
        for (int i = 0; i < layout.WeaponSections.Length; i++)
            height += HeaderHeight * scale + layout.WeaponSections[i].Rows.Length * RowPitch * scale + (i > 0 ? SectionGap : 0) * scale;
        return height;
    }

    static float MeasureTopPanelsHeight(StatsLayout layout, float scale)
    {
        float left = (TitleFontSize + TitleGap) * scale + layout.ShipRows.Length * RowPitch * scale;
        return MathF.Max(left, MeasureWeaponSectionsHeight(layout, scale)) + 2f * PanelPaddingY * scale;
    }

    static void DrawBox(int x, int y, int width, int height)
    {
        Raylib.DrawRectangle(x, y, width, height, PanelBackground);
        Raylib.DrawRectangleLines(x, y, width, height, PanelBorder);
    }

    static int DrawRow(int x, int y, int contentWidth, StatRow row, float scale)
    {
        int font = (int)(RowFontSize * scale);
        Raylib.DrawText(row.Label, x, y, font, LabelColor);

        int countW = row.Count >= 0 ? (int)(CountColumnWidth * scale) : 0;
        int valueEnd = x + contentWidth - countW;
        Raylib.DrawText(row.Value, valueEnd - Raylib.MeasureText(row.Value, font), y, font, ValueColor);

        if (row.Count >= 0)
        {
            string count = $"x{row.Count}";
            Color color = row.Count > 0 ? CountUpgraded : CountNone;
            int countEnd = x + contentWidth;
            Raylib.DrawText(count, countEnd - Raylib.MeasureText(count, font), y, font, color);
        }

        return y + (int)(RowPitch * scale);
    }

    static void DrawHint(string text, int windowWidth, int windowHeight, float scale)
    {
        int font = (int)(UpgradeMenuRenderer.HintFontSize * scale);
        Raylib.DrawText(text, windowWidth / 2 - Raylib.MeasureText(text, font) / 2, UpgradeMenuRenderer.GetHintY(windowHeight, scale), font, HintColor);
    }

    static StatsLayout BuildStats(EntityManager em, Entity playerEntity)
    {
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

        var weaponSections = new List<StatsSection>();
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
            weaponSections.Add(new StatsSection(weaponName.ToUpper(), [.. rows]));
        }

        return new StatsLayout([.. shipRows], [.. weaponSections]);
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
