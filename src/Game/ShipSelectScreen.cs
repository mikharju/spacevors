using Raylib_cs;
using Spacevors.Domain.Components;

namespace Spacevors.Game;

// Ship selection screen: a scrollable vertical list of all defined ships.
// Browse with mouse wheel or arrow/WASD keys (the highlight auto-scrolls the view);
// click a card or press Enter to select it. Number keys 1-9 pick directly.
public sealed class ShipSelectScreen
{
    const int CardWidth = 340;
    const int CardHeight = 160;
    const int CardSpacing = 60;
    const int ListTopMargin = 70;
    const int ListBottomMargin = 90; // room for the hint line
    const int ScrollbarWidth = 10;
    const int ScrollbarGap = 20;
    const float NavInitialDelay = 0.25f;
    const float NavRepeatInterval = 0.1f;
    const float WheelScrollPixelsPerUnit = 120f; // wheel events arrive as small notch values

    static readonly KeyboardKey[] DigitKeys = [KeyboardKey.One, KeyboardKey.Two, KeyboardKey.Three, KeyboardKey.Four, KeyboardKey.Five, KeyboardKey.Six, KeyboardKey.Seven, KeyboardKey.Eight, KeyboardKey.Nine];

    int _highlightedIndex;
    float _scrollOffset;
    float _navTimer;
    bool _hasStepped;

