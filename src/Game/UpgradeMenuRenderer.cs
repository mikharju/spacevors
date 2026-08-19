using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class UpgradeMenuRenderer
{
    const int UpgradeCardMinWidth = 60;
    const int UpgradeCardMaxWidth = 320;
    const int UpgradeCardHeight = 200;
    const int UpgradeCardSpacing = 16;
    const int UpgradeCardSideMargin = 40;
    const int UpgradeCardLabelFontSize = 20;
    const int UpgradeCardValueFontSize = 36;
    const int MaxUpgradeLabelLines = 3;

    public static void Draw(int windowWidth, int windowHeight, PendingUpgradeOptions? options = null, int playerLevel = 1)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, new Color(15, 15, 25, 215));

        string levelText = $"Level {playerLevel}";
        Raylib.DrawText(levelText, windowWidth / 2 - Raylib.MeasureText(levelText, 36) / 2, 20, 36, new Color(255, 255, 255, 255));

        if (options is { Options.Length: > 0 } opts)
        {
            int mouseX = Raylib.GetMouseX();
            int mouseY = Raylib.GetMouseY();
            for (int i = 0; i < opts.Options.Length; i++)
            {
                var opt = opts.Options[i];
                var (topLeft, w, h) = GetUpgradeCardRect(i, opts.Options.Length, windowWidth, windowHeight);
                bool hovered = mouseX >= topLeft.X && mouseX <= topLeft.X + w && mouseY >= topLeft.Y && mouseY <= topLeft.Y + h;
                DrawCard((int)topLeft.X, (int)topLeft.Y, w, h, GetUpgradeLabel(opt), GetUpgradeValue(opt.Stat), new Color(50, 150, 255, 255), (i + 1).ToString(), hovered);
            }
        }

        string hint = "Click a card or press keys";
        int hintX = windowWidth / 2 - Raylib.MeasureText(hint, 16) / 2;
        Raylib.DrawText(hint, hintX, windowHeight / 2 + UpgradeCardHeight / 2 + 30, 16, new Color(200, 200, 200, 255));
    }

    public static (Vector2 topLeft, int Width, int Height) GetUpgradeCardRect(int index, int optionCount, int windowWidth, int windowHeight)
    {
        int cardW = Math.Max(UpgradeCardMinWidth, Math.Min(
            UpgradeCardMaxWidth,
            (windowWidth - 2 * UpgradeCardSideMargin - UpgradeCardSpacing * (optionCount - 1)) / optionCount));
        int totalW = cardW * optionCount + UpgradeCardSpacing * (optionCount - 1);
        int startX = (windowWidth - totalW) / 2;
        int startY = windowHeight / 2 - UpgradeCardHeight / 2;

        int x = startX + index * (cardW + UpgradeCardSpacing);
        return (new Vector2(x, startY), cardW, UpgradeCardHeight);
    }

    private static void DrawCard(int x, int y, int width, int height, string label, string statValue, Color borderColor, string key, bool hovered)
    {
        Color background = hovered ? new Color(50, 50, 68, 255) : new Color(35, 35, 45, 255);
        Color border = hovered ? new Color(130, 200, 255, 255) : borderColor;

        Raylib.DrawRectangle(x, y, width, height, background);
        Raylib.DrawRectangleLines(x, y, width, height, border);

        DrawKeyBadge(x + 10, y + 10, key);

        string[] labelLines = WrapText(label, width - 24, UpgradeCardLabelFontSize);
        int numLines = Math.Min(labelLines.Length, MaxUpgradeLabelLines);
        const int LabelValueGap = 28;
        int linePitch = UpgradeCardLabelFontSize + 4;
        int labelHeight = numLines * linePitch - 4;
        int contentY = y + (height - labelHeight - LabelValueGap - UpgradeCardValueFontSize) / 2;

        for (int i = 0; i < numLines; i++)
        {
            int lineW = Raylib.MeasureText(labelLines[i], UpgradeCardLabelFontSize);
            Raylib.DrawText(labelLines[i], x + width / 2 - lineW / 2, contentY + i * linePitch, UpgradeCardLabelFontSize, new Color(255, 255, 255, 255));
        }

        int valueWidth = Raylib.MeasureText(statValue, UpgradeCardValueFontSize);
        Raylib.DrawText(statValue, x + width / 2 - valueWidth / 2, contentY + labelHeight + LabelValueGap, UpgradeCardValueFontSize, borderColor);
    }

    private static void DrawKeyBadge(int x, int y, string key)
    {
        const int BadgeSize = 24;
        Raylib.DrawRectangle(x, y, BadgeSize, BadgeSize, new Color(60, 60, 78, 255));

        int keyWidth = Raylib.MeasureText(key, 18);
        Raylib.DrawText(key, x + (BadgeSize - keyWidth) / 2, y + 4, 18, new Color(220, 220, 230, 255));
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
            (_, UpgradeOption.AutoTargetRange) => "auto target range",
            (_, UpgradeOption.ShotLifetime) => "shot lifetime",
            (_, UpgradeOption.Damage) => $"{option.WeaponName} damage",
            (_, UpgradeOption.PickupRadius) => "pickup radius",
            _ => $"{option.WeaponName} {option.Stat}"
        };
    }

    private static string GetUpgradeValue(UpgradeOption option) => UpgradeDefinition.For(option).DisplayValue;
}
