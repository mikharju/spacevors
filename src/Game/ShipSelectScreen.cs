using Raylib_cs;
using Spacevors.Domain;
using Spacevors.Domain.Components;
using Spacevors.Domain.Stats;

namespace Spacevors.Game;

// The start screen: the ships on offer as a scrollable list, with a control panel beside it so a new
// player can read how the game is played and what each hull is before committing to a run
// (plans/START_SCREEN_ONBOARDING.md). The Shadow copies only appear in diagnostics mode.
// Browse with the mouse wheel or arrows/W/S — the highlight follows the mouse while it moves; click a
// card or press Enter to select; number keys 1-9 pick directly.
//
// Every size below is a design pixel at a 1920-wide window, multiplied by `_scale` at draw time, so
// the text and the cards grow with the screen the way the stats screen's UI does.
public sealed class ShipSelectScreen
{
    // A card is a preview panel, a gap, then the text column: 240 + 20 + 404 of room for the ship's
    // name, role and four labelled rows. The column is a little wider than the longest line needs at
    // 1920 because the bitmap font rounds every glyph advance up as it scales, so text grows slightly
    // faster than the card does and a line that exactly fits at 1x wraps at 1.3x.
    const int CardWidth = 680;
    const int CardHeight = 300;
    const int ShipPanelWidth = 240;
    const int TextGap = 20;
    const int CardPadding = 16;
    const int CardSpacing = 52;
    const int HeaderHeight = 96; // title, subtitle and rule
    const int HintBandHeight = 90; // the hint line under the list
    const int ScrollbarWidth = 10;
    const int ScrollbarGap = 20;
    const float NavRepeatInterval = 0.1f;
    const float WheelScrollDesignPixels = 120f; // wheel events arrive as small notch values

    // The instruction panel to the left of the list, and the group it sits in: panel + gap + cards are
    // centred together so the screen reads as one composition instead of a column adrift. Sized with
    // the same slack as the card's text column, so no control row wraps when the text scales up.
    const int PanelWidth = 440;
    const int PanelGap = 36;
    const int EdgeMargin = 32;

    // Card text: a muted label column, then wrapped values.
    const int LabelColumn = 88;
    const int TitleFontSize = 30;
    const int NameFontSize = 24;
    const int RoleFontSize = 16;
    const int RowFontSize = 15;
    const int RowPitch = 22;
    const int MaxRoleLines = 2;
    const int MaxEngineLines = 2;
    const int MaxGunsLines = 3;
    const int PreviewPadding = 12;

    // Below this the card text stops being readable, so a very short window scrolls the list instead
    // of shrinking the type further.
    const float MinScale = 0.6f;

    const string Title = "SPACEVORS";
    const string Subtitle = "Pick a hull. The guns aim and fire themselves. You just fly.";
    const string Hint = "W/S or arrows to browse · scroll the list · Click or Enter to fly";

    static readonly ControlPanel.Section[] StartSections =
    [
        new("Flying", MenuCopy.FlyingRows),
        new("In a fight", MenuCopy.FightingRows),
    ];

    static readonly KeyboardKey[] DigitKeys = [KeyboardKey.One, KeyboardKey.Two, KeyboardKey.Three, KeyboardKey.Four, KeyboardKey.Five, KeyboardKey.Six, KeyboardKey.Seven, KeyboardKey.Eight, KeyboardKey.Nine];

    int _highlightedIndex;
    float _scrollOffset;
    float _navTimer;
    float _scale = 1f;
    int _lastMouseX = -1;
    int _lastMouseY = -1;

    // The ships on offer: the real ones, plus the diagnostics copies in diagnostics mode.
    readonly ShipType[] _ships;
    readonly bool _diagnostics;
    readonly float _previewScale;
    readonly List<(Vector2 Position, float Size, Color Color, float Parallax)> _stars = MenuBackdrop.CreateStars();