    // Returns the chosen ship when a selection is made this frame.
    public ShipType? Update(int windowWidth, int windowHeight)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.F11)) Raylib.ToggleFullscreen();
        if (Raylib.IsKeyPressed(KeyboardKey.F12)) Raylib.TakeScreenshot("screenshot.png");

        var ships = ShipType.All;
        SetScroll(_scrollOffset, windowHeight); // re-clamp after window resizes

        HandleNavigation(windowHeight, ships.Length);

        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f)
            SetScroll(_scrollOffset - wheel * WheelScrollPixelsPerUnit, windowHeight);

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            int mouseX = Raylib.GetMouseX();
            int mouseY = Raylib.GetMouseY();
            for (int i = 0; i < ships.Length; i++)
            {
                var rect = GetCardRect(i, windowWidth, windowHeight);
                if (mouseX >= rect.X && mouseX <= rect.X + rect.Width && mouseY >= rect.Y && mouseY <= rect.Y + rect.Height)
                    return ships[i];
            }
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
            return ships[_highlightedIndex];

        for (int i = 0; i < ships.Length && i < DigitKeys.Length; i++)
            if (Raylib.IsKeyPressed(DigitKeys[i]))
                return ships[i];

        return null;
    }

    public void Draw(int windowWidth, int windowHeight)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(15, 15, 25, 255));
        Lighting.BeginFrame(0f, 0f, windowWidth, windowHeight);

        var ships = ShipType.All;
        int listX = windowWidth / 2 - CardWidth / 2;
        float listTopY = GetListTopY(windowHeight);

        for (int i = 0; i < ships.Length; i++)
        {
            int y = (int)(listTopY + i * (CardHeight + CardSpacing));
            if (y + CardHeight < ListTopMargin || y > windowHeight - ListBottomMargin) continue; // off-screen

            var ship = ships[i];
            string stats = $"HP: {ship.MaxHealth} · Radius: {(int)ship.Radius}\n{ship.Engine.Name} engines · {ship.Weapon.Turrets.Count} turret{(ship.Weapon.Turrets.Count > 1 ? "s" : "")}";
            var borderColor = i == _highlightedIndex ? new Color(255, 255, 255, 255) : new Color((int)ship.DrawR, (int)ship.DrawG, (int)ship.DrawB, 255);
            DrawShipCard(listX, y, ship.Name, stats, borderColor, i < DigitKeys.Length ? $"{i + 1}" : "", i == _highlightedIndex);

            float cardBottom = y + CardHeight - 15;
            DrawShipPreview((float)(listX + CardWidth / 2), cardBottom, ship);
        }

        DrawScrollbar(windowWidth, windowHeight);

        string hint = "Arrows/WASD or scroll to browse · Click or Enter to select";
        int hintWidth = Raylib.MeasureText(hint, 16);
        Raylib.DrawText(hint, windowWidth / 2 - hintWidth / 2, windowHeight - ListBottomMargin / 2, 16, new Color(200, 200, 200, 255));

        Raylib.EndDrawing();
    }

    void HandleNavigation(int windowHeight, int shipCount)
    {
        bool up = Raylib.IsKeyDown(KeyboardKey.Up) || Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Left) || Raylib.IsKeyDown(KeyboardKey.A);
        bool down = Raylib.IsKeyDown(KeyboardKey.Down) || Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Right) || Raylib.IsKeyDown(KeyboardKey.D);
        int step = up && !down ? -1 : down && !up ? 1 : 0;

        if (step == 0)
        {
            _navTimer = 0f;
            _hasStepped = false;
            return;
        }

        // Step once on press, then repeat while held.
        _navTimer += Raylib.GetFrameTime();
        float delay = _hasStepped ? NavRepeatInterval : NavInitialDelay;
        if (_navTimer < delay) return;

        _navTimer = 0f;
        _hasStepped = true;
        _highlightedIndex = Math.Clamp(_highlightedIndex + step, 0, shipCount - 1);
        EnsureHighlightedVisible(windowHeight);
    }

    void SetScroll(float offset, int windowHeight)
    {
        float maxScroll = Math.Max(0f, GetContentHeight() - (windowHeight - ListTopMargin - ListBottomMargin));
        _scrollOffset = Math.Clamp(offset, 0f, maxScroll);
    }

    // Scrolls just enough to keep the highlighted card inside the list viewport.
    void EnsureHighlightedVisible(int windowHeight)
    {
        int viewH = windowHeight - ListTopMargin - ListBottomMargin;
        float cardTop = GetListTopY(windowHeight) + _highlightedIndex * (CardHeight + CardSpacing);
        if (cardTop < ListTopMargin)
            SetScroll(_scrollOffset - (ListTopMargin - cardTop), windowHeight);
        else if (cardTop + CardHeight > ListTopMargin + viewH)
            SetScroll(_scrollOffset + (cardTop + CardHeight - (ListTopMargin + viewH)), windowHeight);
    }

    float GetContentHeight() => ShipType.All.Length * CardHeight + (ShipType.All.Length - 1) * CardSpacing;

    // Top of the first card: centered when everything fits, otherwise shifted up by the scroll offset.
    float GetListTopY(int windowHeight)
    {
        int viewH = windowHeight - ListTopMargin - ListBottomMargin;
        return ListTopMargin + Math.Max(0f, (viewH - GetContentHeight()) / 2f) - _scrollOffset;
    }

    (int X, int Y, int Width, int Height) GetCardRect(int index, int windowWidth, int windowHeight)
    {
        float y = GetListTopY(windowHeight) + index * (CardHeight + CardSpacing);
        return (windowWidth / 2 - CardWidth / 2, (int)y, CardWidth, CardHeight);
    }

    void DrawScrollbar(int windowWidth, int windowHeight)
    {
        int viewH = windowHeight - ListTopMargin - ListBottomMargin;
        float contentH = GetContentHeight();
        if (contentH <= viewH) return; // everything fits — no scrollbar needed

        int trackX = windowWidth / 2 + CardWidth / 2 + ScrollbarGap;
        float maxScroll = contentH - viewH;
        Raylib.DrawRectangle(trackX, ListTopMargin, ScrollbarWidth, viewH, new Color(45, 45, 60, 255));

        int thumbHeight = Math.Max(30, (int)(viewH * ((float)viewH / contentH)));
        int thumbY = ListTopMargin + (int)(_scrollOffset / maxScroll * (viewH - thumbHeight));
        Raylib.DrawRectangle(trackX, thumbY, ScrollbarWidth, thumbHeight, new Color(120, 120, 150, 255));
    }

    void DrawShipPreview(float cx, float cy, ShipType ship)
    {
        const float PreviewZoom = 1.6f;
        ShipSpriteRenderer.DrawShipSprite(ship, cx, cy, ship.Radius * 2f * PreviewZoom, 0f);
    }

    void DrawShipCard(int x, int y, string title, string details, Color borderColor, string keyLabel, bool highlighted)
    {
        Raylib.DrawRectangle(x, y, CardWidth, CardHeight, highlighted ? new Color(48, 48, 64, 255) : new Color(35, 35, 45, 255));
        Raylib.DrawRectangleLines(x, y, CardWidth, CardHeight, borderColor);

        if (keyLabel.Length > 0)
            Raylib.DrawText(keyLabel, x + 10, y + 10, 18, new Color(200, 200, 200, 255));

        int titleWidth = Raylib.MeasureText(title, 24);
        Raylib.DrawText(title, x + CardWidth / 2 - titleWidth / 2, y + 30, 24, new Color(255, 255, 255, 255));

        string[] lines = details.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineW = Raylib.MeasureText(lines[i], 16);
            Raylib.DrawText(lines[i], x + CardWidth / 2 - lineW / 2, y + 65 + i * 20, 16, new Color(200, 200, 200, 255));
        }
    }
}
