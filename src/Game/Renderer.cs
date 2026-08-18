using System.Linq;
using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class Renderer
{
    public static void Render(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        bool gameOver,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        ShipType shipType,
        bool diagnostics)
    {
        Raylib.BeginDrawing();
        DrawScene(em, camX, camY, windowWidth, windowHeight, stars, clutter, playerEntity, shipType, diagnostics);

        if (gameOver)
        {
            HudRenderer.DrawGameOverText(windowWidth, windowHeight);
        }

        Raylib.EndDrawing();
    }

    public static void RenderUpgradePause(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        ShipType shipType,
        bool diagnostics,
        PendingUpgradeOptions? upgradeOptions,
        int playerLevel)
    {
        Raylib.BeginDrawing();
        DrawScene(em, camX, camY, windowWidth, windowHeight, stars, clutter, playerEntity, shipType, diagnostics);
        DrawUpgradeCards(windowWidth, windowHeight, upgradeOptions, playerLevel);
        Raylib.EndDrawing();
    }

    private static void DrawScene(
        EntityManager em,
        float camX, float camY,
        int windowWidth, int windowHeight,
        List<(Vector2 Position, float Size, Color Color, float Parallax)> stars,
        List<(Vector2 Position, float Width, float Height, Color Color)> clutter,
        Entity playerEntity,
        ShipType shipType,
        bool diagnostics)
    {
        Raylib.ClearBackground(new Color(15, 15, 25, 255));

        BackgroundRenderer.Draw(stars, clutter, camX, camY, windowWidth, windowHeight);
        WorldRenderer.Draw(em, playerEntity, shipType, diagnostics, camX, camY, windowWidth, windowHeight);
        HudRenderer.DrawHealthBar(em, playerEntity, windowWidth, windowHeight);
    }

    const int UpgradeCardMinWidth = 60;
    const int UpgradeCardMaxWidth = 320;
    const int UpgradeCardHeight = 200;
    const int UpgradeCardSpacing = 16;
    const int UpgradeCardSideMargin = 40;
    const int UpgradeCardLabelFontSize = 20;
    const int UpgradeCardValueFontSize = 36;
    const int MaxUpgradeLabelLines = 3;

    public static void DrawUpgradeCards(int windowWidth, int windowHeight, PendingUpgradeOptions? options = null, int playerLevel = 1)
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

    private static string GetUpgradeValue(UpgradeOption option) => option switch
    {
        UpgradeOption.FireRate => "+15%",
        UpgradeOption.ProjectileSpeed => "+30%",
        UpgradeOption.PickupRadius => "+20%",
        UpgradeOption.Hp => "+2",
        UpgradeOption.ForwardAcceleration => "+10%",
        UpgradeOption.TurnSpeed => "+10%",
        UpgradeOption.SideThrust => "+10%",
        UpgradeOption.BackThrust => "+10%",
        _ => "?"
    };

    const int ShipCardWidth = 340;
    const int ShipCardHeight = 160;
    const int ShipCardSpacing = 60;

    public static void DrawShipCards(int windowWidth, int windowHeight)
    {
        Raylib.DrawRectangle(0, 0, windowWidth, windowHeight, new Color(15, 15, 25, 180));

        var ships = ShipType.All;
        var (startX, startY) = GetShipCardOrigin(windowWidth, windowHeight);

        for (int i = 0; i < ships.Length; i++)
        {
            var ship = ships[i];
            string stats = $"HP: {ship.MaxHealth} · Radius: {(int)ship.Radius}\n{ship.Engine.Name} engines · {ship.Weapon.Turrets.Count} turret{(ship.Weapon.Turrets.Count > 1 ? "s" : "")}";
            var borderColor = new Raylib_cs.Color((int)ship.DrawR, (int)ship.DrawG, (int)ship.DrawB, 255);
            DrawShipCard(startX + i * (ShipCardWidth + ShipCardSpacing), startY, ship.Name, stats, borderColor, $"{i + 1}");

            var cardBottom = startY + ShipCardHeight - 15;
            DrawShipPreview((float)(startX + i * (ShipCardWidth + ShipCardSpacing) + ShipCardWidth / 2), cardBottom, ship);
        }

        string hint = $"Click a card or press {string.Join(", ", Enumerable.Range(1, ships.Length))}";
        int hintWidth = Raylib.MeasureText(hint, 16);
        Raylib.DrawText(hint, windowWidth / 2 - hintWidth / 2, windowHeight / 2 + ShipCardHeight / 2 + 30, 16, new Color(200, 200, 200, 255));
    }

    private static (int X, int Y) GetShipCardOrigin(int windowWidth, int windowHeight)
    {
        int count = ShipType.All.Length;
        int totalW = ShipCardWidth * count + ShipCardSpacing * (count - 1);
        return ((windowWidth - totalW) / 2, windowHeight / 2 - ShipCardHeight / 2);
    }

    private static void DrawShipPreview(float cx, float cy, ShipType ship)
    {
        const float PreviewZoom = 1.6f;
        DrawShipSprite(ship, cx, cy, ship.Radius * 2f * PreviewZoom, 0f);
    }

    // Draws the ship sprite: lit (normal + depth maps) when available, otherwise flat texture.
    private static void DrawShipSprite(ShipType ship, float cx, float cy, float diameter, float angleDeg)
    {
        string key = ship.Name.ToLower();

        LitSprite? lit = null;
        Texture2D? tex = null;
        if (ImageLoader.PlayerShipLitSprites != null && ImageLoader.PlayerShipLitSprites.TryGetValue(key, out var litSprite))
            lit = litSprite;
        else if (ImageLoader.PlayerShipTextures != null && ImageLoader.PlayerShipTextures.TryGetValue(key, out var flat) && flat.Id != 0)
            tex = flat;

        Texture2D baseTex;
        if (lit != null) baseTex = lit.Base;
        else if (tex.HasValue) baseTex = tex.Value;
        else return;
        if (baseTex.Id == 0) return;

        float scale = diameter / baseTex.Width;
        var source = new Rectangle(0f, 0f, baseTex.Width, baseTex.Height);
        var dest = new Rectangle(cx, cy, baseTex.Width * scale, baseTex.Height * scale);
        var origin = new System.Numerics.Vector2(dest.Width / 2f, dest.Height / 2f);

        if (lit != null && Lighting.TryDraw(lit, source, dest, origin, angleDeg)) return;

        Raylib.DrawTexturePro(baseTex, source, dest, origin, angleDeg, Color.White);
    }

    public static (Vector2 topLeft, int Width, int Height) GetShipCardRect(int index, int windowWidth, int windowHeight)
    {
        var (startX, startY) = GetShipCardOrigin(windowWidth, windowHeight);
        return (new Vector2(startX + index * (ShipCardWidth + ShipCardSpacing), startY), ShipCardWidth, ShipCardHeight);
    }

    private static void DrawShipCard(int x, int y, string title, string details, Color borderColor, string key)
    {
        Raylib.DrawRectangle(x, y, 340, 160, new Color(35, 35, 45, 255));
        Raylib.DrawRectangleLines(x, y, 340, 160, borderColor);

        int keyWidth = Raylib.MeasureText(key, 18);
        Raylib.DrawText(key, x + 10, y + 10, 18, new Color(200, 200, 200, 255));

        int titleWidth = Raylib.MeasureText(title, 24);
        Raylib.DrawText(title, x + 170 - titleWidth / 2, y + 30, 24, new Color(255, 255, 255, 255));

        string[] lines = details.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineW = Raylib.MeasureText(lines[i], 16);
            Raylib.DrawText(lines[i], x + 170 - lineW / 2, y + 65 + i * 20, 16, new Color(200, 200, 200, 255));
        }
    }

}
