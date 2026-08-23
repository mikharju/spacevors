using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class UpgradeMenuRenderer
{
    const int UpgradeCardMinWidth = 60;
    const int UpgradeCardMaxWidth = 320;
    public const int UpgradeCardHeight = 200;
    const int UpgradeCardSpacing = 16;
    const int UpgradeCardSideMargin = 40;
    const int UpgradeCardLabelFontSize = 20;
    const int UpgradeCardValueFontSize = 36;
    const int MaxUpgradeLabelLines = 3;

    public const int HintFontSize = 16;
    public const int HintGap = 28; // gap between card bottom and hint text top
    const int BottomMargin = 32;

    public static int GetHintY(int windowHeight, float scale) => (int)(windowHeight - (BottomMargin + HintFontSize) * scale);

    public static void DrawCards(int windowWidth, int windowHeight, PendingUpgradeOptions options, int hoveredIndex, float scale)
    {
        if (options.Options.Length == 0) return;

        for (int i = 0; i < options.Options.Length; i++)
        {
            var opt = options.Options[i];
            var (topLeft, w, h) = GetUpgradeCardRect(i, options.Options.Length, windowWidth, windowHeight, scale);
            DrawCard((int)topLeft.X, (int)topLeft.Y, w, h, GetUpgradeLabel(opt), GetUpgradeValue(opt.Stat), new Color(50, 150, 255, 255), (i + 1).ToString(), i == hoveredIndex, scale);
        }

        string hint = "Click a card or press keys";
        int font = (int)(HintFontSize * scale);
        Raylib.DrawText(hint, windowWidth / 2 - Raylib.MeasureText(hint, font) / 2, GetHintY(windowHeight, scale), font, new Color(200, 200, 200, 255));
    }

    public static (Vector2 topLeft, int Width, int Height) GetUpgradeCardRect(int index, int optionCount, int windowWidth, int windowHeight, float scale)
    {
        if (optionCount <= 0) return (new Vector2(0, 0), 0, 0);

        int cardH = (int)(UpgradeCardHeight * scale);
        int sideMargin = (int)(UpgradeCardSideMargin * scale);
        int spacing = (int)(UpgradeCardSpacing * scale);
        int minW = (int)(UpgradeCardMinWidth * scale);
        int maxW = (int)(UpgradeCardMaxWidth * scale);

        int cardW = Math.Max(minW, Math.Min(maxW, (windowWidth - 2 * sideMargin - spacing * (optionCount - 1)) / optionCount));
        int totalW = cardW * optionCount + spacing * (optionCount - 1);
        int startX = (windowWidth - totalW) / 2;

        // Bottom row, above the hint line.
        int startY = GetHintY(windowHeight, scale) - (int)(HintGap * scale) - cardH;

        int x = startX + index * (cardW + spacing);
        return (new Vector2(x, startY), cardW, cardH);
    }

    private static void DrawCard(int x, int y, int width, int height, string label, string statValue, Color borderColor, string key, bool hovered, float scale)
    {
        int labelFont = (int)(UpgradeCardLabelFontSize * scale);
        int valueFont = (int)(UpgradeCardValueFontSize * scale);

        Color background = hovered ? new Color(50, 50, 68, 255) : new Color(35, 35, 45, 255);
        Color border = hovered ? new Color(130, 200, 255, 255) : borderColor;

        Raylib.DrawRectangle(x, y, width, height, background);
        Raylib.DrawRectangleLines(x, y, width, height, border);

        DrawKeyBadge(x + (int)(10 * scale), y + (int)(10 * scale), key, scale);

        string[] labelLines = WrapText(label, width - (int)(24 * scale), labelFont);
        int numLines = Math.Min(labelLines.Length, MaxUpgradeLabelLines);
        int labelValueGap = (int)(28 * scale);
        int linePitch = labelFont + (int)(4 * scale);
        int labelHeight = numLines * linePitch - (int)(4 * scale);
        int contentY = y + (height - labelHeight - labelValueGap - valueFont) / 2;

        for (int i = 0; i < numLines; i++)
        {
            int lineW = Raylib.MeasureText(labelLines[i], labelFont);
            Raylib.DrawText(labelLines[i], x + width / 2 - lineW / 2, contentY + i * linePitch, labelFont, new Color(255, 255, 255, 255));
        }

        int valueWidth = Raylib.MeasureText(statValue, valueFont);
        Raylib.DrawText(statValue, x + width / 2 - valueWidth / 2, contentY + labelHeight + labelValueGap, valueFont, borderColor);
    }

    private static void DrawKeyBadge(int x, int y, string key, float scale)
    {
        int badgeSize = (int)(24 * scale);
        Raylib.DrawRectangle(x, y, badgeSize, badgeSize, new Color(60, 60, 78, 255));

        int font = (int)(18 * scale);
        int keyWidth = Raylib.MeasureText(key, font);
        Raylib.DrawText(key, x + (badgeSize - keyWidth) / 2, y + (int)(4 * scale), font, new Color(220, 220, 230, 255));
    }

    private static string[] WrapText(string text, int maxWidth, int fontSize)
    {
        var lines = new List<string>();
        string current = "";

        foreach (var word in text.Split(' '))
        {
            string candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length == 0 || Raylib.MeasureText(candidate, fontSize) <= maxWidth)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0) lines.Add(current);
        return lines.ToArray();
    }

    private static string GetUpgradeLabel(UpgradableOption option)
    {
        if (option.IsNewWeapon && option.Stat == UpgradeOption.Damage)
        {
            return $"new weapon {option.WeaponName}";
        }

        if (string.IsNullOrEmpty(option.WeaponName))
        {
            return option.Stat switch
            {
                UpgradeOption.Hp => "hit points",
                UpgradeOption.ForwardAcceleration => "forward acceleration",
                UpgradeOption.TurnSpeed => "turn speed",
                UpgradeOption.SideThrust => "side thrust",
                UpgradeOption.BackThrust => "back thrust",
                _ => $"{option.Stat}"
            };
        }

        return (option.WeaponName, option.Stat) switch
        {
            ("MachineGun", UpgradeOption.FireRate) => "machine gun attack speed",
            ("MachineGun", UpgradeOption.ProjectileSpeed) => "machine gun projectile speed",
            ("Shotgun", UpgradeOption.FireRate) => "side shot attack speed",
            ("Shotgun", UpgradeOption.ProjectileSpeed) => "side shot projectile speed",
            (_, UpgradeOption.Range) => $"{option.WeaponName} range",
            (_, UpgradeOption.Damage) => $"{option.WeaponName} damage",
            (_, UpgradeOption.PickupRadius) => "pickup radius",
            _ => $"{option.WeaponName} {option.Stat}"
        };
    }

    private static string GetUpgradeValue(UpgradeOption option) => UpgradeDefinition.For(option).DisplayValue;
}
