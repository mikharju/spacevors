using System.Linq;
using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

public static class ShipSelectRenderer
{
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

    public static (Vector2 topLeft, int Width, int Height) GetShipCardRect(int index, int windowWidth, int windowHeight)
    {
        var (startX, startY) = GetShipCardOrigin(windowWidth, windowHeight);
        return (new Vector2(startX + index * (ShipCardWidth + ShipCardSpacing), startY), ShipCardWidth, ShipCardHeight);
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
        ShipSpriteRenderer.DrawShipSprite(ship, cx, cy, ship.Radius * 2f * PreviewZoom, 0f);
    }

    private static void DrawShipCard(int x, int y, string title, string details, Color borderColor, string key)
    {
        Raylib.DrawRectangle(x, y, ShipCardWidth, ShipCardHeight, new Color(35, 35, 45, 255));
        Raylib.DrawRectangleLines(x, y, ShipCardWidth, ShipCardHeight, borderColor);

        int keyWidth = Raylib.MeasureText(key, 18);
        Raylib.DrawText(key, x + 10, y + 10, 18, new Color(200, 200, 200, 255));

        int titleWidth = Raylib.MeasureText(title, 24);
        Raylib.DrawText(title, x + ShipCardWidth / 2 - titleWidth / 2, y + 30, 24, new Color(255, 255, 255, 255));

        string[] lines = details.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineW = Raylib.MeasureText(lines[i], 16);
            Raylib.DrawText(lines[i], x + ShipCardWidth / 2 - lineW / 2, y + 65 + i * 20, 16, new Color(200, 200, 200, 255));
        }
    }
}