    public ShipSelectScreen(bool diagnostics)
    {
        _ships = diagnostics ? [.. ShipType.All, .. ShipType.DiagnosticShips] : ShipType.All;
        _diagnostics = diagnostics;
        _previewScale = ComputePreviewScale();
    }

    // One hull scale for the whole bench, on the game's own rule: a ship is drawn `Radius * 2` wide in
    // flight (`WorldRenderer`), so a card shows the Heavy against the Scout at the sizes the player
    // will actually see. Chosen so the largest hull on the bench still fits the preview panel in both
    // directions — which is why the Scout comes out small: that is the size difference, and the point
    // of drawing them together.
    float ComputePreviewScale()
    {
        float panelWidth = ShipPanelWidth - 2 * PreviewPadding;
        float panelHeight = CardHeight - 2 * PreviewPadding;
        float scale = float.MaxValue;

        foreach (var ship in _ships)
        {
            // The width this hull needs to be as tall as the panel; Infinity asks for the aspect alone
            // (a hull with no texture returns the panel width, which is the width-only cap).
            float widthByHeight = ShipSpriteRenderer.FittedWidth(ship, float.PositiveInfinity, panelHeight);
            scale = Math.Min(scale, Math.Min(panelWidth, widthByHeight) / (ship.Radius * 2f));
        }

        return scale;
    }

