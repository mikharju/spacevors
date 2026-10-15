using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

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

    static readonly Color RowTint = new(255, 255, 255, 6);
    static readonly Color CountUpgraded = new(120, 255, 140, 255);
    static readonly Color CountNone = new(110, 110, 130, 255);

    public readonly record struct StatRow(string Label, string Value, int Count); // Count < 0: no count column
    public readonly record struct StatsSection(string Title, StatRow[] Rows);
    public readonly record struct StatsLayout(StatRow[] ShipRows, StatsSection[] WeaponSections);

    // Stats layout + window scale for one paused frame. Prepared once and used for both hover
    // hit-testing and drawing, so the two can never disagree and the work is not repeated.
    public sealed record Prepared(StatsLayout Layout, float Scale);

    public static Prepared Prepare(EntityManager em, Entity playerEntity, int windowWidth, int windowHeight, bool hasCards)
    {
        var layout = BuildStats(em, playerEntity);
        return new Prepared(layout, ComputeScale(windowWidth, windowHeight, layout, hasCards));
    }

    public static void Draw(Prepared prepared, string shipName, int level, int windowWidth, int windowHeight, PendingUpgradeOptions? options, int hoveredIndex)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, Palette.ScreenDim);

        bool hasCards = options is { Options.Length: > 0 };

        DrawTopPanels(prepared.Layout, shipName, level, windowWidth, prepared.Scale);

        if (hasCards)
            UpgradeMenuRenderer.DrawCards(windowWidth, windowHeight, options!.Value, hoveredIndex, prepared.Scale);
        else
        {
            UpgradeMenuRenderer.ResetFade();
            DrawHint("Tab to close", windowWidth, windowHeight, prepared.Scale);
        }
    }

    public static int GetHoveredCardIndex(Prepared prepared, PendingUpgradeOptions options, int mouseX, int mouseY, int windowWidth, int windowHeight)
    {
        if (options.Options.Length == 0) return -1;

        for (int i = 0; i < options.Options.Length; i++)
        {
            var (topLeft, w, h) = UpgradeMenuRenderer.GetUpgradeCardRect(i, options.Options.Length, windowWidth, windowHeight, prepared.Scale);
            if (mouseX >= topLeft.X && mouseX <= topLeft.X + w && mouseY >= topLeft.Y && mouseY <= topLeft.Y + h)
                return i;
        }

        return -1;
    }

    // UI scale from the window width, before the vertical shrink-to-fit. Shared with the slot-picking
    // screen so both paused screens size their text the same way.
    public static float UiScale(int windowWidth) => Math.Clamp(windowWidth / (float)BaseDesignWidth, 1f, MaxTextScale);

    static float ComputeScale(int windowWidth, int windowHeight, StatsLayout layout, bool hasCards)
    {
        float scale = UiScale(windowWidth);

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
        int radius = (int)(6 * scale);
        DrawBox(margin, margin, leftW, panelH, radius);

        int titleFont = (int)(TitleFontSize * scale);
        Raylib.DrawText(shipName, margin + padX, margin + padY, titleFont, Palette.TextBright);
        string levelText = $"LV {level}";
        Raylib.DrawText(levelText, margin + padX + Raylib.MeasureText(shipName, titleFont) + 12, margin + padY, titleFont, Palette.Accent);
        int y = margin + padY + (int)((TitleFontSize + TitleGap) * scale);
        for (int i = 0; i < layout.ShipRows.Length; i++)
            y = DrawRow(margin + padX, y, leftW - 2 * padX, layout.ShipRows[i], i, scale);

        int gap = (int)(ColumnGap * scale);
        int rightX = margin + leftW + gap;
        int rightW = windowWidth - 2 * margin - leftW - gap;
        DrawBox(rightX, margin, rightW, panelH, radius);

        y = margin + padY;
        for (int i = 0; i < layout.WeaponSections.Length; i++)
        {
            var section = layout.WeaponSections[i];
            int headerFont = (int)(HeaderFontSize * scale);
            Raylib.DrawText(section.Title.ToUpper(), rightX + padX, y, headerFont, Palette.Accent);
            Raylib.DrawRectangle(rightX + padX, y + headerFont + 4, rightW - 2 * padX, 1, Palette.PanelEdge);
            y += (int)(HeaderHeight * scale);
            for (int r = 0; r < section.Rows.Length; r++)
                y = DrawRow(rightX + padX, y, rightW - 2 * padX, section.Rows[r], r, scale);
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

    static void DrawBox(int x, int y, int width, int height, int radius)
        => Ui.DrawPanel(x, y, width, height, radius, Palette.PanelFill, Palette.PanelEdge);

    static int DrawRow(int x, int y, int contentWidth, StatRow row, int rowIndex, float scale)
    {
        int rowHeight = (int)(RowPitch * scale);
        if (rowIndex % 2 == 1)
            Ui.DrawRoundedRect(x - 6, y - 3, contentWidth + 12, rowHeight, 3, RowTint);

        int font = (int)(RowFontSize * scale);
        Raylib.DrawText(row.Label, x, y, font, Palette.TextMuted);

        int countW = row.Count >= 0 ? (int)(CountColumnWidth * scale) : 0;
        int valueEnd = x + contentWidth - countW;
        Raylib.DrawText(row.Value, valueEnd - Raylib.MeasureText(row.Value, font), y, font, Palette.TextBright);

        if (row.Count >= 0)
        {
            string count = $"x{row.Count}";
            Color color = row.Count > 0 ? CountUpgraded : CountNone;
            int countEnd = x + contentWidth;
            Raylib.DrawText(count, countEnd - Raylib.MeasureText(count, font), y, font, color);
        }

        return y + rowHeight;
    }

    static void DrawHint(string text, int windowWidth, int windowHeight, float scale)
    {
        int font = (int)(UpgradeMenuRenderer.HintFontSize * scale);
        Raylib.DrawText(text, windowWidth / 2 - Raylib.MeasureText(text, font) / 2, UpgradeMenuRenderer.GetHintY(windowHeight, scale), font, Palette.TextMuted);
    }

    static StatsLayout BuildStats(EntityManager em, Entity playerEntity)
    {
        var player = em.GetComponent<Player>(playerEntity);
        // Health.Max is the live value; Player.MaxHealth is its tuning-side twin. Show the live one,
        // as the health bar and hp bars do.
        bool hasHealth = em.TryGetComponent<Health>(playerEntity, out var health);
        int currentHp = hasHealth ? health.Current : 0;
        int maxHp = hasHealth ? health.Max : player.MaxHealth;

        var shipRows = new List<StatRow>
        {
            new("Health", $"{currentHp}/{maxHp}", CountOf(em, playerEntity, UpgradeOption.Hp)),
            new("Forward thrust", FormatInt(player.Thrust), CountOf(em, playerEntity, UpgradeOption.ForwardAcceleration)),
            new("Side thrust", FormatInt(player.SideThrust), CountOf(em, playerEntity, UpgradeOption.SideThrust)),
            new("Back thrust", FormatInt(player.BackThrust), CountOf(em, playerEntity, UpgradeOption.BackThrust)),
            new("Turn speed", $"{FormatDegPerSec(player.RotationSpeed)}°/s", CountOf(em, playerEntity, UpgradeOption.TurnSpeed)),
            new("Pickup radius", FormatInt(player.PickupRadius), CountOf(em, playerEntity, UpgradeOption.PickupRadius)),
        };

        var mounts = CountMounts(em);
        if (mounts.SmallTotal > 0) shipRows.Add(new("Small mounts", $"{mounts.SmallUsed}/{mounts.SmallTotal}", -1));
        if (mounts.LargeTotal > 0) shipRows.Add(new("Large mounts", $"{mounts.LargeUsed}/{mounts.LargeTotal}", -1));

        var weaponSections = new List<StatsSection>();
        foreach (var weaponName in GetPlayerWeaponNames(em))
        {
            var turret = FirstTurretOfWeapon(em, weaponName);
            var mount = MountOf(em, weaponName);
            var rows = new List<StatRow>
            {
                new("Damage", FormatInt(turret.Weapon.Damage), CountOf(em, playerEntity, UpgradeOption.Damage, weaponName)),
                new("Fire rate", $"{FormatRate(turret.Weapon.FireRate)}/s{PelletSuffix(turret.Weapon.PelletCount)}", CountOf(em, playerEntity, UpgradeOption.FireRate, weaponName)),
                new("Projectile speed", FormatInt(turret.Weapon.AmmoSpeed), CountOf(em, playerEntity, UpgradeOption.ProjectileSpeed, weaponName)),
                new("Range / bullet life", $"{FormatInt(turret.Range)} px · {turret.Weapon.ShotLifetime:0.0}s", CountOf(em, playerEntity, UpgradeOption.Range, weaponName)),
                // The arc belongs to the hull mount, the slew rate to the weapon: together they say
                // whether this gun keeps its target while the ship turns.
                new("Mount / firing arc", mount is { } m ? $"{m.Name} · {(int)MathF.Round(Angle.ToDeg(m.ArcAngle))}°" : "—", -1),
                new("Turret turn speed", $"{(int)MathF.Round(Angle.ToDeg(turret.Weapon.TurnSpeed))}°/s", -1),
            };
            weaponSections.Add(new StatsSection(weaponName.ToUpper(), [.. rows]));
        }

        return new StatsLayout([.. shipRows], [.. weaponSections]);
    }

    static (int SmallUsed, int SmallTotal, int LargeUsed, int LargeTotal) CountMounts(EntityManager em)
    {
        int smallUsed = 0, smallTotal = 0, largeUsed = 0, largeTotal = 0;
        foreach (var (entity, slot) in em.GetEntitiesWithComponents<TurretSlot>())
        {
            bool occupied = em.HasComponent<Turret>(entity);
            if (slot.Definition.Size == TurretSize.Large)
            {
                largeTotal++;
                if (occupied) largeUsed++;
            }
            else
            {
                smallTotal++;
                if (occupied) smallUsed++;
            }
        }

        return (smallUsed, smallTotal, largeUsed, largeTotal);
    }

    static TurretSlotDefinition? MountOf(EntityManager em, string weaponName)
    {
        foreach (var (_, turret, slot) in em.GetEntitiesWithComponents<Turret, TurretSlot>())
            if (turret.WeaponName == weaponName) return slot.Definition;
        return null;
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