    // Returns the chosen ship when a selection is made this frame.
    public ShipType? Update(int windowWidth, int windowHeight)
    {
        SetScale(windowWidth, windowHeight);
        var ships = _ships;
        SetScroll(_scrollOffset, windowHeight); // re-clamp after window resizes

        HandleNavigation(windowHeight, ships.Length);
        FollowMouseToCard(windowWidth, windowHeight, ships.Length);

        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f)
            SetScroll(_scrollOffset - wheel * WheelScrollDesignPixels * _scale, windowHeight);

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            int clicked = CardAt(Raylib.GetMouseX(), Raylib.GetMouseY(), windowWidth, windowHeight, ships.Length);
            if (clicked >= 0) return ships[clicked];
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
        SetScale(windowWidth, windowHeight);

        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(15, 15, 25, 255));
        MenuBackdrop.DrawStars(_stars, windowWidth, windowHeight);
        Lighting.BeginFrame(0f, 0f, windowWidth, windowHeight);

        var ships = _ships;
        DrawHeader(windowWidth);
        if (ShowsPanel(windowWidth))
            ControlPanel.Draw(GroupLeft(windowWidth), S(HeaderHeight), S(PanelWidth), StartSections,
                MenuCopy.StartNotes, MenuCopy.StartRecommendation, _scale);

        // The list is clipped to its own band, so a card scrolling out of the bottom stops drawing
        // straight through the hint line.
        int viewTop = S(HeaderHeight);
        Raylib.BeginScissorMode(0, viewTop, windowWidth, Math.Max(0, windowHeight - viewTop - S(HintBandHeight)));

        for (int i = 0; i < ships.Length; i++)
        {
            // Same rect the click hit-test uses, so the card cannot be drawn where it is not clickable.
            (int x, int y, int width, int height) = GetCardRect(i, windowWidth, windowHeight);
            if (y + height < viewTop || y > windowHeight - S(HintBandHeight)) continue; // off-screen

            bool highlighted = i == _highlightedIndex;
            DrawShipCard(x, y, ships[i], i < DigitKeys.Length ? $"{i + 1}" : "", highlighted);
        }

        Raylib.EndScissorMode();

        DrawScrollbar(windowWidth, windowHeight);
        DrawHint(windowWidth, windowHeight);

        Raylib.EndDrawing();
    }

    // Text and cards grow with the window like the stats screen's UI — 1x at 1920, up to 2x. A short
    // window shrinks a card instead of hiding half of it, down to MinScale.
    void SetScale(int windowWidth, int windowHeight)
    {
        float widthScale = StatsScreenRenderer.UiScale(windowWidth);
        float roomForOneCard = windowHeight - (HeaderHeight + HintBandHeight) * widthScale;
        _scale = Math.Clamp(Math.Min(widthScale, roomForOneCard / CardHeight), MinScale, widthScale);
    }

    int S(int designPixels) => (int)(designPixels * _scale);

    // Title, one line of what to expect, and a rule that ties the header to the group below it.
    void DrawHeader(int windowWidth)
    {
        int x = GroupLeft(windowWidth);
        int right = CardLeft(windowWidth) + S(CardWidth);
        int titleFont = S(TitleFontSize);

        Raylib.DrawText(Title, x, S(18), titleFont, Palette.Accent);
        Raylib.DrawText(Subtitle, x + Raylib.MeasureText(Title, titleFont) + S(20), S(18) + titleFont / 2 - S(4),
            S(RoleFontSize), Palette.TextMuted);
        Raylib.DrawRectangle(x, S(78), right - x, 1, Ui.WithAlpha(Palette.Accent, 0.25f));
    }

    void DrawHint(int windowWidth, int windowHeight)
    {
        int font = S(RoleFontSize);
        Raylib.DrawText(Hint, windowWidth / 2 - Raylib.MeasureText(Hint, font) / 2, windowHeight - S(HintBandHeight) / 2,
            font, Palette.TextMuted);
    }

    void HandleNavigation(int windowHeight, int shipCount)
    {
        // Only Up/W and Down/S browse: A and D are the hull's strafe keys in flight, and a start screen
        // that moves the list with them teaches the wrong thing.
        bool up = Raylib.IsKeyDown(KeyboardKey.Up) || Raylib.IsKeyDown(KeyboardKey.W);
        bool down = Raylib.IsKeyDown(KeyboardKey.Down) || Raylib.IsKeyDown(KeyboardKey.S);
        int step = up && !down ? -1 : down && !up ? 1 : 0;

        if (step == 0)
        {
            _navTimer = 0f;
            return;
        }

        // Step immediately on press, then repeat while held.
        bool pressed = step < 0
            ? Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)
            : Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S);

        if (pressed)
        {
            StepHighlight(step, shipCount, windowHeight);
            _navTimer = 0f;
            return;
        }

        _navTimer += Raylib.GetFrameTime();
        if (_navTimer < NavRepeatInterval) return;
        _navTimer = 0f;
        StepHighlight(step, shipCount, windowHeight);
    }

    // The highlight follows the mouse, but only while the mouse is moving: a stationary cursor must not
    // fight the keyboard player, and a hovered card is what Enter should pick.
    void FollowMouseToCard(int windowWidth, int windowHeight, int shipCount)
    {
        int mouseX = Raylib.GetMouseX();
        int mouseY = Raylib.GetMouseY();
        if (mouseX == _lastMouseX && mouseY == _lastMouseY) return;

        _lastMouseX = mouseX;
        _lastMouseY = mouseY;

        int hovered = CardAt(mouseX, mouseY, windowWidth, windowHeight, shipCount);
        if (hovered < 0) return;

        _highlightedIndex = hovered;
        EnsureHighlightedVisible(windowHeight);
    }

    int CardAt(int mouseX, int mouseY, int windowWidth, int windowHeight, int shipCount)
    {
        for (int i = 0; i < shipCount; i++)
        {
            (int x, int y, int width, int height) = GetCardRect(i, windowWidth, windowHeight);
            if (mouseX >= x && mouseX <= x + width && mouseY >= y && mouseY <= y + height) return i;
        }

        return -1;
    }

    void StepHighlight(int step, int shipCount, int windowHeight)
    {
        _highlightedIndex = Math.Clamp(_highlightedIndex + step, 0, shipCount - 1);
        EnsureHighlightedVisible(windowHeight);
    }

    void SetScroll(float offset, int windowHeight)
    {
        _scrollOffset = Math.Clamp(offset, 0f, Math.Max(0f, GetContentHeight() - ViewHeight(windowHeight)));
    }

    // Scrolls just enough to keep the highlighted card inside the list viewport.
    void EnsureHighlightedVisible(int windowHeight)
    {
        int viewH = ViewHeight(windowHeight);
        int viewTop = S(HeaderHeight);
        float cardTop = GetListTopY(windowHeight) + _highlightedIndex * (S(CardHeight) + S(CardSpacing));

        if (cardTop < viewTop)
            SetScroll(_scrollOffset - (viewTop - cardTop), windowHeight);
        else if (cardTop + S(CardHeight) > viewTop + viewH)
            SetScroll(_scrollOffset + (cardTop + S(CardHeight) - (viewTop + viewH)), windowHeight);
    }

    int ViewHeight(int windowHeight) => Math.Max(0, windowHeight - S(HeaderHeight) - S(HintBandHeight));

    float GetContentHeight() => _ships.Length * S(CardHeight) + (_ships.Length - 1) * S(CardSpacing);

    // Top of the first card: centered when everything fits, otherwise shifted up by the scroll offset.
    float GetListTopY(int windowHeight)
    {
        int viewTop = S(HeaderHeight);
        return viewTop + Math.Max(0f, (ViewHeight(windowHeight) - GetContentHeight()) / 2f) - _scrollOffset;
    }

    // The panel only fits beside the cards on a wide enough window; a narrower one gets the centred
    // list and the hint line, which is the same information in one line.
    bool ShowsPanel(int windowWidth) => windowWidth >= S(CardWidth + PanelWidth + PanelGap + 2 * EdgeMargin);

    int GroupLeft(int windowWidth) => ShowsPanel(windowWidth)
        ? (windowWidth - S(PanelWidth + PanelGap + CardWidth)) / 2
        : (windowWidth - S(CardWidth)) / 2;

    int CardLeft(int windowWidth) => GroupLeft(windowWidth) + (ShowsPanel(windowWidth) ? S(PanelWidth + PanelGap) : 0);

    (int x, int y, int width, int height) GetCardRect(int index, int windowWidth, int windowHeight) =>
        (CardLeft(windowWidth), (int)(GetListTopY(windowHeight) + index * (S(CardHeight) + S(CardSpacing))),
            S(CardWidth), S(CardHeight));

    void DrawScrollbar(int windowWidth, int windowHeight)
    {
        int viewH = ViewHeight(windowHeight);
        float contentH = GetContentHeight();
        if (contentH <= viewH) return; // everything fits — no scrollbar needed

        int trackX = CardLeft(windowWidth) + S(CardWidth) + S(ScrollbarGap);
        int thumbWidth = S(ScrollbarWidth);
        float maxScroll = contentH - viewH;

        // Trackless scrollbar: just a rounded translucent thumb.
        int thumbHeight = Math.Max(S(30), (int)(viewH * (viewH / contentH)));
        int thumbY = S(HeaderHeight) + (int)(_scrollOffset / maxScroll * (viewH - thumbHeight));
        Ui.DrawRoundedRect(trackX, thumbY, thumbWidth, thumbHeight, thumbWidth / 2, new Color(120, 130, 150, 180));
    }

    // Name and key, the role line, then the facts that decide a pick: what it can take, how it flies,
    // what it shoots.
    void DrawShipCard(int x, int y, ShipType ship, string keyLabel, bool highlighted)
    {
        int width = S(CardWidth);
        int height = S(CardHeight);
        Color fill = highlighted ? new Color(48, 50, 68, 235) : new Color(28, 30, 40, 235);
        Color border = highlighted ? new Color(255, 255, 255, 255) : new Color((int)ship.DrawR, (int)ship.DrawG, (int)ship.DrawB, 255);

        Ui.DrawPanel(x, y, width, height, S(8), fill, border);
        if (highlighted)
            Ui.DrawRoundedRect(x + S(4), y + S(10), S(3), height - S(20), 1, Palette.Accent);

        DrawShipPreview(x + S(ShipPanelWidth) / 2f, y + height / 2f, ship);

        int textX = x + S(ShipPanelWidth) + S(TextGap);
        int cursor = y + S(CardPadding);

        int nameX = textX;
        if (keyLabel.Length > 0)
            nameX = Ui.DrawKeyCap(textX, cursor + S(6), keyLabel, S(RowFontSize), Palette.TextBright, Palette.KeyCapFill) + S(12);

        int nameFont = S(NameFontSize);
        Raylib.DrawText(ship.Name, nameX, cursor, nameFont, Palette.TextBright);
        cursor += nameFont + S(6);

        cursor = DrawWrapped(textX, cursor, TextWidth(), ship.Description, Palette.Accent, S(RoleFontSize), MaxRoleLines);
        Raylib.DrawRectangle(textX, cursor - S(6), TextWidth(), 1, Palette.PanelEdge);
        cursor += S(2);

        cursor = DrawLabeledRow(textX, cursor, "HULL", MenuCopy.HullLine(ship), MaxRoleLines);
        cursor = DrawLabeledRow(textX, cursor, "ENGINES", MenuCopy.EngineLine(ship), MaxEngineLines);
        cursor = DrawLabeledRow(textX, cursor, "GUNS", MenuCopy.GunsLine(ship), MaxGunsLines);

        string free = MenuCopy.FreeMountLine(ship);
        if (free.Length > 0)
            DrawLabeledRow(textX, cursor, "MOUNTS", free, MaxRoleLines);
    }

    // The text column: the card minus the preview panel, the gap and the right-hand padding.
    int TextWidth() => S(CardWidth - ShipPanelWidth - TextGap - CardPadding);

    int DrawLabeledRow(int x, int y, string label, string value, int maxLines)
    {
        Raylib.DrawText(label, x, y + S(2), S(RowFontSize), Palette.TextMuted);
        return DrawWrapped(x + S(LabelColumn), y, TextWidth() - S(LabelColumn), value, Palette.TextBright,
            S(RowFontSize), maxLines);
    }

    // The hull at the bench-wide scale, so its size reads against every other hull on the list. The
    // panel caps are a safety net: by construction the chosen scale already fits the largest hull.
    void DrawShipPreview(float cx, float cy, ShipType ship)
    {
        float width = ShipSpriteRenderer.FittedWidth(ship, ship.Radius * 2f * _previewScale * _scale,
            S(CardHeight - 2 * PreviewPadding));
        ShipSpriteRenderer.DrawShipSprite(ship, cx, cy, width, 0f);

        // The measurement overlay, diagnostics only. The bounding circle is the honest size comparison —
        // every sprite has its own transparent margins, so the paint alone understates the gap — and the
        // cyan collider is the fit between paint and hitbox. Both are for judging a hull, not for showing
        // a player a card, so a normal run draws the art alone.
        if (!_diagnostics) return;

        var hullColor = new Color((int)ship.DrawR, (int)ship.DrawG, (int)ship.DrawB, 255);
        Raylib.DrawCircleLines((int)cx, (int)cy, width / 2f, Ui.WithAlpha(hullColor, 0.55f));
        ShipSpriteRenderer.DrawHullShape(ship.Shape, width / 2f, cx, cy, 0f);
    }

    int DrawWrapped(int x, int y, int width, string text, Color color, int font, int maxLines)
    {
        var lines = Ui.WrapText(text, width, font);
        int count = Math.Min(lines.Length, maxLines);
        int pitch = S(RowPitch);
        for (int i = 0; i < count; i++)
            Raylib.DrawText(lines[i], x, y + i * pitch, font, color);

        return y + count * pitch;
    }
}
